using ConsulChangeLogger.Core;
using Serilog;

namespace ConsulChangeLogger.Proxy.Audit;

internal sealed class AuditEventLogger : IDisposable
{
    private readonly Serilog.Core.Logger logger;

    public AuditEventLogger(AuditOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.AuditLogPath)!);

        logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(new AuditEventTextFormatter(), options.AuditLogPath, shared: true)
            .CreateLogger();
    }

    public void Write(string eventJson) =>
        logger.ForContext("AuditEventJson", eventJson).Information("Consul KV change recorded");

    public void Dispose() => logger.Dispose();
}
