using ConsulChangeLogger.Core;
using ConsulChangeLogger.Proxy.Configuration;
using Serilog;
using System.Text;
using System.Text.Json;

namespace ConsulChangeLogger.Proxy.ChangeLogging;

internal sealed class ChangeRecordDispatchWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    private readonly ChangeRecordQueue changeRecordQueue;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ChangeLogConfiguration options;
    private readonly ElasticsearchConfiguration elasticsearchConfiguration;

    public ChangeRecordDispatchWorker(ChangeRecordQueue changeRecordQueue, IHttpClientFactory httpClientFactory, ChangeLogConfiguration options, ElasticsearchConfiguration elasticsearchConfiguration)
    {
        this.changeRecordQueue = changeRecordQueue;
        this.httpClientFactory = httpClientFactory;
        this.options = options;
        this.elasticsearchConfiguration = elasticsearchConfiguration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(options.OutboxPath);
        ChangeRecordOutbox.DeleteExpiredDailyDirectories(
            options.OutboxPath,
            options.RetentionDays,
            DateTimeOffset.UtcNow);

        foreach (var path in ChangeRecordOutbox.EnumeratePendingFiles(options.OutboxPath))
        {
            await changeRecordQueue.EnqueueAsync(path, stoppingToken);
        }

        await foreach (var path in changeRecordQueue.ReadAllAsync(stoppingToken))
        {
            await SendToElasticsearchAsync(path, stoppingToken);
        }
    }

    private async Task SendToElasticsearchAsync(string outboxPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(outboxPath))
        {
            return;
        }

        var eventJson = await File.ReadAllTextAsync(outboxPath, cancellationToken);
        var changeRecord = JsonSerializer.Deserialize<ChangeRecord>(eventJson, JsonOptions);
        if (changeRecord is null)
        {
            File.Delete(outboxPath);
            return;
        }

        var documentId = string.IsNullOrWhiteSpace(changeRecord.EventId)
            ? changeRecord.RequestId
            : changeRecord.EventId;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var content = new StringContent(eventJson, Encoding.UTF8, "application/json");
                using var response = await httpClientFactory
                    .CreateClient("elasticsearch")
                    .PutAsync($"/{elasticsearchConfiguration.Index}/_doc/{Uri.EscapeDataString(documentId)}", content, cancellationToken);
                response.EnsureSuccessStatusCode();
                File.Delete(outboxPath);
                DeleteParentDirectoryIfEmpty(outboxPath);
                return;
            }
            catch (HttpRequestException error)
            {
                Log.Error(error, "Failed to send change record to Elasticsearch");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Warning("Failed to send change record to Elasticsearch: timeout");
            }

            await Task.Delay(TimeSpan.FromSeconds(elasticsearchConfiguration.RetryDelaySeconds), cancellationToken);
        }
    }

    private static void DeleteParentDirectoryIfEmpty(string outboxPath)
    {
        var directory = Path.GetDirectoryName(outboxPath);
        if (string.IsNullOrWhiteSpace(directory) || Directory.EnumerateFileSystemEntries(directory).Any())
        {
            return;
        }

        try
        {
            Directory.Delete(directory);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
