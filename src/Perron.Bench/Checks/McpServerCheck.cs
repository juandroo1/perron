using Microsoft.Extensions.Logging;

using ModelContextProtocol.Client;

using Perron.Bench.Registry;

using McpTool = ModelContextProtocol.Protocol.Tool;

namespace Perron.Bench.Checks;

/// <summary>Result of connecting to one MCP server and listing its tools.</summary>
public sealed record ServerCheckResult(
    string ServerId,
    string? ServerName,
    string? ServerVersion,
    string? ProtocolVersion,
    IReadOnlyList<McpTool> Tools)
{
    public IReadOnlyList<string> ToolNames { get; } = [.. Tools.Select(tool => tool.Name).Order(StringComparer.Ordinal)];
}

/// <summary>Connects to an MCP server over Streamable HTTP or stdio and lists its tools.</summary>
public sealed class McpServerCheck(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory)
{
    public const string HttpClientName = "mcp";

    /// <param name="endpoint">For HTTP servers: the endpoint to use.</param>
    /// <param name="environment">For stdio servers: variables for the server process.</param>
    public async Task<ServerCheckResult> RunAsync(
        ServerDefinition server,
        Uri? endpoint,
        IReadOnlyDictionary<string, string?> environment,
        CancellationToken cancellationToken)
    {
        await using var client = await McpClient.CreateAsync(
            CreateTransport(server, endpoint, environment), loggerFactory: loggerFactory, cancellationToken: cancellationToken);

        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);

        return new ServerCheckResult(
            server.Id,
            client.ServerInfo?.Name,
            client.ServerInfo?.Version,
            client.NegotiatedProtocolVersion,
            [.. tools.Select(tool => tool.ProtocolTool)]);
    }

    private IClientTransport CreateTransport(
        ServerDefinition server, Uri? endpoint, IReadOnlyDictionary<string, string?> environment) =>
        server.Transport switch
        {
            ServerDefinition.Http => new HttpClientTransport(
                new HttpClientTransportOptions
                {
                    Endpoint = endpoint ?? new Uri(server.Url!, UriKind.Absolute),
                    Name = server.Id,
                    TransportMode = HttpTransportMode.StreamableHttp,
                },
                httpClientFactory.CreateClient(HttpClientName),
                loggerFactory,
                true),
            ServerDefinition.Stdio => new StdioClientTransport(
                new StdioClientTransportOptions
                {
                    Name = server.Id,
                    Command = server.Command!,
                    Arguments = [.. server.Args ?? []],
                    EnvironmentVariables = environment.ToDictionary(),
                },
                loggerFactory),
            _ => throw new InvalidOperationException($"Unknown transport '{server.Transport}'."),
        };
}
