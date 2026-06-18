namespace ConsulChangeLogger.Proxy.Configuration;

internal static class ConfigurationValidator
{
    public static void Validate(BootstrapOptions options)
    {
        var errors = new List<string>();

        RequireNonBlank(options.ConsulUpstreamUrl, "ConsulUpstreamUrl", errors);
        RequireNonBlank(options.ConfigKey, "ConfigKey", errors);
        RequireNotNull(options.Authentication, "Authentication", errors);

        if (!Uri.TryCreate(options.ConsulUpstreamUrl, UriKind.Absolute, out _))
        {
            errors.Add("ConsulUpstreamUrl must be a valid absolute URI.");
        }

        ThrowIfAny(errors);
    }

    public static void Validate(RuntimeConfiguration configuration)
    {
        var errors = new List<string>();

        RequireSection(configuration.Elasticsearch, "Elasticsearch", errors);
        RequireSection(configuration.ChangeLog, "ChangeLog", errors);
        RequireSection(configuration.LdapConfiguration, "LdapConfiguration", errors);

        RequireNonBlank(configuration.Elasticsearch.Url, "Elasticsearch.Url", errors);
        RequireNonBlank(configuration.Elasticsearch.Index, "Elasticsearch.Index", errors);

        RequireNonBlank(configuration.ChangeLog.OutboxPath, "ChangeLog.OutboxPath", errors);
        RequireNotNull(configuration.ChangeLog.ReadMatchWindowSeconds, "ChangeLog.ReadMatchWindowSeconds", errors);
        RequireNotNull(configuration.ChangeLog.QueueCapacity, "ChangeLog.QueueCapacity", errors);
        RequireNotNull(configuration.ChangeLog.RetentionDays, "ChangeLog.RetentionDays", errors);

        RequireNonBlank(configuration.LdapConfiguration.Domain, "LdapConfiguration.Domain", errors);
        RequireNotNull(configuration.LdapConfiguration.Port, "LdapConfiguration.Port", errors);
        RequireNotNull(configuration.LdapConfiguration.SecurePort, "LdapConfiguration.SecurePort", errors);
        RequireNotNull(configuration.LdapConfiguration.UseSSL, "LdapConfiguration.UseSSL", errors);

        if (!string.IsNullOrWhiteSpace(configuration.Elasticsearch.Url) &&
            !Uri.TryCreate(configuration.Elasticsearch.Url, UriKind.Absolute, out _))
        {
            errors.Add("Elasticsearch.Url must be a valid absolute URI.");
        }

        RequirePositive(configuration.ChangeLog.ReadMatchWindowSeconds, "ChangeLog.ReadMatchWindowSeconds", errors);
        RequirePositive(configuration.ChangeLog.QueueCapacity, "ChangeLog.QueueCapacity", errors);
        RequirePositive(configuration.ChangeLog.RetentionDays, "ChangeLog.RetentionDays", errors);
        RequirePort(configuration.LdapConfiguration.Port, "LdapConfiguration.Port", errors);
        RequirePort(configuration.LdapConfiguration.SecurePort, "LdapConfiguration.SecurePort", errors);

        ThrowIfAny(errors);
    }

    private static void RequireSection<T>(T section, string name, List<string> errors)
        where T : class
    {
        if (section is null)
        {
            errors.Add($"{name} section is required.");
        }
    }

    private static void RequireNonBlank(string? value, string name, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors.Add($"{name} is required.");
        }
    }

    private static void RequireNotNull<T>(T? value, string name, List<string> errors)
        where T : struct
    {
        if (!value.HasValue)
        {
            errors.Add($"{name} is required.");
        }
    }

    private static void RequirePositive(int? value, string name, List<string> errors)
    {
        if (value.HasValue && value.Value <= 0)
        {
            errors.Add($"{name} must be greater than 0.");
        }
    }

    private static void RequirePort(int? value, string name, List<string> errors)
    {
        if (value.HasValue && (value.Value < 1 || value.Value > 65535))
        {
            errors.Add($"{name} must be between 1 and 65535.");
        }
    }

    private static void ThrowIfAny(List<string> errors)
    {
        if (errors.Count > 0)
        {
            throw new ConfigurationValidationException(errors);
        }
    }
}
