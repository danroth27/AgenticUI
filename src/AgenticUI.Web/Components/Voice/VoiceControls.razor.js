// Browser side of the voice scenario. The browser talks to Deepgram directly: Flux speech-to-text
// over wss://api.deepgram.com/v2/listen and Flux text-to-speech over wss://api.deepgram.com/v2/speak.
// Both sockets authenticate with a short-lived JWT from the app's /api/deepgram/token endpoint, so
// the API key stays on the server and no audio flows through the Blazor circuit.
//
// It follows the Deepgram voice UI best practices
// (https://github.com/dg-edcharbeneau/voice-best-practices): one explicit state machine, Flux turn
// events for turn-taking, instant barge-in, a mic meter, AudioWorklet capture, gap-free playback,
// one TTS socket per session, and full teardown on stop.
//
// States (the only source of truth; .NET mirrors it for rendering):
//   idle       - no session; microphone and sockets closed
//   connecting - acquiring the microphone and opening the sockets
//   listening  - microphone live, waiting for or hearing the user
//   thinking   - the user's turn ended (or a message was typed); the agent is responding
//   speaking   - the agent's reply is playing
//   error      - the session failed; .NET shows why
const TOKEN_URL = "api/deepgram/token";
const TOKEN_REFRESH_MARGIN_MS = 10_000;
const WORKLET_URL = "js/pcm-worklet.js";
const LISTEN_URL = "wss://api.deepgram.com/v2/listen?model=flux-general-en&encoding=linear16&sample_rate=16000";
const SPEAK_SAMPLE_RATE = 24000;
// Delivery register for Flux TTS (beta): an integer from -2 (calm) to 2 (animated), default 0.
// It's a connection parameter, so it applies to the whole session.
const SPEAK_EXPRESSIVITY = 1;
const SPEAK_URL = `wss://api.deepgram.com/v2/speak?model=flux-haley-en&encoding=linear16&sample_rate=${SPEAK_SAMPLE_RATE}&expressivity=${SPEAK_EXPRESSIVITY}`;
const SPEAK_SPEED = 1;

// Flux TTS closes a session after 60 s without a client message. Browsers can't send WebSocket
// pings, so an idle session is kept alive with a no-op Configure (re-sending the current speed).
// Keeping the socket matters: cross-turn voice context lives on it, and a reconnect resets it.
const KEEP_ALIVE_IDLE_MS = 20_000;
const KEEP_ALIVE_CHECK_MS = 5_000;

const INTERRUPT_IGNORED = ["NO_AUDIO_GENERATED", "INTERRUPT_IN_PROGRESS", "INVALID_INTERRUPT_OFFSET"];

// Returns null when the browser can run voice, or a plain-language reason when it can't.
export function preflight() {
    if (!window.isSecureContext) {
        return "Voice needs a secure connection. Open the app over https or localhost.";
    }
    if (!navigator.mediaDevices?.getUserMedia) {
        return "This browser doesn't support microphone capture.";
    }
    if (typeof AudioWorkletNode === "undefined" || typeof WebSocket === "undefined") {
        return "This browser doesn't support real-time audio.";
    }
    return null;
}

export function createSession(dotnetRef) {
    return new VoiceSession(dotnetRef);
}

class VoiceError extends Error {
    constructor(name, message) {
        super(message);
        this.name = name;
    }
}

// One token covers both sockets: it only has to be valid for the WebSocket handshake, and an open
// socket outlives it.
let cachedToken = null; // { value, expiresAt }

async function getToken() {
    if (cachedToken && cachedToken.expiresAt - TOKEN_REFRESH_MARGIN_MS > Date.now()) {
        return cachedToken.value;
    }

    let response;
    try {
        response = await fetch(new URL(TOKEN_URL, document.baseURI), { cache: "no-store" });
    } catch (error) {
        throw new VoiceError("TokenError", error.message);
    }
    if (!response.ok) {
        throw new VoiceError("TokenError", `Token request failed (${response.status}).`);
    }

    const { access_token, expires_in } = await response.json();
    cachedToken = { value: access_token, expiresAt: Date.now() + (expires_in ?? 30) * 1000 };
    return access_token;
}

