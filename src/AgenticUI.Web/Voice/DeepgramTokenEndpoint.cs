using System.Net.Http.Headers;
using System.Text.Json.Serialization;

namespace AgenticUI.Web.Voice;

/// <summary>
/// Mints short-lived Deepgram JWTs for the browser. The browser opens its Flux speech-to-text and
/// Flux text-to-speech WebSockets straight to Deepgram, so audio never flows through the Blazor
/// circuit, and the API key never leaves the server.
/// </summary>
internal static class DeepgramTokenEndpoint
{
    public const string HttpClientName = "deepgram";
    public const string ApiKeyVariable = "DEEPGRAM_API_KEY";

    // The JWT only needs to outlive the WebSocket handshake; open connections survive its expiry.
    private const int TokenTtlSeconds = 30;

    public static IServiceCollection AddDeepgramTokens(this IServiceCollection services)
    {
        services.AddHttpClient(HttpClientName, (sp, client) =>
        {
            client.BaseAddress = new Uri("https://api.deepgram.com");

            var apiKey = sp.GetRequiredService<IConfiguration>()[ApiKeyVariable];
            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Token", apiKey);
            }
        });

        return services;
    }

    public static IEndpointRouteBuilder MapDeepgramToken(this IEndpointRouteBuilder endpoints)
    {
        // The demo has no authentication, so this endpoint is open to anyone who can reach the app.
        // Put it behind authorization before deploying anywhere public.
        endpoints.MapGet("/api/deepgram/token", async (
            HttpContext context,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            context.Response.Headers.CacheControl = "no-store";

            if (string.IsNullOrWhiteSpace(configuration[ApiKeyVariable]))
            {
                return Results.Problem(
                    "Voice is disabled because no Deepgram API key is configured.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var http = httpClientFactory.CreateClient(HttpClientName);
            using var response = await http.PostAsJsonAsync(
                "/v1/auth/grant",
                new GrantRequest(TokenTtlSeconds),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                loggerFactory.CreateLogger(typeof(DeepgramTokenEndpoint)).LogWarning(
                    "Deepgram token grant failed with status {StatusCode}.",
                    (int)response.StatusCode);
                return Results.Problem(
                    "Deepgram rejected the token request.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var grant = await response.Content.ReadFromJsonAsync<GrantResponse>(cancellationToken);
            return grant?.AccessToken is { Length: > 0 }
                ? Results.Ok(grant)
                : Results.Problem(
                    "Deepgram returned an empty token.",
                    statusCode: StatusCodes.Status502BadGateway);
        });

        return endpoints;
    }

    private sealed record GrantRequest(
        [property: JsonPropertyName("ttl_seconds")] int TtlSeconds);

    private sealed record GrantResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] double ExpiresIn);
}
