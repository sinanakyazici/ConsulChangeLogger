using System.Text;
using System.Text.Json;

namespace ConsulChangeLogger.Core;

public static class ConsulKvChangeHelpers
{
    public static bool IsSuccess(int statusCode) => statusCode is >= 200 and < 300;

    public static string Truncate(string? value, int maxBodyBytes)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxBodyBytes ? value : value[..maxBodyBytes] + "...[truncated]";
    }

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
}
