using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConsulChangeLogger.Proxy;
using ConsulChangeLogger.Proxy.Configuration;
using Serilog;

namespace ConsulChangeLogger.Proxy.ChangeLogging;

internal sealed class ChangeRecordSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    private readonly IHttpClientFactory httpClientFactory;
    private readonly ChangeLogConfiguration options;
    private readonly ElasticsearchConfiguration elasticsearchConfiguration;
    private readonly ChangeRecordQueue changeRecordQueue;

    public ChangeRecordSink(
        IHttpClientFactory httpClientFactory,
        ChangeLogConfiguration options,
        ElasticsearchConfiguration elasticsearchConfiguration,
        ChangeRecordQueue changeRecordQueue)
    {
        this.httpClientFactory = httpClientFactory;
        this.options = options;
        this.elasticsearchConfiguration = elasticsearchConfiguration;
        this.changeRecordQueue = changeRecordQueue;
    }

    public async Task WaitForElasticsearchAsync(CancellationToken cancellationToken)
    {
        Log.Information("Waiting for Elasticsearch availability at {ElasticsearchUrl}", elasticsearchConfiguration.Url);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var response = await httpClientFactory.CreateClient("elasticsearch").GetAsync("/", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    Log.Information("Elasticsearch is reachable at {ElasticsearchUrl}", elasticsearchConfiguration.Url);
                    return;
                }

                Log.Warning(
                    "Elasticsearch health probe returned status {StatusCode} for {ElasticsearchUrl}",
                    (int)response.StatusCode,
                    elasticsearchConfiguration.Url);
            }
            catch (HttpRequestException ex)
            {                              
                Log.Error(ex, "Failed to connect to Elasticsearch at {ElasticsearchUrl}", elasticsearchConfiguration.Url);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Warning("Elasticsearch health probe timed out for {ElasticsearchUrl}", elasticsearchConfiguration.Url);
            }

            Log.Information("Waiting for Elasticsearch");
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    public async Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        Log.Information(
            "Ensuring Elasticsearch index {IndexName} exists on {ElasticsearchUrl}",
            elasticsearchConfiguration.Index,
            elasticsearchConfiguration.Url);

        var properties = new Dictionary<string, object>
        {
            ["@timestamp"] = new { type = "date" },
            ["event_id"] = new { type = "keyword" },
            ["action"] = new { type = "keyword" },
            ["kv_key"] = new { type = "keyword" },
            ["is_folder"] = new { type = "boolean" },
            ["old_value"] = new { type = "text" },
            ["old_value_observed_at"] = new { type = "date" },
            ["new_value"] = new { type = "text" },
            ["new_value_json_error"] = new { type = "text" },
            ["is_create"] = new { type = "boolean" },
            ["is_update"] = new { type = "boolean" },
            ["is_delete"] = new { type = "boolean" },
            ["is_success"] = new { type = "boolean" },
            ["response_status_code"] = new { type = "integer" },
            ["client_ip"] = new { type = "ip" },
            ["user_email"] = new { type = "keyword" },
            ["user_agent"] = new { type = "keyword" },
            ["request_id"] = new { type = "keyword" },
            ["source"] = new { type = "keyword" }
        };

        var mapping = new
        {
            mappings = new
            {
                properties
            }
        };

        using var content = JsonContent(mapping);
        using var response = await httpClientFactory
            .CreateClient("elasticsearch")
            .PutAsync($"/{elasticsearchConfiguration.Index}", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == HttpStatusCode.BadRequest && IsIndexAlreadyExists(body))
            {
                Log.Information("Elasticsearch index {IndexName} already exists", elasticsearchConfiguration.Index);
                return;
            }

            Log.Error(
                "Failed to ensure Elasticsearch index {IndexName}. StatusCode={StatusCode} Body={Body}",
                elasticsearchConfiguration.Index,
                (int)response.StatusCode,
                body);
            response.EnsureSuccessStatusCode();
        }

        var mappingUpdate = new
        {
            properties
        };

        using var mappingContent = JsonContent(mappingUpdate);
        using var mappingResponse = await httpClientFactory
            .CreateClient("elasticsearch")
            .PutAsync($"/{elasticsearchConfiguration.Index}/_mapping", mappingContent, cancellationToken);

        if (!mappingResponse.IsSuccessStatusCode)
        {
            var body = await mappingResponse.Content.ReadAsStringAsync(cancellationToken);
            Log.Error(
                "Failed to update Elasticsearch mapping for index {IndexName}. StatusCode={StatusCode} Body={Body}",
                elasticsearchConfiguration.Index,
                (int)mappingResponse.StatusCode,
                body);
            mappingResponse.EnsureSuccessStatusCode();
        }

        Log.Information("Elasticsearch mapping for index {IndexName} is up to date", elasticsearchConfiguration.Index);
        Log.Information("Elasticsearch index {IndexName} is ready", elasticsearchConfiguration.Index);
    }

    private static bool IsIndexAlreadyExists(string body)
    {
        try
        {
            var node = JsonNode.Parse(body);
            return node?["error"]?["type"]?.GetValue<string>() == "resource_already_exists_exception";
        }
        catch (JsonException)
        {
            return false;
        }
    }

    public async Task SendAsync(ChangeRecord changeRecord, CancellationToken cancellationToken)
    {
        var eventJson = JsonSerializer.Serialize(changeRecord, JsonOptions);
        Log.Debug(
            "Preparing change record EventId={EventId} Action={Action} Key={Key} Success={Success} RequestId={RequestId}",
            changeRecord.EventId,
            changeRecord.Action,
            changeRecord.KvKey,
            changeRecord.IsSuccess,
            changeRecord.RequestId);
        Log.Debug("Change record JSON: {ChangeRecordJson}", eventJson);

        ChangeRecordOutbox.DeleteExpiredDailyDirectories(
            options.OutboxPath,
            options.RetentionDays,
            DateTimeOffset.UtcNow);

        var outboxPath = ChangeRecordOutbox.BuildPath(
            options.OutboxPath,
            changeRecord.EventId,
            ReadTimestamp(changeRecord.Timestamp));

        Directory.CreateDirectory(Path.GetDirectoryName(outboxPath)!);
        await File.WriteAllTextAsync(outboxPath, eventJson, Encoding.UTF8, cancellationToken);
        Log.Information(
            "Persisted change record EventId={EventId} to outbox {OutboxPath}",
            changeRecord.EventId,
            outboxPath);
        await changeRecordQueue.EnqueueAsync(outboxPath, cancellationToken);
    }

    private static DateTimeOffset ReadTimestamp(string timestamp) =>
        DateTimeOffset.TryParse(timestamp, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

    private static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");
}
