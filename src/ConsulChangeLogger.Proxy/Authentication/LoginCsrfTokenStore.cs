using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ConsulChangeLogger.Proxy.Authentication;

internal sealed class LoginCsrfTokenStore
{
    private static readonly TimeSpan TokenLifetime = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, DateTimeOffset> tokens = new(StringComparer.Ordinal);

    public string Issue()
    {
        CleanupExpired();
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        tokens[token] = DateTimeOffset.UtcNow.Add(TokenLifetime);
        return token;
    }

    public bool Consume(string token)
    {
        if (string.IsNullOrWhiteSpace(token) || !tokens.TryRemove(token, out var expiresAt))
        {
            return false;
        }

        return expiresAt > DateTimeOffset.UtcNow;
    }

    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (token, expiresAt) in tokens)
        {
            if (expiresAt <= now)
            {
                tokens.TryRemove(token, out _);
            }
        }
    }
}
