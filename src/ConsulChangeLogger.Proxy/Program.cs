using ConsulChangeLogger.Core;
using ConsulChangeLogger.Proxy.ChangeLogging;
using ConsulChangeLogger.Proxy.Authentication;
using ConsulChangeLogger.Proxy.Configuration;
using ConsulChangeLogger.Proxy.DependencyInjection;
using ConsulChangeLogger.Proxy.Health;
using ConsulChangeLogger.Proxy.Proxying;
using ConsulChangeLogger.Proxy.Security;
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .WriteTo.Console()
    .CreateBootstrapLogger();

var bootstrapOptions = BootstrapOptions.FromEnvironment();
var consulConfig = await ConsulConfigLoader.LoadAsync(bootstrapOptions, CancellationToken.None);
var options = ChangeLoggerOptions.FromConfiguration(consulConfig);
var authOptions = AuthOptions.FromConfiguration(consulConfig);
var listenPort = ConfigValue.ReadString(consulConfig, "LISTEN_PORT", Environment.GetEnvironmentVariable("LISTEN_PORT") ?? "8080");
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((_, loggerConfiguration) =>
{
    loggerConfiguration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .WriteTo.Console();
});

builder.WebHost.UseUrls($"http://0.0.0.0:{listenPort}");
builder.Services.AddConsulChangeLoggerServices(options, authOptions);

var app = builder.Build();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var sink = scope.ServiceProvider.GetRequiredService<ChangeRecordSink>();
    await sink.WaitForElasticsearchAsync(app.Lifetime.ApplicationStopping);
    await sink.EnsureIndexAsync(app.Lifetime.ApplicationStopping);
}

app.MapHealthEndpoints();
app.MapAuthenticationEndpoints();
app.MapConsulProxyEndpoint(options);

try
{
    Log.Information("Consul Change Logger listening on port {ListenPort}, upstream {ConsulUpstreamUrl}, config prefix {ConfigPrefix}",
        listenPort,
        options.ConsulUpstreamUrl,
        bootstrapOptions.ConfigPrefix);
    await app.RunAsync();
}
finally
{
    await Log.CloseAndFlushAsync();
}
