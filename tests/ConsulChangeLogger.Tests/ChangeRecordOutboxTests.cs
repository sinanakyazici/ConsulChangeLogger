using ConsulChangeLogger.Proxy.ChangeLogging;

namespace ConsulChangeLogger.Tests;

public sealed class ChangeRecordOutboxTests : IDisposable
{
    private readonly string rootPath = Path.Combine(Path.GetTempPath(), "consul-change-logger-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void BuildPath_UsesDailyDirectory_AndEscapesEventId()
    {
        var timestamp = new DateTimeOffset(2026, 6, 16, 10, 3, 13, TimeSpan.Zero);

        var path = ChangeRecordOutbox.BuildPath(rootPath, "evt/01", timestamp);

        Assert.EndsWith(Path.Combine("2026-06-16", "evt_2F01.json"), path, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnumeratePendingFiles_ReturnsFilesAcrossSubdirectories()
    {
        var first = Path.Combine(rootPath, "2026-06-15", "a.json");
        var second = Path.Combine(rootPath, "2026-06-16", "b.json");
        Directory.CreateDirectory(Path.GetDirectoryName(first)!);
        Directory.CreateDirectory(Path.GetDirectoryName(second)!);
        File.WriteAllText(first, "{}");
        File.WriteAllText(second, "{}");
        File.SetCreationTimeUtc(first, new DateTime(2026, 6, 16, 10, 0, 0, DateTimeKind.Utc));
        File.SetCreationTimeUtc(second, new DateTime(2026, 6, 16, 10, 0, 1, DateTimeKind.Utc));

        var files = ChangeRecordOutbox.EnumeratePendingFiles(rootPath).ToArray();

        Assert.Equal(new[] { first, second }, files);
    }

    [Fact]
    public void DeleteExpiredDailyDirectories_RemovesOnlyDirectoriesOutsideRetentionWindow()
    {
        Directory.CreateDirectory(Path.Combine(rootPath, "2026-06-10"));
        Directory.CreateDirectory(Path.Combine(rootPath, "2026-06-15"));
        Directory.CreateDirectory(Path.Combine(rootPath, "misc"));

        ChangeRecordOutbox.DeleteExpiredDailyDirectories(rootPath, retentionDays: 3, new DateTimeOffset(2026, 6, 16, 12, 0, 0, TimeSpan.Zero));

        Assert.False(Directory.Exists(Path.Combine(rootPath, "2026-06-10")));
        Assert.True(Directory.Exists(Path.Combine(rootPath, "2026-06-15")));
        Assert.True(Directory.Exists(Path.Combine(rootPath, "misc")));
    }

    public void Dispose()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }
}