// Browsers can't set an Authorization header on a WebSocket, so Deepgram accepts the JWT as the
// "bearer" subprotocol instead.
function openSocket(url, token) {
    return new Promise((resolve, reject) => {
        const socket = new WebSocket(url, ["bearer", token]);
        socket.binaryType = "arraybuffer";
        socket.onopen = () => resolve(socket);
        socket.onerror = () => reject(new VoiceError("ConnectionError", "Could not connect to Deepgram."));
    });
}

class VoiceSession {
    constructor(dotnetRef) {
        this.dotnet = dotnetRef;
        this.state = "idle";
        this.muted = false;
        this.speaking = false;
        this.resetSession();
    }

    resetSession() {
        // Capture and speech-to-text.
        this.listenSocket = null;
        this.micStream = null;
        this.captureContext = null;
        this.meterElement = null;
        this.level = 0;
        this.meterFrame = 0;

        // Text-to-speech and playback.
        this.speakSocket = null;
        this.playbackContext = null;
        this.sources = new Set();
        this.playhead = 0;
        this.leftover = null; // Odd trailing byte when a chunk splits a 16-bit sample.
        this.keepAliveTimer = 0;
        this.lastClientMessageAt = 0;
        this.speechChain = Promise.resolve();

        // Replies and turns. Each agent reply gets a generation number and is one Flux TTS turn.
        // Turns start in the order they were opened, so a queue of generations tells us which reply
        // incoming audio belongs to, and audio from a superseded reply is never played.
        this.generation = 0;
        this.awaitingResponse = false; // The user's turn was sent; the reply hasn't started yet.
        this.responseActive = false; // A reply is streaming from the agent.
        this.interrupted = false; // The current reply was cut off; drop the rest of it.
        this.openTurn = null; // Generation of the turn we're still sending text into.
        this.lastOpenedTurn = null; // Generation of the most recently opened turn.
        this.pendingTurns = []; // Generations of opened turns the server hasn't started yet.
        this.activeTurn = null; // Generation of the turn the server is synthesizing.
        this.interruptPending = false;
        this.reportInterrupt = false; // Tell .NET what was heard when SpeechInterrupted arrives.
        this.sessionAudioMs = 0; // Length of the session's audio timeline, as the server counts it.
        this.lastInterruptMs = -1;
    }

    get active() {
        return this.state === "listening" || this.state === "thinking" || this.state === "speaking";
    }

    setState(next) {
        if (this.state !== next) {
            this.state = next;
            this.notify("OnStateChanged", next);
        }
    }

    // ---- Lifecycle ------------------------------------------------------------------------

    async start(meterElement) {
        if (this.state !== "idle" && this.state !== "error") {
            return;
        }

        this.setState("connecting");
        try {
            // Microphone first: the permission prompt can take a while, and the sockets shouldn't
            // sit idle behind it.
            const micStream = await navigator.mediaDevices.getUserMedia({
                audio: { channelCount: 1, echoCancellation: true, noiseSuppression: true, autoGainControl: true },
            });
            this.micStream = micStream;
            if (this.state !== "connecting") {
                micStream.getTracks().forEach(track => track.stop()); // stop() ran meanwhile.
                return;
            }

            this.meterElement = meterElement ?? null;

            // Started by the user's click, so autoplay policy lets it run.
            this.playbackContext = new AudioContext({ sampleRate: SPEAK_SAMPLE_RATE });
            await this.playbackContext.resume();

            const token = await getToken();
            const [speakSocket, listenSocket] = await Promise.all([
                openSocket(SPEAK_URL, token),
                openSocket(LISTEN_URL, token),
            ]);
            if (this.state !== "connecting") {
                speakSocket.close();
                listenSocket.close();
                return;
            }

            this.speakSocket = speakSocket;
            this.listenSocket = listenSocket;
            speakSocket.onmessage = event => this.onSpeakMessage(event);
            speakSocket.onclose = event => this.onSocketClosed(speakSocket, event);
            listenSocket.onmessage = event => this.onListenMessage(event);
            listenSocket.onclose = event => this.onSocketClosed(listenSocket, event);
            this.lastClientMessageAt = performance.now();
            this.keepAliveTimer = setInterval(() => this.keepAlive(), KEEP_ALIVE_CHECK_MS);

            await this.startCapture();
            if (this.state === "connecting") {
                this.setState("listening");
            }
        } catch (error) {
            if (this.state === "connecting") {
                this.fail(error);
            }
        }
    }

