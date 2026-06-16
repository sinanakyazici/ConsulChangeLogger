using System.Threading.Channels;
using ConsulChangeLogger.Core;
using ConsulChangeLogger.Proxy.Configuration;

namespace ConsulChangeLogger.Proxy.ChangeLogging;

internal sealed class ChangeRecordQueue
{
    private readonly Channel<string> channel;

    public ChangeRecordQueue(ChangeLogConfiguration options)
    {
        channel = Channel.CreateBounded<string>(new BoundedChannelOptions(options.QueueCapacity)
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
