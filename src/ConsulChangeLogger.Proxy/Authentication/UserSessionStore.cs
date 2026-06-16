using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace ConsulChangeLogger.Proxy.Authentication;

internal sealed class UserSessionStore
{
    public const string CookieName = "consul_change_logger_session";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(8);
    private readonly ConcurrentDictionary<string, UserSession> sessions = new(StringComparer.Ordinal);

    public UserSession Create(string email)
    {
        CleanupExpired();
        var session = new UserSession(
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            email,
            DateTimeOffset.UtcNow.Add(SessionLifetime));
        sessions[session.Id] = session;
        return session;
    }

    public bool TryGet(string id, out UserSession session)
    {
        if (sessions.TryGetValue(id, out var existing) && existing.ExpiresAt > DateTimeOffset.UtcNow)
        {
            session = existing;
            return true;
        }

        sessions.TryRemove(id, out _);
        session = new UserSession(string.Empty, string.Empty, DateTimeOffset.MinValue);
        return false;
    }

    public void Remove(string id) => sessions.TryRemove(id, out _);

    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, session) in sessions)
        {
            if (session.ExpiresAt <= now)
            {
                sessions.TryRemove(id, out _);
            }
        }
    }
}