    async startCapture() {
        this.captureContext = new AudioContext();
        await this.captureContext.audioWorklet.addModule(new URL(WORKLET_URL, document.baseURI));

        const source = this.captureContext.createMediaStreamSource(this.micStream);
        const worklet = new AudioWorkletNode(this.captureContext, "pcm-capture");
        worklet.port.onmessage = event => {
            const message = event.data;
            if (message.type === "audio") {
                // A zero-length frame would close the stream, so only send real audio.
                if (message.buffer.byteLength > 0 && this.canSend(this.listenSocket)) {
                    this.listenSocket.send(message.buffer);
                }
            } else if (message.type === "level") {
                this.showLevel(message.level);
            }
        };

        // A worklet only runs while its graph reaches a destination. Route it through a muted gain
        // node so every browser pulls it, without playing the microphone back to the user.
        const mute = this.captureContext.createGain();
        mute.gain.value = 0;
        source.connect(worklet);
        worklet.connect(mute);
        mute.connect(this.captureContext.destination);
    }

    stop() {
        if (this.state === "idle") {
            return;
        }

        this.teardown();
        this.setState("idle");
    }

    // Releases everything: flush and close speech-to-text, stop the microphone tracks (which turns
    // off the browser's recording indicator), close the speech socket, and close both audio graphs.
    teardown() {
        clearInterval(this.keepAliveTimer);
        cancelAnimationFrame(this.meterFrame);
        this.meterElement?.style.setProperty("--level", "0");

        const listen = this.listenSocket;
        const speak = this.speakSocket;
        this.listenSocket = null;
        this.speakSocket = null;
        if (this.canSend(listen)) {
            listen.send(JSON.stringify({ type: "CloseStream" }));
        }
        listen?.close();
        if (this.canSend(speak)) {
            speak.send(JSON.stringify({ type: "Close" }));
        }
        speak?.close();

        this.micStream?.getTracks().forEach(track => track.stop());
        this.captureContext?.close();
        this.stopPlayback();
        this.playbackContext?.close();
        this.resetSession();
    }

    fail(error) {
        this.notify("OnError", error?.name ?? "Error", error?.message ?? String(error));
        this.teardown();
        this.setState("error");
    }

    onSocketClosed(socket, event) {
        // Our own teardown clears the socket fields first; anything else is the server or network
        // ending the session (including Deepgram's one-hour cap), which the user needs to see.
        if (socket === this.listenSocket || socket === this.speakSocket) {
            this.fail(new VoiceError("SessionClosed", event.reason || `Deepgram closed the connection (${event.code}).`));
        }
    }

    // ---- Speech-to-text: Flux turn events --------------------------------------------------

