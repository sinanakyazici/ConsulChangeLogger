using System.DirectoryServices.Protocols;
using System.Net;
using ConsulChangeLogger.Proxy.Configuration;
using Serilog;

namespace ConsulChangeLogger.Proxy.Authentication;

internal sealed class LdapAuthenticator
{
    private readonly LdapConfiguration options;

    public LdapAuthenticator(LdapConfiguration options)
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

            return true;
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
        if (!string.IsNullOrWhiteSpace(options.BindDn))
        {
            connection.Bind(new NetworkCredential(options.BindDn, options.BindCredentials));
        }

        var filter = string.Format(options.SearchFilter, EscapeLdapFilterValue(email));
        var request = new SearchRequest(options.SearchBase, filter, SearchScope.Subtree, "distinguishedName", "dn", "mail", "userPrincipalName");
        var response = (SearchResponse)connection.SendRequest(request);
        return response.Entries.Count == 0 ? null : response.Entries[0].DistinguishedName;
    }

    private LdapConnection CreateConnection()
    {
        var port = options.UseSSL ? options.SecurePort : options.Port;
        var identifier = new LdapDirectoryIdentifier(options.Domain, port);
        var connection = new LdapConnection(identifier);
        connection.SessionOptions.SecureSocketLayer = options.UseSSL;
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
