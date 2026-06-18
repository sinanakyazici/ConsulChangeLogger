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
                ["AUTHENTICATION"] = "true"
            })
            .Build();

        var options = BootstrapOptions.FromConfiguration(configuration);

        Assert.Equal("http://127.0.0.1:8500", options.ConsulUpstreamUrl);
        Assert.Equal("consul-change-logger/appsettings.json", options.ConfigKey);
    }

    [Fact]
    public void FromConfiguration_FallsBackToSectionKeys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConsulConfiguration:UpstreamUrl"] = "http://consul:8500/",
                ["ConsulConfiguration:ConfigKey"] = "/consul/appsettings.json/",
                ["Authentication"] = "true"
            })
            .Build();

        var options = BootstrapOptions.FromConfiguration(configuration);

        Assert.Equal("http://consul:8500", options.ConsulUpstreamUrl);
        Assert.Equal("consul/appsettings.json", options.ConfigKey);
    }

    [Fact]
    public void FromConfiguration_UsesAuthenticationFlag_WhenExplicitlyDisabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CONSUL_UPSTREAM_URL"] = "http://consul:8500/",
                ["CONSUL_CONFIG_KEY"] = "consul-change-logger/appsettings.json",
                ["AUTHENTICATION"] = "false"
            })
            .Build();

        var options = BootstrapOptions.FromConfiguration(configuration);

        Assert.False(options.Authentication);
    }

    [Fact]
    public void FromConfiguration_UsesAuthenticationFlag_WhenExplicitlyEnabled()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CONSUL_UPSTREAM_URL"] = "http://consul:8500/",
                ["CONSUL_CONFIG_KEY"] = "consul-change-logger/appsettings.json",
                ["AUTHENTICATION"] = "true"
            })
            .Build();

        var options = BootstrapOptions.FromConfiguration(configuration);

        Assert.True(options.Authentication);
    }
}
