// AudioWorklet that converts microphone audio into 16 kHz mono linear16 PCM frames for Deepgram
// Flux, and reports the input level for the mic meter. It runs on the audio thread, so capture
// doesn't glitch under main-thread load the way the deprecated ScriptProcessorNode does.
// The AudioContext runs at the device's native rate (Firefox refuses to connect a microphone
// to a context with a different rate), so downsampling happens here.
const TARGET_RATE = 16000;
const FRAME_SAMPLES = 1280; // 80 ms at 16 kHz, Deepgram's recommended streaming chunk.
const LEVEL_EVERY_QUANTA = 4; // ~10 ms between level reports at 48 kHz.

class PcmCaptureProcessor extends AudioWorkletProcessor {
    constructor() {
        super();
        this.ratio = sampleRate / TARGET_RATE;
        this.position = 0; // Fractional read position into the current input block.
        this.frame = new Int16Array(FRAME_SAMPLES);
        this.length = 0;
        this.sumSquares = 0;
        this.levelSamples = 0;
        this.quanta = 0;
    }

    process(inputs) {
        const channel = inputs[0]?.[0];
        if (!channel || channel.length === 0) {
            return true;
        }

        for (let i = 0; i < channel.length; i++) {
            this.sumSquares += channel[i] * channel[i];
        }

        this.levelSamples += channel.length;
        if (++this.quanta % LEVEL_EVERY_QUANTA === 0) {
            this.port.postMessage({ type: "level", level: Math.sqrt(this.sumSquares / this.levelSamples) });
            this.sumSquares = 0;
            this.levelSamples = 0;
        }

        // Box-filter decimation: each output sample averages the source samples it covers. Crude
        // next to a real resampler, but plenty for speech recognition.
        while (this.position < channel.length) {
            const start = Math.floor(this.position);
            const end = Math.min(channel.length, Math.max(start + 1, Math.floor(this.position + this.ratio)));
            let sum = 0;
            for (let i = start; i < end; i++) {
                sum += channel[i];
            }

            const sample = Math.max(-1, Math.min(1, sum / (end - start)));
            this.frame[this.length++] = sample < 0 ? sample * 0x8000 : sample * 0x7fff;
            if (this.length === FRAME_SAMPLES) {
                this.port.postMessage({ type: "audio", buffer: this.frame.buffer }, [this.frame.buffer]);
                this.frame = new Int16Array(FRAME_SAMPLES);
                this.length = 0;
            }

            this.position += this.ratio;
        }

        this.position -= channel.length;
        return true;
    }
}

registerProcessor("pcm-capture", PcmCaptureProcessor);
