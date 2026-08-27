// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Text.Json.Serialization;

namespace AgenticUI.AgentServer.Scenarios.SharedState;

/// <summary>A recipe that the shared-state agent keeps in sync with the client.</summary>
public sealed class Recipe
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("skill_level")]
    [Description("One of: Beginner, Intermediate, Advanced.")]
    public string SkillLevel { get; set; } = string.Empty;

    [JsonPropertyName("cooking_time")]
    [Description("One of: 15 min, 30 min, 45 min, 1 hr, 1.5 hr, 2 hr.")]
    public string CookingTime { get; set; } = string.Empty;

    [JsonPropertyName("special_preferences")]
    [Description("Dietary preferences relevant to the recipe.")]
    public List<string> SpecialPreferences { get; set; } = [];

    [JsonPropertyName("ingredients")]
    public List<Ingredient> Ingredients { get; set; } = [];

    [JsonPropertyName("instructions")]
    public List<string> Instructions { get; set; } = [];
}

/// <summary>A single ingredient in a <see cref="Recipe"/>.</summary>
public sealed class Ingredient
{
    [JsonPropertyName("icon")]
    public string Icon { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;
}

/// <summary>Structured-output wrapper used as the JSON schema response format.</summary>
public sealed class RecipeResponse
{
    [JsonPropertyName("recipe")]
    public Recipe Recipe { get; set; } = new();
}
