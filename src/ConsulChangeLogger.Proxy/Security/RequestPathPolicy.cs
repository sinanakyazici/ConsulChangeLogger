namespace ConsulChangeLogger.Proxy.Security;

internal static class RequestPathPolicy
{
    public static bool RequiresAuthenticatedUiSession(PathString path) =>
        path == "/" || path.StartsWithSegments("/ui");
}
