extern alias PerronMcp;

using Microsoft.Extensions.DependencyInjection;

using Perron.Bench.Checks;
using Perron.Bench.Settings;

using McpTool = ModelContextProtocol.Protocol.Tool;

namespace Perron.Bench.Tests;

/// <summary><c>bench check</c> against Perron MCP in memory (HTTP) and as a child process (stdio); AK-7.1, AK-7.2.</summary>
public sealed class BenchCliTests(PerronMcpFactory server) : IClassFixture<PerronMcpFactory>
{
    private const string InMemoryUrl = "http://localhost/mcp";
    private static readonly string RepositoryBench = Path.Combine(AppContext.BaseDirectory, "bench");

    [Fact]
    public async Task Check_PerronRich_ListsTools_HashesThem_AndShowsCoverage()
    {
        var (exitCode, output, _) = await RunAsync(Settings(), "check", "--servers", "perron-rich", "--expect-tools", "find_stops,get_departures,plan_trip");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.StartsWith("perron-rich     OK      serverInfo perron-rich ", output);
        Assert.Contains("tools (3): find_stops, get_departures, plan_trip", output);
        Assert.Matches(@"tools/list [0-9a-f]{12} ", output);
        Assert.Contains("tool tokens n/a", output);
        Assert.Contains("covers stops, trips, departures", output);
    }

    [Fact]
    public async Task Check_WithATokenCounter_ShowsTheToolTokens()
    {
        var counter = new FakeTokenCounter(1234);

        var (exitCode, output, _) = await RunAsync(Settings(), services => services.AddSingleton<IToolTokenCounter>(counter), "check", "--servers", "perron-rich");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("tool tokens 1234", output);
        Assert.Equal(["find_stops", "get_departures", "plan_trip"], counter.Counted.Select(tool => tool.Name).Order());
    }

    [Fact]
    public async Task Check_ExpectedToolsDiffer_ReturnsOneAndNamesTheDifference()
    {
        var (exitCode, output, _) = await RunAsync(Settings(), "check", "--servers", "perron-rich", "--expect-tools", "find_stops,get_departures,plan_trip,show_disruptions");

        Assert.Equal(ExitCodes.CheckFailed, exitCode);
        Assert.Contains("FAIL", output);
        Assert.Contains("missing: show_disruptions | unexpected: -", output);
    }

    [Fact]
    public async Task Check_StdioServer_StartsTheCommand_WithItsMappedEnvironment()
    {
        // Perron MCP itself as a stdio server: no third-party code in tests. The registry maps the process variable
        // PERRON_DESCRIPTIONS to TEST_DESCRIPTIONS from the environment, so the server reports perron-minimal.
        var serverDll = typeof(PerronMcp::Program).Assembly.Location;
        using var bench = TemporaryBench($$"""
            { "id": "perron-stdio", "transport": "stdio", "command": "dotnet", "args": [{{Json(serverDll)}}, "--stdio"],
              "env": { "PERRON_DESCRIPTIONS": "TEST_DESCRIPTIONS" }, "version": "test",
              "capabilities": [{ "name": "stops", "reason": "find_stops" }] }
            """);

        var (exitCode, output, _) = await RunAsync(Settings(bench.Directory, ["TEST_DESCRIPTIONS", "minimal"]), "check");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.StartsWith("perron-stdio    OK      serverInfo perron-minimal ", output);
        Assert.Contains("tools (3)", output);
    }

    [Fact]
    public async Task Check_OptionalServerWithoutItsKey_IsSkipped_ARequiredOneFails()
    {
        using var bench = TemporaryBench(
            """{ "id": "extra", "transport": "http", "url": "http://localhost/mcp", "version": "1", "optional": true, "env_required": ["EXTRA_KEY"], "capabilities": [{ "name": "stops", "reason": "r" }] }""",
            """{ "id": "needed", "transport": "http", "url": "http://localhost/mcp", "version": "1", "env_required": ["NEEDED_KEY"], "capabilities": [{ "name": "stops", "reason": "r" }] }""");

        var (exitCode, output, _) = await RunAsync(Settings(bench.Directory), "check");

        Assert.Equal(ExitCodes.CheckFailed, exitCode);
        Assert.Contains("extra           SKIPPED missing EXTRA_KEY (optional server)", output);
        Assert.Contains("needed          FAIL    missing NEEDED_KEY", output);
    }

    [Fact]
    public async Task Check_OptionalServerAlone_IsSkipped_WithoutFailing()
    {
        using var bench = TemporaryBench(
            """{ "id": "extra", "transport": "http", "url": "http://localhost/mcp", "version": "1", "optional": true, "env_required": ["EXTRA_KEY"], "capabilities": [{ "name": "stops", "reason": "r" }] }""");

        var (exitCode, _, _) = await RunAsync(Settings(bench.Directory), "check");

        Assert.Equal(ExitCodes.Success, exitCode);
    }

    [Fact]
    public async Task Check_HostedServerWithOtherTools_IsChanged_ButNotAFailure()
    {
        using var bench = TemporaryBench($$"""
            { "id": "hosted", "transport": "http", "url": "{{InMemoryUrl}}", "hosted": true, "tools_hash": "{{new string('0', 64)}}",
              "capabilities": [{ "name": "stops", "reason": "r" }] }
            """);

        var (exitCode, output, _) = await RunAsync(Settings(bench.Directory), "check");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.StartsWith("hosted          CHANGED ", output);
        Assert.Contains("tools/list differs from servers.json (000000000000)", output);
    }

