using ConsulChangeLogger.Proxy.Configuration;

namespace ConsulChangeLogger.Proxy.Authentication;

internal sealed record AuthOptions
{
    public string Mode { get; init; } = "disabled";
    public string MockPassword { get; init; } = "Passw0rd!";
    public string LdapUrl { get; init; } = "ldap://localhost:389";
    public string LdapBindDn { get; init; } = string.Empty;
    public string LdapBindPassword { get; init; } = string.Empty;
    public string LdapBaseDn { get; init; } = string.Empty;
    public string LdapUserFilter { get; init; } = "(mail={0})";

    public static AuthOptions FromConfiguration(IReadOnlyDictionary<string, string> config) => new()
    {
        Mode = ConfigValue.ReadString(config, "AUTH_MODE", "disabled"),
        MockPassword = ConfigValue.ReadString(config, "AUTH_MOCK_PASSWORD", "Passw0rd!"),
        LdapUrl = ConfigValue.ReadString(config, "LDAP_URL", "ldap://localhost:389"),
        LdapBindDn = ConfigValue.ReadString(config, "LDAP_BIND_DN", string.Empty),
        LdapBindPassword = Environment.GetEnvironmentVariable("LDAP_BIND_PASSWORD") ?? string.Empty,
        LdapBaseDn = ConfigValue.ReadString(config, "LDAP_BASE_DN", string.Empty),
        LdapUserFilter = ConfigValue.ReadString(config, "LDAP_USER_FILTER", "(mail={0})")
    };
}
