using System.Globalization;
using Serilog;

namespace ConsulChangeLogger.Proxy.ChangeLogging;

internal static class ChangeRecordOutbox
{
    private const string DateDirectoryFormat = "yyyy-MM-dd";

    public static string BuildPath(string rootPath, string eventId, DateTimeOffset timestamp)
    {
        var dailyDirectory = Path.Combine(rootPath, timestamp.UtcDateTime.ToString(DateDirectoryFormat, CultureInfo.InvariantCulture));
        var safeName = Uri.EscapeDataString(eventId).Replace("%", "_", StringComparison.Ordinal);
        return Path.Combine(dailyDirectory, $"{safeName}.json");
    }

    public static IEnumerable<string> EnumeratePendingFiles(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(rootPath, "*.json", SearchOption.AllDirectories)
            .OrderBy(File.GetCreationTimeUtc);
    }

    public static void DeleteExpiredDailyDirectories(string rootPath, int retentionDays, DateTimeOffset now)
    {
        if (!Directory.Exists(rootPath))
        {
            return;
        }

        var cutoffDate = now.UtcDateTime.Date.AddDays(1 - retentionDays);
        foreach (var directory in Directory.EnumerateDirectories(rootPath))
        {
            var directoryName = Path.GetFileName(directory);
            if (!DateTime.TryParseExact(
                    directoryName,
                    DateDirectoryFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal,
                    out var directoryDate))
            {
                continue;
            }

            if (directoryDate.Date < cutoffDate)
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (IOException error)
                {
                    Log.Warning(error, "Failed to delete expired change record outbox directory {OutboxDirectory}", directory);
                }
                catch (UnauthorizedAccessException error)
                {
                    Log.Warning(error, "Failed to delete expired change record outbox directory {OutboxDirectory}", directory);
                }
            }
        }
    }
}