    onListenMessage(event) {
        if (typeof event.data !== "string") {
            return;
        }

        const message = JSON.parse(event.data);
        if (message.type === "Error" || message.type === "FatalError") {
            this.fail(new VoiceError("ConnectionError", message.description ?? "Speech recognition failed."));
            return;
        }
        if (message.type !== "TurnInfo") {
            return;
        }

        const turnIndex = message.turn_index ?? -1;
        switch (message.event) {
            case "StartOfTurn":
                // Barge-in: the user talking over the agent silences it immediately, and .NET
                // cancels the reply that's still being generated.
                if (this.state === "thinking" || this.state === "speaking") {
                    this.bargeIn();
                }
                this.setState("listening");
                this.notify("OnInterimTranscript", message.transcript ?? "", turnIndex);
                break;
            case "Update":
            case "TurnResumed":
                this.notify("OnInterimTranscript", message.transcript ?? "", turnIndex);
                break;
            case "EagerEndOfTurn":
                // The user is probably done. An app could start a speculative response here and
                // cancel it on TurnResumed; this one waits for EndOfTurn.
                this.notify("OnInterimTranscript", message.transcript ?? "", turnIndex);
                break;
            case "EndOfTurn": {
                const transcript = (message.transcript ?? "").trim();
                if (transcript) {
                    this.awaitingResponse = true;
                    this.setState("thinking");
                    this.notify("OnFinalTranscript", transcript, turnIndex);
                } else {
                    this.notify("OnInterimTranscript", "", turnIndex);
                }
                break;
            }
        }
    }

    bargeIn() {
        // Only a reply that has started (streaming, or still playing) can be interrupted. While the
        // user's previous turn is still waiting on the agent there is nothing to cut off.
        if (this.responseActive || this.state === "speaking") {
            this.interruptSpeech({ report: !this.muted });
        }
        this.awaitingResponse = false;
        this.notify("OnBargeIn");
    }

    // .NET decided not to send the transcript (for example a tool is awaiting input).
    resumeListening() {
        this.awaitingResponse = false;
        this.maybeSettle();
    }

    // ---- Text-to-speech: Flux TTS turns ----------------------------------------------------
    //
    // Each agent reply is one turn: text streams in with Speak and audio starts on its own; Flush
    // ends the turn. The server brackets each turn with SpeechStarted and SpeechMetadata (or
    // SpeechInterrupted), and all of a turn's audio arrives between the two.
    //
    // Speech calls arrive from .NET without waiting for each other, so they run through one promise
    // chain to keep Speak and Flush in order.

    // A new agent reply started streaming: silence the previous one and re-arm speech.
    beginResponse() {
        if (!this.active) {
            return;
        }

        this.generation++;
        this.awaitingResponse = false;
        this.responseActive = true;
        this.interrupted = false;
        this.stopPlayback();
        this.enqueueSpeech(() => this.closeOpenTurn());
        this.setState("thinking");
    }

    speak(text) {
        const generation = this.generation;
        this.enqueueSpeech(() => {
            if (!text?.trim() || this.muted || this.isStale(generation) || !this.canSend(this.speakSocket)) {
                return;
            }

            if (this.openTurn === null) {
                this.openTurn = generation;
                this.lastOpenedTurn = generation;
                this.pendingTurns.push(generation);
            }

            // The server doesn't add whitespace between Speak messages, and sentences arrive trimmed.
            this.sendSpeech({ type: "Speak", text: `${text} ` });
        });
    }

    // The reply finished (completed, cancelled or failed): end its turn so whatever was sent gets
    // synthesized, and settle once its audio has played.
    endResponse() {
        if (!this.responseActive) {
            return;
        }

        const generation = this.generation;
        this.responseActive = false;
        this.enqueueSpeech(() => {
            if (this.openTurn === generation) {
                this.closeOpenTurn();
            }
            this.maybeSettle();
        });
    }

    setMuted(muted) {
        this.muted = muted;
        if (muted) {
            // The user chose to read instead of listen, so there's nothing to reconcile.
            this.interruptSpeech({ report: false });
            this.maybeSettle();
        }
    }

    closeOpenTurn() {
        if (this.openTurn !== null && this.canSend(this.speakSocket)) {
            this.sendSpeech({ type: "Flush" });
        }

        this.openTurn = null;
    }

