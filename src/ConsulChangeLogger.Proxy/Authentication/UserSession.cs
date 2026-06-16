namespace ConsulChangeLogger.Proxy.Authentication;

internal sealed record UserSession(string Id, string Email, DateTimeOffset ExpiresAt);
