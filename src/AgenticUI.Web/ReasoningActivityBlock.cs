using System.Text;
using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace AgenticUI.Web;

public sealed class ReasoningActivityBlock : ActivityContentBlock
{
    private readonly StringBuilder _text = new();

    public string Text => _text.ToString();

    public string? ProtectedData { get; private set; }

    internal void Append(TextReasoningContent content)
    {
        if (content.ProtectedData is not null)
        {
            ProtectedData = content.ProtectedData;
        }

        if (!string.IsNullOrEmpty(content.Text))
        {
            _text.Append(content.Text);
        }
    }
}

public sealed class ReasoningActivityHandler : ActivityHandler<ReasoningActivityBlock>
{
    protected override bool TryCreateBlock(
        BlockMappingContext context,
        ReasoningActivityBlock state)
        => TryAppend(context, state);

    protected override bool TryUpdateBlock(
        BlockMappingContext context,
        ReasoningActivityBlock state,
        out bool isCompleted)
    {
        if (TryAppend(context, state))
        {
            isCompleted = false;
            return true;
        }

        isCompleted = true;
        return true;
    }

    private static bool TryAppend(
        BlockMappingContext context,
        ReasoningActivityBlock state)
    {
        foreach (var content in context.UnhandledContents)
        {
            if (content is TextReasoningContent reasoning)
            {
                context.MarkHandled(reasoning);
                state.Append(reasoning);
                return true;
            }
        }

        return false;
    }
}
