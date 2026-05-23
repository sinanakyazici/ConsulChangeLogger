using Serilog.Events;
using Serilog.Formatting;

namespace ConsulChangeLogger.Proxy.Audit;

internal sealed class AuditEventTextFormatter : ITextFormatter
{
    public void Format(LogEvent logEvent, TextWriter output)
    {
        if (logEvent.Properties.TryGetValue("AuditEventJson", out var property) &&
            property is ScalarValue { Value: string eventJson })
        {
            output.WriteLine(eventJson);
        }
    }
}
