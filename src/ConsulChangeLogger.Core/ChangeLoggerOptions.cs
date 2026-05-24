namespace ConsulChangeLogger.Core;

public sealed record ChangeLoggerOptions
{
    public string ConsulUpstreamUrl { get; init; } = "http://consul:8500";
    public string ElasticsearchUrl { get; init; } = "http://elasticsearch:9200";
    public string ChangeRecordIndex { get; init; } = "consul-change-logger";
    public string ChangeRecordOutboxPath { get; init; } = "/var/lib/consul-change-logger/outbox";
    public string DataProtectionPath { get; init; } = "/var/lib/consul-change-logger/dp-keys";
    public int ReadMatchWindowSeconds { get; init; } = 1800;
    public int MaxBodyBytes { get; init; } = 8192;
    public int ChangeRecordQueueCapacity { get; init; } = 1000;
    public int ChangeRecordRetentionDays { get; init; } = 30;
    public int ElasticsearchRetryDelaySeconds { get; init; } = 2;
    public bool AuthCookieSecure { get; init; }

    public static ChangeLoggerOptions FromConfiguration(IReadOnlyDictionary<string, string> config) => new()
    {
        ConsulUpstreamUrl = ReadString(config, "CONSUL_UPSTREAM_URL", "http://consul:8500").TrimEnd('/'),
        ElasticsearchUrl = ReadString(config, "ELASTICSEARCH_URL", "http://elasticsearch:9200").TrimEnd('/'),
        ChangeRecordIndex = ReadString(config, "CHANGE_LOG_INDEX", "consul-change-logger"),
        ChangeRecordOutboxPath = ReadString(config, "CHANGE_LOG_OUTBOX_PATH", "/var/lib/consul-change-logger/outbox"),
        DataProtectionPath = ReadString(config, "DATA_PROTECTION_PATH", "/var/lib/consul-change-logger/dp-keys"),
        ReadMatchWindowSeconds = ReadPositiveInt(config, "READ_MATCH_WINDOW_SECONDS", 1800),
        MaxBodyBytes = ReadPositiveInt(config, "MAX_BODY_BYTES", 8192),
        ChangeRecordQueueCapacity = ReadPositiveInt(config, "CHANGE_LOG_QUEUE_CAPACITY", 1000),
        ChangeRecordRetentionDays = ReadPositiveInt(config, "CHANGE_LOG_RETENTION_DAYS", 30),
        ElasticsearchRetryDelaySeconds = ReadPositiveInt(config, "ELASTICSEARCH_RETRY_DELAY_SECONDS", 2),
        AuthCookieSecure = ReadBool(config, "AUTH_COOKIE_SECURE", false)
    };

    private static string ReadString(IReadOnlyDictionary<string, string> config, string name, string fallback) =>
        config.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))
            ? fallback
            : Environment.GetEnvironmentVariable(name)!;

    private static int ReadInt(IReadOnlyDictionary<string, string> config, string name, int fallback) =>
        config.TryGetValue(name, out var rawValue) && int.TryParse(rawValue, out var configValue)
            ? configValue
            : int.TryParse(Environment.GetEnvironmentVariable(name), out var envValue)
                ? envValue
                : fallback;

    private static int ReadPositiveInt(IReadOnlyDictionary<string, string> config, string name, int fallback) =>
        Math.Max(1, ReadInt(config, name, fallback));

    private static bool ReadBool(IReadOnlyDictionary<string, string> config, string name, bool fallback) =>
        config.TryGetValue(name, out var rawValue) && bool.TryParse(rawValue, out var configValue)
            ? configValue
            : bool.TryParse(Environment.GetEnvironmentVariable(name), out var envValue)
                ? envValue
                : fallback;
}
