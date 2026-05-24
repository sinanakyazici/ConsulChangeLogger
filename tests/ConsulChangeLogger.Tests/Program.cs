using System.Text;
using System.Text.Json;
using System.Security.Claims;
using ConsulChangeLogger.Core;
using ConsulChangeLogger.Proxy.Authentication;
using ConsulChangeLogger.Proxy.ChangeLogging;
using ConsulChangeLogger.Proxy.Proxying;
using Microsoft.AspNetCore.Http;

var tests = new List<(string Name, Func<Task> Test)>
{
    ("extracts raw value", () =>
    {
        Equal("{ \"a\" : 1 }", ConsulKvChangeHelpers.ExtractReadValue("/v1/kv/test/test1?raw", "{ \"a\" : 1 }"));
        return Task.CompletedTask;
    }),
    ("decodes consul kv value", () =>
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("{ \"a\" : 1 }"));
        var body = JsonSerializer.Serialize(new[] { new { Key = "test/test1", Value = encoded } });
        Equal("{ \"a\" : 1 }", ConsulKvChangeHelpers.ExtractReadValue("/v1/kv/test/test1?dc=dc1", body));
        return Task.CompletedTask;
    }),
    ("ignores key list response", () =>
    {
        IsNull(ConsulKvChangeHelpers.ExtractReadValue("/v1/kv/?keys", JsonSerializer.Serialize(new[] { "test/" })));
        return Task.CompletedTask;
    }),
    ("ignores multi key response", () =>
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("value"));
        var body = JsonSerializer.Serialize(new[]
        {
            new { Key = "test/a", Value = encoded },
            new { Key = "test/b", Value = encoded }
        });
        IsNull(ConsulKvChangeHelpers.ExtractReadValue("/v1/kv/test?recurse", body));
        return Task.CompletedTask;
    }),
    ("extracts kv key without query", () =>
    {
        Equal("test/test1", ConsulKvChangeHelpers.KvKeyFromPath("/v1/kv/test/test1?dc=dc1"));
        return Task.CompletedTask;
    }),
    ("reads production hardening options", () =>
    {
        var options = ChangeLoggerOptions.FromConfiguration(new Dictionary<string, string>
        {
            ["CONSUL_ALLOWED_PATH_PREFIXES"] = "/ui,/v1/kv",
            ["CHANGE_LOG_RETENTION_DAYS"] = "45"
        });

        Equal("45", options.ChangeRecordRetentionDays.ToString());
        Equal("/ui", options.ConsulAllowedPathPrefixes[0]);
        Equal("/v1/kv", options.ConsulAllowedPathPrefixes[1]);
        return Task.CompletedTask;
    }),
    ("builds daily outbox path", () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "outbox");
        var path = ChangeRecordOutbox.BuildPath(root, "abc/123", new DateTimeOffset(2026, 5, 24, 10, 0, 0, TimeSpan.Zero));
        Equal(Path.Combine(root, "2026-05-24", "abc_2F123.json"), path);
        return Task.CompletedTask;
    }),
    ("deletes expired outbox directories", () =>
    {
        var root = Path.Combine(Path.GetTempPath(), "consul-change-logger-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "2026-04-24"));
        Directory.CreateDirectory(Path.Combine(root, "2026-04-25"));
        File.WriteAllText(Path.Combine(root, "2026-04-24", "old.json"), "{}");

        try
        {
            ChangeRecordOutbox.DeleteExpiredDailyDirectories(root, 30, new DateTimeOffset(2026, 5, 24, 12, 0, 0, TimeSpan.Zero));
            IsFalse(Directory.Exists(Path.Combine(root, "2026-04-24")));
            IsTrue(Directory.Exists(Path.Combine(root, "2026-04-25")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
        return Task.CompletedTask;
    }),
    ("blocks non kv mutations", () =>
    {
        var options = ChangeLoggerOptions.FromConfiguration(new Dictionary<string, string>
        {
            ["CONSUL_ALLOWED_PATH_PREFIXES"] = "/ui,/v1/kv,/v1/agent"
        });

        IsTrue(ConsulRequestPolicy.IsAllowed(Request("GET", "/v1/agent/self"), options));
        IsFalse(ConsulRequestPolicy.IsAllowed(Request("PUT", "/v1/agent/service/register"), options));
        IsTrue(ConsulRequestPolicy.IsAllowed(Request("PUT", "/v1/kv/test/key"), options));
        return Task.CompletedTask;
    }),
    ("validates login csrf token", () =>
    {
        var context = new DefaultHttpContext();
        var options = ChangeLoggerOptions.FromConfiguration(new Dictionary<string, string>());
        var token = LoginCsrfToken.Issue(context, options);
        context.Request.Headers.Cookie = $"{LoginCsrfToken.CookieName}={token}";

        IsTrue(LoginCsrfToken.IsValid(context, token));
        IsFalse(LoginCsrfToken.IsValid(context, "invalid-token"));
        return Task.CompletedTask;
    }),
    ("captures old and new values through proxy flow", async () =>
    {
        var root = TempRoot();
        try
        {
            var options = ChangeLoggerOptions.FromConfiguration(new Dictionary<string, string>
            {
                ["CHANGE_LOG_OUTBOX_PATH"] = root
            });
            var readCache = new ReadCache(TimeSpan.FromMinutes(30));
            var queue = new ChangeRecordQueue(options);
            var factory = new FakeHttpClientFactory((name, request) =>
            {
                if (name == "consul" && request.Method == HttpMethod.Get)
                {
                    return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                    {
                        Content = new StringContent("{ \"a\" : 1 }", Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("true", Encoding.UTF8, "application/json")
                };
            });
            var sink = new ChangeRecordSink(factory, options, queue);

            await RunProxyAsync("GET", "/v1/kv/demo/key", "?raw", string.Empty, options, factory, readCache, sink);
            await RunProxyAsync("PUT", "/v1/kv/demo/key", string.Empty, "{ \"a\" : 2 }", options, factory, readCache, sink);

            var file = Single(ChangeRecordOutbox.EnumeratePendingFiles(root));
            var record = JsonSerializer.Deserialize<ChangeRecord>(File.ReadAllText(file));
            Equal("{ \"a\" : 1 }", record?.OldValue);
            Equal("{ \"a\" : 2 }", record?.NewValue);
            Equal("user@example.com", record?.UserEmail);
            Equal("demo/key", record?.KvKey);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }),
    ("dispatches outbox file to elasticsearch and deletes it", async () =>
    {
        var root = TempRoot();
        try
        {
            var options = ChangeLoggerOptions.FromConfiguration(new Dictionary<string, string>
            {
                ["CHANGE_LOG_OUTBOX_PATH"] = root,
                ["ELASTICSEARCH_RETRY_DELAY_SECONDS"] = "1"
            });
            var path = WriteChangeRecord(root);
            var factory = new FakeHttpClientFactory((_, _) => new HttpResponseMessage(System.Net.HttpStatusCode.Created));
            var worker = new ChangeRecordDispatchWorker(new ChangeRecordQueue(options), factory, options);

            await worker.StartAsync(CancellationToken.None);
            await WaitUntilAsync(() => !File.Exists(path), TimeSpan.FromSeconds(5));
            await worker.StopAsync(CancellationToken.None);

            IsFalse(File.Exists(path));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }),
    ("keeps outbox file when elasticsearch is unavailable", async () =>
    {
        var root = TempRoot();
        try
        {
            var options = ChangeLoggerOptions.FromConfiguration(new Dictionary<string, string>
            {
                ["CHANGE_LOG_OUTBOX_PATH"] = root,
                ["ELASTICSEARCH_RETRY_DELAY_SECONDS"] = "1"
            });
            var path = WriteChangeRecord(root);
            var factory = new FakeHttpClientFactory((_, _) => new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
            var worker = new ChangeRecordDispatchWorker(new ChangeRecordQueue(options), factory, options);

            await worker.StartAsync(CancellationToken.None);
            await Task.Delay(250);
            await worker.StopAsync(CancellationToken.None);

            IsTrue(File.Exists(path));
        }
        finally
        {
            DeleteDirectory(root);
        }
    })
};

foreach (var test in tests)
{
    await test.Test();
    Console.WriteLine($"PASS {test.Name}");
}

static void Equal(string expected, string? actual)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'");
    }
}

static void IsNull(string? actual)
{
    if (actual is not null)
    {
        throw new InvalidOperationException($"Expected null, got '{actual}'");
    }
}

static void IsTrue(bool actual)
{
    if (!actual)
    {
        throw new InvalidOperationException("Expected true, got false");
    }
}

static void IsFalse(bool actual)
{
    if (actual)
    {
        throw new InvalidOperationException("Expected false, got true");
    }
}

static HttpRequest Request(string method, string path)
{
    var context = new DefaultHttpContext();
    context.Request.Method = method;
    context.Request.Path = path;
    return context.Request;
}

static async Task RunProxyAsync(
    string method,
    string path,
    string query,
    string body,
    ChangeLoggerOptions options,
    IHttpClientFactory factory,
    ReadCache readCache,
    ChangeRecordSink sink)
{
    var context = new DefaultHttpContext();
    context.Request.Method = method;
    context.Request.Path = path;
    context.Request.QueryString = new QueryString(query);
    context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
    context.Request.ContentLength = context.Request.Body.Length;
    context.Request.Headers.UserAgent = "functional-test";
    context.Response.Body = new MemoryStream();
    context.User = new ClaimsPrincipal(new ClaimsIdentity(
        [new Claim(ClaimTypes.Name, "user@example.com"), new Claim(ClaimTypes.Email, "user@example.com")],
        "Test"));

    var proxy = new ConsulProxy(context, options, factory, readCache, sink);
    await proxy.HandleAsync();
}

static string TempRoot() =>
    Path.Combine(Path.GetTempPath(), "consul-change-logger-tests", Guid.NewGuid().ToString("N"));

static void DeleteDirectory(string path)
{
    if (Directory.Exists(path))
    {
        Directory.Delete(path, recursive: true);
    }
}

static string WriteChangeRecord(string root)
{
    var record = new ChangeRecord
    {
        Timestamp = "2026-05-24T10:00:00Z",
        EventId = Guid.NewGuid().ToString("N"),
        Action = "kv_write",
        KvKey = "demo/key",
        NewValue = "{ \"a\" : 2 }",
        Success = true,
        ResponseCode = 200,
        RequestId = Guid.NewGuid().ToString("N"),
        SourcePath = "/v1/kv/demo/key"
    };
    var path = ChangeRecordOutbox.BuildPath(root, record.EventId, DateTimeOffset.Parse(record.Timestamp));
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, JsonSerializer.Serialize(record));
    return path;
}

static T Single<T>(IEnumerable<T> values)
{
    var list = values.ToList();
    if (list.Count != 1)
    {
        throw new InvalidOperationException($"Expected 1 item, got {list.Count}");
    }

    return list[0];
}

static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
{
    var deadline = DateTimeOffset.UtcNow + timeout;
    while (DateTimeOffset.UtcNow < deadline)
    {
        if (condition())
        {
            return;
        }

        await Task.Delay(50);
    }

    throw new TimeoutException("Condition was not met in time.");
}

internal sealed class FakeHttpClientFactory : IHttpClientFactory
{
    private readonly Func<string, HttpRequestMessage, HttpResponseMessage> handler;

    public FakeHttpClientFactory(Func<string, HttpRequestMessage, HttpResponseMessage> handler)
    {
        this.handler = handler;
    }

    public HttpClient CreateClient(string name) =>
        new(new FakeHttpMessageHandler(request => handler(name, request)))
        {
            BaseAddress = new Uri("http://localhost")
        };
}

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> handler;

    public FakeHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        this.handler = handler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(handler(request));
}
