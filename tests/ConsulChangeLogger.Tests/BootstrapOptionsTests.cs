using ConsulChangeLogger.Proxy.Configuration;
using Microsoft.Extensions.Configuration;

namespace ConsulChangeLogger.Tests;

public sealed class BootstrapOptionsTests
{
    [Fact]
    public void FromConfiguration_PrefersSimpleEnvironmentStyleKeys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CONSUL_UPSTREAM_URL"] = "http://127.0.0.1:8500/",
                ["CONSUL_CONFIG_KEY"] = "/consul-change-logger/appsettings.json/",
                ["CONSUL_HTTP_TOKEN"] = "token-123"
            })
            .Build();

        var options = BootstrapOptions.FromConfiguration(configuration);

        Assert.Equal("http://127.0.0.1:8500", options.ConsulUpstreamUrl);
        Assert.Equal("consul-change-logger/appsettings.json", options.ConfigKey);
        Assert.Equal("token-123", options.ConsulHttpToken);
    }

    [Fact]
    public void FromConfiguration_FallsBackToSectionKeys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConsulConfiguration:UpstreamUrl"] = "http://consul:8500/",
                ["ConsulConfiguration:ConfigKey"] = "/consul/appsettings.json/",
                ["ConsulConfiguration:HttpToken"] = "section-token"
            })
            .Build();

        var options = BootstrapOptions.FromConfiguration(configuration);

        Assert.Equal("http://consul:8500", options.ConsulUpstreamUrl);
        Assert.Equal("consul/appsettings.json", options.ConfigKey);
        Assert.Equal("section-token", options.ConsulHttpToken);
    }

    [Fact]
    public void FromConfiguration_UsesAuthenticationFlag_WhenExplicitlyDisabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AUTHENTICATION"] = "false"
            })
            .Build();

        var options = BootstrapOptions.FromConfiguration(configuration);

        Assert.False(options.Authentication);
    }

    [Fact]
    public void FromConfiguration_DefaultsAuthenticationToEnabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var options = BootstrapOptions.FromConfiguration(configuration);

        Assert.True(options.Authentication);
    }
}
