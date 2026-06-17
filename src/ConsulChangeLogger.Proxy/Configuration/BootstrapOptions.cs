namespace ConsulChangeLogger.Proxy.Configuration;

internal sealed record BootstrapOptions
{
    public string? ConsulUpstreamUrl { get; init; }
    public string? ConfigKey { get; init; }
    public string? ConsulHttpToken { get; init; }
    public bool? Authentication { get; init; }

    public static BootstrapOptions FromConfiguration(IConfiguration configuration) => new()
    {
        ConsulUpstreamUrl = (
            configuration["CONSUL_UPSTREAM_URL"] ??
            configuration["ConsulConfiguration:UpstreamUrl"] ??
            "http://consul:8500").TrimEnd('/'),
        ConfigKey = (
            configuration["CONSUL_CONFIG_KEY"] ??
            configuration["ConsulConfiguration:ConfigKey"] ??
            "consul-change-logger/appsettings.json").Trim('/'),
        ConsulHttpToken = (
            configuration["CONSUL_HTTP_TOKEN"] ??
            configuration["ConsulConfiguration:HttpToken"])?.Trim(),
        Authentication= !(
            bool.TryParse(
                configuration["AUTHENTICATION"] ??
                configuration["Authentication"],
                out var enabled) &&
            !enabled)
    };
}
