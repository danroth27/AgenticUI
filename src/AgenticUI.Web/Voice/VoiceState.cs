namespace AgenticUI.Web.Voice;

/// <summary>
/// The single source of truth for the voice UI. The browser module drives the transitions; this is
/// the C# mirror the component renders, so the UI is a projection of one value rather than a set
/// of booleans that can contradict each other.
/// </summary>
public enum VoiceState
{
    /// <summary>No voice session; the microphone and Deepgram sockets are closed.</summary>
    Idle,

    /// <summary>Acquiring the microphone and opening the Deepgram sockets.</summary>
    Connecting,

    /// <summary>The microphone is live, waiting for or hearing the user.</summary>
    Listening,

    /// <summary>The user's turn is done (or a message was typed) and the agent is responding.</summary>
    Thinking,

    /// <summary>The agent's reply is playing.</summary>
    Speaking,

    /// <summary>The session failed; the reason is shown to the user.</summary>
    Error,
}

public static class VoiceStates
{
    public static string Label(this VoiceState state) => state switch
    {
        VoiceState.Connecting => "Connecting…",
        VoiceState.Listening => "Listening",
        VoiceState.Thinking => "Thinking…",
        VoiceState.Speaking => "Speaking",
        VoiceState.Error => "Error",
        _ => "Idle",
    };

    /// <summary>The <c>data-state</c> value that drives the state-based styles.</summary>
    public static string DataAttribute(this VoiceState state) => state.ToString().ToLowerInvariant();

    /// <summary>Parses the lowercase state name sent by the browser module.</summary>
    public static VoiceState Parse(string value) => value switch
    {
        "idle" => VoiceState.Idle,
        "connecting" => VoiceState.Connecting,
        "listening" => VoiceState.Listening,
        "thinking" => VoiceState.Thinking,
        "speaking" => VoiceState.Speaking,
        _ => VoiceState.Error,
    };

    /// <summary>Whether a voice session is open (the microphone and sockets are live).</summary>
    public static bool IsActive(this VoiceState state) =>
        state is VoiceState.Listening or VoiceState.Thinking or VoiceState.Speaking;

    /// <summary>Turns a browser error into a plain-language, actionable message.</summary>
    public static string Humanize(string name, string message) => name switch
    {
        "NotAllowedError" or "SecurityError" =>
            "Microphone access was blocked. Allow the microphone for this site and try again.",
        "NotFoundError" =>
            "No microphone was found. Connect one and try again.",
        "NotReadableError" =>
            "The microphone is in use by another application.",
        "TokenError" =>
            "Couldn't get a Deepgram token from the server. Check the API key configuration.",
        "ConnectionError" =>
            "Couldn't connect to Deepgram. Check your network and try again.",
        "SessionClosed" =>
            "The voice session ended. Click the microphone to start a new one.",
        _ => string.IsNullOrWhiteSpace(message) ? "Voice failed. Please try again." : message,
    };
}
