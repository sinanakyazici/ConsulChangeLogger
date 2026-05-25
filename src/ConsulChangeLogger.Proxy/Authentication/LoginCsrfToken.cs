using System.Security.Cryptography;
using ConsulChangeLogger.Core;

namespace ConsulChangeLogger.Proxy.Authentication;

internal static class LoginCsrfToken
{
    public const string CookieName = "consul_change_logger_csrf";

    public static string Issue(HttpContext context, ChangeLoggerOptions options)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        context.Response.Cookies.Append(CookieName, token, new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Strict,
            Secure = options.AuthCookieSecure || context.Request.IsHttps,
            MaxAge = TimeSpan.FromMinutes(30)
        });
        return token;
    }

    public static bool IsValid(HttpContext context, string formToken) =>
        !string.IsNullOrWhiteSpace(formToken) &&
        context.Request.Cookies.TryGetValue(CookieName, out var cookieToken) &&
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(formToken),
            System.Text.Encoding.UTF8.GetBytes(cookieToken));
}
