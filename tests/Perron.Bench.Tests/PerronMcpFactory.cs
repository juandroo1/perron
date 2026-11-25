extern alias PerronMcp;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

using Perron.Bench.Checks;

namespace Perron.Bench.Tests;

/// <summary>Perron MCP running in memory as the server under check.</summary>
public sealed class PerronMcpFactory : WebApplicationFactory<PerronMcp::Program>
{
    /// <summary>Routes the bench's "mcp" HTTP client to the in-memory server (keeps the resilience pipeline).</summary>
    public void RouteBenchClientToServer(IServiceCollection services) =>
        services.AddHttpClient(McpServerCheck.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => Server.CreateHandler());
}

/// <summary>Hands out clients created by a delegate, e.g. for an in-memory test server.</summary>
public sealed class DelegateHttpClientFactory(Func<HttpClient> create) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => create();
}
