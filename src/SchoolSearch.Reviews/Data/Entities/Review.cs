namespace SchoolSearch.Reviews.Data;

/// <summary>
/// A parent's opinion about one school for one school year. A reviewer's rating is that person's own opinion:
/// it is never aggregated into the school score. Only <see cref="ReviewStatuses.Published"/> reviews are public.
/// </summary>
public sealed class Review
{
    public Guid Id { get; set; }

    /// <summary>Slug of the school. No foreign key: the Api service owns schools (database per service).</summary>
    public string SchoolSlug { get; set; } = "";

    /// <summary>Hex SHA-256 of (pepper + authservice sub), 64 characters. No e-mail address and no raw sub is stored.</summary>
    public string AuthorHash { get; set; } = "";

    /// <summary>1..5.</summary>
    public int Rating { get; set; }

    /// <summary>Up to 120 characters.</summary>
    public string Title { get; set; } = "";

    /// <summary>40..4000 characters, plain text.</summary>
    public string Body { get; set; } = "";

    /// <summary>First year of the school year, for example 2025 for "2025/26".</summary>
    public int SchoolYearFrom { get; set; }

    /// <summary>One of <see cref="ReviewStatuses"/>. New reviews start pending (pre-moderation in v1).</summary>
    public string Status { get; set; } = ReviewStatuses.Pending;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? PublishedAt { get; set; }

    /// <summary>The terms version the author accepted when writing the review.</summary>
    public string TermsVersion { get; set; } = "";

    /// <summary>The right of reply, at most one per review.</summary>
    public ReviewReply? Reply { get; set; }
}
