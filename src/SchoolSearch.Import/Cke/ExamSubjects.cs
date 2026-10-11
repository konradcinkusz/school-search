namespace SchoolSearch.Import.Cke;

/// <summary>
/// The subject codes stored in ExamResult.Subject and NationalBaselineRow.Subject. Plain strings on
/// purpose: the Import project does not reference the Api scoring types.
/// </summary>
public static class ExamSubjects
{
    public const string Polish = "polish";
    public const string Mathematics = "mathematics";
    public const string ForeignLanguage = "foreign_language";

    public static IReadOnlyList<string> All { get; } = new[] { Polish, Mathematics, ForeignLanguage };
}
