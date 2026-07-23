// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;
using AGUI.Abstractions;
using Microsoft.AspNetCore.Components.AI;

namespace AgenticUI.Web;

/// <summary>
/// Bridges AG-UI state events to the Blazor AI components' <c>UIAgent&lt;TState&gt;</c> state.
/// <para>
/// AG-UI surfaces shared state on the client as a <see cref="ChatResponseUpdate"/> whose
/// <c>RawRepresentation</c> is a <see cref="StateSnapshotEvent"/> (a full snapshot) or a
/// <see cref="StateDeltaEvent"/> (an RFC 6902 JSON Patch). This helper decodes both into
/// <typeparamref name="TState"/> so a scenario page can render live state.
/// </para>
/// </summary>
public static class AguiState
{
    private static readonly JsonSerializerOptions s_json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Attempts to map an AG-UI state event on <paramref name="context"/> into the next state value.
    /// </summary>
    /// <param name="context">The state-mapper context supplied by the component pipeline.</param>
    /// <param name="current">A getter for the current state (used to apply deltas).</param>
    /// <param name="next">The resulting state, when this update carried state.</param>
    /// <returns><c>true</c> if the update was a state snapshot or delta.</returns>
    public static bool TryMap<TState>(StateMapperContext context, Func<TState?> current, out TState? next)
        where TState : class
    {
        switch (context.Update.RawRepresentation)
        {
            case StateSnapshotEvent snapshot:
                next = snapshot.Snapshot.Deserialize<TState>(s_json);
                return true;

            case StateDeltaEvent delta:
                JsonNode node = JsonSerializer.SerializeToNode(current(), s_json) ?? new JsonObject();
                ApplyJsonPatch(node, delta.Delta);
                next = node.Deserialize<TState>(s_json);
                return true;

            default:
                next = null;
                return false;
        }
    }

    /// <summary>Applies a minimal subset of RFC 6902 (add/replace/remove) to <paramref name="root"/>.</summary>
    private static void ApplyJsonPatch(JsonNode root, JsonElement patch)
    {
        if (patch.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (JsonElement op in patch.EnumerateArray())
        {
            string? operation = op.TryGetProperty("op", out var o) ? o.GetString() : null;
            string? path = op.TryGetProperty("path", out var p) ? p.GetString() : null;
            if (operation is null || string.IsNullOrEmpty(path))
            {
                continue;
            }

            JsonNode? value = op.TryGetProperty("value", out var v)
                ? JsonNode.Parse(v.GetRawText())
                : null;

            ApplyOperation(root, operation, path, value);
        }
    }

    private static void ApplyOperation(JsonNode root, string op, string path, JsonNode? value)
    {
        string[] segments = path.TrimStart('/').Split('/');
        JsonNode? parent = root;

        for (int i = 0; i < segments.Length - 1 && parent is not null; i++)
        {
            parent = Navigate(parent, Unescape(segments[i]));
        }

        if (parent is null)
        {
            return;
        }

        string leaf = Unescape(segments[^1]);
        switch (op)
        {
            case "add":
            case "replace":
                SetChild(parent, leaf, value);
                break;
            case "remove":
                RemoveChild(parent, leaf);
                break;
        }
    }

    private static JsonNode? Navigate(JsonNode node, string segment) => node switch
    {
        JsonObject obj => obj.TryGetPropertyValue(segment, out var child) ? child : null,
        JsonArray arr when int.TryParse(segment, out int index) && index >= 0 && index < arr.Count => arr[index],
        _ => null
    };

    private static void SetChild(JsonNode parent, string segment, JsonNode? value)
    {
        switch (parent)
        {
            case JsonObject obj:
                obj[segment] = value;
                break;
            case JsonArray arr when int.TryParse(segment, out int index) && index >= 0 && index < arr.Count:
                arr[index] = value;
                break;
        }
    }

    private static void RemoveChild(JsonNode parent, string segment)
    {
        switch (parent)
        {
            case JsonObject obj:
                obj.Remove(segment);
                break;
            case JsonArray arr when int.TryParse(segment, out int index) && index >= 0 && index < arr.Count:
                arr.RemoveAt(index);
                break;
        }
    }

    private static string Unescape(string segment) => segment.Replace("~1", "/").Replace("~0", "~");
}
