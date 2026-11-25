namespace Perron.Bench.Tests;

public sealed class BenchCliTests(PerronMcpFactory server) : IClassFixture<PerronMcpFactory>
{
    private static readonly Dictionary<string, string?> InMemoryEndpoint = new()
    {
        [PerronEndpointOptions.EnvironmentVariable] = "http://localhost/mcp",
    };

    [Fact]
    public async Task Check_ExpectedToolsMatch_ReturnsZeroAndPrintsOk()
    {
        var (exitCode, output, _) = await RunAsync("check", "--servers", "perron", "--expect-tools", "ping");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Contains("perron", output);
        Assert.Contains("OK", output);
        Assert.Contains("serverInfo perron", output);
        Assert.Contains("tools (1): ping", output);
    }

    [Fact]
    public async Task Check_AllServers_IsTheDefault()
    {
        var (exitCode, output, _) = await RunAsync("check");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.StartsWith("perron", output);
    }

    [Fact]
    public async Task Check_ExpectedToolsDiffer_ReturnsOneAndNamesTheDifference()
    {
        var (exitCode, output, _) = await RunAsync("check", "--servers", "perron", "--expect-tools", "find_stops,ping,plan_trip");

        Assert.Equal(ExitCodes.CheckFailed, exitCode);
        Assert.Contains("FAIL", output);
        Assert.Contains("missing: find_stops, plan_trip", output);
        Assert.Contains("unexpected: -", output);
    }

    [Fact]
    public async Task Check_UnknownServer_ReturnsUsageError()
    {
        var (exitCode, _, error) = await RunAsync("check", "--servers", "official");

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Contains("Unknown server(s): official", error);
        Assert.Contains("task 3.1", error);
    }

    [Fact]
    public async Task UnknownOption_ReturnsUsageError()
    {
        var (exitCode, _, error) = await RunAsync("check", "--no-such-option");

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Contains("--help", error);
    }

    [Fact]
    public async Task Check_InvalidEndpointUrl_FailsValidationAtStart()
    {
        var configuration = new Dictionary<string, string?> { [PerronEndpointOptions.EnvironmentVariable] = "not-a-url" };

        var (exitCode, _, error) = await RunAsync(configuration, "check");

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Contains("Invalid configuration", error);
        Assert.Contains("PERRON_MCP_URL", error);
    }

    private Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args) =>
        RunAsync(InMemoryEndpoint, args);

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(
        IReadOnlyDictionary<string, string?> configuration, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await BenchCli.RunAsync(
            args, output, error, configuration, server.RouteBenchClientToServer, TestContext.Current.CancellationToken);

        return (exitCode, output.ToString(), error.ToString());
    }
}
