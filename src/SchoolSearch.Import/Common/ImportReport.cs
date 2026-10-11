namespace SchoolSearch.Import;

/// <summary>
/// Outcome of one loader run. A rejected row is counted and explained, never silently dropped.
/// A run that cannot be trusted at all throws instead of returning a report.
/// </summary>
/// <param name="RowsRead">Data rows read from the input (headers excluded).</param>
/// <param name="RowsWritten">Rows inserted or updated in the catalog.</param>
/// <param name="RowsRejected">Rows not written, for example an unmatched join key.</param>
/// <param name="RejectionReasons">One line per rejected row or group of rows.</param>
public sealed record ImportReport(
    int RowsRead,
    int RowsWritten,
    int RowsRejected,
    IReadOnlyList<string> RejectionReasons);
