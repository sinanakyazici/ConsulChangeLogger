using System.Security.Claims;
using System.Net.Sockets;
using System.Text;
using ConsulChangeLogger.Proxy;
using ConsulChangeLogger.Proxy.ChangeLogging;
using ConsulChangeLogger.Proxy.Configuration;
using ConsulChangeLogger.Proxy.Security;
using Serilog;

namespace ConsulChangeLogger.Proxy.Proxying;

internal sealed class ConsulProxy
{
    private const string KvWriteAction = "kv_write";
    private const string KvDeleteAction = "kv_delete";
    private const string KvOtherAction = "kv_other";

    private sealed record MutationPrefetchState(bool WasChecked, bool ValueExists);

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
    private readonly BootstrapOptions bootstrapOptions;
    private readonly ReadCache readCache;
    private readonly ChangeRecordSink changeRecordSink;

    public ConsulProxy(
        HttpContext context,
        IHttpClientFactory httpClientFactory,
        BootstrapOptions bootstrapOptions,
        ReadCache readCache,
        ChangeRecordSink changeRecordSink)
    {
        this.context = context;
        this.httpClientFactory = httpClientFactory;
        this.bootstrapOptions = bootstrapOptions;
        this.readCache = readCache;
        this.changeRecordSink = changeRecordSink;
    }

    public async Task HandleAsync()
    {
        try
        {
            if (context.User.Identity?.IsAuthenticated != true && RequestPathPolicy.IsConsulApiPath(context.Request.Path))
            {
                if (bootstrapOptions.Authentication!.Value && RequestPathPolicy.IsMarkedUiRequest(context.Request.Headers))
                {
                    Log.Debug(
                        "Unauthenticated marked UI API request for {Method} {Path}{Query}; returning 401",
                        context.Request.Method,
                        context.Request.Path,
                        context.Request.QueryString);

                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsync("Authentication required.", context.RequestAborted);
                    return;
                }

                await ForwardUnauthenticatedApiRequestAsync();
                return;
            }

            Log.Debug(
                "Proxying {Method} {Path}{Query} for {Username}",
                context.Request.Method,
                context.Request.Path,
                context.Request.QueryString,
                context.User.Identity?.Name);

            var requestBodyBytes = await ReadRequestBodyAsync();
            var requestBody = Encoding.UTF8.GetString(requestBodyBytes);
            var mutationPrefetchState = await PrefetchOldValueForMutationAsync();
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
            await CaptureChangeRecordAsync(requestBody, upstreamResponse, responseBodyBytes, mutationPrefetchState);
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
        catch (TaskCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        {
            Log.Warning(
                "Consul upstream timed out for {Method} {Path}{Query} after {TimeoutSeconds} seconds. User={Username}",
                context.Request.Method,
                context.Request.Path,
                context.Request.QueryString,
                95,
                context.User.Identity?.Name);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status504GatewayTimeout;
                await context.Response.WriteAsync("Consul upstream timed out.", context.RequestAborted);
            }
        }
        catch (HttpRequestException ex)
        {
            Log.Warning(
                ex,
                "Consul upstream request failed for {Method} {Path}{Query} for {Username}",
                context.Request.Method,
                context.Request.Path,
                context.Request.QueryString,
                context.User.Identity?.Name);

            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                await context.Response.WriteAsync("Consul upstream request failed.", context.RequestAborted);
            }
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

    private async Task ForwardUnauthenticatedApiRequestAsync()
    {
        using var upstreamRequest = BuildStreamingUpstreamRequest();
        using var upstreamResponse = await httpClientFactory
            .CreateClient("consul")
            .SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

        context.Response.StatusCode = (int)upstreamResponse.StatusCode;
        CopyResponseHeaders(upstreamResponse, includeContentLength: true);
        await upstreamResponse.Content.CopyToAsync(context.Response.Body, context.RequestAborted);
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

        CopyRequestHeaders(request);

        if (requestBodyBytes.Length > 0)
        {
            request.Content = new ByteArrayContent(requestBodyBytes);
            CopyRequestContentHeaders(request.Content);
        }

        return request;
    }

    private HttpRequestMessage BuildStreamingUpstreamRequest()
    {
        var target = context.Request.PathBase + context.Request.Path + context.Request.QueryString;
        var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), target);
        CopyRequestHeaders(request);

        if (RequestCanHaveBody(context.Request.Method) && context.Request.ContentLength != 0)
        {
            request.Content = new StreamContent(context.Request.Body);
            CopyRequestContentHeaders(request.Content);
        }

        return request;
    }

