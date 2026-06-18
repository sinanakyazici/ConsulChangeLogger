namespace ConsulChangeLogger.Proxy.Configuration;

internal sealed record BootstrapOptions
{
    public string? ConsulUpstreamUrl { get; init; }
    public string? ConfigKey { get; init; }
    public bool? Authentication { get; init; }

    public static BootstrapOptions FromConfiguration(IConfiguration configuration)
    {
        var authenticationRaw = configuration["AUTHENTICATION"] ?? configuration["Authentication"];
        var options = new BootstrapOptions
        {
            ConsulUpstreamUrl = (
                configuration["CONSUL_UPSTREAM_URL"] ??
                configuration["ConsulConfiguration:UpstreamUrl"])?.TrimEnd('/'),
            ConfigKey = (
                configuration["CONSUL_CONFIG_KEY"] ??
                configuration["ConsulConfiguration:ConfigKey"])?.Trim('/'),
            Authentication = bool.TryParse(authenticationRaw, out var enabled)
                ? enabled
                : null
        };

        ConfigurationValidator.Validate(options);
        return options;
    }
}
