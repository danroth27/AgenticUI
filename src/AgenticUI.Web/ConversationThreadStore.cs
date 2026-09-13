// Copyright (c) Microsoft. All rights reserved.

using Microsoft.AspNetCore.Components.AI;
using Microsoft.Extensions.AI;

namespace AgenticUI.Web;

public sealed class ConversationThreadStore
{
    private readonly Dictionary<string, InMemoryConversationThread> _threads = [];

    public IConversationThread Get(string threadId)
    {
        if (!_threads.TryGetValue(threadId, out var thread))
        {
            thread = new InMemoryConversationThread(threadId);
            _threads.Add(threadId, thread);
        }

        return thread;
    }
}

internal sealed class InMemoryConversationThread(string threadId) : IConversationThread
{
    private readonly List<ChatResponseUpdate> _updates = [];
    private List<ChatResponseUpdate>? _currentTurn;

    public string ThreadId { get; } = threadId;

    public bool IsStateful { get; private set; }

    public string? ConversationId { get; private set; }

    public void AppendUserMessage(ChatMessage message)
    {
        _currentTurn =
        [
            new ChatResponseUpdate
            {
                Role = message.Role,
                Contents = [.. message.Contents],
            },
        ];
    }

    public void AppendUpdate(ChatResponseUpdate update)
    {
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
