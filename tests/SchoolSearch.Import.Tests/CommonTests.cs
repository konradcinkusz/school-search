using Microsoft.EntityFrameworkCore;
using SchoolSearch.Catalog.Data;
using SchoolSearch.Import.Sio;
using Xunit;

namespace SchoolSearch.Import.Tests;

public sealed class CommonTests
{
    [Fact]
    public async Task Schema_guard_is_a_no_op_on_InMemory()
    {
        await using var db = TestDb.Create();

        Assert.True(await SchemaGuard.IsCompatibleAsync(db, CancellationToken.None));
    }

    [Fact]
    public void Personal_data_guard_names_the_offending_header_and_accepts_clean_headers()
    {
        Assert.Throws<InvalidOperationException>(() =>
            PersonalDataGuard.EnsureNoPersonalColumns(["idPodmiot", "Imie"], "test"));

        PersonalDataGuard.EnsureNoPersonalColumns(["idPodmiot", "Nazwa"], "test");
    }

    [Fact]
    public void Column_map_override_with_an_unknown_property_is_rejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"map-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ \"NotAColumnInTheMap\": \"x\" }");
        try
        {
            Assert.ThrowsAny<Exception>(() => ColumnMapLoader.Load<SioColumnMap>(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Column_map_without_a_file_gives_the_built_in_defaults()
    {
        Assert.NotNull(ColumnMapLoader.Load<SioColumnMap>(null));
    }

    [Fact]
    public async Task Import_run_recorder_leaves_FinishedAt_empty_until_completed()
    {
        await using var db = TestDb.Create();
        var recorder = new ImportRunRecorder(db, TimeProvider.System);

        var run = await recorder.StartAsync("sio", CancellationToken.None);
        Assert.Null(run.FinishedAt);

        await recorder.CompleteAsync(run, new ImportReport(3, 2, 1, ["row 3: test"]), CancellationToken.None);

        var stored = await db.ImportRuns.AsNoTracking().SingleAsync(r => r.Id == run.Id);
        Assert.NotNull(stored.FinishedAt);
        Assert.Equal(3, stored.RowsRead);
        Assert.Equal(2, stored.RowsWritten);
        Assert.Equal(1, stored.RowsRejected);
        Assert.Equal("sio", stored.Kind);
    }
}