    private void CopyRequestHeaders(HttpRequestMessage request)
    {
        foreach (var header in context.Request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key) || header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    private void CopyRequestContentHeaders(HttpContent content)
    {
        foreach (var header in context.Request.Headers)
        {
            if (HopByHopHeaders.Contains(header.Key) || header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            content.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
        }
    }

    private async Task WriteDownstreamResponseAsync(HttpResponseMessage upstreamResponse, byte[] responseBodyBytes)
    {
        responseBodyBytes = InjectClientScriptIfNeeded(upstreamResponse, responseBodyBytes);
        context.Response.StatusCode = (int)upstreamResponse.StatusCode;
        CopyResponseHeaders(upstreamResponse, includeContentLength: false);
        context.Response.Headers.ContentLength = responseBodyBytes.Length;
        await context.Response.Body.WriteAsync(responseBodyBytes, context.RequestAborted);
    }

    private void CopyResponseHeaders(HttpResponseMessage upstreamResponse, bool includeContentLength)
    {
        foreach (var header in upstreamResponse.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }

        foreach (var header in upstreamResponse.Content.Headers)
        {
            if (!HopByHopHeaders.Contains(header.Key) &&
                (includeContentLength || !header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)))
            {
                context.Response.Headers[header.Key] = header.Value.ToArray();
            }
        }
    }

    private async Task<MutationPrefetchState> PrefetchOldValueForMutationAsync()
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return new MutationPrefetchState(false, false);
        }

        var sourcePath = context.Request.Path + context.Request.QueryString;
        var action = ConsulKvChangeHelpers.KvAction(context.Request.Method);
        if (action is not (KvWriteAction or KvDeleteAction))
        {
            return new MutationPrefetchState(false, false);
        }

        var kvKey = ConsulKvChangeHelpers.KvKeyFromPath(sourcePath);
        var clientIp = context.Connection.RemoteIpAddress?.ToString();
        var userEmail = context.User.FindFirstValue(ClaimTypes.Email) ?? context.User.Identity?.Name;
        var userAgent = context.Request.Headers.UserAgent.ToString();
        var identity = ConsulKvChangeHelpers.ReadIdentity(clientIp, userAgent, kvKey, userEmail);

        if (readCache.Get(identity) is not null)
        {
            return new MutationPrefetchState(true, true);
        }

        var prefetchPath = ConsulKvChangeHelpers.BuildMutationPrefetchPath(sourcePath);
        if (string.IsNullOrWhiteSpace(prefetchPath))
        {
            Log.Debug("Skipping mutation old_value prefetch for {Path} because the request is not a single-key mutation", sourcePath);
            return new MutationPrefetchState(false, false);
        }

