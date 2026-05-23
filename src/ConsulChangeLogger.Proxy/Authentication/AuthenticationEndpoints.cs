using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace ConsulChangeLogger.Proxy.Authentication;

internal static class AuthenticationEndpoints
{
    public static WebApplication MapAuthenticationEndpoints(this WebApplication app)
    {
        app.MapGet("/login", async context =>
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                context.Response.Redirect("/");
                return;
            }

            await LoginPage.WriteAsync(context);
        });

        app.MapPost("/login", async context =>
        {
            var form = await context.Request.ReadFormAsync();
            var email = form["email"].ToString().Trim();
            var password = form["password"].ToString();
            var authenticator = app.Services.GetRequiredService<LdapAuthenticator>();

            if (await authenticator.AuthenticateAsync(email, password, context.RequestAborted))
            {
                var claims = new[]
                {
                    new Claim(ClaimTypes.Name, email),
                    new Claim(ClaimTypes.Email, email)
                };
                var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
                context.Response.Redirect("/");
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await LoginPage.WriteAsync(context, "Email veya password hatali.");
        });

        app.MapPost("/logout", async context =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/login");
        });

        return app;
    }
}
