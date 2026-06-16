namespace ConsulChangeLogger.Proxy.Configuration;

internal sealed record BootstrapOptions
{
    public string ConsulUpstreamUrl { get; init; } = "http://consul:8500";
    public string ConfigKey { get; init; } = "consul-change-logger/appsettings.json";

    public static BootstrapOptions FromConfiguration(IConfiguration configuration) => new()
    {
        ConsulUpstreamUrl = (configuration["ConsulConfiguration:UpstreamUrl"] ?? "http://consul:8500").TrimEnd('/'),
        ConfigKey = (configuration["ConsulConfiguration:ConfigKey"] ?? "consul-change-logger/appsettings.json").Trim('/')
    };
}
