using ConsulChangeLogger.Proxy.Authentication;

namespace ConsulChangeLogger.Tests;

public sealed class LoginCsrfTokenStoreTests
{
    [Fact]
    public void Consume_ReturnsTrue_OnlyOnceForIssuedToken()
    {
        var store = new LoginCsrfTokenStore();
        var token = store.Issue();

        Assert.True(store.Consume(token));
        Assert.False(store.Consume(token));
    }

    [Fact]
    public void Consume_ReturnsFalse_ForMissingToken()
    {
        var store = new LoginCsrfTokenStore();

        Assert.False(store.Consume("missing-token"));
        Assert.False(store.Consume(string.Empty));
    }
}
