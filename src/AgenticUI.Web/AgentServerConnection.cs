// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;
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

    /// <summary>
    /// Creates an <see cref="IChatClient"/> that sends the client's current state to the agent on
    /// every run, so edits the user makes directly in the UI are part of the next turn's input.
    /// </summary>
    /// <param name="endpoint">The endpoint path on the agent server, e.g. <c>/shared_state</c>.</param>
    /// <param name="stateProvider">Supplies the state to send, or <see langword="null"/> to send none.</param>
    public IChatClient CreateChatClient(string endpoint, Func<JsonElement?> stateProvider)
    {
        HttpClient http = httpClientFactory.CreateClient("agentserver");

        // Build the default HTTP/SSE transport, then wrap it so each run carries the current state.
        var defaults = new AGUIChatClientOptions(http, endpoint);

        return new AGUIChatClient(new AGUIChatClientOptions
        {
            Transport = new StateForwardingTransport(defaults.Transport, stateProvider),
            JsonSerializerOptions = defaults.JsonSerializerOptions,
        });
    }
}
