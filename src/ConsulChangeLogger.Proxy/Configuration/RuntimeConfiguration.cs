namespace ConsulChangeLogger.Proxy.Configuration;

internal sealed record RuntimeConfiguration
{
    public ElasticsearchConfiguration Elasticsearch { get; init; } = new();
    public ChangeLogConfiguration ChangeLog { get; init; } = new();
    public LdapConfiguration LdapConfiguration { get; init; } = new();
    public LogLevelConfiguration LogLevel { get; init; } = new();
}

internal sealed record ElasticsearchConfiguration
{
    public string? Url { get; init; } 
    public string? Username { get; init; } 
    public string? Password { get; init; }
    public string? Index { get; init; }
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

internal sealed record LogLevelConfiguration
{
    public string? Default { get; init; }
    public string? Microsoft { get; init; }
    public string? System { get; init; }
}
