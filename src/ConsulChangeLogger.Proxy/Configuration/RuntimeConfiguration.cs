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
    public int RetryDelaySeconds { get; init; }
    public bool SkipCertificateValidation { get; init; }
}

internal sealed record ChangeLogConfiguration
{
    public string OutboxPath { get; init; } = "/var/lib/consul-change-logger/outbox";
    public string DataProtectionPath { get; init; } = ".local-data/data-protection";
    public int MaxBodyBytes { get; init; } = 8192;
    public int QueueCapacity { get; init; } = 1000;
    public int RetentionDays { get; init; } = 30;
}

internal sealed record LdapConfiguration
{
    public string Domain { get; init; } = "localhost";
    public int Port { get; init; } = 389;
    public int SecurePort { get; init; } = 636;
    public string BindDn { get; init; } = string.Empty;
    public string BindCredentials { get; init; } = string.Empty;
    public string SearchBase { get; init; } = string.Empty;
    public string SearchFilter { get; init; } = "(mail={0})";
    public bool UseSSL { get; init; }
}
