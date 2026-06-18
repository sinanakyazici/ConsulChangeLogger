using ConsulChangeLogger.Proxy.Configuration;
using Microsoft.Extensions.Configuration;

namespace ConsulChangeLogger.Tests;

public sealed class ConfigurationValidatorTests
{
    [Fact]
    public void FromConfiguration_ThrowsReadableValidationException_WhenConsulUpstreamUrlIsInvalid()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CONSUL_UPSTREAM_URL"] = "not-a-uri",
                ["CONSUL_CONFIG_KEY"] = "consul-change-logger/appsettings.json",
                ["AUTHENTICATION"] = "true"
            })
            .Build();

        var exception = Assert.Throws<ConfigurationValidationException>(() => BootstrapOptions.FromConfiguration(configuration));

        Assert.Contains("ConsulUpstreamUrl must be a valid absolute URI.", exception.Message);
    }

    [Fact]
    public void FromConfiguration_ThrowsReadableValidationException_WhenBootstrapPropertiesAreMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var exception = Assert.Throws<ConfigurationValidationException>(() => BootstrapOptions.FromConfiguration(configuration));

        Assert.Contains("ConsulUpstreamUrl is required.", exception.Message);
        Assert.Contains("ConfigKey is required.", exception.Message);
        Assert.Contains("Authentication is required.", exception.Message);
    }

    [Fact]
    public void Parse_ThrowsReadableValidationException_WhenRequiredRuntimePropertiesAreMissing()
    {
        const string json = """
        {
          "Elasticsearch": {
            "Url": "https://localhost:9200"
          },
          "ChangeLog": {
            "OutboxPath": ".local-data/outbox"
          },
          "LdapConfiguration": {
            "Domain": "localhost"
          }
        }
        """;

        var exception = Assert.Throws<ConfigurationValidationException>(() => ConsulConfigLoader.Parse(json));

        Assert.Contains("Elasticsearch.Index is required.", exception.Message);
        Assert.Contains("Elasticsearch.RetryDelaySeconds is required.", exception.Message);
        Assert.Contains("ChangeLog.QueueCapacity is required.", exception.Message);
        Assert.Contains("LdapConfiguration.Port is required.", exception.Message);
        Assert.Contains("LdapConfiguration.UseSSL is required.", exception.Message);
    }

    [Fact]
    public void Parse_ThrowsReadableValidationException_WhenNumericRuntimePropertiesAreOutOfRange()
    {
        const string json = """
        {
          "Elasticsearch": {
            "Url": "https://localhost:9200",
            "Index": "consul-change-logger",
            "RetryDelaySeconds": 0,
            "SkipCertificateValidation": true
          },
          "ChangeLog": {
            "OutboxPath": ".local-data/outbox",
            "ReadMatchWindowSeconds": 0,
            "QueueCapacity": 0,
            "RetentionDays": 0
          },
          "LdapConfiguration": {
            "Domain": "localhost",
            "Port": 0,
            "SecurePort": 70000,
            "UseSSL": false
          }
        }
        """;

        var exception = Assert.Throws<ConfigurationValidationException>(() => ConsulConfigLoader.Parse(json));

        Assert.Contains("Elasticsearch.RetryDelaySeconds must be greater than 0.", exception.Message);
        Assert.Contains("ChangeLog.ReadMatchWindowSeconds must be greater than 0.", exception.Message);
        Assert.Contains("ChangeLog.QueueCapacity must be greater than 0.", exception.Message);
        Assert.Contains("ChangeLog.RetentionDays must be greater than 0.", exception.Message);
        Assert.Contains("LdapConfiguration.Port must be between 1 and 65535.", exception.Message);
        Assert.Contains("LdapConfiguration.SecurePort must be between 1 and 65535.", exception.Message);
    }
}
