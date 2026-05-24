using System.Text;
using System.Text.Json;
using ConsulChangeLogger.Core;

var tests = new List<(string Name, Action Test)>
{
    ("extracts raw value", () =>
    {
        Equal("{ \"a\" : 1 }", ConsulKvChangeHelpers.ExtractReadValue("/v1/kv/test/test1?raw", "{ \"a\" : 1 }"));
    }),
    ("decodes consul kv value", () =>
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("{ \"a\" : 1 }"));
        var body = JsonSerializer.Serialize(new[] { new { Key = "test/test1", Value = encoded } });
        Equal("{ \"a\" : 1 }", ConsulKvChangeHelpers.ExtractReadValue("/v1/kv/test/test1?dc=dc1", body));
    }),
    ("ignores key list response", () =>
    {
        IsNull(ConsulKvChangeHelpers.ExtractReadValue("/v1/kv/?keys", JsonSerializer.Serialize(new[] { "test/" })));
    }),
    ("ignores multi key response", () =>
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("value"));
        var body = JsonSerializer.Serialize(new[]
        {
            new { Key = "test/a", Value = encoded },
            new { Key = "test/b", Value = encoded }
        });
        IsNull(ConsulKvChangeHelpers.ExtractReadValue("/v1/kv/test?recurse", body));
    }),
    ("extracts kv key without query", () =>
    {
        Equal("test/test1", ConsulKvChangeHelpers.KvKeyFromPath("/v1/kv/test/test1?dc=dc1"));
    })
};

foreach (var test in tests)
{
    test.Test();
    Console.WriteLine($"PASS {test.Name}");
}

static void Equal(string expected, string? actual)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'");
    }
}

static void IsNull(string? actual)
{
    if (actual is not null)
    {
        throw new InvalidOperationException($"Expected null, got '{actual}'");
    }
}
