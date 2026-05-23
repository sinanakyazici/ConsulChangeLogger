namespace ConsulChangeLogger.Proxy.Health;

internal static class HttpHealthCheck
{
    public static async Task<bool> IsReadyAsync(HttpClient httpClient, string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await httpClient.GetAsync(path, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
