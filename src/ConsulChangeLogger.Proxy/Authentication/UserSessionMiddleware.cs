using System.Security.Claims;

namespace ConsulChangeLogger.Proxy.Authentication;

internal sealed class UserSessionMiddleware
{
    private readonly RequestDelegate next;

    public UserSessionMiddleware(RequestDelegate next)
    {
        this.next = next;
    }

    public async Task InvokeAsync(HttpContext context, UserSessionStore sessions)
    {
        var bootstrapOptions = context.RequestServices.GetRequiredService<Configuration.BootstrapOptions>();

        if (!bootstrapOptions.Authentication!.Value)
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, "authentication-disabled"),
                new Claim(ClaimTypes.Email, "authentication-disabled")
            };
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "AuthenticationDisabled"));
            await next(context);
            return;
        }

        if (context.Request.Cookies.TryGetValue(UserSessionStore.CookieName, out var sessionId) &&
            sessions.TryGet(sessionId, out var session))
        {
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, session.Email),
                new Claim(ClaimTypes.Email, session.Email)
            };
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "InMemorySession"));
        }

        await next(context);
    }
}
