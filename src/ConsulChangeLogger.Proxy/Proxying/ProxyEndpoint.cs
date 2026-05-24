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

            if (context.Request.Path == "/")
            {
                context.Response.Redirect("/ui/");
                return;
            }

            if (!ConsulRequestPolicy.IsAllowed(context.Request, options))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Consul path or method is not allowed.");
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
