// Copyright (c) Microsoft. All rights reserved.

using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace AgenticUI.Web.Components.Pages.Scenarios;

public sealed class ReasoningActivityBlock : ActivityContentBlock
{
    private readonly StringBuilder _text = new();

    public string Text => _text.ToString();

    public void Append(string text)
    {
        _text.Append(text);
        Content = JsonSerializer.SerializeToElement(Text);
    }
}

public sealed class ReasoningActivityHandler : ActivityHandler<ReasoningActivityBlock>
{
    protected override bool TryCreateBlock(
        BlockMappingContext context,
        ReasoningActivityBlock state)
        => TryAppend(context, state, out _);

    protected override bool TryUpdateBlock(
        BlockMappingContext context,
        ReasoningActivityBlock state,
        out bool isCompleted)
        => TryAppend(context, state, out isCompleted);

    private static bool TryAppend(
        BlockMappingContext context,
        ReasoningActivityBlock state,
        out bool isCompleted)
    {
        foreach (var content in context.UnhandledContents)
        {
            if (content is TextReasoningContent reasoning &&
                !string.IsNullOrEmpty(reasoning.Text))
            {
                context.MarkHandled(content);
                state.ActivityType = "reasoning";
                state.Append(reasoning.Text);
                isCompleted = false;
                return true;
            }
        }

        isCompleted = false;
        return false;
    }
}
