namespace SchoolSearch.Evals.Tests;

/// <summary>One named check inside a grade. <see cref="Detail"/> may name the offending value; it never holds a secret.</summary>
public sealed record GradeCheck(string Name, bool Pass, string Detail);

/// <summary>A grade: passes only when every check passes.</summary>
public sealed record GradeResult(bool Pass, IReadOnlyList<GradeCheck> Checks);

/// <summary>Helpers that build grades. A grade is always the conjunction of its checks.</summary>
public static class EvalGrade
{
    public static GradeCheck Check(string name, bool pass, string detail = "") => new(name, pass, detail);

    public static GradeResult Of(IEnumerable<GradeCheck> checks)
    {
        var list = checks.ToList();
        return new GradeResult(list.All(check => check.Pass), list);
    }

    /// <summary>Names of the failed checks, for assertion messages.</summary>
    public static string Describe(GradeResult grade) =>
        grade.Pass
            ? "pass"
            : string.Join("; ", grade.Checks.Where(check => !check.Pass).Select(check => $"{check.Name} [{check.Detail}]"));
}
