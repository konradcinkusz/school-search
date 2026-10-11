using System.Text;
using System.Text.RegularExpressions;

namespace SchoolSearch.Billing.Leads;

/// <summary>Shape checks for parent enquiries. Deliverability of the address is not checked (no e-mail is sent in v1).</summary>
internal static class LeadValidation
{
    /// <summary>Structural check only: one '@', no whitespace, a dot in the domain.</summary>
    private static readonly Regex EmailShape = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public static LeadValidated Validate(LeadEnquiryRequest body)
    {
        var email = Clean(body.Email);
        if (email is null || email.Length > LeadRules.MaxEmailLength || !EmailShape.IsMatch(email))
        {
            return Invalid("email is not a valid address.");
        }

        var message = Clean(body.Message);
        if (message is null || message.Length < LeadRules.MinMessageLength || message.Length > LeadRules.MaxMessageLength)
        {
            return Invalid($"message must be between {LeadRules.MinMessageLength} and {LeadRules.MaxMessageLength} characters.");
        }

        return new LeadValidated(new LeadEnquiry(email, message), null);
    }

    /// <summary>Removes control characters (except tab, CR and LF), trims the edges, returns null for empty text.</summary>
    public static string? Clean(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        var builder = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (!char.IsControl(c) || c is '\t' or '\n' or '\r')
            {
                builder.Append(c);
            }
        }

        var text = builder.ToString().Trim();
        return text.Length == 0 ? null : text;
    }

    private static LeadValidated Invalid(string error) => new(null, error);
}
