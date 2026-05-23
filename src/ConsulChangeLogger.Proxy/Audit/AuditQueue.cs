using System.Threading.Channels;
using ConsulChangeLogger.Core;

namespace ConsulChangeLogger.Proxy.Audit;

internal sealed class AuditQueue
{
    private readonly Channel<string> channel;

    public AuditQueue(AuditOptions options)
    {
        channel = Channel.CreateBounded<string>(new BoundedChannelOptions(options.AuditQueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    }

    public ValueTask EnqueueAsync(string outboxPath, CancellationToken cancellationToken) =>
        channel.Writer.WriteAsync(outboxPath, cancellationToken);

    public IAsyncEnumerable<string> ReadAllAsync(CancellationToken cancellationToken) =>
        channel.Reader.ReadAllAsync(cancellationToken);
}
