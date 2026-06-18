namespace ConsulChangeLogger.Proxy.Security;

internal static class RequestPathPolicy
{
    public static bool RequiresAuthenticatedUiSession(PathString path) =>
        path == "/" || path.StartsWithSegments("/ui");

    public static bool IsConsulApiPath(PathString path) =>
        path.StartsWithSegments("/v1");
}
