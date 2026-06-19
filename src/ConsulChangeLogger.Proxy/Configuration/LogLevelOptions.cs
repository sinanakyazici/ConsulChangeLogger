using Serilog.Events;

namespace ConsulChangeLogger.Proxy.Configuration;

internal sealed record LogLevelOptions(LogEventLevel Default, LogEventLevel Microsoft, LogEventLevel System)
{
    public static LogLevelOptions From(LogLevelConfiguration? configuration)
    {
        return new LogLevelOptions(
            Parse(configuration?.Default, LogEventLevel.Debug),
            Parse(configuration?.Microsoft, LogEventLevel.Warning),
            Parse(configuration?.System, LogEventLevel.Warning));
    }

    private static LogEventLevel Parse(string? value, LogEventLevel fallback)
    {
        return Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out var level)
            ? level
            : fallback;
    }
}
