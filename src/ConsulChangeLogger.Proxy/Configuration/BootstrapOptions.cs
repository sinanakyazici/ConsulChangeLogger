namespace ConsulChangeLogger.Proxy.Configuration;

internal sealed record BootstrapOptions
{
    public string ConsulUpstreamUrl { get; init; } = "http://consul:8500";
    public string ConfigPrefix { get; init; } = "consul-change-logger/config";

    public static BootstrapOptions FromEnvironment() => new()
    {
        ConsulUpstreamUrl = Environment.GetEnvironmentVariable("CONSUL_UPSTREAM_URL")?.TrimEnd('/') ?? "http://consul:8500",
        ConfigPrefix = Environment.GetEnvironmentVariable("CONSUL_CONFIG_PREFIX")?.Trim('/') ?? "consul-change-logger/config"
    };
}
