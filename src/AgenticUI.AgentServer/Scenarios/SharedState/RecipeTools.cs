// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;

namespace AgenticUI.AgentServer.Scenarios.SharedState;

/// <summary>
/// The backend tool the shared-state agent calls. It simply returns the recipe the model produced;
/// the hosting layer turns that tool result into an AG-UI <c>STATE_SNAPSHOT</c> via
/// <c>AGUIStreamOptions.MapResultAsStateSnapshot("generate_recipe")</c> — no custom agent or
/// hand-built protocol content required.
/// </summary>
internal static class RecipeTools
{
    [Description("Generate or update the shared recipe and display it to the user.")]
    public static RecipeResponse GenerateRecipe(
        [Description("The complete recipe to display.")] Recipe recipe) => new() { Recipe = recipe };
}
