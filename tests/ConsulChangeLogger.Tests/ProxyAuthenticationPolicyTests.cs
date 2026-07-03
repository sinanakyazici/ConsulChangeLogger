using System.Security.Claims;
using ConsulChangeLogger.Proxy.Authentication;

namespace ConsulChangeLogger.Tests;

public sealed class ProxyAuthenticationPolicyTests
{
    [Fact]
    public void RequiresSession_ReturnsTrue_WhenAuthenticationIsEnabledAndUserIsAnonymous()
    {
        Assert.True(ProxyAuthenticationPolicy.RequiresSession(true, new ClaimsPrincipal()));
    }

    [Fact]
    public void RequiresSession_ReturnsFalse_WhenAuthenticationIsDisabled()
    {
        Assert.False(ProxyAuthenticationPolicy.RequiresSession(false, new ClaimsPrincipal()));
    }

    [Fact]
    public void RequiresSession_ReturnsFalse_WhenUserHasAuthenticatedSession()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "user@example.com")], "InMemorySession");

        Assert.False(ProxyAuthenticationPolicy.RequiresSession(true, new ClaimsPrincipal(identity)));
    }
}
