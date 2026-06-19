using ConsulChangeLogger.Proxy.Configuration;
using Serilog.Events;

namespace ConsulChangeLogger.Tests;

public sealed class LogLevelOptionsTests
{
    [Fact]
    public void From_UsesConfiguredLevels_WhenProvided()
    {
        var options = LogLevelOptions.From(new LogLevelConfiguration
        {
            Default = "Information",
            Microsoft = "Error",
            System = "Fatal"
        });

        Assert.Equal(LogEventLevel.Information, options.Default);
        Assert.Equal(LogEventLevel.Error, options.Microsoft);
        Assert.Equal(LogEventLevel.Fatal, options.System);
    }

    [Fact]
    public void From_UsesCurrentDefaults_WhenValuesAreMissingOrInvalid()
    {
        var options = LogLevelOptions.From(new LogLevelConfiguration
        {
            Default = "invalid"
        });

        Assert.Equal(LogEventLevel.Debug, options.Default);
        Assert.Equal(LogEventLevel.Warning, options.Microsoft);
        Assert.Equal(LogEventLevel.Warning, options.System);
    }
}
