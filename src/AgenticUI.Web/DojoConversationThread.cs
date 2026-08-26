// Copyright (c) Microsoft. All rights reserved.

using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace AgenticUI.Web;

internal sealed class DojoConversationThread(string threadId) : IConversationThread
{
    private readonly List<ChatResponseUpdate> _updates = [];
    private List<ChatResponseUpdate>? _currentTurn;

    public string ThreadId { get; } =
        !string.IsNullOrEmpty(threadId)
            ? threadId
            : throw new ArgumentException("A thread identifier is required.", nameof(threadId));

    public bool IsStateful { get; private set; }

    public string? ConversationId { get; private set; }

    public void AppendMessages(IEnumerable<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        _currentTurn =
        [
            .. messages.Select(message => new ChatResponseUpdate
            {
                Role = message.Role,
                Contents = [.. message.Contents],
            }),
        ];
    }

    public void AppendUpdate(ChatResponseUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);
        _currentTurn?.Add(update);

        if (update.ConversationId is not null)
        {
            IsStateful = true;
            ConversationId = update.ConversationId;
        }
    }

    public void CompleteTurn()
    {
        if (_currentTurn is not null)
        {
            _updates.AddRange(_currentTurn);
            _currentTurn = null;
        }
    }

    public IReadOnlyList<ChatResponseUpdate> GetUpdates() => _updates;
}
