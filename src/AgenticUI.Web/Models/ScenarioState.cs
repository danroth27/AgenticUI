// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace AgenticUI.Web.Models;

// ---- Shared state (recipe) ----

/// <summary>Client view of the shared recipe state (matches the server's RecipeResponse shape).</summary>
public sealed record RecipeState
{
    [JsonPropertyName("recipe")]
    public Recipe Recipe { get; init; } = new();
}

public sealed record Recipe
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("skill_level")]
    public string SkillLevel { get; init; } = string.Empty;

    [JsonPropertyName("cooking_time")]
    public string CookingTime { get; init; } = string.Empty;

    [JsonPropertyName("special_preferences")]
    public List<string> SpecialPreferences { get; init; } = [];

    [JsonPropertyName("ingredients")]
    public List<Ingredient> Ingredients { get; init; } = [];

    [JsonPropertyName("instructions")]
    public List<string> Instructions { get; init; } = [];
}

public sealed record Ingredient
{
    [JsonPropertyName("icon")]
    public string Icon { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("amount")]
    public string Amount { get; init; } = string.Empty;
}

// ---- Agentic generative UI (plan) ----

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

// ---- Predictive state (document editor) ----

public sealed class DocumentState
{
    [JsonPropertyName("document")]
    public string Document { get; set; } = string.Empty;
}