        try
        {
            using var prefetchRequest = new HttpRequestMessage(HttpMethod.Get, context.Request.PathBase + prefetchPath);
            foreach (var header in context.Request.Headers)
            {
                if (HopByHopHeaders.Contains(header.Key) || header.Key.Equals("Host", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                prefetchRequest.Headers.TryAddWithoutValidation(header.Key, header.Value.ToArray());
            }

            using var prefetchResponse = await httpClientFactory
                .CreateClient("consul")
                .SendAsync(prefetchRequest, HttpCompletionOption.ResponseHeadersRead, context.RequestAborted);

            if (prefetchResponse.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Log.Debug(
                    "Mutation old_value prefetch returned 404 for {Path}; key does not exist before {Action}",
                    prefetchPath,
                    action);
                return new MutationPrefetchState(true, false);
            }

            if (!ConsulKvChangeHelpers.IsSuccess((int)prefetchResponse.StatusCode))
            {
                Log.Debug(
                    "Mutation old_value prefetch returned {StatusCode} for {Path}",
                    (int)prefetchResponse.StatusCode,
                    prefetchPath);
                return new MutationPrefetchState(false, false);
            }

            var responseBody = await prefetchResponse.Content.ReadAsStringAsync(context.RequestAborted);
            var oldValue = ConsulKvChangeHelpers.ExtractReadValue(prefetchPath, responseBody);
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            var requestId = Guid.NewGuid().ToString("N");
            readCache.Store(identity, oldValue, timestamp, requestId);

            Log.Debug(
                "Prefetched old_value for {Action} {Key} by {Username}. RequestId={RequestId} OldValuePresent={OldValuePresent}",
                action,
                kvKey,
                userEmail,
                requestId,
                oldValue is not null);
            return new MutationPrefetchState(true, oldValue is not null);
        }
        catch (HttpRequestException ex)
        {
            Log.Warning(ex, "Failed to prefetch old_value for {Path}", sourcePath);
        }
        catch (TaskCanceledException ex) when (!context.RequestAborted.IsCancellationRequested)
        {
            Log.Warning(ex, "Timed out while prefetching old_value for {Path}", sourcePath);
        }

        return new MutationPrefetchState(false, false);
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

    private async Task CaptureChangeRecordAsync(string requestBody, HttpResponseMessage upstreamResponse, byte[] responseBodyBytes, MutationPrefetchState mutationPrefetchState)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            Log.Debug("Skipping audit capture for unauthenticated request {Method} {Path}", context.Request.Method, context.Request.Path);
            return;
        }

        var sourcePath = context.Request.Path + context.Request.QueryString;
        if (!ConsulKvChangeHelpers.IsKvPath(sourcePath))
        {
            Log.Debug("Skipping audit capture for non-KV path {Path}", sourcePath);
            return;
        }

        var action = ConsulKvChangeHelpers.KvAction(context.Request.Method);
        if (action == KvOtherAction)
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
        var isFolder = ConsulKvChangeHelpers.IsFolderKey(kvKey);

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

        if (action is not (KvWriteAction or KvDeleteAction))
        {
            Log.Debug("Skipping audit capture after method normalization for action {Action}", action);
            return;
        }

        var read = readCache.Get(identity);
        var newValue = action == "kv_write" ? requestBody : null;
        var newValueJson = ConsulKvChangeHelpers.InspectJson(newValue);
        var isCreate = action == "kv_write" && mutationPrefetchState.WasChecked && !mutationPrefetchState.ValueExists;
        var isUpdate = action == "kv_write" && mutationPrefetchState.WasChecked && mutationPrefetchState.ValueExists;
        var isDelete = action == "kv_delete";
        var changeRecord = new ChangeRecord
        {
            Timestamp = timestamp,
            EventId = eventId,
            Action = action,
            KvKey = kvKey,
            IsFolder = isFolder,
            OldValue = read?.Value,
            OldValueObservedAt = read?.SeenAt,
            NewValue = newValue,
            NewValueJsonError = newValueJson.Error,
            IsCreate = isCreate,
            IsUpdate = isUpdate,
            IsDelete = isDelete,
            IsSuccess = ConsulKvChangeHelpers.IsSuccess(responseCode),
            ResponseStatusCode = responseCode,
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

        if (isCreate)
        {
            Log.Information(
                "Detected first-time KV creation for {Key} by {Username}. RequestId={RequestId}",
                kvKey,
                userEmail,
                requestId);
        }

        if (isUpdate)
        {
            Log.Information(
                "Detected KV update for {Key} by {Username}. RequestId={RequestId}",
                kvKey,
                userEmail,
                requestId);
        }

        await changeRecordSink.SendAsync(changeRecord, context.RequestAborted);
        Log.Information(
            "Queued audit record Action={Action} Key={Key} IsCreate={IsCreate} IsUpdate={IsUpdate} IsDelete={IsDelete} IsSuccess={IsSuccess} User={Username} RequestId={RequestId}",
            action,
            kvKey,
            isCreate,
            isUpdate,
            isDelete,
            changeRecord.IsSuccess,
            userEmail,
            requestId);
    }

    private static bool IsClientDisconnect(IOException exception) =>
        exception.InnerException is SocketException socketException &&
        socketException.ErrorCode is 995 or 10053 or 10054;

    private static bool RequestCanHaveBody(string method) =>
        HttpMethods.IsPost(method) ||
        HttpMethods.IsPut(method) ||
        HttpMethods.IsPatch(method) ||
        HttpMethods.IsDelete(method);
}
