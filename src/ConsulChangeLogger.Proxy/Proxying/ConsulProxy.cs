using System.Security.Claims;
using System.Net.Sockets;
using System.Text;
using ConsulChangeLogger.Proxy;
using ConsulChangeLogger.Proxy.ChangeLogging;
using Serilog;

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
    private readonly IHttpClientFactory httpClientFactory;
    private readonly ReadCache readCache;
    private readonly ChangeRecordSink changeRecordSink;

    public ConsulProxy(
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        ReadCache readCache,
        ChangeRecordSink changeRecordSink)
    {
        this.context = context;
        this.httpClientFactory = httpClientFactory;
        this.readCache = readCache;
        this.changeRecordSink = changeRecordSink;
    }

    public async Task HandleAsync()
    {
        try
        {
            Log.Debug(
                "Proxying {Method} {Path}{Query} for {Username}",
                context.Request.Method,
                context.Request.Path,
                context.Request.QueryString,
                context.User.Identity?.Name);

            var requestBodyBytes = await ReadRequestBodyAsync();
            var requestBody = Encoding.UTF8.GetString(requestBodyBytes);
            using var upstreamRequest = BuildUpstreamRequest(requestBodyBytes);
            using var upstreamResponse = await httpClientFactory
                .CreateClient("consul")
                .SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

            var responseBodyBytes = await upstreamResponse.Content.ReadAsByteArrayAsync(context.RequestAborted);
            Log.Debug(
                "Upstream response {StatusCode} for {Method} {Path} requestBytes={RequestBytes} responseBytes={ResponseBytes}",
                (int)upstreamResponse.StatusCode,
                context.Request.Method,
                context.Request.Path,
                requestBodyBytes.Length,
                responseBodyBytes.Length);
            await WriteDownstreamResponseAsync(upstreamResponse, responseBodyBytes);
            await CaptureChangeRecordAsync(requestBody, upstreamResponse, responseBodyBytes);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            Log.Debug(
                "Request was canceled by client while proxying {Method} {Path}{Query} for {Username}",
                context.Request.Method,
                context.Request.Path,
                context.Request.QueryString,
                context.User.Identity?.Name);
        }
        catch (IOException ex) when (IsClientDisconnect(ex))
        {
            Log.Debug(
                ex,
                "Client disconnected while proxying {Method} {Path}{Query} for {Username}",
                context.Request.Method,
                context.Request.Path,
                context.Request.QueryString,
                context.User.Identity?.Name);
        }
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
        responseBodyBytes = InjectClientScriptIfNeeded(upstreamResponse, responseBodyBytes);
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

    private byte[] InjectClientScriptIfNeeded(HttpResponseMessage upstreamResponse, byte[] responseBodyBytes)
    {
        if (!ShouldInjectClientScript(upstreamResponse))
        {
            return responseBodyBytes;
        }

        var html = Encoding.UTF8.GetString(responseBodyBytes);
        const string marker = "</head>";
        var injection = $"""<script src="{JsonValidationClientScript.Path}"></script>""";
        var index = html.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            Log.Debug("Skipping client script injection because </head> marker was not found for {Path}", context.Request.Path);
            return responseBodyBytes;
        }

        if (html.Contains(JsonValidationClientScript.Path, StringComparison.Ordinal))
        {
            return responseBodyBytes;
        }

        var updatedHtml = html.Insert(index, injection);
        Log.Debug("Injected JSON validation client script into HTML response for {Path}", context.Request.Path);
        return Encoding.UTF8.GetBytes(updatedHtml);
    }

    private bool ShouldInjectClientScript(HttpResponseMessage upstreamResponse)
    {
        if (!HttpMethods.IsGet(context.Request.Method))
        {
            return false;
        }

        if (!context.Request.Path.StartsWithSegments("/ui"))
        {
            return false;
        }

        var mediaType = upstreamResponse.Content.Headers.ContentType?.MediaType;
        return string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase);
    }

    private async Task CaptureChangeRecordAsync(string requestBody, HttpResponseMessage upstreamResponse, byte[] responseBodyBytes)
    {
        var sourcePath = context.Request.Path + context.Request.QueryString;
        if (!ConsulKvChangeHelpers.IsKvPath(sourcePath))
        {
            Log.Debug("Skipping audit capture for non-KV path {Path}", sourcePath);
            return;
        }

        var action = ConsulKvChangeHelpers.KvAction(context.Request.Method);
        if (action == "kv_other")
        {
            Log.Debug("Skipping audit capture for unsupported KV action {Method} {Path}", context.Request.Method, sourcePath);
            return;
        }

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var eventId = Guid.NewGuid().ToString("N");
        var requestId = context.Request.Headers.TryGetValue("X-Request-ID", out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : eventId;
        var kvKey = ConsulKvChangeHelpers.KvKeyFromPath(sourcePath);
        var clientIp = context.Connection.RemoteIpAddress?.ToString();
        var userEmail = context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.Identity?.Name;
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var identity = ConsulKvChangeHelpers.ReadIdentity(clientIp, userAgent, kvKey, userEmail);
        var responseBody = Encoding.UTF8.GetString(responseBodyBytes);
        var responseCode = (int)upstreamResponse.StatusCode;

        if (action == "kv_read" && ConsulKvChangeHelpers.IsSuccess(responseCode))
        {
            var oldValue = ConsulKvChangeHelpers.ExtractReadValue(sourcePath, responseBody);
            readCache.Store(identity, oldValue, timestamp, requestId);
            Log.Debug(
                "Cached KV read for {Key} by {Username}. RequestId={RequestId} OldValuePresent={OldValuePresent}",
                kvKey,
                userEmail,
                requestId,
                oldValue is not null);
            return;
        }

        if (action is not ("kv_write" or "kv_delete"))
        {
            Log.Debug("Skipping audit capture after method normalization for action {Action}", action);
            return;
        }

        var read = readCache.Get(identity);
        var oldValueJson = ConsulKvChangeHelpers.InspectJson(read?.Value);
        var newValue = action == "kv_write" ? requestBody : null;
        var newValueJson = ConsulKvChangeHelpers.InspectJson(newValue);
        var changeRecord = new ChangeRecord
        {
            Timestamp = timestamp,
            EventId = eventId,
            Action = action,
            KvKey = kvKey,
            OldValue = read?.Value,
            OldValueLooksLikeJson = oldValueJson.LooksLikeJson,
            OldValueJsonValidationStatus = ConsulKvChangeHelpers.JsonValidationStatus(oldValueJson),
            OldValueIsValidJson = oldValueJson.IsValidJson,
            OldValueJsonError = oldValueJson.Error,
            OldValueSeenAt = read?.SeenAt,
            OldValueReadRequestId = read?.RequestId,
            NewValue = newValue,
            NewValueLooksLikeJson = newValueJson.LooksLikeJson,
            NewValueJsonValidationStatus = ConsulKvChangeHelpers.JsonValidationStatus(newValueJson),
            NewValueIsValidJson = newValueJson.IsValidJson,
            NewValueJsonError = newValueJson.Error,
            DeleteConfirmed = action == "kv_delete",
            Success = ConsulKvChangeHelpers.IsSuccess(responseCode),
            ResponseCode = responseCode,
            ClientIp = clientIp,
            UserEmail = userEmail,
            UserAgent = userAgent,
            RequestId = requestId,
            SourcePath = sourcePath
        };

        if (action == "kv_write" && newValueJson is { LooksLikeJson: true, IsValidJson: false })
        {
            Log.Warning(
                "Detected invalid JSON payload for KV write. Key={Key} User={Username} RequestId={RequestId} Error={JsonError}",
                kvKey,
                userEmail,
                requestId,
                newValueJson.Error);
        }

        await changeRecordSink.SendAsync(changeRecord, context.RequestAborted);
        Log.Information(
            "Queued audit record Action={Action} Key={Key} Success={Success} User={Username} RequestId={RequestId}",
            action,
            kvKey,
            changeRecord.Success,
            userEmail,
            requestId);
    }

    private static bool IsClientDisconnect(IOException exception) =>
        exception.InnerException is SocketException socketException &&
        socketException.ErrorCode is 995 or 10053 or 10054;
}
