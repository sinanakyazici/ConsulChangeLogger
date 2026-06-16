using System.Text;
using System.Text.Json;

namespace ConsulChangeLogger.Proxy;

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

    public static string ReadIdentity(string? clientIp, string? userAgent, string? kvKey, string? userEmail = null) =>
        string.Join("|", userEmail ?? string.Empty, clientIp ?? string.Empty, userAgent ?? string.Empty, kvKey ?? string.Empty);

    public static string? ExtractReadValue(string path, string? responseBody)
    {
        if (string.IsNullOrEmpty(responseBody))
        {
            return string.Empty;
        }

        if (path.Contains("?raw", StringComparison.Ordinal))
        {
            return responseBody;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(responseBody);
        }
        catch (JsonException)
        {
            return responseBody;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 1)
            {
                return null;
            }

            var item = root[0];
            if (item.ValueKind != JsonValueKind.Object ||
                !item.TryGetProperty("Value", out var valueProperty) ||
                valueProperty.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var encodedValue = valueProperty.GetString();
            if (string.IsNullOrEmpty(encodedValue))
            {
                return string.Empty;
            }

            try
            {
                return Encoding.UTF8.GetString(Convert.FromBase64String(encodedValue));
            }
            catch (FormatException)
            {
                return responseBody;
            }
        }
    }

    public static string? BuildMutationPrefetchPath(string path)
    {
        if (!IsKvPath(path))
        {
            return null;
        }

        var cleanPath = PathWithoutQuery(path);
        var kvKey = KvKeyFromPath(path);
        if (string.IsNullOrWhiteSpace(kvKey))
        {
            return null;
        }

        var queryIndex = path.IndexOf('?', StringComparison.Ordinal);
        if (queryIndex < 0)
        {
            return $"{cleanPath}?raw";
        }

        var query = path[(queryIndex + 1)..];
        var parameters = ParseQueryString(query);
        if (parameters.Any(x => x.Key.Equals("recurse", StringComparison.OrdinalIgnoreCase)) ||
            parameters.Any(x => x.Key.Equals("keys", StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var items = new List<string>();
        foreach (var parameter in parameters)
        {
            if (parameter.Key.Equals("raw", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var value in parameter.Value)
            {
                var encodedKey = Uri.EscapeDataString(parameter.Key);
                var encodedValue = Uri.EscapeDataString(value ?? string.Empty);
                items.Add($"{encodedKey}={encodedValue}");
            }
        }

        items.Add("raw");
        return $"{cleanPath}?{string.Join("&", items)}";
    }

    private static IEnumerable<KeyValuePair<string, List<string?>>> ParseQueryString(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var map = new Dictionary<string, List<string?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : null;

            if (!map.TryGetValue(key, out var values))
            {
                values = [];
                map[key] = values;
            }

            values.Add(value);
        }

        return map;
    }

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
