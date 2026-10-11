namespace SchoolSearch.Mcp.Options;

/// <summary>
/// Bound from the SchoolSearch section (env: SchoolSearch__...), the same section the Api uses.
/// </summary>
public sealed class McpOptions
{
    public const string SectionName = "SchoolSearch";

    /// <summary>
    /// Trust the Fly-Client-IP header. Only true behind the Fly edge (set in flyio/mcp.fly.toml). Without a proxy in
    /// front the header is client-supplied, and trusting it would let every caller pick their own rate-limit bucket.
    /// </summary>
    public bool TrustProxyClientIpHeader { get; set; }

    /// <summary>Requests per client IP per minute on the MCP endpoint.</summary>
    public int PerIpPerMinute { get; set; } = 60;
}