    isStale(generation) {
        return this.interrupted || !this.active || generation !== this.generation;
    }

    enqueueSpeech(step) {
        this.speechChain = this.speechChain
            .then(step)
            .catch(error => console.warn("Deepgram TTS failed:", error));
    }

    canSend(socket) {
        return socket?.readyState === WebSocket.OPEN;
    }

    sendSpeech(message) {
        this.speakSocket.send(JSON.stringify(message));
        this.lastClientMessageAt = performance.now();
    }

    keepAlive() {
        if (this.canSend(this.speakSocket) &&
            performance.now() - this.lastClientMessageAt >= KEEP_ALIVE_IDLE_MS) {
            this.sendSpeech({ type: "Configure", speed: SPEAK_SPEED });
        }
    }

    // Stop playback locally right away, then tell the server how much was actually heard. The
    // round-trip is bookkeeping (it cancels synthesis and reports text_spoken); the silence is
    // immediate. It's sent even after synthesis finished, since audio can still be playing locally.
    interruptSpeech({ report }) {
        this.interrupted = true;
        const playedMs = this.playedMs();
        this.stopPlayback();

        // Did the current reply's turn produce audio the user could have heard?
        const turnStarted = this.lastOpenedTurn === this.generation &&
            this.generation > 0 &&
            !this.pendingTurns.includes(this.generation) &&
            this.sessionAudioMs > 0;
        if (!turnStarted) {
            if (report && this.responseActive) {
                this.notify("OnSpeechInterrupted", null); // None of this reply was heard.
            }
            this.lastOpenedTurn = null;
            return;
        }

        this.lastOpenedTurn = null; // Report each reply at most once.
        if (this.interruptPending || !this.canSend(this.speakSocket)) {
            return;
        }

        // The offset counts from the start of the session's audio and must advance past the previous
        // interrupt, so a session-wide counter is used rather than a per-turn one.
        const offset = Math.max(Math.round(playedMs), this.lastInterruptMs + 1);
        this.sendSpeech({ type: "Interrupt", playback_offset: { type: "time_ms", value: offset } });
        this.interruptPending = true;
        this.reportInterrupt = report;
        this.lastInterruptMs = offset;
        this.sessionAudioMs = offset; // The server rebases the session onto what was actually heard.
        if (this.openTurn === this.generation) {
            this.openTurn = null; // The interrupt cancelled the turn we were still writing.
        }
    }

    // Position on the session's audio timeline the user has heard up to: all audio received, minus
    // what is still queued ahead of the playhead.
    playedMs() {
        const ahead = this.playbackContext
            ? Math.max(0, this.playhead - this.playbackContext.currentTime) * 1000
            : 0;
        return Math.max(0, this.sessionAudioMs - ahead);
    }

    stopPlayback() {
        for (const source of this.sources) {
            source.onended = null;
            source.stop();
        }

        this.sources.clear();
        this.playhead = 0;
        this.leftover = null;
        this.speaking = false;
    }

    onSpeakMessage(event) {
        if (typeof event.data !== "string") {
            // Track the server's audio timeline (used for Interrupt offsets) whether or not the audio
            // is played. Frames still in flight after an Interrupt fall off it when the server rebases.
            if (!this.interruptPending) {
                this.sessionAudioMs += event.data.byteLength / 2 / SPEAK_SAMPLE_RATE * 1000;
            }

            if (!this.interrupted && !this.muted && this.active && this.activeTurn === this.generation) {
                this.enqueueAudio(event.data);
            }
            return;
        }

        const message = JSON.parse(event.data);
        switch (message.type) {
            case "SpeechStarted":
                this.activeTurn = this.pendingTurns.shift() ?? null;
                break;
            case "SpeechMetadata":
                this.activeTurn = null;
                this.maybeSettle();
                break;
            case "SpeechInterrupted":
                this.activeTurn = null;
                this.interruptPending = false;
                if (this.reportInterrupt) {
                    this.notify("OnSpeechInterrupted", message.text_spoken ?? null);
                }
                this.reportInterrupt = false;
                this.maybeSettle();
                break;
            case "Warning":
                if (INTERRUPT_IGNORED.includes(message.code)) {
                    this.interruptPending = false;
                    if (this.reportInterrupt) {
                        this.notify("OnSpeechInterrupted", null);
                    }
                    this.reportInterrupt = false;
                }
                console.warn(`Deepgram TTS warning ${message.code}:`, message.description);
                break;
            case "Error":
                this.fail(new VoiceError("ConnectionError", message.description ?? "Speech synthesis failed."));
                break;
        }
    }

