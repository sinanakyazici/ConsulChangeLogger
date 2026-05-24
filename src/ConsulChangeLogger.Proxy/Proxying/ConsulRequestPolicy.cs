using ConsulChangeLogger.Core;

namespace ConsulChangeLogger.Proxy.Proxying;

internal static class ConsulRequestPolicy
{
    public static bool IsAllowed(HttpRequest request, ChangeLoggerOptions options)
    {
        var path = request.Path.ToString();
        if (!options.ConsulAllowedPathPrefixes.Any(prefix => IsPathWithinPrefix(path, prefix)))
        {
            return false;
        }

        if (HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method))
        {
            return true;
        }

        return ConsulKvChangeHelpers.IsKvPath(path);
    }

    private static bool IsPathWithinPrefix(string path, string prefix) =>
        path.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(prefix.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);
}
