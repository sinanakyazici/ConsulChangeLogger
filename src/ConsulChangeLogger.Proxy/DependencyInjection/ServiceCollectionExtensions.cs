using ConsulChangeLogger.Core;
using ConsulChangeLogger.Proxy.Audit;
using ConsulChangeLogger.Proxy.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

namespace ConsulChangeLogger.Proxy.DependencyInjection;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddConsulChangeLoggerServices(
        this IServiceCollection services,
        AuditOptions options,
        AuthOptions authOptions)
    {
        Directory.CreateDirectory(options.DataProtectionPath);

        services
            .AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(options.DataProtectionPath))
            .SetApplicationName("ConsulChangeLogger");

        services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(cookieOptions =>
            {
                cookieOptions.Cookie.Name = "consul_audit_auth";
                cookieOptions.Cookie.HttpOnly = true;
                cookieOptions.Cookie.SameSite = SameSiteMode.Lax;
                cookieOptions.Cookie.SecurePolicy = options.AuthCookieSecure
                    ? CookieSecurePolicy.Always
                    : CookieSecurePolicy.SameAsRequest;
                cookieOptions.LoginPath = "/login";
                cookieOptions.LogoutPath = "/logout";
                cookieOptions.SlidingExpiration = true;
                cookieOptions.ExpireTimeSpan = TimeSpan.FromHours(8);
            });

        services.AddAuthorization();
        services.AddSingleton(options);
        services.AddSingleton(authOptions);
        services.AddSingleton<LdapAuthenticator>();
        services.AddSingleton(new ReadCache(TimeSpan.FromSeconds(options.ReadMatchWindowSeconds)));
        services.AddHttpClient("consul", client =>
        {
            client.BaseAddress = new Uri(options.ConsulUpstreamUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
        });
        services.AddHttpClient("elasticsearch", client =>
        {
            client.BaseAddress = new Uri(options.ElasticsearchUrl);
            client.Timeout = TimeSpan.FromSeconds(10);
        });
        services.AddSingleton<AuditQueue>();
        services.AddSingleton<AuditSink>();
        services.AddHostedService<AuditDispatchWorker>();

        return services;
    }
}
