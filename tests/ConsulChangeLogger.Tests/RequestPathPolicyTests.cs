using ConsulChangeLogger.Proxy.Security;
using Microsoft.AspNetCore.Http;

namespace ConsulChangeLogger.Tests;

public sealed class RequestPathPolicyTests
{
    [Theory]
    [InlineData("/", true)]
    [InlineData("/ui", true)]
    [InlineData("/ui/", true)]
    [InlineData("/ui/services", true)]
    [InlineData("/v1/kv/app/key", false)]
    [InlineData("/v1/status/leader", false)]
    [InlineData("/login", false)]
    public void RequiresAuthenticatedUiSession_ReturnsExpectedResult(string path, bool expected)
    {
        Assert.Equal(expected, RequestPathPolicy.RequiresAuthenticatedUiSession(new PathString(path)));
    }

    [Theory]
    [InlineData("/v1", true)]
    [InlineData("/v1/kv/app/key", true)]
    [InlineData("/v1/status/leader", true)]
    [InlineData("/ui", false)]
    [InlineData("/login", false)]
    public void IsConsulApiPath_ReturnsExpectedResult(string path, bool expected)
    {
        Assert.Equal(expected, RequestPathPolicy.IsConsulApiPath(new PathString(path)));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    [InlineData("", false)]
    public void IsMarkedUiRequest_ReturnsExpectedResult(string headerValue, bool expected)
    {
        var headers = new HeaderDictionary();
        headers[RequestPathPolicy.UiRequestHeaderName] = headerValue;

        Assert.Equal(expected, RequestPathPolicy.IsMarkedUiRequest(headers));
    }

    [Fact]
    public void IsMarkedUiRequest_ReturnsFalse_WhenHeaderIsMissing()
    {
        Assert.False(RequestPathPolicy.IsMarkedUiRequest(new HeaderDictionary()));
    }
}
