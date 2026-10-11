using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using SchoolSearch.Api.Errors;

namespace SchoolSearch.Api.Security;

/// <summary>
/// The configured ADMIN_API_KEY, held as a SHA-256 digest so comparisons are always over fixed-length
/// values. Null digest means the admin surface is disabled.
/// </summary>
public sealed class AdminKeyCredential
{
    public const string ConfigurationKey = "ADMIN_API_KEY";
    public const string HeaderName = "X-Admin-Key";

    private AdminKeyCredential(byte[]? digest) => Digest = digest;

    public byte[]? Digest { get; }

    public bool IsConfigured => Digest is not null;

    public static AdminKeyCredential FromSecret(string? secret) =>
        string.IsNullOrWhiteSpace(secret)
            ? new AdminKeyCredential(null)
            : new AdminKeyCredential(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}

/// <summary>
/// Gates the admin group. When ADMIN_API_KEY is unset the group answers 404 exactly like an unknown route,
/// so its existence is not advertised. When set, the presented X-Admin-Key is compared in constant time
/// over SHA-256 digests of both values; a missing or wrong key is a uniform 401.
/// </summary>
public sealed class AdminKeyFilter(AdminKeyCredential credential) : IEndpointFilter
{
    public ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!credential.IsConfigured)
        {
            return ValueTask.FromResult<object?>(ApiErrorWriter.Result(StatusCodes.Status404NotFound, "not_found", "Resource not found."));
        }

        var presented = context.HttpContext.Request.Headers[AdminKeyCredential.HeaderName].ToString();
        var presentedDigest = SHA256.HashData(Encoding.UTF8.GetBytes(presented));

        var matches = CryptographicOperations.FixedTimeEquals(presentedDigest, credential.Digest!);
        if (!matches)
        {
            return ValueTask.FromResult<object?>(ApiErrorWriter.Result(StatusCodes.Status401Unauthorized, "unauthorized", "Unauthorized."));
        }

        return next(context);
    }
}
