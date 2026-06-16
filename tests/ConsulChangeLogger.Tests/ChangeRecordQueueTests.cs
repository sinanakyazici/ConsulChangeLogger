using ConsulChangeLogger.Proxy.ChangeLogging;
using ConsulChangeLogger.Proxy.Configuration;

namespace ConsulChangeLogger.Tests;

public sealed class ChangeRecordQueueTests
{
    [Fact]
    public async Task EnqueueAsync_MakesItemAvailableToReader()
    {
        var queue = new ChangeRecordQueue(new ChangeLogConfiguration { QueueCapacity = 4 });
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await queue.EnqueueAsync("outbox/2026-06-16/test.json", cts.Token);
        await using var enumerator = queue.ReadAllAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("outbox/2026-06-16/test.json", enumerator.Current);
    }
}
