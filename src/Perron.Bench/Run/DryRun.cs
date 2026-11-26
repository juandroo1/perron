using System.Text.Json;

using Perron.Bench.Settings;
using Perron.Bench.Tasks;

namespace Perron.Bench.Run;

/// <summary>What <c>--dry-run</c> knows about one server.</summary>
/// <param name="ToolTokens">Tokens of the tool definitions from count_tokens, or <c>null</c> to estimate from characters.</param>
public sealed record DryRunServer(string Id, int ToolChars, long? ToolTokens, string SystemPrompt, IReadOnlyList<BenchTask> CoveredTasks);

/// <summary>Estimate for one server.</summary>
/// <param name="Basis">Where turns and tool result sizes come from: earlier runs or bench.json.</param>
/// <param name="Estimated">The tool definitions are estimated from characters (no API key); the system prompt and the task always are.</param>
public sealed record DryRunEstimate(
    string ServerId, int Runs, double Turns, long LlmCalls, long InputTokens, long OutputTokens, decimal CostUsd, string Basis, bool Estimated);

/// <summary>
/// <c>--dry-run</c> (AK-8.3): estimates model calls and cost without calling the model. Per run:
/// input = turns × (tool definitions + system prompt + task) + (tool result + output per turn) × turns × (turns − 1) / 2,
/// output = turns × output per turn. Turns and sizes come from earlier runs of the server, otherwise from bench.json.
/// </summary>
public static class DryRun
{
    public const double CharactersPerToken = 3.5;

    public static DryRunEstimate Estimate(
        DryRunServer server, int repeats, ModelPricing pricing, DryRunAssumptions assumptions, IReadOnlyList<RunSummary> history)
    {
        var (turns, toolResultTokens, outputPerTurn, basis) = Expectations(server.Id, assumptions, history);

        var toolTokens = server.ToolTokens ?? (long)Math.Ceiling(server.ToolChars / CharactersPerToken);
        var systemTokens = Math.Ceiling(server.SystemPrompt.Length / CharactersPerToken);
        var runs = server.CoveredTasks.Count * repeats;

        double input = 0;
        foreach (var task in server.CoveredTasks)
        {
            var promptTokens = Math.Ceiling(task.Prompt.Length / CharactersPerToken);
            input += (turns * (toolTokens + systemTokens + promptTokens)) + ((toolResultTokens + outputPerTurn) * turns * (turns - 1) / 2);
        }

        var inputTokens = (long)Math.Round(input * repeats);
        var outputTokens = (long)Math.Round(turns * outputPerTurn * runs);
        var cost = new TokenUsage(inputTokens, outputTokens, 0, 0).Cost(pricing);
        return new DryRunEstimate(
            server.Id, runs, turns, (long)Math.Round(turns * runs), inputTokens, outputTokens, cost, basis, server.ToolTokens is null);
    }

    /// <summary>Mean turns, tool result tokens and output tokens per turn of earlier runs of a server, else bench.json.</summary>
    public static (double Turns, double ToolResultTokens, double OutputPerTurn, string Basis) Expectations(
        string serverId, DryRunAssumptions assumptions, IReadOnlyList<RunSummary> history)
    {
        var runs = history
            .Where(run => run.ServerId == serverId && run.Turns > 0 && run.Status is not (RunStatus.InfraError or RunStatus.NotCovered))
            .ToList();
        if (runs.Count == 0)
        {
            return (assumptions.Turns, assumptions.ToolResultTokens, assumptions.OutputTokensPerTurn, "bench.json assumptions");
        }

        return (
            runs.Average(run => run.Turns),
            runs.Average(run => run.ToolResultChars / CharactersPerToken / run.Turns),
            runs.Average(run => (double)run.OutputTokens / run.Turns),
            $"calibrated from {runs.Count} earlier runs");
    }

    /// <summary>All runs.jsonl lines under <paramref name="resultsDirectory"/>.</summary>
    public static IReadOnlyList<RunSummary> History(string resultsDirectory)
    {
        if (!Directory.Exists(resultsDirectory))
        {
            return [];
        }

        return
        [
            .. Directory.EnumerateFiles(resultsDirectory, "runs.jsonl", SearchOption.AllDirectories)
                .SelectMany(File.ReadLines)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => JsonSerializer.Deserialize(line, SummaryJsonContext.Default.RunSummary)!),
        ];
    }
}
