using System.Security.Claims;
using ConsulChangeLogger.Core;
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
                context.Response.Redirect("/ui/");
                return;
            }

            var options = app.Services.GetRequiredService<ChangeLoggerOptions>();
            await LoginPage.WriteAsync(context, LoginCsrfToken.Issue(context, options));
        });

        app.MapPost("/login", async context =>
        {
            var form = await context.Request.ReadFormAsync();
            if (!LoginCsrfToken.IsValid(context, form["csrf_token"].ToString()))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                var options = app.Services.GetRequiredService<ChangeLoggerOptions>();
                await LoginPage.WriteAsync(context, LoginCsrfToken.Issue(context, options), "Login form expired. Please try again.");
                return;
            }

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
                context.Response.Redirect("/ui/");
                return;
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            var changeLoggerOptions = app.Services.GetRequiredService<ChangeLoggerOptions>();
            await LoginPage.WriteAsync(context, LoginCsrfToken.Issue(context, changeLoggerOptions), "Email veya password hatali.");
        });

        app.MapPost("/logout", async context =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/login");
        });

        return app;
    }
}
