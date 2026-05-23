namespace ConsulChangeLogger.Proxy.Health;

internal static class HealthEndpoints
{
    public static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));

        app.MapGet("/health/ready", async (IHttpClientFactory httpClientFactory, CancellationToken cancellationToken) =>
        {
            var elasticsearchReady = await HttpHealthCheck.IsReadyAsync(httpClientFactory.CreateClient("elasticsearch"), "/", cancellationToken);
            var consulReady = await HttpHealthCheck.IsReadyAsync(httpClientFactory.CreateClient("consul"), "/v1/status/leader", cancellationToken);

            return elasticsearchReady && consulReady
                ? Results.Ok(new { status = "ready" })
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        });

        return app;
    }
}
