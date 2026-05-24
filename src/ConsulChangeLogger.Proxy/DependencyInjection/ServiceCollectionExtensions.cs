using ConsulChangeLogger.Core;
using ConsulChangeLogger.Proxy.ChangeLogging;
using ConsulChangeLogger.Proxy.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using System.Net.Http.Headers;
using System.Text;

namespace ConsulChangeLogger.Proxy.DependencyInjection;

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddConsulChangeLoggerServices(
        this IServiceCollection services,
        ChangeLoggerOptions options,
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
                cookieOptions.Cookie.Name = "consul_change_logger_auth";
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
            ConfigureElasticsearchAuthentication(client, options);
        });
        services.AddSingleton<ChangeRecordQueue>();
        services.AddSingleton<ChangeRecordSink>();
        services.AddHostedService<ChangeRecordDispatchWorker>();

        return services;
    }

    private static void ConfigureElasticsearchAuthentication(HttpClient client, ChangeLoggerOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ElasticsearchApiKey))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", options.ElasticsearchApiKey);
            return;
        }

        if (!string.IsNullOrWhiteSpace(options.ElasticsearchUsername))
        {
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.ElasticsearchUsername}:{options.ElasticsearchPassword}"));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", token);
        }
    }
}
