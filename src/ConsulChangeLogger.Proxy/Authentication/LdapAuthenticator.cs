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

    public Task<bool> AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            Log.Warning("LDAP authentication rejected because username or password is empty");
            return Task.FromResult(false);
        }

        return Task.Run(() => Authenticate(username, password), cancellationToken);
    }

    private bool Authenticate(string username, string password)
    {
        var port = options.UseSSL ? options.SecurePort : options.Port;

        try
        {
            Log.Debug(
                "Starting LDAP direct bind for {Username} against {Domain}:{Port} UseSSL={UseSSL}",
                username,
                options.Domain,
                port,
                options.UseSSL);

            using var userConnection = CreateConnection();
            userConnection.AuthType = AuthType.Basic;
            userConnection.Bind(new NetworkCredential(username, password));
            Log.Information("LDAP authentication succeeded for {Username}", username);
            return true;
        }
        catch (LdapException ex)
        {
            Log.Warning(
                ex,
                "LDAP authentication failed for {Username}. ServerError={ServerError} ErrorCode={ErrorCode}",
                username,
                ex.ServerErrorMessage,
                ex.ErrorCode);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected LDAP authentication error for {Username}", username);
            return false;
        }
    }

    private LdapConnection CreateConnection()
    {
        var port = options.UseSSL ? options.SecurePort : options.Port;
        var identifier = new LdapDirectoryIdentifier(options.Domain, port);
        var connection = new LdapConnection(identifier);
        connection.SessionOptions.SecureSocketLayer = options.UseSSL;
        if (options.UseSSL)
        {
            connection.SessionOptions.VerifyServerCertificate += (_, _) => true;
        }
        connection.Timeout = TimeSpan.FromSeconds(10);
        return connection;
    }
}