    // Back to listening once the reply is done generating, its turn is fully synthesized, and the
    // last of its audio has played.
    maybeSettle() {
        if ((this.state === "thinking" || this.state === "speaking") &&
            !this.awaitingResponse &&
            !this.responseActive &&
            this.openTurn !== this.generation &&
            this.activeTurn !== this.generation &&
            !this.pendingTurns.includes(this.generation) &&
            this.sources.size === 0) {
            this.setState("listening");
        }
    }

    // Flux TTS streams raw 16-bit PCM with no header, so each chunk is scheduled back to back on a
    // running timeline; that keeps playback gap-free.
    enqueueAudio(buffer) {
        let bytes = new Uint8Array(buffer);
        if (this.leftover) {
            const joined = new Uint8Array(this.leftover.length + bytes.length);
            joined.set(this.leftover);
            joined.set(bytes, this.leftover.length);
            bytes = joined;
            this.leftover = null;
        }

        if (bytes.length % 2 === 1) {
            this.leftover = bytes.slice(bytes.length - 1);
            bytes = bytes.subarray(0, bytes.length - 1);
        }

        if (bytes.length === 0) {
            return;
        }

        const context = this.playbackContext;
        const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
        const samples = new Float32Array(bytes.length / 2);
        for (let i = 0; i < samples.length; i++) {
            samples[i] = view.getInt16(i * 2, true) / 0x8000;
        }

        const audioBuffer = context.createBuffer(1, samples.length, SPEAK_SAMPLE_RATE);
        audioBuffer.copyToChannel(samples, 0);

        const source = context.createBufferSource();
        source.buffer = audioBuffer;
        source.connect(context.destination);
        source.onended = () => {
            this.sources.delete(source);
            if (this.sources.size === 0) {
                this.speaking = false;
                this.maybeSettle();
            }
        };

        // A small lead time on the first chunk absorbs network jitter between chunks.
        this.playhead = Math.max(this.playhead, context.currentTime + 0.05);
        source.start(this.playhead);
        this.playhead += audioBuffer.duration;
        this.sources.add(source);
        if (!this.speaking) {
            this.speaking = true;
            if (this.state === "thinking") {
                this.setState("speaking");
            }
        }
    }

    // ---- Mic meter ------------------------------------------------------------------------

    // The level arrives ~100 times a second. It's applied straight to the meter element once per
    // animation frame rather than sent to .NET, so it costs nothing on the Blazor circuit.
    showLevel(level) {
        this.level = level;
        if (this.meterElement && !this.meterFrame) {
            this.meterFrame = requestAnimationFrame(() => {
                this.meterFrame = 0;
                const scaled = Math.min(1, this.level * 4);
                this.meterElement?.style.setProperty("--level", scaled.toFixed(3));
            });
        }
    }

    // ---- Interop --------------------------------------------------------------------------

    // Calls into .NET; after dispose, or once the circuit is gone, there is nobody left to tell.
    notify(method, ...args) {
        if (!this.disposed) {
            this.dotnet.invokeMethodAsync(method, ...args).catch(() => { });
        }
    }

    dispose() {
        this.disposed = true;
        this.teardown();
        this.state = "idle";
    }
}
