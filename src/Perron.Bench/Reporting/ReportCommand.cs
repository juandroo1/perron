using System.Globalization;
using System.Text.Json;

using Perron.Bench.Run;
using Perron.Bench.Scoring;

namespace Perron.Bench.Reporting;

/// <summary><c>bench report</c>: writes the Markdown report of stored runs, without model or API calls (US-10).</summary>
public static class ReportCommand
{
    public const string DefaultReference = "perron-rich";

    public static async Task<int> RunAsync(
        string benchDirectory, IReadOnlyList<string> requested, string reference, string? outFile, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var available = TraceStore.Runs(benchDirectory)
            .Where(run => File.Exists(ManifestPath(benchDirectory, run)))
            .ToList();
        if (available.Count == 0)
        {
            await error.WriteLineAsync("No runs under bench/results.");
            return ExitCodes.UsageError;
        }

        var runs = requested.Count == 0 ? [available[^1]] : requested.Distinct().ToList();
        var unknown = runs.Where(run => !available.Contains(run)).ToList();
        if (unknown.Count > 0)
        {
            await error.WriteLineAsync($"Unknown run(s): {string.Join(", ", unknown)}.");
            return ExitCodes.UsageError;
        }

        runs = [.. runs.Order(StringComparer.Ordinal)];
        var manifests = runs
            .Select(run => JsonSerializer.Deserialize(File.ReadAllText(ManifestPath(benchDirectory, run)), TraceJsonContext.Default.RunManifest)!)
            .ToList();
        if (Incompatible(manifests) is { } reason)
        {
            await error.WriteLineAsync($"These runs cannot be combined: {reason}.");
            return ExitCodes.UsageError;
        }

        var traces = runs.SelectMany(run => TraceStore.Load(benchDirectory, run)).Select(stored => stored.Trace).ToList();
        var (markdown, metrics) = ReportBuilder.Build(manifests, traces, reference);

        var file = outFile ?? Path.Combine(TraceStore.ResultsDirectory(benchDirectory), runs[^1], runs.Count == 1 ? "report.md" : $"report-{runs.Count}-runs.md");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        await File.WriteAllTextAsync(file, markdown, cancellationToken);

        foreach (var server in metrics)
        {
            var success = server.SuccessCovered is { } estimate ? (estimate.Mean * 100).ToString("0", CultureInfo.InvariantCulture) + " %" : "–";
            await output.WriteLineAsync($"{server.ServerId,-16} covered {server.CoveredTasks}/{server.Tasks}, success {success}, scored runs {server.Scored}");
        }

        await output.WriteLineAsync($"Report written to {file}.");
        return ExitCodes.Success;
    }

    /// <summary>
    /// Why the runs cannot be combined into one report, or <c>null</c>: model, effort, max_tokens and the task set
    /// must be the same, otherwise the figures are not comparable.
    /// </summary>
    public static string? Incompatible(IReadOnlyList<RunManifest> manifests)
    {
        var first = manifests[0];
        foreach (var manifest in manifests.Skip(1))
        {
            if (manifest.Model != first.Model)
            {
                return $"model {first.Model} and {manifest.Model}";
            }

            if (manifest.Effort != first.Effort)
            {
                return $"effort {first.Effort} and {manifest.Effort}";
            }

            if (manifest.MaxTokens != first.MaxTokens)
            {
                return $"max_tokens {first.MaxTokens} and {manifest.MaxTokens}";
            }

            if (manifest.TaskSetSha256 != first.TaskSetSha256)
            {
                return $"different task sets ({manifest.TaskFile} with another hash)";
            }
        }

        return null;
    }

    private static string ManifestPath(string benchDirectory, string run) =>
        Path.Combine(TraceStore.ResultsDirectory(benchDirectory), run, "manifest.json");
}
