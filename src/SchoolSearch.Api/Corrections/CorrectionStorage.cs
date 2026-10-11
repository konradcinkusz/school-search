namespace SchoolSearch.Api.Corrections;

/// <summary>
/// How a correction's target is stored without a schema change. CorrectionRequest has no target-kind column, and school
/// and preschool identifiers come from the same SIO id space, so a preschool is stored as: Field prefixed with
/// "preschool." and SchoolId set to the negated preschool id. No school has a negative id, so a moderator view that
/// looks up schools by SchoolId can never attribute a preschool correction to a school. Only this class knows the encoding.
/// </summary>
internal static class CorrectionStorage
{
    internal const string PreschoolFieldPrefix = "preschool.";

    /// <summary>Field value stored for a correction. Schools keep the bare field code (existing rows stay valid).</summary>
    public static string StoredField(string kind, string field) =>
        kind == CorrectionTargetKinds.Preschool ? PreschoolFieldPrefix + field : field;

    /// <summary>SchoolId value stored for a correction target.</summary>
    public static long StoredTargetId(string kind, long id) =>
        kind == CorrectionTargetKinds.Preschool ? -id : id;

    public static bool IsPreschool(string storedField) =>
        storedField.StartsWith(PreschoolFieldPrefix, StringComparison.Ordinal);

    /// <summary>The field code as the public API returns it, without the storage prefix.</summary>
    public static string BareField(string storedField) =>
        IsPreschool(storedField) ? storedField[PreschoolFieldPrefix.Length..] : storedField;

    /// <summary>The kind the public API returns for a stored row.</summary>
    public static string KindOf(string storedField) =>
        IsPreschool(storedField) ? CorrectionTargetKinds.Preschool : CorrectionTargetKinds.School;

    /// <summary>The preschool id behind a stored, negated SchoolId.</summary>
    public static long PreschoolIdOf(long storedTargetId) => -storedTargetId;
}
