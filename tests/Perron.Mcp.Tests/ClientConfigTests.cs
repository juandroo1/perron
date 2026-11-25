using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Perron.Mcp.Tests;

/// <summary>AK-6.2: <c>.mcp.json</c> binds Perron MCP from Docker Compose into Claude Code.</summary>
public sealed partial class ClientConfigTests
{
    [Fact]
    public void McpJson_PointsClaudeCodeToTheComposeService()
    {
        using var config = JsonDocument.Parse(File.ReadAllText(RepositoryFile(".mcp.json")));

        var servers = config.RootElement.GetProperty("mcpServers");
        var perron = Assert.Single(servers.EnumerateObject());
        Assert.Equal("perron", perron.Name);
        Assert.Equal("http", perron.Value.GetProperty("type").GetString());

        var url = new Uri(perron.Value.GetProperty("url").GetString()!);
        Assert.Equal("localhost", url.Host);
        Assert.Equal("/mcp", url.AbsolutePath);

        // The port must be the one compose publishes for the service mcp (on 127.0.0.1 only).
        var compose = File.ReadAllText(RepositoryFile("docker-compose.yml"));
        var published = PublishedPort().Match(compose);
        Assert.True(published.Success, "docker-compose.yml publishes no port on 127.0.0.1");
        Assert.Equal(published.Groups["port"].Value, url.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static string RepositoryFile(string name, [CallerFilePath] string callerFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", "..", name));

    [GeneratedRegex(@"""127\.0\.0\.1:(?<port>\d+):8080""")]
    private static partial Regex PublishedPort();
}
