using Serilog;

namespace ConsulChangeLogger.Proxy.Authentication;

internal static class AuthenticationEndpoints
{
    public static WebApplication MapAuthenticationEndpoints(this WebApplication app)
    {
        app.MapGet("/login", async context =>
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                Log.Debug("Authenticated user {Username} requested /login; redirecting to /ui/", context.User.Identity?.Name);
                context.Response.Redirect("/ui/");
                return;
            }

            Log.Debug("Rendering login page for remote IP {RemoteIp}", context.Connection.RemoteIpAddress?.ToString());
            var tokens = app.Services.GetRequiredService<LoginCsrfTokenStore>();
            await LoginPage.WriteAsync(context, tokens.Issue());
        });

        app.MapPost("/login", async context =>
        {
            var form = await context.Request.ReadFormAsync();
            var tokens = app.Services.GetRequiredService<LoginCsrfTokenStore>();
            if (!tokens.Consume(form["csrf_token"].ToString()))
            {
                Log.Warning("Login request rejected because CSRF token is invalid. Remote IP {RemoteIp}", context.Connection.RemoteIpAddress?.ToString());
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await LoginPage.WriteAsync(context, tokens.Issue(), "Login form expired. Please try again.");
                return;
            }

            var username = form["username"].ToString().Trim();
            var password = form["password"].ToString();
            Log.Debug("Login request received for {Username} from {RemoteIp}", username, context.Connection.RemoteIpAddress?.ToString());
            var authenticator = app.Services.GetRequiredService<LdapAuthenticator>();

            if (await authenticator.AuthenticateAsync(username, password, context.RequestAborted))
            {
                var sessions = app.Services.GetRequiredService<UserSessionStore>();
                var session = sessions.Create(username);
                Log.Information("Login succeeded for {Username}; session created", username);
                context.Response.Cookies.Append(UserSessionStore.CookieName, session.Id, new CookieOptions
                {
                    HttpOnly = true,
                    IsEssential = true,
                    SameSite = SameSiteMode.Lax,
                    Secure = context.Request.IsHttps
                });
                context.Response.Redirect("/ui/");
                return;
            }

            Log.Warning("Login failed for {Username}", username);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await LoginPage.WriteAsync(context, tokens.Issue(), "Username veya password hatali.");
        });

        app.MapPost("/logout", context =>
        {
            Log.Information("Logout requested by {Username}", context.User.Identity?.Name);
            if (context.Request.Cookies.TryGetValue(UserSessionStore.CookieName, out var sessionId))
            {
                app.Services.GetRequiredService<UserSessionStore>().Remove(sessionId);
            }

            context.Response.Cookies.Delete(UserSessionStore.CookieName);
            context.Response.Redirect("/login");
            return Task.CompletedTask;
        });

        return app;
    }
}
