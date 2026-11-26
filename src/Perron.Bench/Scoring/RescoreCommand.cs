using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

using Perron.Bench.Run;
using Perron.Bench.Tasks;

namespace Perron.Bench.Scoring;

/// <summary>
/// <c>bench rescore</c> (AK-9.6): scores stored traces again with the current rules, without model or API calls (oracle
/// data comes from the trace). Rewrites the evaluation in each trace and the run's scores.jsonl. Traces written before
/// checks were stored get them from the task file named in the manifest, if its hash still matches.
/// </summary>
public static class RescoreCommand
{
    public static async Task<int> RunAsync(string benchDirectory, IReadOnlyList<string> runIds, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var results = Path.Combine(benchDirectory, "results");
        var available = Directory.Exists(results)
            ? Directory.GetDirectories(results).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal).ToList()
            : [];

        var selected = runIds.Contains("all") ? available : [.. runIds.Distinct()];
        var unknown = selected.Where(id => !available.Contains(id)).ToList();
        if (unknown.Count > 0)
        {
            await error.WriteLineAsync($"Unknown run(s): {string.Join(", ", unknown)}. Runs in {results}: {(available.Count == 0 ? "none" : string.Join(", ", available))}.");
            return ExitCodes.UsageError;
        }

        foreach (var runId in selected)
        {
            await RescoreRunAsync(benchDirectory, Path.Combine(results, runId), runId, output, cancellationToken);
        }

        return ExitCodes.Success;
    }

    private static async Task RescoreRunAsync(string benchDirectory, string directory, string runId, TextWriter output, CancellationToken cancellationToken)
    {
        var tasks = TasksOfManifest(benchDirectory, directory);
        var lines = new List<ScoreLine>();
        foreach (var file in Directory.GetFiles(Path.Combine(directory, "traces"), "*.json").Order(StringComparer.Ordinal))
        {
            var trace = JsonSerializer.Deserialize(await File.ReadAllTextAsync(file, cancellationToken), TraceJsonContext.Default.RunTrace)!;
            if (trace.Checks is null && tasks?.FirstOrDefault(task => task.Id == trace.TaskId) is { } task)
            {
                trace = trace with { Category = task.Category, Checks = task.Checks };
            }

            var evaluation = Evaluator.Evaluate(trace);
            trace = trace with { Evaluation = evaluation };
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(trace, TraceJsonContext.Default.RunTrace) + "\n", cancellationToken);
            lines.Add(Evaluator.Line(trace, evaluation));
        }

        await File.WriteAllLinesAsync(
            Path.Combine(directory, "scores.jsonl"),
            lines.Select(line => JsonSerializer.Serialize(line, SummaryJsonContext.Default.ScoreLine)),
            cancellationToken);

        await output.WriteLineAsync($"{runId}: {lines.Count} traces rescored with scorer version {Evaluator.Version}");
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

    /// <summary>The tasks of the run's task file, if the file still has the hash recorded in the manifest.</summary>
    private static List<BenchTask>? TasksOfManifest(string benchDirectory, string directory)
    {
        var manifestPath = Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var manifest = JsonSerializer.Deserialize(File.ReadAllText(manifestPath), TraceJsonContext.Default.RunManifest)!;
        var taskFile = Path.Combine(benchDirectory, manifest.TaskFile);
        if (!File.Exists(taskFile))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(taskFile);
        return Convert.ToHexStringLower(SHA256.HashData(bytes)) == manifest.TaskSetSha256
            ? TaskSuites.Parse(bytes, manifest.TaskFile)
            : null;
    }
}
