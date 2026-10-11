using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SchoolSearch.Billing.Auth;

/// <summary>
/// Token validation against authservice (ADR-0015): RS256 access tokens checked through the discovery document and JWKS,
/// issuer and audience pinned, ClockSkew zero, inbound claim mapping off so the wire names stay as issued.
/// When Auth is not configured, JwtBearer is not registered and every auth-gated policy answers 503 auth_unavailable.
/// </summary>
public static class AuthServiceExtensions
{
    private const string DiscoveryPath = "/.well-known/openid-configuration";

    public static IServiceCollection AddAuthServiceAuthentication(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.Configure<AuthServiceOptions>(configuration.GetSection(AuthServiceOptions.SectionName));
        var auth = configuration.GetSection(AuthServiceOptions.SectionName).Get<AuthServiceOptions>() ?? new AuthServiceOptions();

        if (auth.IsConfigured)
        {
            var metadataAddress = auth.Authority!.TrimEnd('/') + DiscoveryPath;

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.MapInboundClaims = false;
                    options.SaveToken = false;
                    options.RequireHttpsMetadata = !environment.IsDevelopment();
                    options.MetadataAddress = metadataAddress;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = auth.Issuer,
                        ValidateAudience = true,
                        ValidAudience = auth.Audience,
                        ValidateIssuerSigningKey = true,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.Zero,
                        ValidAlgorithms = new[] { SecurityAlgorithms.RsaSha256 },
                        RoleClaimType = AuthClaims.ShortRoleClaim,
                    };
                });
        }
        else
        {
            services.AddAuthentication();
        }

        services.AddAuthorization(options =>
        {
            options.AddPolicy(AuthPolicies.Authenticated, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new AuthServiceAvailableRequirement())
                .RequireAssertion(context => AuthClaims.Subject(context.User) is not null));

            options.AddPolicy(AuthPolicies.VerifiedUser, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new AuthServiceAvailableRequirement(), new VerifiedEmailRequirement())
                .RequireAssertion(context => AuthClaims.Subject(context.User) is not null));

            options.AddPolicy(AuthPolicies.Moderator, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new AuthServiceAvailableRequirement(), new ModeratorRequirement())
                .RequireAssertion(context => AuthClaims.Subject(context.User) is not null));
        });

        // Registered after AddAuthorization so this result handler replaces the framework default.
        services.AddSingleton<IAuthorizationHandler, AuthServiceAvailableHandler>();
        services.AddSingleton<IAuthorizationHandler, VerifiedEmailHandler>();
        services.AddSingleton<IAuthorizationHandler, ModeratorHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, BillingAuthorizationResultHandler>();

        services.AddSingleton<SubjectHasher>();
        services.AddHostedService<AuthIntegrationReporter>();

        return services;
    }
}