    [Theory]
    [InlineData(null, "covers stops (not covered: occupancy needs OCC_KEY)")]
    [InlineData("secret", "covers stops, occupancy")]
    public async Task Check_CapabilityWithoutItsKey_IsNotCovered(string? key, string coverage)
    {
        using var bench = TemporaryBench($$"""
            { "id": "keyed", "transport": "http", "url": "{{InMemoryUrl}}", "version": "1",
              "capabilities": [{ "name": "stops", "reason": "r" }, { "name": "occupancy", "reason": "r", "requires_env": ["OCC_KEY"] }] }
            """);

        var (_, output, _) = await RunAsync(Settings(bench.Directory, key is null ? [] : ["OCC_KEY", key]), "check");

        Assert.Contains(coverage, output);
    }

    [Fact]
    public async Task Check_UnreachableServer_FailsWithExitCodeOne()
    {
        using var bench = TemporaryBench(
            """{ "id": "gone", "transport": "stdio", "command": "perron-no-such-command", "version": "1", "capabilities": [{ "name": "stops", "reason": "r" }] }""");

        var (exitCode, output, _) = await RunAsync(Settings(bench.Directory), "check");

        Assert.Equal(ExitCodes.CheckFailed, exitCode);
        Assert.Contains("gone            FAIL    not reachable via perron-no-such-command", output);
    }

    [Fact]
    public async Task Check_UnknownServer_ReturnsUsageError_AndListsTheRegistry()
    {
        var (exitCode, _, error) = await RunAsync(Settings(), "check", "--servers", "perron");

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Contains("Unknown server(s): perron. Servers in bench/servers.json: perron-rich, perron-minimal, perron-json, perron-many, official, spt, stm.", error);
    }

    [Fact]
    public async Task Check_MissingRegistry_ReturnsUsageError()
    {
        var empty = Directory.CreateTempSubdirectory("perron-bench-");
        try
        {
            var (exitCode, _, error) = await RunAsync(Settings(empty.FullName), "check");

            Assert.Equal(ExitCodes.UsageError, exitCode);
            Assert.Contains("Server registry not found", error);
        }
        finally
        {
            empty.Delete(true);
        }
    }

    [Fact]
    public async Task Check_InvalidEndpointUrl_ReturnsUsageError()
    {
        var (exitCode, _, error) = await RunAsync(Settings(RepositoryBench, ["PERRON_MCP_URL", "not-a-url"]), "check", "--servers", "perron-rich");

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Contains("PERRON_MCP_URL must be an absolute http(s) URL, got 'not-a-url'", error);
    }

    [Fact]
    public async Task UnknownOption_ReturnsUsageError()
    {
        var (exitCode, _, error) = await RunAsync(Settings(), "check", "--no-such-option");

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Contains("--help", error);
    }

    /// <summary>Repository registry, PERRON_MCP_URL pointing to the in-memory server, plus extra key/value pairs.</summary>
    private static Dictionary<string, string?> Settings(string? directory = null, string[]? pairs = null)
    {
        var settings = new Dictionary<string, string?>
        {
            [BenchDirectoryOptions.EnvironmentVariable] = directory ?? RepositoryBench,
            ["PERRON_MCP_URL"] = InMemoryUrl,
            ["ANTHROPIC_API_KEY"] = "",
        };
        for (var i = 0; i + 1 < (pairs ?? []).Length; i += 2)
        {
            settings[pairs![i]] = pairs[i + 1];
        }

        return settings;
    }

    private static TemporaryDirectory TemporaryBench(params string[] servers)
    {
        var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Directory, "servers.json"), $$"""{ "servers": [ {{string.Join(", ", servers)}} ] }""");
        File.Copy(Path.Combine(RepositoryBench, "bench.json"), Path.Combine(directory.Directory, "bench.json"));
        return directory;
    }

    private static string Json(string value) => System.Text.Json.JsonSerializer.Serialize(value);

    private Task<(int ExitCode, string Output, string Error)> RunAsync(Dictionary<string, string?> configuration, params string[] args) =>
        RunAsync(configuration, null, args);

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(
        Dictionary<string, string?> configuration, Action<IServiceCollection>? configure, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await BenchCli.RunAsync(
            args,
            output,
            error,
            configuration,
            services =>
            {
                server.RouteBenchClientToServer(services);
                configure?.Invoke(services);
            },
            TestContext.Current.CancellationToken);

        return (exitCode, output.ToString(), error.ToString());
    }

    private sealed class FakeTokenCounter(long tokens) : IToolTokenCounter
    {
        public List<McpTool> Counted { get; } = [];

        public bool IsAvailable => true;

        public Task<long?> CountAsync(IReadOnlyList<McpTool> tools, CancellationToken cancellationToken)
        {
            Counted.AddRange(tools);
            return Task.FromResult<long?>(tokens);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Directory { get; } = System.IO.Directory.CreateTempSubdirectory("perron-bench-").FullName;

        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
}
