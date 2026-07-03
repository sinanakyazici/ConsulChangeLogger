using System.Security.Claims;

namespace ConsulChangeLogger.Proxy.Authentication;

internal static class ProxyAuthenticationPolicy
{
    public static bool RequiresSession(bool authenticationEnabled, ClaimsPrincipal user) =>
        authenticationEnabled && user.Identity?.IsAuthenticated != true;
}
