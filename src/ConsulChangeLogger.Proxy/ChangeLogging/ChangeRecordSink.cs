using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConsulChangeLogger.Core;
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
    private readonly ChangeLoggerOptions options;
    private readonly ChangeRecordQueue changeRecordQueue;

    public ChangeRecordSink(
        IHttpClientFactory httpClientFactory,
        ChangeLoggerOptions options,
        ChangeRecordQueue changeRecordQueue)
    {
        this.httpClientFactory = httpClientFactory;
        this.options = options;
        this.changeRecordQueue = changeRecordQueue;
    }

    public async Task WaitForElasticsearchAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var response = await httpClientFactory.CreateClient("elasticsearch").GetAsync("/", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            Log.Information("Waiting for Elasticsearch");
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    public async Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        var mapping = new
        {
            mappings = new
            {
                properties = new Dictionary<string, object>
                {
                    ["@timestamp"] = new { type = "date" },
                    ["event_id"] = new { type = "keyword" },
                    ["action"] = new { type = "keyword" },
                    ["kv_key"] = new { type = "keyword" },
                    ["old_value"] = new { type = "text" },
                    ["new_value"] = new { type = "text" },
                    ["delete_confirmed"] = new { type = "boolean" },
                    ["success"] = new { type = "boolean" },
                    ["response_code"] = new { type = "integer" },
                    ["client_ip"] = new { type = "ip" },
                    ["user_email"] = new { type = "keyword" },
                    ["user_agent"] = new { type = "keyword" },
                    ["request_id"] = new { type = "keyword" },
                    ["source"] = new { type = "keyword" }
                }
            }
        };

        using var content = JsonContent(mapping);
        using var response = await httpClientFactory
            .CreateClient("elasticsearch")
            .PutAsync($"/{options.ChangeRecordIndex}", content, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.StatusCode == HttpStatusCode.BadRequest && IsIndexAlreadyExists(body))
            {
                return;
            }

            response.EnsureSuccessStatusCode();
        }
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
        ChangeRecordOutbox.DeleteExpiredDailyDirectories(
            options.ChangeRecordOutboxPath,
            options.ChangeRecordRetentionDays,
            DateTimeOffset.UtcNow);

        var outboxPath = ChangeRecordOutbox.BuildPath(
            options.ChangeRecordOutboxPath,
            changeRecord.EventId,
            ReadTimestamp(changeRecord.Timestamp));

        Directory.CreateDirectory(Path.GetDirectoryName(outboxPath)!);
        await File.WriteAllTextAsync(outboxPath, eventJson, Encoding.UTF8, cancellationToken);
        await changeRecordQueue.EnqueueAsync(outboxPath, cancellationToken);
    }

    private static DateTimeOffset ReadTimestamp(string timestamp) =>
        DateTimeOffset.TryParse(timestamp, out var parsed)
            ? parsed
            : DateTimeOffset.UtcNow;

    private static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");
}
