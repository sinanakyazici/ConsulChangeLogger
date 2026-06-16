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
        Log.Information(
            "Change record dispatch worker starting. OutboxPath={OutboxPath} RetentionDays={RetentionDays} ElasticsearchIndex={IndexName}",
            options.OutboxPath,
            options.RetentionDays,
            elasticsearchConfiguration.Index);

        ChangeRecordOutbox.DeleteExpiredDailyDirectories(
            options.OutboxPath,
            options.RetentionDays,
            DateTimeOffset.UtcNow);

        foreach (var path in ChangeRecordOutbox.EnumeratePendingFiles(options.OutboxPath))
        {
            Log.Information("Re-queueing pending outbox file {OutboxPath} on startup", path);
            await changeRecordQueue.EnqueueAsync(path, stoppingToken);
        }

        await foreach (var path in changeRecordQueue.ReadAllAsync(stoppingToken))
        {
            Log.Debug("Dequeued outbox file {OutboxPath} for Elasticsearch dispatch", path);
            await SendToElasticsearchAsync(path, stoppingToken);
        }
    }

    private async Task SendToElasticsearchAsync(string outboxPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(outboxPath))
        {
            Log.Warning("Outbox file {OutboxPath} disappeared before dispatch", outboxPath);
            return;
        }

        var eventJson = await File.ReadAllTextAsync(outboxPath, cancellationToken);
        var changeRecord = JsonSerializer.Deserialize<ChangeRecord>(eventJson, JsonOptions);
        if (changeRecord is null)
        {
            Log.Warning("Failed to deserialize outbox file {OutboxPath}; deleting corrupt file", outboxPath);
            File.Delete(outboxPath);
            return;
        }

        var documentId = string.IsNullOrWhiteSpace(changeRecord.EventId)
            ? changeRecord.RequestId
            : changeRecord.EventId;
        var attempt = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            attempt++;

            try
            {
                Log.Debug(
                    "Dispatching change record EventId={EventId} DocumentId={DocumentId} Attempt={Attempt} Index={IndexName}",
                    changeRecord.EventId,
                    documentId,
                    attempt,
                    elasticsearchConfiguration.Index);

                using var content = new StringContent(eventJson, Encoding.UTF8, "application/json");
                using var response = await httpClientFactory
                    .CreateClient("elasticsearch")
                    .PutAsync($"/{elasticsearchConfiguration.Index}/_doc/{Uri.EscapeDataString(documentId)}", content, cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken);
                    Log.Warning(
                        "Elasticsearch rejected change record EventId={EventId} StatusCode={StatusCode} Attempt={Attempt} Body={Body}",
                        changeRecord.EventId,
                        (int)response.StatusCode,
                        attempt,
                        body);
                }

                response.EnsureSuccessStatusCode();
                File.Delete(outboxPath);
                DeleteParentDirectoryIfEmpty(outboxPath);
                Log.Information(
                    "Dispatched change record EventId={EventId} Action={Action} Key={Key} Attempt={Attempt} and deleted outbox file {OutboxPath}",
                    changeRecord.EventId,
                    changeRecord.Action,
                    changeRecord.KvKey,
                    attempt,
                    outboxPath);
                return;
            }
            catch (HttpRequestException error)
            {
                Log.Error(
                    error,
                    "Failed to send change record EventId={EventId} to Elasticsearch on attempt {Attempt}",
                    changeRecord.EventId,
                    attempt);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Warning(
                    "Timed out sending change record EventId={EventId} to Elasticsearch on attempt {Attempt}",
                    changeRecord.EventId,
                    attempt);
            }

            Log.Debug(
                "Retrying change record EventId={EventId} in {RetryDelaySeconds} seconds",
                changeRecord.EventId,
                elasticsearchConfiguration.RetryDelaySeconds);
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
            Log.Debug("Deleted empty outbox directory {Directory}", directory);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
