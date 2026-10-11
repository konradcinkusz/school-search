namespace SchoolSearch.Reviews.Auth;

/// <summary>Authorization policy names. Endpoints reference these constants, never string literals.</summary>
public static class AuthPolicies
{
    /// <summary>A valid authservice token with a subject. No e-mail requirement.</summary>
    public const string Authenticated = "Authenticated";

    /// <summary>Authenticated, with email_verified true. Required to write reviews and replies.</summary>
    public const string VerifiedUser = "VerifiedUser";

    /// <summary>Authenticated with role Admin or SuperAdmin. The moderation endpoints (E1b) use this policy.</summary>
    public const string Moderator = "Moderator";
}
