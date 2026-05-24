using ConsulChangeLogger.Core;
using ConsulChangeLogger.Proxy.ChangeLogging;

namespace ConsulChangeLogger.Proxy.Proxying;

internal static class ProxyEndpoint
{
    public static WebApplication MapConsulProxyEndpoint(this WebApplication app, ChangeLoggerOptions options)
    {
        app.Map("/{**path}", async context =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
            {
                context.Response.Redirect("/login");
                return;
            }

            var proxy = new ConsulProxy(
                context,
                options,
                app.Services.GetRequiredService<IHttpClientFactory>(),
                app.Services.GetRequiredService<ReadCache>(),
                app.Services.GetRequiredService<ChangeRecordSink>());

            await proxy.HandleAsync();
        });

        return app;
    }
}
