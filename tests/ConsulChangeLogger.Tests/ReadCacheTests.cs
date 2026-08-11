using ConsulChangeLogger.Proxy;

namespace ConsulChangeLogger.Tests;

public sealed class ReadCacheTests
{
    [Fact]
    public void Store_ThenGet_ReturnsSnapshot()
    {
        var cache = new ReadCache(TimeSpan.FromMinutes(30));

        cache.Store("user|client|agent|key", "{ \"a\": 1 }", "2026-06-16T10:02:00Z", "req-1");
        var snapshot = cache.Get("user|client|agent|key");

        Assert.NotNull(snapshot);
        Assert.Equal("{ \"a\": 1 }", snapshot!.Value);
        Assert.Equal("2026-06-16T10:02:00Z", snapshot.SeenAt);
        Assert.Equal("req-1", snapshot.RequestId);
    }

    [Fact]
    public void Store_DoesNotPersistNullValue()
    {
        var cache = new ReadCache(TimeSpan.FromMinutes(30));

        cache.Store("user|client|agent|key", null, "2026-06-16T10:02:00Z", "req-1");

        Assert.Null(cache.Get("user|client|agent|key"));
    }

    [Fact]
    public void Store_RemovesExpiredSnapshotsBeforePersistingNewValue()
    {
        var cache = new ReadCache(TimeSpan.Zero);

        cache.Store("old", "old-value", "2026-06-16T10:00:00Z", "req-old");
        Thread.Sleep(5);
        cache.Store("new", "new-value", "2026-06-16T10:01:00Z", "req-new");

        Assert.Null(cache.Get("old"));
        Assert.NotNull(cache.Get("new"));
    }
}
