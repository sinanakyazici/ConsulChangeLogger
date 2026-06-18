namespace ConsulChangeLogger.Proxy.Configuration;

internal sealed record RuntimeConfiguration
{
    public ElasticsearchConfiguration Elasticsearch { get; init; } = new();
    public ChangeLogConfiguration ChangeLog { get; init; } = new();
    public LdapConfiguration LdapConfiguration { get; init; } = new();
}

internal sealed record ElasticsearchConfiguration
{
    public string? Url { get; init; } 
    public string? Username { get; init; } 
    public string? Password { get; init; }
    public string? Index { get; init; }
    public int? RetryDelaySeconds { get; init; }
    public bool? SkipCertificateValidation { get; init; }
}

internal sealed record ChangeLogConfiguration
{
    public string? OutboxPath { get; init; }
    public int? ReadMatchWindowSeconds { get; init; }
    public int? QueueCapacity { get; init; }
    public int? RetentionDays { get; init; }
}

internal sealed record LdapConfiguration
{
    public string? Domain { get; init; }
    public int? Port { get; init; }
    public int? SecurePort { get; init; }
    public bool? UseSSL { get; init; }
}
