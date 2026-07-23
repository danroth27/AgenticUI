// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace AgenticUI.AgentServer.Scenarios.PredictiveStateUpdates;

/// <summary>The document state streamed progressively as the model writes.</summary>
public sealed class DocumentState
{
    [JsonPropertyName("document")]
    public string Document { get; set; } = string.Empty;
}
