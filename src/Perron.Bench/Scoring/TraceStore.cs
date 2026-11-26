using System.Security.Cryptography;
using System.Text.Json;

using Perron.Bench.Run;
using Perron.Bench.Tasks;

namespace Perron.Bench.Scoring;

/// <summary>A stored trace and the file it came from.</summary>
public sealed record StoredTrace(string Path, RunTrace Trace);

/// <summary>Reads and writes the traces of runs under bench/results.</summary>
public static class TraceStore
{
    public static string ResultsDirectory(string benchDirectory) => System.IO.Path.Combine(benchDirectory, "results");

    /// <summary>Run IDs under bench/results, oldest first.</summary>
    public static IReadOnlyList<string> Runs(string benchDirectory)
    {
        var results = ResultsDirectory(benchDirectory);
        return Directory.Exists(results)
            ? [.. Directory.GetDirectories(results).Select(System.IO.Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)]
            : [];
    }

    /// <summary>
    /// The requested runs (<c>all</c> for every run), or the unknown IDs. An empty request means all runs.
    /// </summary>
    public static (IReadOnlyList<string> Runs, IReadOnlyList<string> Unknown) Select(string benchDirectory, IReadOnlyList<string> requested)
    {
        var available = Runs(benchDirectory);
        if (requested.Count == 0 || requested.Contains("all"))
        {
            return (available, []);
        }

        return ([.. requested.Distinct().Where(available.Contains)], [.. requested.Where(id => !available.Contains(id))]);
    }

    /// <summary>
    /// The traces of a run. Traces written before checks were stored get them from the manifest's task file, if its
    /// hash still matches the manifest.
    /// </summary>
    public static List<StoredTrace> Load(string benchDirectory, string runId)
    {
        var directory = System.IO.Path.Combine(ResultsDirectory(benchDirectory), runId);
        var tasks = TasksOfManifest(benchDirectory, directory);
        var traces = new List<StoredTrace>();
        foreach (var file in Directory.GetFiles(System.IO.Path.Combine(directory, "traces"), "*.json").Order(StringComparer.Ordinal))
        {
            var trace = JsonSerializer.Deserialize(File.ReadAllText(file), TraceJsonContext.Default.RunTrace)!;
            if (trace.Checks is null && tasks?.FirstOrDefault(task => task.Id == trace.TaskId) is { } task)
            {
                trace = trace with { Category = task.Category, Checks = task.Checks };
            }

            traces.Add(new StoredTrace(file, trace));
        }

        return traces;
    }

    public static Task SaveAsync(StoredTrace stored, CancellationToken cancellationToken) =>
        File.WriteAllTextAsync(stored.Path, JsonSerializer.Serialize(stored.Trace, TraceJsonContext.Default.RunTrace) + "\n", cancellationToken);

    /// <summary>Scores every trace of a run again and rewrites the traces and scores.jsonl.</summary>
    public static async Task<List<ScoreLine>> ScoreAsync(string benchDirectory, string runId, IReadOnlyList<StoredTrace> traces, CancellationToken cancellationToken)
    {
        var lines = new List<ScoreLine>();
        foreach (var stored in traces)
        {
            var evaluation = Evaluator.Evaluate(stored.Trace);
            var scored = stored with { Trace = stored.Trace with { Evaluation = evaluation } };
            await SaveAsync(scored, cancellationToken);
            lines.Add(Evaluator.Line(scored.Trace, evaluation));
        }

        await File.WriteAllLinesAsync(
            System.IO.Path.Combine(ResultsDirectory(benchDirectory), runId, "scores.jsonl"),
            lines.Select(line => JsonSerializer.Serialize(line, SummaryJsonContext.Default.ScoreLine)),
            cancellationToken);
        return lines;
    }

    /// <summary>The tasks of the run's task file, if the file still has the hash recorded in the manifest.</summary>
    private static List<BenchTask>? TasksOfManifest(string benchDirectory, string directory)
    {
        var manifestPath = System.IO.Path.Combine(directory, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var manifest = JsonSerializer.Deserialize(File.ReadAllText(manifestPath), TraceJsonContext.Default.RunManifest)!;
        var taskFile = System.IO.Path.Combine(benchDirectory, manifest.TaskFile);
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
