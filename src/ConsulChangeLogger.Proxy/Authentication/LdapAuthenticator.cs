using System.DirectoryServices.Protocols;
using System.Net;

namespace ConsulChangeLogger.Proxy.Authentication;

internal sealed class LdapAuthenticator
{
    private readonly AuthOptions options;

    public LdapAuthenticator(AuthOptions options)
    {
        this.options = options;
    }

    public Task<bool> AuthenticateAsync(string email, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return Task.FromResult(false);
        }

        return Task.Run(() => Authenticate(email, password), cancellationToken);
    }

    private bool Authenticate(string email, string password)
    {
        if (options.Mode.Equals("disabled", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (options.Mode.Equals("mock", StringComparison.OrdinalIgnoreCase))
        {
            return password == options.MockPassword;
        }

        if (!options.Mode.Equals("ldap", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            var userDn = FindUserDn(email);
            if (string.IsNullOrWhiteSpace(userDn))
            {
                return false;
            }

            using (var userConnection = CreateConnection())
            {
                userConnection.AuthType = AuthType.Basic;
                userConnection.Bind(new NetworkCredential(userDn, password));
            }

            return IsUserAllowed(userDn, email);
        }
        catch (LdapException)
        {
            return false;
        }
    }

    private string? FindUserDn(string email)
    {
        using var connection = CreateConnection();
        connection.AuthType = AuthType.Basic;
        if (!string.IsNullOrWhiteSpace(options.LdapBindDn))
        {
            connection.Bind(new NetworkCredential(options.LdapBindDn, options.LdapBindPassword));
        }

        var filter = string.Format(options.LdapUserFilter, EscapeLdapFilterValue(email));
        var request = new SearchRequest(options.LdapBaseDn, filter, SearchScope.Subtree, "distinguishedName", "dn", "mail", "userPrincipalName");
        var response = (SearchResponse)connection.SendRequest(request);
        return response.Entries.Count == 0 ? null : response.Entries[0].DistinguishedName;
    }

    private bool IsUserAllowed(string userDn, string email)
    {
        if (string.IsNullOrWhiteSpace(options.LdapGroupFilter))
        {
            return true;
        }

        using var connection = CreateConnection();
        connection.AuthType = AuthType.Basic;
        if (!string.IsNullOrWhiteSpace(options.LdapBindDn))
        {
            connection.Bind(new NetworkCredential(options.LdapBindDn, options.LdapBindPassword));
        }

        var filter = string.Format(
            options.LdapGroupFilter,
            EscapeLdapFilterValue(userDn),
            EscapeLdapFilterValue(email));
        var request = new SearchRequest(options.LdapBaseDn, filter, SearchScope.Subtree, "distinguishedName", "cn");
        var response = (SearchResponse)connection.SendRequest(request);
        return response.Entries.Count > 0;
    }

    private LdapConnection CreateConnection()
    {
        var uri = new Uri(options.LdapUrl);
        var identifier = new LdapDirectoryIdentifier(uri.Host, uri.Port > 0 ? uri.Port : (uri.Scheme == "ldaps" ? 636 : 389));
        var connection = new LdapConnection(identifier);
        connection.SessionOptions.SecureSocketLayer = uri.Scheme.Equals("ldaps", StringComparison.OrdinalIgnoreCase);
        connection.Timeout = TimeSpan.FromSeconds(10);
        return connection;
    }

    private static string EscapeLdapFilterValue(string value) =>
        value
            .Replace(@"\", @"\5c", StringComparison.Ordinal)
            .Replace("*", @"\2a", StringComparison.Ordinal)
            .Replace("(", @"\28", StringComparison.Ordinal)
            .Replace(")", @"\29", StringComparison.Ordinal)
            .Replace("\0", @"\00", StringComparison.Ordinal);
}
