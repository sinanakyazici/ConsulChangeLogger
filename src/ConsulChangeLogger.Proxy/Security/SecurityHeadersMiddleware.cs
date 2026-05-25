namespace ConsulChangeLogger.Proxy.Security;

internal sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        headers.TryAdd("X-Content-Type-Options", "nosniff");
        headers.TryAdd("X-Frame-Options", "DENY");
        headers.TryAdd("Referrer-Policy", "no-referrer");
        headers.TryAdd("Permissions-Policy", "camera=(), microphone=(), geolocation=()");

        if (context.Request.IsHttps)
        {
            headers.TryAdd("Strict-Transport-Security", "max-age=31536000; includeSubDomains");
        }

        if (context.Request.Path.StartsWithSegments("/login"))
        {
            headers.TryAdd("Content-Security-Policy",
                "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'");
        }

        await next(context);
    }
}
