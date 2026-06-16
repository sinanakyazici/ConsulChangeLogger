using ConsulChangeLogger.Proxy.Authentication;
using ConsulChangeLogger.Proxy.ChangeLogging;
using ConsulChangeLogger.Proxy.Configuration;
using ConsulChangeLogger.Proxy.Health;
using ConsulChangeLogger.Proxy.Proxying;
using ConsulChangeLogger.Proxy.Security;
using Serilog;
using Serilog.Events;
using System.Net.Http.Headers;
using System.Text;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateBootstrapLogger();

var builder = WebApplication.CreateBuilder(args);
var bootstrapOptions = BootstrapOptions.FromConfiguration(builder.Configuration);
var runtimeConfig = await ConsulConfigLoader.LoadAsync(bootstrapOptions, CancellationToken.None);

builder.Host.UseSerilog((_, loggerConfiguration) =>
{
    loggerConfiguration
        .MinimumLevel.Debug()
        .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
        .MinimumLevel.Override("System", LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .WriteTo.Console();
});

builder.Services.AddSingleton(runtimeConfig.Elasticsearch);
builder.Services.AddSingleton(runtimeConfig.ChangeLog);
builder.Services.AddSingleton(runtimeConfig.LdapConfiguration);

builder.Services.AddSingleton<LdapAuthenticator>();
builder.Services.AddSingleton<LoginCsrfTokenStore>();
builder.Services.AddSingleton<UserSessionStore>();
builder.Services.AddSingleton<ChangeRecordQueue>();
builder.Services.AddSingleton<ChangeRecordSink>();
builder.Services.AddHostedService<ChangeRecordDispatchWorker>();

builder.Services.AddHttpClient("consul", client =>
{
    client.BaseAddress = new Uri(bootstrapOptions.ConsulUpstreamUrl);
    client.Timeout = TimeSpan.FromSeconds(30);
});

var elasticsearchClient = builder.Services.AddHttpClient("elasticsearch", client =>
{
    client.BaseAddress = new Uri(runtimeConfig.Elasticsearch.Url!);
    client.Timeout = TimeSpan.FromSeconds(10);
    var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{runtimeConfig.Elasticsearch.Username}:{runtimeConfig.Elasticsearch.Password}"));
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
});

if (runtimeConfig.Elasticsearch.SkipCertificateValidation)
{
    elasticsearchClient.ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
}

var app = builder.Build();
app.UseSerilogRequestLogging(options =>
{
    options.GetLevel = (httpContext, _, ex) =>
    {
        if (ex is not null || httpContext.Response.StatusCode >= 500)
        {
            return LogEventLevel.Error;
        }

        return LogEventLevel.Debug;
    };
});
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<UserSessionMiddleware>();

Log.Information(
    "Consul Change Logger starting. Consul={ConsulUrl} Elasticsearch={ElasticsearchUrl} Ldap={LdapHost}:{LdapPort} UseSSL={UseSSL}",
    bootstrapOptions.ConsulUpstreamUrl,
    runtimeConfig.Elasticsearch.Url,
    runtimeConfig.LdapConfiguration.Domain,
    runtimeConfig.LdapConfiguration.UseSSL ? runtimeConfig.LdapConfiguration.SecurePort : runtimeConfig.LdapConfiguration.Port,
    runtimeConfig.LdapConfiguration.UseSSL);

using (var scope = app.Services.CreateScope())
{
    var sink = scope.ServiceProvider.GetRequiredService<ChangeRecordSink>();
    await sink.WaitForElasticsearchAsync(app.Lifetime.ApplicationStopping);
    await sink.EnsureIndexAsync(app.Lifetime.ApplicationStopping);
}

app.MapHealthEndpoints();
app.MapAuthenticationEndpoints();
app.MapGet(JsonValidationClientScript.Path, () =>
{
    return Results.Text(JsonValidationClientScript.Content, "application/javascript; charset=utf-8");
});
app.Map("/{**path}", async context =>
{
    if (context.User.Identity?.IsAuthenticated != true)
    {
        Log.Debug("Unauthenticated request for {Path}; redirecting to /login", context.Request.Path);
        context.Response.Redirect("/login");
        return;
    }

    if (context.Request.Path == "/")
    {
        Log.Debug("Authenticated root request; redirecting to /ui/");
        context.Response.Redirect("/ui/");
        return;
    }

    var proxy = new ConsulProxy(
        context,
        app.Services.GetRequiredService<IHttpClientFactory>(),
        app.Services.GetRequiredService<ChangeRecordSink>());

    await proxy.HandleAsync();
});


await app.RunAsync();
