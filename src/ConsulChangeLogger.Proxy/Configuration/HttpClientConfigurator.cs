using System.Net.Http.Headers;
using System.Text;

namespace ConsulChangeLogger.Proxy.Configuration;

internal static class HttpClientConfigurator
{
    public static void ConfigureConsul(HttpClient client, BootstrapOptions options)
    {
        client.BaseAddress = new Uri(options.ConsulUpstreamUrl!);
        client.Timeout = TimeSpan.FromSeconds(95);
    }

    public static void ConfigureElasticsearch(HttpClient client, ElasticsearchConfiguration configuration)
    {
        client.BaseAddress = new Uri(configuration.Url!);
        client.Timeout = TimeSpan.FromSeconds(10);

        if (!string.IsNullOrWhiteSpace(configuration.Username) && !string.IsNullOrWhiteSpace(configuration.Password))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{configuration.Username}:{configuration.Password}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }
}
