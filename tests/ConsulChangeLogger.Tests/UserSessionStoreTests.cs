using ConsulChangeLogger.Proxy.Authentication;

namespace ConsulChangeLogger.Tests;

public sealed class UserSessionStoreTests
{
    [Fact]
    public void Create_ThenTryGet_ReturnsStoredSession()
    {
        var store = new UserSessionStore();

        var created = store.Create("user@example.com");
        var found = store.TryGet(created.Id, out var session);

        Assert.True(found);
        Assert.Equal(created.Id, session.Id);
        Assert.Equal("user@example.com", session.Email);
    }

    [Fact]
    public void Remove_DeletesSession()
    {
        var store = new UserSessionStore();
        var created = store.Create("user@example.com");

        store.Remove(created.Id);

        Assert.False(store.TryGet(created.Id, out _));
    }
}
