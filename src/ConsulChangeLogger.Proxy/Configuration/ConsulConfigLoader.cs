using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;

namespace ConsulChangeLogger.Proxy.Configuration;

internal static class ConsulConfigLoader
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static async Task<RuntimeConfiguration> LoadAsync(BootstrapOptions bootstrapOptions, CancellationToken cancellationToken)
    {
        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri(bootstrapOptions.ConsulUpstreamUrl),
            Timeout = TimeSpan.FromSeconds(10)
        };
        if (!string.IsNullOrWhiteSpace(bootstrapOptions.ConsulHttpToken))
        {
            httpClient.DefaultRequestHeaders.Add("X-Consul-Token", bootstrapOptions.ConsulHttpToken);
        }

        var path = EscapeKvPath(bootstrapOptions.ConfigKey);
        var deadline = DateTimeOffset.UtcNow.Add(StartupTimeout);
        Exception? lastError = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var response = await httpClient.GetAsync($"/v1/kv/{path}?raw", cancellationToken);
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    lastError = new InvalidOperationException(
                        $"Consul configuration key '{bootstrapOptions.ConfigKey}' was not found.");
                }
                else
                {
                    response.EnsureSuccessStatusCode();
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    return Parse(json);
                }
            }
            catch (JsonException error)
            {
                throw new InvalidOperationException(
                    $"Consul configuration key '{bootstrapOptions.ConfigKey}' contains invalid JSON.", error);
            }
            catch (HttpRequestException error)
            {
                lastError = error;
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = new TimeoutException(
                    $"Timed out while reading Consul configuration key '{bootstrapOptions.ConfigKey}'.");
            }

            Log.Warning(lastError,
                "Consul configuration is not ready; retrying key {ConfigKey} in {RetryDelaySeconds} seconds",
                bootstrapOptions.ConfigKey,
                RetryDelay.TotalSeconds);
            await Task.Delay(RetryDelay, cancellationToken);
        }

        throw new TimeoutException(
            $"Consul configuration key '{bootstrapOptions.ConfigKey}' was not available within {StartupTimeout.TotalSeconds:0} seconds.",
            lastError);
    }

    internal static RuntimeConfiguration Parse(string json)
    {
        var config = JsonSerializer.Deserialize<RuntimeConfiguration>(json, JsonOptions);
        return config ?? throw new JsonException("The configuration root must be a JSON object.");
    }

    private static string EscapeKvPath(string key) =>
        string.Join("/", key.Trim('/').Split('/').Select(Uri.EscapeDataString));
}
