extern alias PerronMcp;

using System.Net;
using System.Text;
using System.Web;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using Perron.Bench.Checks;

namespace Perron.Bench.Tests;

/// <summary>
/// Perron MCP running in memory as the server under check. Its calls to transport.opendata.ch are answered from the
/// recorded fixtures, so no test reaches the real API.
/// </summary>
public sealed class PerronMcpFactory : WebApplicationFactory<PerronMcp::Program>
{
    /// <summary>Routes the bench's "mcp" HTTP client to the in-memory server (keeps the resilience pipeline).</summary>
    public void RouteBenchClientToServer(IServiceCollection services) =>
        services.AddHttpClient(McpServerCheck.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => Server.CreateHandler());

    protected override void ConfigureWebHost(IWebHostBuilder builder) =>
        builder.ConfigureTestServices(services =>
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => new TransportFixtureHandler())));
}

/// <summary>Answers transport.opendata.ch requests with recorded responses (tests/fixtures/transport-opendata).</summary>
public sealed class TransportFixtureHandler : HttpMessageHandler
{
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "transport-opendata");

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var file = uri.AbsolutePath switch
        {
            var path when path.EndsWith("/locations", StringComparison.Ordinal) => HttpUtility.ParseQueryString(uri.Query)["query"] switch
            {
                "Bern" => "locations-bern-station.json",
                "Zürich HB" => "locations-zurich-hb.json",
                _ => "locations-unknown.json",
            },
            var path when path.EndsWith("/connections", StringComparison.Ordinal) => "connections-zurich-bern-tomorrow-0800.json",
            _ => "stationboard-bern-departures.json",
        };

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(File.ReadAllText(Path.Combine(Directory, file)), Encoding.UTF8, "application/json"),
        });
    }
}

/// <summary>Hands out clients created by a delegate, e.g. for an in-memory test server.</summary>
public sealed class DelegateHttpClientFactory(Func<HttpClient> create) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => create();
}
