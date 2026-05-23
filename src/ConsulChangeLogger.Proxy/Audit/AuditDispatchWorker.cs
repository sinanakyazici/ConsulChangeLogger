using System.Text;
using System.Text.Json;
using ConsulChangeLogger.Core;
using Serilog;

namespace ConsulChangeLogger.Proxy.Audit;

internal sealed class AuditDispatchWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
    };

    private readonly AuditQueue auditQueue;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly AuditOptions options;

    public AuditDispatchWorker(AuditQueue auditQueue, IHttpClientFactory httpClientFactory, AuditOptions options)
    {
        this.auditQueue = auditQueue;
        this.httpClientFactory = httpClientFactory;
        this.options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Directory.CreateDirectory(options.AuditOutboxPath);
        foreach (var path in Directory.EnumerateFiles(options.AuditOutboxPath, "*.json").OrderBy(File.GetCreationTimeUtc))
        {
            await auditQueue.EnqueueAsync(path, stoppingToken);
        }

        await foreach (var path in auditQueue.ReadAllAsync(stoppingToken))
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
        var auditEvent = JsonSerializer.Deserialize<AuditEvent>(eventJson, JsonOptions);
        if (auditEvent is null)
        {
            File.Delete(outboxPath);
            return;
        }

        var documentId = string.IsNullOrWhiteSpace(auditEvent.EventId)
            ? auditEvent.RequestId
            : auditEvent.EventId;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var content = new StringContent(eventJson, Encoding.UTF8, "application/json");
                using var response = await httpClientFactory
                    .CreateClient("elasticsearch")
                    .PutAsync($"/{options.AuditIndex}/_doc/{Uri.EscapeDataString(documentId)}", content, cancellationToken);
                response.EnsureSuccessStatusCode();
                File.Delete(outboxPath);
                return;
            }
            catch (HttpRequestException error)
            {
                Log.Error(error, "Failed to send audit event to Elasticsearch");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Log.Warning("Failed to send audit event to Elasticsearch: timeout");
            }

            await Task.Delay(TimeSpan.FromSeconds(options.ElasticsearchRetryDelaySeconds), cancellationToken);
        }
    }
}
