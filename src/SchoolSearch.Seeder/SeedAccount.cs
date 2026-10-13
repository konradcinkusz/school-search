using System.Collections.Generic;

namespace SchoolSearch.Seeder;

/// <summary>One account the seeder knows about. <see cref="Key"/> is the subject key used in the dev seed bodies.</summary>
public sealed record SeedAccount(string Key, string Email, string Role)
{
    public const string UserRole = "User";
    public const string AdminRole = "Admin";
    public const string SuperAdminRole = "SuperAdmin";

    /// <summary>
    /// The test accounts the seeder registers, or signs in to when they exist. Their role is the role the seeder
    /// ensures through the admin API; <see cref="UserRole"/> needs no assignment because registration grants it.
    /// The admin account is not in this list: its email comes from SEED_ADMIN_EMAIL and it is created by authservice.
    /// </summary>
    public static IReadOnlyList<SeedAccount> TestAccounts { get; } =
    [
        new SeedAccount("moderator", "moderator@school-search.test", AdminRole),
        new SeedAccount("parent", "parent@school-search.test", UserRole),
        new SeedAccount("parent2", "parent2@school-search.test", UserRole),
        new SeedAccount("owner", "owner@school-search.test", UserRole),
    ];
}
