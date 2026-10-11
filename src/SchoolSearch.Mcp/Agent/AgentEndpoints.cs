using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SchoolSearch.Mcp.Options;

namespace SchoolSearch.Mcp.Agent;

/// <summary>Answer of <c>POST /agent/chat</c>. Steps and citations are for display; the reply is plain text.</summary>
public sealed record AgentChatResponse(
    string Reply,
    IReadOnlyList<AgentStep> Steps,
    IReadOnlyList<string> CitedSlugs,
    string AiLabel,
    string Model,
    string StopReason);

/// <summary>
/// <c>POST /agent/chat</c>, plain HTTP next to the MCP endpoint (not an MCP tool). Failures use the uniform
/// {"error":{"code","message"}} shape. Without a key or with AI_ENABLED=false the answer is 503 ai_unavailable and no
/// model is called.
/// </summary>
public static class AgentEndpoints
{
    /// <summary>Largest body accepted. A conversation of 12 turns of 1000 characters fits well below this.</summary>
    private const long MaxBodyBytes = 32 * 1024;

    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/agent/chat", ChatAsync).WithName("AgentChat");
        return endpoints;
    }

    private static async Task<IResult> ChatAsync(HttpContext http, CancellationToken ct)
    {
        var services = http.RequestServices;
        var options = services.GetRequiredService<AgentOptions>();
        var agent = services.GetService<AgentService>();
        if (!options.Enabled || agent is null)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "ai_unavailable", "Doradca AI jest wyłączony albo nie ma skonfigurowanego klucza.");
        }

        var clientIp = AgentClientIp.Resolve(http, services.GetRequiredService<IOptions<McpOptions>>().Value);
        if (!services.GetRequiredService<AgentRateLimiter>().TryAcquire(clientIp))
        {
            http.Response.Headers["Retry-After"] = "60";
            return Error(StatusCodes.Status429TooManyRequests, "rate_limited", "Zbyt wiele zapytań. Spróbuj ponownie za chwilę.");
        }

        if (services.GetRequiredService<AgentTokenBudget>().IsExhausted())
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "ai_budget_exhausted", "Dzienny limit AI został wykorzystany. Spróbuj jutro.");
        }

        if (http.Request.ContentLength is > MaxBodyBytes)
        {
            return Error(StatusCodes.Status400BadRequest, AgentRequestParser.ValidationCode, "Zapytanie jest zbyt duże.");
        }

        AgentChatRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync<AgentChatRequest>(http.Request.Body, RequestJson, ct);
        }
        catch (JsonException)
        {
            return Error(StatusCodes.Status400BadRequest, AgentRequestParser.ValidationCode, "Treść zapytania nie jest poprawnym JSON-em.");
        }

        var validation = AgentRequestParser.Parse(request);
        if (validation.ErrorCode is not null || validation.Conversation is null)
        {
            return Error(StatusCodes.Status400BadRequest, validation.ErrorCode ?? AgentRequestParser.ValidationCode, validation.ErrorMessage ?? "Nieprawidłowe zapytanie.");
        }

        try
        {
            var result = await agent.RunAsync(validation.Conversation, validation.Gmina, ct);
            return Results.Ok(new AgentChatResponse(
                result.Reply,
                result.Steps,
                result.CitedSlugs,
                AgentPrompts.AiLabel,
                options.Model,
                result.StopReason));
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            // The SDK or the network failed. The message is generic: no upstream body, key or prompt is returned.
            return Error(StatusCodes.Status502BadGateway, "agent_upstream_error", "Usługa AI jest chwilowo niedostępna. Spróbuj ponownie.");
        }
    }

    private static IResult Error(int status, string code, string message) =>
        Results.Json(new { error = new { code, message } }, statusCode: status);
}

/// <summary>
/// The client address the agent rate limit uses. Same rule as the MCP limiter: the socket peer, unless the Fly edge is
/// trusted (McpOptions.TrustProxyClientIpHeader), then Fly-Client-IP.
/// </summary>
internal static class AgentClientIp
{
    private const string FlyClientIpHeader = "Fly-Client-IP";

    public static string Resolve(HttpContext context, McpOptions options)
    {
        if (options.TrustProxyClientIpHeader
            && context.Request.Headers.TryGetValue(FlyClientIpHeader, out var header))
        {
            var value = header.ToString().Trim();
            if (value.Length > 0)
            {
                return value;
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
