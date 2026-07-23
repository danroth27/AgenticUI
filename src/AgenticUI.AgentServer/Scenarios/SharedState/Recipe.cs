// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json.Serialization;

namespace AgenticUI.AgentServer.Scenarios.SharedState;

/// <summary>A recipe that the shared-state agent keeps in sync with the client.</summary>
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
