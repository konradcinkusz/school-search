using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SchoolSearch.Api.Errors;

namespace SchoolSearch.Api.Security;

/// <summary>
/// Gates the admin group with the authservice bearer token. Without Auth:Authority the group answers 404 exactly
/// like an unknown route, so its existence is not advertised. A missing or invalid token is a uniform 401; a valid
/// token without an Admin or SuperAdmin role is 403. The token is read from the Authorization header only.
/// </summary>
public sealed class AdminAccessFilter(IOptions<AuthServiceOptions> options) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!options.Value.IsConfigured)
        {
            return ApiErrorWriter.Result(StatusCodes.Status404NotFound, "not_found", "Resource not found.");
        }

        var httpContext = context.HttpContext;
        var result = await httpContext.AuthenticateAsync();
        if (!result.Succeeded || result.Principal is null)
        {
            httpContext.Response.Headers["WWW-Authenticate"] = "Bearer";
            return ApiErrorWriter.Result(StatusCodes.Status401Unauthorized, "unauthorized", "Unauthorized.");
        }

        if (!AuthServiceAuthentication.IsAdmin(result.Principal))
        {
            return ApiErrorWriter.Result(StatusCodes.Status403Forbidden, "forbidden", "Forbidden.");
        }

        httpContext.User = result.Principal;
        return await next(context);
    }
}
