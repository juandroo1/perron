using System.Globalization;

namespace Perron.Bench.Scoring;

/// <summary>
/// <c>bench rescore</c> (AK-9.6): scores stored traces again with the current rules, without model or API calls
/// (oracle data and judge verdicts come from the trace). Rewrites the evaluation in each trace and the run's scores.jsonl.
/// </summary>
public static class RescoreCommand
{
    public static async Task<int> RunAsync(string benchDirectory, IReadOnlyList<string> runIds, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var (runs, unknown) = TraceStore.Select(benchDirectory, runIds);
        if (unknown.Count > 0)
        {
            await error.WriteLineAsync($"Unknown run(s): {string.Join(", ", unknown)}. Runs: {Available(benchDirectory)}.");
            return ExitCodes.UsageError;
        }

        foreach (var runId in runs)
        {
            var lines = await TraceStore.ScoreAsync(benchDirectory, runId, TraceStore.Load(benchDirectory, runId), cancellationToken);
            await output.WriteLineAsync($"{runId}: {lines.Count} traces rescored with scorer version {Evaluator.Version}");
            await WriteSummaryAsync(lines, output);
        }

        return ExitCodes.Success;
    }

    public static string Available(string benchDirectory)
    {
        var runs = TraceStore.Runs(benchDirectory);
        return runs.Count == 0 ? "none" : string.Join(", ", runs);
    }

    public static async Task WriteSummaryAsync(IReadOnlyList<ScoreLine> lines, TextWriter output)
    {
        foreach (var server in lines.GroupBy(line => line.ServerId).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var outcomes = server.GroupBy(line => line.Outcome).OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => $"{group.Key} {group.Count()}");
            var scored = server.Count(line => line.Outcome is Results.Passed or Results.Failed);
            var passed = server.Count(line => line.Outcome == Results.Passed);
            var answered = server.Where(line => line.Outcome != Results.Excluded).ToList();
            await output.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"  {server.Key,-15} {string.Join(", ", outcomes)} | passed {passed}/{scored} scored | ungrounded answers {answered.Count(line => !line.Grounded)}/{answered.Count}"));
        }
    }
}
