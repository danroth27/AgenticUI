// Copyright (c) Microsoft. All rights reserved.

using AGUI.Client;
using Microsoft.Extensions.AI;

namespace AgenticUI.Web;

/// <summary>
/// Creates an <see cref="IChatClient"/> for an AG-UI endpoint on the agent server. Each scenario in
/// the demo has its own AG-UI endpoint (e.g. <c>/agentic_chat</c>, <c>/shared_state</c>); the
/// <see cref="AGUIChatClient"/> turns that HTTP+SSE endpoint into a standard
/// <see cref="IChatClient"/> that the Blazor AI components consume through a <c>UIAgent</c>.
/// </summary>
public sealed class AgentServerConnection(IHttpClientFactory httpClientFactory)
{
    /// <summary>Creates an <see cref="IChatClient"/> for the given AG-UI endpoint path.</summary>
    /// <param name="endpoint">The endpoint path on the agent server, e.g. <c>/agentic_chat</c>.</param>
    public IChatClient CreateChatClient(string endpoint)
    {
        HttpClient http = httpClientFactory.CreateClient("agentserver");
        return new AGUIChatClient(new AGUIChatClientOptions(http, endpoint));
    }
}
