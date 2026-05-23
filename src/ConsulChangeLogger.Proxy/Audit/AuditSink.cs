using System.Net;
using System.Text;
using System.Text.Json;
using ConsulChangeLogger.Core;
using Serilog;

namespace ConsulChangeLogger.Proxy.Audit;

internal sealed class AuditSink
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    private readonly IHttpClientFactory httpClientFactory;
    private readonly AuditOptions options;
    private readonly AuditQueue auditQueue;
    private readonly AuditEventLogger auditEventLogger;

    public AuditSink(
        IHttpClientFactory httpClientFactory,
        AuditOptions options,
        AuditQueue auditQueue,
        AuditEventLogger auditEventLogger)
    {
        this.httpClientFactory = httpClientFactory;
        this.options = options;
        this.auditQueue = auditQueue;
        this.auditEventLogger = auditEventLogger;
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
            .PutAsync($"/{options.AuditIndex}", content, cancellationToken);

        if (response.StatusCode != HttpStatusCode.BadRequest)
        {
            response.EnsureSuccessStatusCode();
        }
    }

    public async Task SendAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        var eventJson = JsonSerializer.Serialize(auditEvent, JsonOptions);
        var outboxPath = BuildOutboxPath(auditEvent.EventId);

        Directory.CreateDirectory(options.AuditOutboxPath);
        auditEventLogger.Write(eventJson);
        await File.WriteAllTextAsync(outboxPath, eventJson, Encoding.UTF8, cancellationToken);
        await auditQueue.EnqueueAsync(outboxPath, cancellationToken);
    }

    private string BuildOutboxPath(string requestId)
    {
        var safeName = Uri.EscapeDataString(requestId).Replace("%", "_", StringComparison.Ordinal);
        return Path.Combine(options.AuditOutboxPath, $"{safeName}.json");
    }

    private static StringContent JsonContent(object value) =>
        new(JsonSerializer.Serialize(value, JsonOptions), Encoding.UTF8, "application/json");
}
