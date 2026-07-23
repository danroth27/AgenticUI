// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace AgenticUI.Web.Models;

// ---- Shared state (recipe) ----

/// <summary>Client view of the shared recipe state (matches the server's RecipeResponse shape).</summary>
public sealed class RecipeState
{
    [JsonPropertyName("recipe")]
    public Recipe Recipe { get; set; } = new();
}

public sealed class Recipe
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("skill_level")]
    public string SkillLevel { get; set; } = string.Empty;

    [JsonPropertyName("cooking_time")]
    public string CookingTime { get; set; } = string.Empty;

    [JsonPropertyName("special_preferences")]
    public List<string> SpecialPreferences { get; set; } = [];

    [JsonPropertyName("ingredients")]
    public List<Ingredient> Ingredients { get; set; } = [];

    [JsonPropertyName("instructions")]
    public List<string> Instructions { get; set; } = [];
}

public sealed class Ingredient
{
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;
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

// ---- Predictive state updates (document) ----

/// <summary>Client view of the document being progressively written.</summary>
public sealed class DocumentState
{
    [JsonPropertyName("document")]
    public string Document { get; set; } = string.Empty;
}
