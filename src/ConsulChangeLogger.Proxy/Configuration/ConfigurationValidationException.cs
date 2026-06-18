namespace ConsulChangeLogger.Proxy.Configuration;

internal sealed class ConfigurationValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ConfigurationValidationException(IEnumerable<string> errors)
        : base(BuildMessage(errors))
    {
        Errors = errors.ToArray();
    }

    private static string BuildMessage(IEnumerable<string> errors)
    {
        var materialized = errors.Where(static error => !string.IsNullOrWhiteSpace(error)).ToArray();
        return materialized.Length == 0
            ? "Configuration validation failed."
            : "Configuration validation failed:" + Environment.NewLine + string.Join(Environment.NewLine, materialized.Select(static error => $"- {error}"));
    }
}
