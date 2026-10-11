using System.Text.Json;
using ModelContextProtocol.Protocol;
using SchoolSearch.Mcp.Catalog;

namespace SchoolSearch.Mcp.Tools;

/// <summary>
/// Turns catalog responses into MCP tool results. This is the only file that builds <see cref="CallToolResult"/>.
/// Success returns the Api JSON verbatim as text. Failure is an MCP tool error (IsError) with the uniform
/// {"error":{"code","message"}} shape: never a stack trace and never an upstream body.
/// </summary>
internal static class ToolResponse
{
    private const string UnavailableMessage = "The school catalog is temporarily unavailable. Try again shortly.";
    private const string RejectedMessage = "The school catalog rejected the request.";

    public static CallToolResult Json(string body) => new()
    {
        Content = [new TextContentBlock { Text = body }],
        IsError = false,
    };

    public static CallToolResult Failure(string code, string message) => new()
    {
        Content = [new TextContentBlock { Text = JsonSerializer.Serialize(new { error = new { code, message } }) }],
        IsError = true,
    };

    public static CallToolResult From(CatalogResponse response)
    {
        if (response.IsSuccess)
        {
            return Json(response.Body);
        }

        // The Api's own uniform errors (404 unknown school, 400 bad parameter) are passed through: they carry no
        // internals. Anything else, including every 5xx and an unreachable catalog, becomes a generic message.
        if (response.StatusCode is >= 400 and < 500
            && TryReadEnvelope(response.Body, out var code, out var message))
        {
            return Failure(code, message);
        }

        return response.StatusCode >= 500 || response.StatusCode == 0
            ? Failure("upstream_unavailable", UnavailableMessage)
            : Failure("upstream_error", RejectedMessage);
    }

    /// <summary>Reads {"error":{"code","message"}} from an upstream body. Returns false for anything else.</summary>
    internal static bool TryReadEnvelope(string body, out string code, out string message)
    {
        code = string.Empty;
        message = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("error", out var error)
                || error.ValueKind != JsonValueKind.Object
                || !error.TryGetProperty("code", out var codeElement)
                || codeElement.ValueKind != JsonValueKind.String
                || !error.TryGetProperty("message", out var messageElement)
                || messageElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            code = codeElement.GetString() ?? string.Empty;
            message = messageElement.GetString() ?? string.Empty;
            return code.Length > 0 && message.Length > 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
