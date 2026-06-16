using System.Text.Json;

namespace ConsulChangeLogger.Core;

public static class ConsulKvChangeHelpers
{
    public sealed record JsonInspection(bool LooksLikeJson, bool? IsValidJson, string? Error);

    public static bool IsSuccess(int statusCode) => statusCode is >= 200 and < 300;

    public static string PathWithoutQuery(string path)
    {
        var index = path.IndexOf('?', StringComparison.Ordinal);
        return index < 0 ? path : path[..index];
    }

    public static bool IsKvPath(string path)
    {
        var cleanPath = PathWithoutQuery(path);
        return cleanPath == "/v1/kv" || cleanPath.StartsWith("/v1/kv/", StringComparison.Ordinal);
    }

    public static string KvKeyFromPath(string path)
    {
        const string prefix = "/v1/kv/";
        var cleanPath = PathWithoutQuery(path);
        return cleanPath.StartsWith(prefix, StringComparison.Ordinal)
            ? Uri.UnescapeDataString(cleanPath[prefix.Length..])
            : string.Empty;
    }

    public static string KvAction(string method) => method.ToUpperInvariant() switch
    {
        "GET" => "kv_read",
        "PUT" => "kv_write",
        "DELETE" => "kv_delete",
        _ => "kv_other"
    };

    public static JsonInspection InspectJson(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return new JsonInspection(false, null, null);
        }

        var trimmed = value.TrimStart();
        var looksLikeJson = trimmed.StartsWith('{') || trimmed.StartsWith('[');
        if (!looksLikeJson)
        {
            return new JsonInspection(false, null, null);
        }

        try
        {
            using var _ = JsonDocument.Parse(value);
            return new JsonInspection(true, true, null);
        }
        catch (JsonException ex)
        {
            return new JsonInspection(true, false, ex.Message);
        }
    }

    public static string JsonValidationStatus(JsonInspection inspection) =>
        inspection switch
        {
            { LooksLikeJson: false } => "not_json",
            { IsValidJson: true } => "valid_json",
            { IsValidJson: false } => "invalid_json",
            _ => "not_json"
        };
}
