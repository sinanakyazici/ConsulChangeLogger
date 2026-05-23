using System.Net;

namespace ConsulChangeLogger.Proxy.Configuration;

internal static class ConsulConfigLoader
{
    private static readonly string[] ForbiddenKeys =
    [
        "LDAP_BIND_PASSWORD"
    ];

    private static readonly string[] Keys =
    [
        "LISTEN_PORT",
        "CONSUL_UPSTREAM_URL",
        "ELASTICSEARCH_URL",
        "AUDIT_INDEX",
        "AUDIT_LOG_PATH",
        "AUDIT_OUTBOX_PATH",
        "DATA_PROTECTION_PATH",
        "READ_MATCH_WINDOW_SECONDS",
        "MAX_BODY_BYTES",
        "AUDIT_QUEUE_CAPACITY",
        "ELASTICSEARCH_RETRY_DELAY_SECONDS",
        "AUTH_COOKIE_SECURE",
        "AUTH_MODE",
        "AUTH_MOCK_PASSWORD",
        "LDAP_URL",
        "LDAP_BIND_DN",
        "LDAP_BASE_DN",
        "LDAP_USER_FILTER"
    ];

    public static async Task<IReadOnlyDictionary<string, string>> LoadAsync(BootstrapOptions bootstrapOptions, CancellationToken cancellationToken)
    {
        var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(bootstrapOptions.ConsulUpstreamUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };

        foreach (var key in Keys)
        {
            var path = BuildConsulKvPath(bootstrapOptions.ConfigPrefix, key);
            try
            {
                using var response = await httpClient.GetAsync($"/v1/kv/{path}?raw", cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var value = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
                config[key] = value;
            }
            catch (HttpRequestException error)
            {
                Console.WriteLine($"failed to read config key {key} from consul: {error.Message}");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"failed to read config key {key} from consul: timeout");
            }
        }

        await WarnIfForbiddenKeysExistAsync(httpClient, bootstrapOptions.ConfigPrefix, cancellationToken);

        return config;
    }

    private static async Task WarnIfForbiddenKeysExistAsync(HttpClient httpClient, string prefix, CancellationToken cancellationToken)
    {
        foreach (var key in ForbiddenKeys)
        {
            var path = BuildConsulKvPath(prefix, key);
            try
            {
                using var response = await httpClient.GetAsync($"/v1/kv/{path}", cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    continue;
                }

                Console.WriteLine($"warning: secret-like config key {key} exists in Consul KV and will be ignored; move it to a Kubernetes Secret/env var");
            }
            catch (HttpRequestException error)
            {
                Console.WriteLine($"failed to check forbidden config key {key} in consul: {error.Message}");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine($"failed to check forbidden config key {key} in consul: timeout");
            }
        }
    }

    private static string BuildConsulKvPath(string prefix, string key)
    {
        var fullPath = $"{prefix.TrimEnd('/')}/{key}";
        return string.Join("/", fullPath.Split('/').Select(Uri.EscapeDataString));
    }
}
