using ConsulChangeLogger.Proxy;

namespace ConsulChangeLogger.Tests;

public sealed class ConsulKvChangeHelpersTests
{
    [Theory]
    [InlineData("/v1/kv", true)]
    [InlineData("/v1/kv/app/config", true)]
    [InlineData("/v1/kv/app/config?raw", true)]
    [InlineData("/v1/status/leader", false)]
    public void IsKvPath_ReturnsExpectedResult(string path, bool expected)
    {
        Assert.Equal(expected, ConsulKvChangeHelpers.IsKvPath(path));
    }

    [Fact]
    public void KvKeyFromPath_DecodesEscapedSegments()
    {
        var key = ConsulKvChangeHelpers.KvKeyFromPath("/v1/kv/app%2Fconfig/value?dc=dc1");

        Assert.Equal("app/config/value", key);
    }

    [Theory]
    [InlineData("app/config/", true)]
    [InlineData("app/config/value", false)]
    [InlineData("", false)]
    public void IsFolderKey_ReturnsExpectedResult(string key, bool expected)
    {
        Assert.Equal(expected, ConsulKvChangeHelpers.IsFolderKey(key));
    }

    [Theory]
    [InlineData("GET", "kv_read")]
    [InlineData("PUT", "kv_write")]
    [InlineData("DELETE", "kv_delete")]
    [InlineData("PATCH", "kv_other")]
    public void KvAction_NormalizesMethods(string method, string expected)
    {
        Assert.Equal(expected, ConsulKvChangeHelpers.KvAction(method));
    }

    [Fact]
    public void ExtractReadValue_DecodesRawResponse()
    {
        var result = ConsulKvChangeHelpers.ExtractReadValue("/v1/kv/app/key?raw", "{ \"a\": 1 }");

        Assert.Equal("{ \"a\": 1 }", result);
    }

    [Fact]
    public void ExtractReadValue_DecodesBase64ValueFromConsulEnvelope()
    {
        var responseBody = """[{ "Value": "eyAiYSIgOiAxIH0=" }]""";

        var result = ConsulKvChangeHelpers.ExtractReadValue("/v1/kv/app/key", responseBody);

        Assert.Equal("{ \"a\" : 1 }", result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-json")]
    public void ExtractReadValue_ReturnsOriginalBody_WhenResponseIsEmptyOrNotJson(string responseBody)
    {
        var result = ConsulKvChangeHelpers.ExtractReadValue("/v1/kv/app/key", responseBody);

        Assert.Equal(responseBody, result);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""[{ "Value": null }]""")]
    [InlineData("""[{ "Key": "app/key" }]""")]
    [InlineData("""[{ "Value": "one" }, { "Value": "two" }]""")]
    public void ExtractReadValue_ReturnsNull_ForUnsupportedConsulEnvelope(string responseBody)
    {
        var result = ConsulKvChangeHelpers.ExtractReadValue("/v1/kv/app/key", responseBody);

        Assert.Null(result);
    }

    [Fact]
    public void ExtractReadValue_ReturnsEmptyString_ForEmptyBase64Value()
    {
        var result = ConsulKvChangeHelpers.ExtractReadValue("/v1/kv/app/key", """[{ "Value": "" }]""");

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void ExtractReadValue_ReturnsOriginalBody_ForInvalidBase64Value()
    {
        var responseBody = """[{ "Value": "not-base64" }]""";

        var result = ConsulKvChangeHelpers.ExtractReadValue("/v1/kv/app/key", responseBody);

        Assert.Equal(responseBody, result);
    }

    [Fact]
    public void BuildMutationPrefetchPath_AddsRawToSingleKeyDelete()
    {
        var result = ConsulKvChangeHelpers.BuildMutationPrefetchPath("/v1/kv/app/key?dc=dc1");

        Assert.Equal("/v1/kv/app/key?dc=dc1&raw", result);
    }

    [Fact]
    public void BuildMutationPrefetchPath_ReturnsNullForRecurseDelete()
    {
        var result = ConsulKvChangeHelpers.BuildMutationPrefetchPath("/v1/kv/app/key?recurse");

        Assert.Null(result);
    }

    [Theory]
    [InlineData("/v1/status/leader")]
    [InlineData("/v1/kv")]
    [InlineData("/v1/kv/?dc=dc1")]
    [InlineData("/v1/kv/app/key?keys")]
    public void BuildMutationPrefetchPath_ReturnsNull_ForUnsupportedMutationTargets(string path)
    {
        var result = ConsulKvChangeHelpers.BuildMutationPrefetchPath(path);

        Assert.Null(result);
    }

    [Fact]
    public void BuildMutationPrefetchPath_RemovesExistingRawAndPreservesOtherQueryParameters()
    {
        var result = ConsulKvChangeHelpers.BuildMutationPrefetchPath("/v1/kv/app/key?dc=dc1&raw&flags=0");

        Assert.Equal("/v1/kv/app/key?dc=dc1&flags=0&raw", result);
    }

    [Fact]
    public void InspectJson_ReturnsValidJson_ForObjectPayload()
    {
        var result = ConsulKvChangeHelpers.InspectJson("{\"a\":1}");

        Assert.True(result.LooksLikeJson);
        Assert.True(result.IsValidJson);
        Assert.Null(result.Error);
        Assert.Equal("valid_json", ConsulKvChangeHelpers.JsonValidationStatus(result));
    }

    [Fact]
    public void InspectJson_ReturnsInvalidJson_ForBrokenObjectPayload()
    {
        var result = ConsulKvChangeHelpers.InspectJson("{\"a\":1");

        Assert.True(result.LooksLikeJson);
        Assert.False(result.IsValidJson);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
        Assert.Equal("invalid_json", ConsulKvChangeHelpers.JsonValidationStatus(result));
    }

    [Theory]
    [InlineData("plain-text")]
    [InlineData("123")]
    [InlineData("true")]
    [InlineData("")]
    public void InspectJson_ReturnsNotJson_ForNonObjectNonArrayPayloads(string value)
    {
        var result = ConsulKvChangeHelpers.InspectJson(value);

        Assert.False(result.LooksLikeJson);
        Assert.Null(result.IsValidJson);
        Assert.Null(result.Error);
        Assert.Equal("not_json", ConsulKvChangeHelpers.JsonValidationStatus(result));
    }
}
