using Microsoft.Extensions.Options;

namespace Perron.Bench.Checks;

/// <summary>
/// <c>bench check</c>: connects to each server, lists its tools and optionally compares them with an expected list.
/// Minimal version until the server registry (task 3.1): only the Perron server is known.
/// </summary>
public sealed class CheckCommand(McpServerCheck check, IOptions<PerronEndpointOptions> perronEndpoint)
{
    public const string PerronServerId = "perron";

    public static IReadOnlyList<string> KnownServers { get; } = [PerronServerId];

    public async Task<int> RunAsync(
        IReadOnlyList<string> serverIds,
        IReadOnlyList<string> expectedTools,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var allPassed = true;

        foreach (var serverId in serverIds)
        {
            var endpoint = perronEndpoint.Value.Endpoint;
            try
            {
                var result = await check.RunAsync(serverId, endpoint, cancellationToken);
                var summary =
                    $"serverInfo {result.ServerName ?? "?"} {result.ServerVersion ?? "?"} | " +
                    $"protocol {result.ProtocolVersion ?? "?"} | " +
                    $"tools ({result.ToolNames.Count}): {string.Join(", ", result.ToolNames)}";

                if (expectedTools.Count > 0)
                {
                    var comparison = ToolListComparison.Compare(expectedTools, result.ToolNames);
                    if (!comparison.Matches)
                    {
                        allPassed = false;
                        await output.WriteLineAsync(
                            $"{serverId,-10} FAIL  {summary} | " +
                            $"missing: {FormatList(comparison.Missing)} | unexpected: {FormatList(comparison.Unexpected)}");
                        continue;
                    }
                }

                await output.WriteLineAsync($"{serverId,-10} OK    {summary}");
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                allPassed = false;
                await output.WriteLineAsync(
                    $"{serverId,-10} FAIL  not reachable at {endpoint}: {ex.GetType().Name}: {ex.Message}");
            }
        }

        return allPassed ? ExitCodes.Success : ExitCodes.CheckFailed;
    }

    private static string FormatList(IReadOnlyList<string> items) =>
        items.Count == 0 ? "-" : string.Join(", ", items);
}
