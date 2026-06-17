using System.Text.Json.Serialization;

namespace ConsulChangeLogger.Proxy;

public sealed record ChangeRecord
{
    [JsonPropertyName("@timestamp")]
    public required string Timestamp { get; init; }

    [JsonPropertyName("event_id")]
    public string EventId { get; init; } = string.Empty;

    [JsonPropertyName("action")]
    public required string Action { get; init; }

    [JsonPropertyName("kv_key")]
    public required string KvKey { get; init; }

    [JsonPropertyName("is_folder")]
    public bool IsFolder { get; init; }

    [JsonPropertyName("old_value")]
    public string? OldValue { get; init; }

    [JsonPropertyName("old_value_observed_at")]
    public string? OldValueObservedAt { get; init; }

    [JsonPropertyName("new_value")]
    public string? NewValue { get; init; }

    [JsonPropertyName("new_value_json_error")]
    public string? NewValueJsonError { get; init; }

    [JsonPropertyName("is_create")]
    public bool IsCreate { get; init; }

    [JsonPropertyName("is_update")]
    public bool IsUpdate { get; init; }

    [JsonPropertyName("is_delete")]
    public bool IsDelete { get; init; }

    [JsonPropertyName("is_success")]
    public bool IsSuccess { get; init; }

    [JsonPropertyName("response_status_code")]
    public int ResponseStatusCode { get; init; }

    [JsonPropertyName("client_ip")]
    public string? ClientIp { get; init; }

    [JsonPropertyName("user_email")]
    public string? UserEmail { get; init; }

    [JsonPropertyName("user_agent")]
    public string? UserAgent { get; init; }

    [JsonPropertyName("request_id")]
    public required string RequestId { get; init; }

    [JsonPropertyName("source_path")]
    public required string SourcePath { get; init; }

    [JsonPropertyName("source")]
    public string Source { get; init; } = "consul-change-logger";
}
