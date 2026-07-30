// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using AGUI.Abstractions;
using AGUI.Client;

namespace AgenticUI.Web;

/// <summary>
/// Wraps an <see cref="IAGUITransport"/> so that every run carries the client's current state in
/// <see cref="RunAgentInput.State"/>. AG-UI already streams state from the agent to the UI via
/// <c>STATE_SNAPSHOT</c>; this closes the loop in the other direction, so edits the user makes
/// directly in the UI are visible to the agent on the next turn.
/// </summary>
internal sealed class StateForwardingTransport(
    IAGUITransport inner,
    Func<JsonElement?> stateProvider) : IAGUITransport
{
    public IAsyncEnumerable<BaseEvent> SendAsync(
        RunAgentInput input, CancellationToken cancellationToken = default)
    {
        input.State = stateProvider();
        return inner.SendAsync(input, cancellationToken);
    }
}
