using ConsulChangeLogger.Core;

namespace ConsulChangeLogger.Proxy.Authentication;

internal static class AuthenticationEndpoints
{
    public static WebApplication MapAuthenticationEndpoints(this WebApplication app)
    {
        app.MapGet("/login", async context =>
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                context.Response.Redirect("/ui/");
                return;
            }

            var tokens = app.Services.GetRequiredService<LoginCsrfTokenStore>();
            await LoginPage.WriteAsync(context, tokens.Issue());
        });

        app.MapPost("/login", async context =>
        {
            var form = await context.Request.ReadFormAsync();
            var tokens = app.Services.GetRequiredService<LoginCsrfTokenStore>();
            if (!tokens.Consume(form["csrf_token"].ToString()))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await LoginPage.WriteAsync(context, tokens.Issue(), "Login form expired. Please try again.");
                return;
            }

            var email = form["email"].ToString().Trim();
            var password = form["password"].ToString();
            var authenticator = app.Services.GetRequiredService<LdapAuthenticator>();

            if (await authenticator.AuthenticateAsync(email, password, context.RequestAborted))
            {
                var sessions = app.Services.GetRequiredService<UserSessionStore>();
                var session = sessions.Create(email);
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

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await LoginPage.WriteAsync(context, tokens.Issue(), "Email veya password hatali.");
        });

        app.MapPost("/logout", context =>
        {
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
