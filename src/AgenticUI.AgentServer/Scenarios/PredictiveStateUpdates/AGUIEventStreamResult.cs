// Copyright (c) Microsoft. All rights reserved.

using AGUI.Abstractions;
using AGUI.Formatting;
using Microsoft.AspNetCore.Http.Features;

namespace AgenticUI.AgentServer.Scenarios.PredictiveStateUpdates;

internal sealed class AGUIEventStreamResult(
    IAsyncEnumerable<BaseEvent> events,
    IAGUIEventStreamFormatter formatter,
    CancellationToken cancellationToken) : IResult
{
    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = formatter.MediaType;
        response.Headers.CacheControl = "no-cache,no-store";
        response.Headers.Pragma = "no-cache";
        httpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            httpContext.RequestAborted,
            cancellationToken);

        await formatter.WriteAsync(events, response.Body, linked.Token);
        await response.Body.FlushAsync(linked.Token);
    }
}
