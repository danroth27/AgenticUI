// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace AgenticUI.Web.Models;

/// <summary>Client view of the plan state produced by create_plan / update_plan_step.</summary>
public sealed class PlanState
{
    [JsonPropertyName("steps")]
    public List<PlanStep> Steps { get; set; } = [];
}

public sealed class PlanStep
{
    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; set; } = "pending";
}
