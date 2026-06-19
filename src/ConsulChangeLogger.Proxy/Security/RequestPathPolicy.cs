namespace ConsulChangeLogger.Proxy.Security;

internal static class RequestPathPolicy
{
    public const string UiRequestHeaderName = "X-Consul-Change-Logger-UI";
    public const string UiRequestHeaderValue = "true";

    public static bool RequiresAuthenticatedUiSession(PathString path) =>
        path == "/" || path.StartsWithSegments("/ui");

    public static bool IsConsulApiPath(PathString path) =>
        path.StartsWithSegments("/v1");

    public static bool IsMarkedUiRequest(IHeaderDictionary headers) =>
        headers.TryGetValue(UiRequestHeaderName, out var value) &&
        string.Equals(value.ToString(), UiRequestHeaderValue, StringComparison.OrdinalIgnoreCase);
}
