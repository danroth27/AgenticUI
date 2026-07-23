// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace AgenticUI.AgentServer.Scenarios.AgenticGenerativeUi;

/// <summary>A multi-step plan produced by the <c>create_plan</c> tool.</summary>
public sealed class Plan
{
    [JsonPropertyName("steps")]
    public List<Step> Steps { get; set; } = [];
}

/// <summary>A single step in a <see cref="Plan"/>.</summary>
public sealed class Step
{
    [JsonPropertyName("description")]
    public required string Description { get; set; }

    [JsonPropertyName("status")]
    public StepStatus Status { get; set; } = StepStatus.Pending;
}

/// <summary>The lifecycle status of a plan <see cref="Step"/>.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StepStatus>))]
public enum StepStatus
{
    Pending,
    Completed
}

/// <summary>A JSON Patch (RFC 6902) operation emitted as a state delta.</summary>
public sealed class JsonPatchOperation
{
    [JsonPropertyName("op")]
    public required string Op { get; set; }

    [JsonPropertyName("path")]
    public required string Path { get; set; }

    [JsonPropertyName("value")]
    public object? Value { get; set; }

    [JsonPropertyName("from")]
    public string? From { get; set; }
}
