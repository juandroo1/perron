using Microsoft.Extensions.Configuration;

using Perron.Bench.Registry;

namespace Perron.Bench.Checks;

/// <summary>
/// <c>bench check</c> (AK-7.2): connects to each server of the registry, lists its tools, hashes <c>tools/list</c>,
/// counts the tokens of the tool definitions and shows which capabilities are covered.
/// Status per server: OK, CHANGED (hosted server whose tools differ from the registry; not a failure),
/// SKIPPED (optional server without its keys), FAIL.
/// </summary>
public sealed class CheckCommand(McpServerCheck check, IToolTokenCounter tokenCounter, IConfiguration configuration)
{
    public async Task<int> RunAsync(
        ServerRegistry registry,
        IReadOnlyList<string> serverIds,
        IReadOnlyList<string> expectedTools,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var allPassed = true;

        foreach (var server in serverIds.Select(id => registry.Find(id)!))
        {
            var missing = (server.EnvRequired ?? []).Where(key => !IsSet(key)).ToList();
            if (missing.Count > 0)
            {
                allPassed &= server.Optional;
                await output.WriteLineAsync(
                    $"{server.Id,-15} {(server.Optional ? "SKIPPED" : "FAIL"),-7} missing {string.Join(", ", missing)}"
                    + (server.Optional ? " (optional server)" : ""));
                continue;
            }

            var endpoint = Endpoint(server);
            try
            {
                var result = await check.RunAsync(server, endpoint, ProcessEnvironment(server), cancellationToken);
                var hash = ToolListHash.Compute(result.Tools);
                var tokens = await CountTokensAsync(result, cancellationToken);

                var status = "OK";
                var problems = new List<string>();
                if (expectedTools.Count > 0)
                {
                    var comparison = ToolListComparison.Compare(expectedTools, result.ToolNames);
                    if (!comparison.Matches)
                    {
                        status = "FAIL";
                        problems.Add($"missing: {FormatList(comparison.Missing)} | unexpected: {FormatList(comparison.Unexpected)}");
                    }
                }

                if (status == "OK" && server.Hosted && server.ToolsHash != hash)
                {
                    status = "CHANGED";
                    problems.Add($"tools/list differs from servers.json ({Short(server.ToolsHash)})");
                }

                allPassed &= status != "FAIL";
                await output.WriteLineAsync(
                    $"{server.Id,-15} {status,-7} serverInfo {result.ServerName ?? "?"} {result.ServerVersion ?? "?"} | "
                    + $"protocol {result.ProtocolVersion ?? "?"} | "
                    + $"tools ({result.ToolNames.Count}): {string.Join(", ", result.ToolNames)} | "
                    + $"tools/list {Short(hash)} | tool tokens {tokens} | covers {Coverage(server)}"
                    + string.Concat(problems.Select(problem => $" | {problem}")));
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                allPassed = false;
                var where = server.Transport == ServerDefinition.Http ? $"at {endpoint}" : $"via {server.Command}";
                await output.WriteLineAsync($"{server.Id,-15} {"FAIL",-7} not reachable {where}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return allPassed ? ExitCodes.Success : ExitCodes.CheckFailed;
    }

    public Uri? Endpoint(ServerDefinition server) => ServerEnvironment.Endpoint(server, configuration);

    /// <summary>Covered capabilities, and the ones that are not covered for lack of a key.</summary>
    public string Coverage(ServerDefinition server)
    {
        var (covered, notCovered) = ServerCoverage.Of(server, IsSet);
        var text = string.Join(", ", covered);
        return notCovered.Count > 0
            ? $"{text} (not covered: {string.Join("; ", notCovered.Select(item => $"{item.Name} needs {string.Join(", ", item.MissingKeys)}"))})"
            : text;
    }

    private async Task<string> CountTokensAsync(ServerCheckResult result, CancellationToken cancellationToken)
    {
        if (!tokenCounter.IsAvailable)
        {
            return "n/a";
        }

        try
        {
            return (await tokenCounter.CountAsync(result.Tools, cancellationToken))?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "n/a";
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Counting is a measurement, not part of reachability: report it and go on.
            return $"error ({ex.GetType().Name})";
        }
    }

    private Dictionary<string, string?> ProcessEnvironment(ServerDefinition server) => ServerEnvironment.Process(server, configuration);

    private bool IsSet(string key) => ServerEnvironment.IsSet(configuration, key);

    private static string Short(string? hash) => hash is null ? "-" : hash[..Math.Min(12, hash.Length)];

    private static string FormatList(IReadOnlyList<string> items) =>
        items.Count == 0 ? "-" : string.Join(", ", items);
}
