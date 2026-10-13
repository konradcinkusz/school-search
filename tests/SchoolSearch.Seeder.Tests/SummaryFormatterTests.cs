using System.Collections.Generic;
using SchoolSearch.Seeder;

namespace SchoolSearch.Seeder.Tests;

public sealed class SummaryFormatterTests
{
    private const string Password = "generated-secret-42";

    [Fact]
    public void Password_is_hidden_when_it_was_not_generated()
    {
        string text = SummaryFormatter.Format(SeedAccount.TestAccounts, Password, passwordGenerated: false);

        Assert.DoesNotContain(Password, text);
        Assert.Contains("SEED_TEST_PASSWORD", text);
    }

    [Fact]
    public void Password_is_printed_with_a_per_run_note_when_generated()
    {
        string text = SummaryFormatter.Format(SeedAccount.TestAccounts, Password, passwordGenerated: true);

        Assert.Contains(Password, text);
        Assert.Contains("per-run local password", text);
    }

    [Fact]
    public void Table_lists_each_account_with_its_role()
    {
        var accounts = new List<SeedAccount>
        {
            new SeedAccount("admin", "admin@school-search.test", SeedAccount.SuperAdminRole),
        };
        accounts.AddRange(SeedAccount.TestAccounts);

        string text = SummaryFormatter.Format(accounts, Password, passwordGenerated: false);

        Assert.Contains("admin@school-search.test", text);
        Assert.Contains("SuperAdmin", text);
        Assert.Contains("moderator@school-search.test", text);
        Assert.Contains("owner@school-search.test", text);
        Assert.Contains("parent2@school-search.test", text);
    }
}
