using ConsulChangeLogger.Proxy.Proxying;
using System.IO.Compression;
using System.Text;

namespace ConsulChangeLogger.Tests;

public sealed class ConsulProxyTests
{
    [Theory]
    [InlineData("Accept-Encoding")]
    [InlineData("accept-encoding")]
    [InlineData("Host")]
    [InlineData("Connection")]
    [InlineData("Transfer-Encoding")]
    public void ShouldSkipUpstreamRequestHeader_SkipsHeadersThatMustNotBeForwarded(string headerName)
    {
        Assert.True(ConsulProxy.ShouldSkipUpstreamRequestHeader(headerName));
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("X-Consul-Token")]
    [InlineData("User-Agent")]
    [InlineData("X-Requested-With")]
    public void ShouldSkipUpstreamRequestHeader_AllowsRegularRequestHeaders(string headerName)
    {
        Assert.False(ConsulProxy.ShouldSkipUpstreamRequestHeader(headerName));
    }

    [Fact]
    public void DecodeResponseBodyForAudit_DecodesGzipPayload()
    {
        var payload = Encoding.UTF8.GetBytes("""[{ "Value": "eyAiYSIgOiAxIH0=" }]""");
        var compressed = Compress(payload, stream => new GZipStream(stream, CompressionMode.Compress));

        var result = ConsulProxy.DecodeResponseBodyForAudit(compressed, ["gzip"]);

        Assert.Equal(payload, result);
    }

    [Fact]
    public void DecodeResponseBodyForAudit_DecodesDeflatePayload()
    {
        var payload = Encoding.UTF8.GetBytes("""[{ "Value": "eyAiYSIgOiAxIH0=" }]""");
        var compressed = Compress(payload, stream => new DeflateStream(stream, CompressionMode.Compress));

        var result = ConsulProxy.DecodeResponseBodyForAudit(compressed, ["deflate"]);

        Assert.Equal(payload, result);
    }

    [Fact]
    public void DecodeResponseBodyForAudit_DecodesBrotliPayload()
    {
        var payload = Encoding.UTF8.GetBytes("""[{ "Value": "eyAiYSIgOiAxIH0=" }]""");
        var compressed = Compress(payload, stream => new BrotliStream(stream, CompressionMode.Compress));

        var result = ConsulProxy.DecodeResponseBodyForAudit(compressed, ["br"]);

        Assert.Equal(payload, result);
    }

    [Fact]
    public void DecodeResponseBodyForAudit_LeavesUnknownEncodingUnchanged()
    {
        var payload = Encoding.UTF8.GetBytes("""[{ "Value": "eyAiYSIgOiAxIH0=" }]""");

        var result = ConsulProxy.DecodeResponseBodyForAudit(payload, ["zstd"]);

        Assert.Equal(payload, result);
    }

    private static byte[] Compress(byte[] payload, Func<Stream, Stream> createStream)
    {
        using var output = new MemoryStream();
        using (var compressor = createStream(output))
        {
            compressor.Write(payload);
        }

        return output.ToArray();
    }
}
