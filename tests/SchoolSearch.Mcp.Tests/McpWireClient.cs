using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace SchoolSearch.Mcp.Tests;

/// <summary>
/// Speaks MCP over streamable HTTP with raw JSON-RPC 2.0 (no SDK client), so the tests check the
/// wire contract an agent sees. Posts to /mcp, carries Mcp-Session-Id, and accepts a JSON or an SSE reply.
/// </summary>
internal sealed class McpWireClient : IDisposable
{
    public const string RequestedProtocolVersion = "2025-06-18";

    private readonly HttpClient _http;
    private string? _negotiatedVersion;
    private int _nextId = 1;

    private McpWireClient(HttpClient http)
    {
        _http = http;
    }

    public string? SessionId { get; private set; }

    public JsonElement InitializeResult { get; private set; }

    /// <summary>Runs initialize, keeps the session id, and sends notifications/initialized.</summary>
    public static async Task<McpWireClient> ConnectAsync(HttpClient http)
    {
        var client = new McpWireClient(http);
        client.InitializeResult = await client.RequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = RequestedProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject { ["name"] = "school-search-mcp-tests", ["version"] = "1.0.0" },
        });
        client._negotiatedVersion = client.InitializeResult.GetProperty("protocolVersion").GetString();
        await client.NotifyAsync("notifications/initialized");
        return client;
    }

    /// <summary>Sends a request and returns its "result"; a JSON-RPC "error" throws.</summary>
    public async Task<JsonElement> RequestAsync(string method, JsonObject? parameters = null)
    {
        var id = _nextId++;
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (parameters is not null)
        {
            message["params"] = parameters;
        }

        using var response = await PostAsync(message);
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"POST /mcp {method} returned {(int)response.StatusCode}: {body}");
        }

        var envelope = McpResponseParser.FindResponse(response.Content.Headers.ContentType?.MediaType, body, id);
        if (envelope.TryGetProperty("error", out var error))
        {
            throw new InvalidOperationException($"JSON-RPC error for {method}: {error.GetRawText()}");
        }

        return envelope.GetProperty("result");
    }

    /// <summary>Sends a notification (no id). The server answers 202 Accepted.</summary>
    public async Task NotifyAsync(string method)
    {
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };
        using var response = await PostAsync(message);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Notification {method} returned {(int)response.StatusCode}.");
        }
    }

    /// <summary>tools/call; a tool failure arrives as isError=true in the result, not as a JSON-RPC error.</summary>
    public async Task<ToolReply> CallToolAsync(string name, JsonObject arguments)
    {
        var result = await RequestAsync("tools/call", new JsonObject
        {
            ["name"] = name,
            ["arguments"] = arguments,
        });

        var isError = result.TryGetProperty("isError", out var flag) && flag.ValueKind == JsonValueKind.True;
        var text = new StringBuilder();
        if (result.TryGetProperty("content", out var content))
        {
            foreach (var part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out var type)
                    && type.GetString() == "text"
                    && part.TryGetProperty("text", out var value))
                {
                    text.Append(value.GetString());
                }
            }
        }

        return new ToolReply(isError, text.ToString());
    }

    public async Task<JsonElement[]> ListToolsAsync()
    {
        var result = await RequestAsync("tools/list");
        return result.GetProperty("tools").EnumerateArray().ToArray();
    }

    private async Task<HttpResponseMessage> PostAsync(JsonObject message)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
        {
            Content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
        if (SessionId is not null)
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", SessionId);
        }

        if (_negotiatedVersion is not null)
        {
            request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", _negotiatedVersion);
        }

        var response = await _http.SendAsync(request);
        if (SessionId is null && response.Headers.TryGetValues("Mcp-Session-Id", out var sessions))
        {
            SessionId = sessions.FirstOrDefault();
        }

        return response;
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>A tool result: the concatenated text content and the isError flag.</summary>
internal sealed record ToolReply(bool IsError, string Text);

/// <summary>
/// Extracts the JSON-RPC response with a given id from either a plain JSON body or a
/// text/event-stream body ("event: message" / "data: {json}" blocks, blank line between events).
/// </summary>
internal static class McpResponseParser
{
    public static JsonElement FindResponse(string? mediaType, string body, int id)
    {
        if (string.Equals(mediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var data in ReadSseData(body))
            {
                using var document = JsonDocument.Parse(data);
                if (IsResponseTo(document.RootElement, id))
                {
                    return document.RootElement.Clone();
                }
            }

            throw new InvalidOperationException($"No SSE event answered request {id}: {body}");
        }

        using var single = JsonDocument.Parse(body);
        if (IsResponseTo(single.RootElement, id))
        {
            return single.RootElement.Clone();
        }

        throw new InvalidOperationException($"JSON body does not answer request {id}: {body}");
    }

    /// <summary>Yields the joined data lines of each SSE event.</summary>
    public static IEnumerable<string> ReadSseData(string body)
    {
        var current = new StringBuilder();
        var hasData = false;
        foreach (var raw in body.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                if (hasData)
                {
                    yield return current.ToString();
                    current.Clear();
                    hasData = false;
                }

                continue;
            }

            if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (hasData)
                {
                    current.Append('\n');
                }

                current.Append(line.Substring(5).TrimStart());
                hasData = true;
            }
        }

        if (hasData)
        {
            yield return current.ToString();
        }
    }

    private static bool IsResponseTo(JsonElement element, int id) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty("id", out var idElement)
        && idElement.ValueKind == JsonValueKind.Number
        && idElement.GetInt32() == id
        && (element.TryGetProperty("result", out _) || element.TryGetProperty("error", out _));
}
