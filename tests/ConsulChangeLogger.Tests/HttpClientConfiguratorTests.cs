using ConsulChangeLogger.Proxy.Configuration;
using System.Net.Http.Headers;
using System.Text;

namespace ConsulChangeLogger.Tests;

public sealed class HttpClientConfiguratorTests
{
    [Fact]
    public void ConfigureConsul_SetsBaseAddressAndTimeout()
    {
        var client = new HttpClient();
        var options = new BootstrapOptions
        {
            ConsulUpstreamUrl = "http://127.0.0.1:8500",
            ConfigKey = "consul-change-logger/appsettings.json"
        };

        HttpClientConfigurator.ConfigureConsul(client, options);

        Assert.Equal(new Uri("http://127.0.0.1:8500"), client.BaseAddress);
        Assert.Equal(TimeSpan.FromSeconds(95), client.Timeout);
    }

    [Fact]
    public void ConfigureElasticsearch_UsesBasicAuth_WhenUsernameAndPasswordProvided()
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

    [Fact]
    public void CreateElasticsearchHandler_BypassesServerCertificateValidation()
    {
        var handler = HttpClientConfigurator.CreateElasticsearchHandler();
        var callback = handler.ServerCertificateCustomValidationCallback;

        Assert.NotNull(callback);
        Assert.Same(HttpClientHandler.DangerousAcceptAnyServerCertificateValidator, callback);
    }
}
