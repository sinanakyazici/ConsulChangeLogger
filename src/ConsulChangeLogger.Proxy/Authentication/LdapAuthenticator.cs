using ConsulChangeLogger.Proxy.Configuration;
using Novell.Directory.Ldap;
using Serilog;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace ConsulChangeLogger.Proxy.Authentication;

internal sealed class LdapAuthenticator
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private readonly LdapConfiguration options;

    public LdapAuthenticator(LdapConfiguration options)
    {
        this.options = options;
    }

    public async Task<bool> AuthenticateAsync(string username, string password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            Log.Warning("LDAP authentication rejected because username or password is empty");
            return false;
        }

        var port = options.UseSSL!.Value ? options.SecurePort!.Value : options.Port!.Value;

        try
        {
            Log.Debug(
                "Starting LDAP direct bind for {Username} against {Domain}:{Port} UseSSL={UseSSL}",
                username,
                options.Domain,
                port,
                options.UseSSL);

            using var userConnection = CreateConnection();
            await userConnection.ConnectAsync(options.Domain, port, cancellationToken);
            await userConnection.BindAsync(username, password, cancellationToken);
            Log.Information("LDAP authentication succeeded for {Username}", username);
            return true;
        }
        catch (LdapException ex)
        {
            Log.Warning(
                ex,
                "LDAP authentication failed for {Username}. ServerError={ServerError} ErrorCode={ErrorCode}",
                username,
                ex.LdapErrorMessage,
                ex.ResultCode);
            return false;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected LDAP authentication error for {Username}", username);
            return false;
        }
    }

    public async Task WaitForAvailabilityAsync(CancellationToken cancellationToken)
    {
        var port = options.UseSSL!.Value ? options.SecurePort!.Value : options.Port!.Value;
        var deadline = DateTimeOffset.UtcNow.Add(StartupTimeout);
        Exception? lastError = null;

        Log.Information(
            "Waiting for LDAP availability at {Domain}:{Port} UseSSL={UseSSL}",
            options.Domain,
            port,
            options.UseSSL);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var connection = CreateConnection();
                await connection.ConnectAsync(options.Domain, port, cancellationToken);
                connection.Disconnect();

                Log.Information(
                    "LDAP is reachable at {Domain}:{Port} UseSSL={UseSSL}",
                    options.Domain,
                    port,
                    options.UseSSL);
                return;
            }
            catch (LdapException error)
            {
                lastError = error;
            }
            catch (Exception error)
            {
                lastError = error;
            }

            Log.Information(
                "Waiting for LDAP availability at {Domain}:{Port}; retrying in {RetryDelaySeconds} seconds. Reason: {Reason}",
                options.Domain,
                port,
                RetryDelay.TotalSeconds,
                lastError?.Message ?? "unknown");

            await Task.Delay(RetryDelay, cancellationToken);
        }

        throw new TimeoutException(
            $"LDAP at '{options.Domain}:{port}' was not available within {StartupTimeout.TotalSeconds:0} seconds.",
            lastError);
    }

    private LdapConnection CreateConnection()
    {
        var connectionOptions = new LdapConnectionOptions();
        if (options.UseSSL!.Value)
        {
            var remoteCertCallback = new System.Net.Security.RemoteCertificateValidationCallback(RemoteCertValidation);
            connectionOptions = connectionOptions
                .UseSsl()
                .ConfigureSslProtocols(SslProtocols.None)
                .ConfigureRemoteCertificateValidationCallback(remoteCertCallback);
        }

        return new LdapConnection(connectionOptions);
    }

    private bool RemoteCertValidation(
        object? sender,
        X509Certificate? certificate,
        X509Chain? chain,
        SslPolicyErrors sslPolicyErrors) => true;
}
