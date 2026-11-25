using Microsoft.Extensions.Logging;

using ModelContextProtocol.Client;

namespace Perron.Bench.Checks;

/// <summary>Result of connecting to one MCP server and listing its tools.</summary>
public sealed record ServerCheckResult(
    string ServerId,
    string? ServerName,
    string? ServerVersion,
    string? ProtocolVersion,
    IReadOnlyList<string> ToolNames);

/// <summary>Connects to an MCP server over Streamable HTTP and lists its tools.</summary>
public sealed class McpServerCheck(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
{
    public const string HttpClientName = "mcp";

    public async Task<ServerCheckResult> RunAsync(string serverId, Uri endpoint, CancellationToken cancellationToken)
    {
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions
            {
                Endpoint = endpoint,
                Name = serverId,
                TransportMode = HttpTransportMode.StreamableHttp,
            },
            httpClientFactory.CreateClient(HttpClientName),
            loggerFactory,
            true);

        await using var client = await McpClient.CreateAsync(
            transport, loggerFactory: loggerFactory, cancellationToken: cancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

        return new ServerCheckResult(
            serverId,
            client.ServerInfo?.Name,
            client.ServerInfo?.Version,
            client.NegotiatedProtocolVersion,
            [.. tools.Select(tool => tool.Name).Order(StringComparer.Ordinal)]);
    }
}
