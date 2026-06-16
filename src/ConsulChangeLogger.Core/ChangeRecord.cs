using System.Text.Json.Serialization;

namespace ConsulChangeLogger.Core;

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

    [JsonPropertyName("old_value")]
    public string? OldValue { get; init; }

    [JsonPropertyName("old_value_looks_like_json")]
    public bool OldValueLooksLikeJson { get; init; }

    [JsonPropertyName("old_value_json_validation_status")]
    public string OldValueJsonValidationStatus { get; init; } = "not_json";

    [JsonPropertyName("old_value_is_valid_json")]
    public bool? OldValueIsValidJson { get; init; }

    [JsonPropertyName("old_value_json_error")]
    public string? OldValueJsonError { get; init; }

    [JsonPropertyName("old_value_seen_at")]
    public string? OldValueSeenAt { get; init; }

    [JsonPropertyName("old_value_read_request_id")]
    public string? OldValueReadRequestId { get; init; }

    [JsonPropertyName("new_value")]
    public string? NewValue { get; init; }

    [JsonPropertyName("new_value_looks_like_json")]
    public bool NewValueLooksLikeJson { get; init; }

    [JsonPropertyName("new_value_json_validation_status")]
    public string NewValueJsonValidationStatus { get; init; } = "not_json";

    [JsonPropertyName("new_value_is_valid_json")]
    public bool? NewValueIsValidJson { get; init; }

    [JsonPropertyName("new_value_json_error")]
    public string? NewValueJsonError { get; init; }

    [JsonPropertyName("delete_confirmed")]
    public bool DeleteConfirmed { get; init; }

    [JsonPropertyName("success")]
    public bool Success { get; init; }

    [JsonPropertyName("response_code")]
    public int ResponseCode { get; init; }

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
