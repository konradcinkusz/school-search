using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace SchoolSearch.Billing.Auth;

/// <summary>Succeeds only when authservice is configured. Without it every auth-gated policy fails (P8).</summary>
public sealed class AuthServiceAvailableRequirement : IAuthorizationRequirement { }

/// <summary>Succeeds when the token carries email_verified true.</summary>
public sealed class VerifiedEmailRequirement : IAuthorizationRequirement { }

/// <summary>Succeeds when the token carries role Admin or SuperAdmin.</summary>
public sealed class ModeratorRequirement : IAuthorizationRequirement { }

public sealed class AuthServiceAvailableHandler(IOptions<AuthServiceOptions> options)
    : AuthorizationHandler<AuthServiceAvailableRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AuthServiceAvailableRequirement requirement)
    {
        if (options.Value.IsConfigured)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public sealed class VerifiedEmailHandler : AuthorizationHandler<VerifiedEmailRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        VerifiedEmailRequirement requirement)
    {
        if (AuthClaims.IsEmailVerified(context.User))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public sealed class ModeratorHandler : AuthorizationHandler<ModeratorRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ModeratorRequirement requirement)
    {
        if (AuthClaims.HasAnyRole(context.User, AuthClaims.AdminRole, AuthClaims.SuperAdminRole))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
