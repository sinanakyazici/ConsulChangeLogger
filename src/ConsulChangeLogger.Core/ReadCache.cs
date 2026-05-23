using System.Collections.Concurrent;

namespace ConsulChangeLogger.Core;

public sealed class ReadCache
{
    private readonly ConcurrentDictionary<string, ReadSnapshot> reads = new();
    private readonly TimeSpan ttl;

    public ReadCache(TimeSpan ttl)
    {
        this.ttl = ttl;
    }

    public void Store(string identity, string? value, string timestamp, string requestId)
    {
        if (value is null)
        {
            return;
        }

        CleanExpired(DateTimeOffset.UtcNow);
        reads[identity] = new ReadSnapshot(value, timestamp, DateTimeOffset.UtcNow, requestId);
    }

    public ReadSnapshot? Get(string identity) =>
        reads.TryGetValue(identity, out var snapshot) ? snapshot : null;

    private void CleanExpired(DateTimeOffset now)
    {
        foreach (var item in reads)
        {
            if (now - item.Value.SeenAtUtc > ttl)
            {
                reads.TryRemove(item.Key, out _);
            }
        }
    }
}

public sealed record ReadSnapshot(
    string Value,
    string SeenAt,
    DateTimeOffset SeenAtUtc,
    string RequestId);
