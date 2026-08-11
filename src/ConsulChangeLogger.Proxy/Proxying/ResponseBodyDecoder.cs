using System.IO.Compression;

namespace ConsulChangeLogger.Proxy.Proxying;

internal static class ResponseBodyDecoder
{
    public static byte[] DecodeForAudit(byte[] responseBodyBytes, IEnumerable<string> contentEncodings)
    {
        var decodedBytes = responseBodyBytes;
        foreach (var encoding in contentEncodings.SelectMany(SplitContentEncoding).Reverse())
        {
            decodedBytes = DecodeSingleEncoding(decodedBytes, encoding);
        }

        return decodedBytes;
    }

    private static IEnumerable<string> SplitContentEncoding(string value) =>
        value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static byte[] DecodeSingleEncoding(byte[] responseBodyBytes, string contentEncoding)
    {
        if (contentEncoding.Equals("identity", StringComparison.OrdinalIgnoreCase))
        {
            return responseBodyBytes;
        }

        using var input = new MemoryStream(responseBodyBytes);
        using var output = new MemoryStream();
        Stream? decoder = contentEncoding.ToLowerInvariant() switch
        {
            "gzip" => new GZipStream(input, CompressionMode.Decompress),
            "br" => new BrotliStream(input, CompressionMode.Decompress),
            "deflate" => new DeflateStream(input, CompressionMode.Decompress),
            _ => null
        };

        if (decoder is null)
        {
            return responseBodyBytes;
        }

        try
        {
            using (decoder)
            {
                decoder.CopyTo(output);
                return output.ToArray();
            }
        }
        catch (InvalidDataException)
        {
            return responseBodyBytes;
        }
    }
}
