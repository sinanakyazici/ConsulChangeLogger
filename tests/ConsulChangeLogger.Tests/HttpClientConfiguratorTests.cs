using ConsulChangeLogger.Proxy.Configuration;
using System.Net.Http.Headers;
using System.Text;

namespace ConsulChangeLogger.Tests;

public sealed class HttpClientConfiguratorTests
{
    [Fact]
    public void ConfigureConsul_AddsConsulTokenHeader_WhenProvided()
    {
        var client = new HttpClient();
        var options = new BootstrapOptions
        {
            ConsulUpstreamUrl = "http://127.0.0.1:8500",
            ConfigKey = "consul-change-logger/appsettings.json",
            ConsulHttpToken = "token-123"
        };

        HttpClientConfigurator.ConfigureConsul(client, options);

        Assert.Equal(new Uri("http://127.0.0.1:8500"), client.BaseAddress);
        Assert.Equal("token-123", client.DefaultRequestHeaders.GetValues("X-Consul-Token").Single());
    }

    [Fact]
    public void ConfigureElasticsearch_UsesApiKey_WhenProvided()
    {
        var client = new HttpClient();
        var configuration = new ElasticsearchConfiguration
        {
            Url = "https://localhost:9200",
            Username = "elastic",
            Password = "ignored",
            ApiKey = "api-key-123"
        };

        HttpClientConfigurator.ConfigureElasticsearch(client, configuration);

        Assert.Equal(new AuthenticationHeaderValue("ApiKey", "api-key-123"), client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public void ConfigureElasticsearch_UsesBasicAuth_WhenApiKeyMissing()
    {
        var client = new HttpClient();
        var configuration = new ElasticsearchConfiguration
        {
            Url = "https://localhost:9200",
            Username = "elastic",
            Password = "secret"
        };

        HttpClientConfigurator.ConfigureElasticsearch(client, configuration);

        var expected = Convert.ToBase64String(Encoding.UTF8.GetBytes("elastic:secret"));
        Assert.Equal(new AuthenticationHeaderValue("Basic", expected), client.DefaultRequestHeaders.Authorization);
    }
}
