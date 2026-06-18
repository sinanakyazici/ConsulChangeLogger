using ConsulChangeLogger.Proxy.Authentication;
using ConsulChangeLogger.Proxy.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ConsulChangeLogger.Tests;

public sealed class UserSessionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_DoesNotCreateIdentity_WhenAuthenticationIsDisabled()
    {
        var context = new DefaultHttpContext();
        var services = new ServiceCollection()
            .AddSingleton(new BootstrapOptions { Authentication = false })
            .BuildServiceProvider();
        context.RequestServices = services;
        var middleware = new UserSessionMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, new UserSessionStore());

        Assert.False(context.User.Identity?.IsAuthenticated);
    }

    [Fact]
    public async Task InvokeAsync_CreatesIdentity_WhenAuthenticationIsEnabledAndSessionCookieIsValid()
    {
        var store = new UserSessionStore();
        var session = store.Create("user@example.com");
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{UserSessionStore.CookieName}={session.Id}";
        var services = new ServiceCollection()
            .AddSingleton(new BootstrapOptions { Authentication = true })
            .BuildServiceProvider();
        context.RequestServices = services;
        var middleware = new UserSessionMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context, store);

        Assert.True(context.User.Identity?.IsAuthenticated);
        Assert.Equal("user@example.com", context.User.Identity?.Name);
    }
}
