using System;
using System.Collections.Generic;
using System.Text;

namespace SchoolSearch.Seeder;

/// <summary>
/// The final table. The password appears only when it was generated for this run (SEED_PASSWORD_GENERATED=true);
/// a password the operator set is never printed.
/// </summary>
public static class SummaryFormatter
{
    public static string Format(IReadOnlyList<SeedAccount> accounts, string password, bool passwordGenerated)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(password);

        int keyWidth = "Account".Length;
        int emailWidth = "Email".Length;
        foreach (SeedAccount account in accounts)
        {
            keyWidth = Math.Max(keyWidth, account.Key.Length);
            emailWidth = Math.Max(emailWidth, account.Email.Length);
        }

        var text = new StringBuilder();
        text.AppendLine("Seed summary (local authservice accounts, all synthetic):");
        text.AppendLine("  " + "Account".PadRight(keyWidth) + "  " + "Email".PadRight(emailWidth) + "  Role");
        foreach (SeedAccount account in accounts)
        {
            text.AppendLine("  " + account.Key.PadRight(keyWidth) + "  " + account.Email.PadRight(emailWidth) + "  " + account.Role);
        }

        if (passwordGenerated)
        {
            text.AppendLine();
            text.AppendLine("Password for every account above: " + password);
            text.AppendLine("This is a per-run local password, generated for this run only. It is not stored in any file.");
        }
        else
        {
            text.AppendLine();
            text.AppendLine("Password for every account above: the SEED_TEST_PASSWORD value (not printed).");
        }

        return text.ToString();
    }
}
