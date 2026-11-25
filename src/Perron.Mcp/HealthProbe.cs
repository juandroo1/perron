namespace Perron.Mcp;

/// <summary>
/// Container health probe, run as <c>dotnet Perron.Mcp.dll --healthcheck</c>.
/// The .NET runtime images ship without curl or wget, so the probe uses the server's own runtime.
/// </summary>
public static class HealthProbe
{
    public static readonly Uri DefaultUrl = new("http://localhost:8080/health");

    /// <summary>Returns exit code 0 if <paramref name="url"/> answers with a success status, otherwise 1.</summary>
    public static async Task<int> RunAsync(HttpClient client, Uri url, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await client.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException)
        {
            return 1;
        }
        catch (TaskCanceledException)
        {
            return 1;
        }
    }
}
