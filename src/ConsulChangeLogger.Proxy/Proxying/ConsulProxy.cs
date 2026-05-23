using System.Security.Claims;
using System.Text;
using ConsulChangeLogger.Core;
using ConsulChangeLogger.Proxy.Audit;

namespace ConsulChangeLogger.Proxy.Proxying;

internal sealed class ConsulProxy
{
    private static readonly HashSet<string> HopByHopHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Connection",
        "Keep-Alive",
        "Proxy-Authenticate",
        "Proxy-Authorization",
        "TE",
        "Trailer",
        "Transfer-Encoding",
        "Upgrade"
    };

    private readonly HttpContext context;
    private readonly AuditOptions options;
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ReadCache readCache;
    private readonly AuditSink auditSink;

    public ConsulProxy(
        HttpContext context,
        AuditOptions options,
        IHttpClientFactory httpClientFactory,
        ReadCache readCache,
        AuditSink auditSink)
    {
        this.context = context;
        this.options = options;
        this.httpClientFactory = httpClientFactory;
        this.readCache = readCache;
        this.auditSink = auditSink;
    }

    public async Task HandleAsync()
    {
        var requestBodyBytes = await ReadRequestBodyAsync();
        var requestBody = AuditHelpers.Truncate(Encoding.UTF8.GetString(requestBodyBytes), options.MaxBodyBytes);
        using var upstreamRequest = BuildUpstreamRequest(requestBodyBytes);
        using var upstreamResponse = await httpClientFactory
            .CreateClient("consul")
            .SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

        var responseBodyBytes = await upstreamResponse.Content.ReadAsByteArrayAsync(context.RequestAborted);
        await WriteDownstreamResponseAsync(upstreamResponse, responseBodyBytes);
        await CaptureAuditAsync(requestBody, upstreamResponse, responseBodyBytes);
    }

    private async Task<byte[]> ReadRequestBodyAsync()
    {
        if (context.Request.ContentLength is null or 0)
        {
            return [];
        }

        using var memory = new MemoryStream();
        await context.Request.Body.CopyToAsync(memory, context.RequestAborted);
        return memory.ToArray();
    }

    private HttpRequestMessage BuildUpstreamRequest(byte[] requestBodyBytes)
    {
        var target = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
        var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);

        foreach (var header in context.Request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key) || header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }

        if (requestBodyBytes.Length > 0)
        {
            request.Content = new ByteArrayContent(requestBodyBytes);
            foreach (var header in context.Request.Headers)
            {
                request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }
        }

        return request;
    }

    private async Task WriteDownstreamResponseAsync(HttpResponseMessage upstreamResponse, byte[] responseBodyBytes)
    {
        context.Response.StatusCode = (int)upstreamResponse.StatusCode;

        foreach (var header in upstreamResponse.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        foreach (var header in upstreamResponse.Content.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key) && !header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        context.Response.Headers.ContentLength = responseBodyBytes.Length;
        await context.Response.Body.WriteAsync(responseBodyBytes, context.RequestAborted);
    }

    private async Task CaptureAuditAsync(string requestBody, HttpResponseMessage upstreamResponse, byte[] responseBodyBytes)
    {
        var sourcePath = context.Request.Path + context.Request.QueryString;
        if (!AuditHelpers.IsKvPath(sourcePath))
        {
            return;
        }

        var action = AuditHelpers.KvAction(context.Request.Method);
        if (action == "kv_other")
        {
            return;
        }

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var eventId = Guid.NewGuid().ToString("N");
        var requestId = context.Request.Headers.TryGetValue("X-Request-ID", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : eventId;
        var kvKey = AuditHelpers.KvKeyFromPath(sourcePath);
        var clientIp = context.Connection.RemoteIpAddress?.ToString();
        var userEmail = context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.Identity?.Name;
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var identity = AuditHelpers.ReadIdentity(clientIp, userAgent, kvKey, userEmail);
        var responseBody = AuditHelpers.Truncate(Encoding.UTF8.GetString(responseBodyBytes), options.MaxBodyBytes);
        var responseCode = (int)upstreamResponse.StatusCode;

        if (action == "kv_read" && AuditHelpers.IsSuccess(responseCode))
        {
            var oldValue = AuditHelpers.ExtractReadValue(sourcePath, responseBody);
            readCache.Store(identity, oldValue, timestamp, requestId);
            return;
        }

        if (action is not ("kv_write" or "kv_delete"))
        {
            return;
        }

        var read = readCache.Get(identity);
        var auditEvent = new AuditEvent
        {
            Timestamp = timestamp,
            EventId = eventId,
            Action = action,
            KvKey = kvKey,
            OldValue = read?.Value,
            OldValueSeenAt = read?.SeenAt,
            OldValueReadRequestId = read?.RequestId,
            NewValue = action == "kv_write" ? requestBody : null,
            DeleteConfirmed = action == "kv_delete",
            Success = AuditHelpers.IsSuccess(responseCode),
            ResponseCode = responseCode,
            ClientIp = clientIp,
            UserEmail = userEmail,
            UserAgent = userAgent,
            RequestId = requestId,
            SourcePath = sourcePath
        };

        await auditSink.SendAsync(auditEvent, context.RequestAborted);
    }
}
