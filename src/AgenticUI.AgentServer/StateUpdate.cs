// Copyright (c) Microsoft. All rights reserved.

using AGUI.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace AgenticUI.AgentServer;

/// <summary>
/// Helper for emitting AG-UI state events from an <see cref="AIAgent"/>. AG-UI raw events are surfaced
/// by wrapping a <see cref="ChatResponseUpdate"/> whose <see cref="ChatResponseUpdate.RawRepresentation"/>
/// is the event, so the <c>AgentResponseUpdate → ChatResponseUpdate</c> bridge forwards it to AGUI.Server.
/// </summary>
internal static class StateUpdate
{
    public static AgentResponseUpdate From(BaseEvent stateEvent) => new()
    {
        Role = ChatRole.Assistant,
        RawRepresentation = new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            RawRepresentation = stateEvent
        }
    };
}
