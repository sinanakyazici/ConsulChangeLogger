using ConsulChangeLogger.Core;
using ConsulChangeLogger.Proxy.Audit;
using ConsulChangeLogger.Proxy.Authentication;
using ConsulChangeLogger.Proxy.Configuration;
using ConsulChangeLogger.Proxy.DependencyInjection;
using ConsulChangeLogger.Proxy.Health;
using ConsulChangeLogger.Proxy.Proxying;

var bootstrapOptions = BootstrapOptions.FromEnvironment();
var consulConfig = await ConsulConfigLoader.LoadAsync(bootstrapOptions, CancellationToken.None);
var options = AuditOptions.FromConfiguration(consulConfig);
var authOptions = AuthOptions.FromConfiguration(consulConfig);
var listenPort = ConfigValue.ReadString(consulConfig, "LISTEN_PORT", Environment.GetEnvironmentVariable("LISTEN_PORT") ?? "8080");
var builder = WebApplication.CreateBuilder(args);

builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls($"http://0.0.0.0:{listenPort}");
builder.Services.AddConsulChangeLoggerServices(options, authOptions);

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

using (var scope = app.Services.CreateScope())
{
    var sink = scope.ServiceProvider.GetRequiredService<AuditSink>();
    await sink.WaitForElasticsearchAsync(app.Lifetime.ApplicationStopping);
    await sink.EnsureIndexAsync(app.Lifetime.ApplicationStopping);
}

app.MapHealthEndpoints();
app.MapAuthenticationEndpoints();
app.MapConsulProxyEndpoint(options);

Console.WriteLine($"ConsulChangeLogger listening on :{listenPort}, upstream={options.ConsulUpstreamUrl}, configPrefix={bootstrapOptions.ConfigPrefix}");
await app.RunAsync();
