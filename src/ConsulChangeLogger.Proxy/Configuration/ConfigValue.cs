namespace ConsulChangeLogger.Proxy.Configuration;

internal static class ConfigValue
{
    public static string ReadString(IReadOnlyDictionary<string, string> config, string name, string fallback) =>
        config.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name))
                ? fallback
                : Environment.GetEnvironmentVariable(name)!;
}
