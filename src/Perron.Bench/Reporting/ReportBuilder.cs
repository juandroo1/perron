using System.Globalization;
using System.Text;

using Perron.Bench.Run;
using Perron.Bench.Scoring;

namespace Perron.Bench.Reporting;

/// <summary>A paired comparison of one server with the reference server on the tasks both cover.</summary>
public sealed record PairedDifference(string ServerId, int Tasks, Estimate? Difference);

/// <summary>
/// The Markdown report (AK-10.1 to AK-10.3): success with bootstrap intervals and pass^k, paired comparison with a
/// reference server, cost and efficiency, errors, two Mermaid charts and the methodology of the runs.
/// </summary>
public static class ReportBuilder
{
    private static readonly string[] ErrorColumns =
        [ErrorCodes.Infra, ErrorCodes.ToolLimit, ErrorCodes.GaveUp, ErrorCodes.Hallucinated, ErrorCodes.NotOptimal, ErrorCodes.WrongTime, ErrorCodes.WrongStop, ErrorCodes.ToolErrorLoop];

    public static (string Markdown, IReadOnlyList<ServerMetrics> Metrics) Build(
        IReadOnlyList<RunManifest> manifests, IReadOnlyList<RunTrace> traces, string reference)
    {
        var first = manifests[0];
        var tasks = traces.Select(trace => trace.TaskId).Distinct().Order(StringComparer.Ordinal).ToList();
        var metrics = Servers(manifests)
            .Select(server => ServerMetrics.Of(server.Id, tasks, traces, manifests.Sum(manifest => manifest.Repeats), server.ToolTokens, first.Pricing))
            .ToList();
        var paired = Paired(metrics, reference);

        var text = new StringBuilder();
        text.AppendLine("# Perron Bench report").AppendLine();
        text.AppendLine(Invariant($"Runs {string.Join(", ", manifests.Select(manifest => $"`{manifest.RunId}`"))}, {Dates(manifests)}. ")
            + Invariant($"Suite `{first.Suite}` ({tasks.Count} tasks), model `{first.Model}`, effort `{first.Effort}`, max_tokens {first.MaxTokens}, ")
            + Invariant($"{manifests.Sum(manifest => manifest.Repeats)} repeat(s) per task and server."));
        text.AppendLine();

        Success(text, metrics);
        Paired(text, paired, reference);
        Efficiency(text, metrics);
        Errors(text, metrics);
        Methodology(text, manifests, metrics);

        text.AppendLine("Fahrplandaten: opentransportdata.swiss (via transport.opendata.ch).");
        return (text.ToString().ReplaceLineEndings("\n"), metrics);
    }

    /// <summary>Per server: difference of the task success to the reference on the tasks both cover and scored.</summary>
    public static IReadOnlyList<PairedDifference> Paired(IReadOnlyList<ServerMetrics> metrics, string reference)
    {
        var baseline = metrics.FirstOrDefault(server => server.ServerId == reference);
        if (baseline is null)
        {
            return [];
        }

        return
        [
            .. metrics
                .Where(server => server.ServerId != reference)
                .Select(server =>
                {
                    var differences = server.TaskSuccess
                        .Where(task => task.Value is not null && baseline.TaskSuccess.GetValueOrDefault(task.Key) is not null)
                        .Select(task => task.Value!.Value - baseline.TaskSuccess[task.Key]!.Value)
                        .ToList();
                    return new PairedDifference(server.ServerId, differences.Count, Statistics.Bootstrap(differences));
                }),
        ];
    }

    private static void Success(StringBuilder text, IReadOnlyList<ServerMetrics> metrics)
    {
        var k = metrics.Count == 0 ? 0 : metrics.Max(server => server.K);
        text.AppendLine("## Success").AppendLine();
        text.AppendLine(Invariant($"| Server | Coverage | Success, covered tasks (95 % CI) | Success, all tasks (95 % CI) | pass^{k} | Scored runs | Pending | Unscored | Infra errors |"));
        text.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var server in metrics)
        {
            text.AppendLine(Invariant(
                $"| {server.ServerId} | {server.CoveredTasks}/{server.Tasks} | {WithInterval(server.SuccessCovered)} | {WithInterval(server.SuccessAll)} | ")
                + Invariant($"{Percent(server.PassK)} | {server.Scored} | {server.Pending} | {server.Unscored} | {server.InfraErrors} |"));
        }

        text.AppendLine().AppendLine(
            "Success is the share of passed scored runs per task, averaged over tasks; the interval is a 95 % percentile bootstrap "
            + "over tasks (2000 resamples, fixed seed). \"All tasks\" counts tasks a server does not cover as 0. "
            + "pass^k: share of covered tasks whose k scored runs all passed. Pending (judge missing) and unscored (no oracle data) runs are not counted.");
        text.AppendLine();
        Chart(text, "Success on covered tasks (%)", "Success (%)", metrics, server => server.SuccessCovered is { } estimate ? Math.Round(estimate.Mean * 100) : 0, 100);
    }

    private static void Paired(StringBuilder text, IReadOnlyList<PairedDifference> paired, string reference)
    {
        text.AppendLine(Invariant($"## Paired comparison with `{reference}`")).AppendLine();
        if (paired.Count == 0)
        {
            text.AppendLine(Invariant($"`{reference}` is not part of these runs.")).AppendLine();
            return;
        }

        text.AppendLine("| Server | Tasks compared | Difference in success (95 % CI) | Interval excludes 0 |").AppendLine("|---|---|---|---|");
        foreach (var pair in paired)
        {
            var difference = pair.Difference is { } d
                ? Invariant($"{d.Mean * 100:+0;-0;0} pp ({d.Low * 100:+0;-0;0} to {d.High * 100:+0;-0;0})")
                : "–";
            text.AppendLine(Invariant($"| {pair.ServerId} | {pair.Tasks} | {difference} | {(pair.Difference?.ExcludesZero == true ? "yes" : "no")} |"));
        }

        text.AppendLine().AppendLine(
            "Differences in percentage points on the tasks both servers cover and scored, bootstrapped over tasks. "
            + "Only differences whose interval excludes 0 support a statement in the README.");
        text.AppendLine();
    }

    private static void Efficiency(StringBuilder text, IReadOnlyList<ServerMetrics> metrics)
    {
        text.AppendLine("## Cost and efficiency").AppendLine();
        text.AppendLine("| Server | Tool definition tokens | Tool calls per run | Failed tool calls | Tokens per run | Cost per task | Cost per solved task | Actual cost per task (cached) | Judge cost (total) | LLM time per run | Tool time per run |");
        text.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var server in metrics)
        {
            text.AppendLine(Invariant(
                $"| {server.ServerId} | {(server.ToolTokens is { } tokens ? tokens.ToString(CultureInfo.InvariantCulture) : "n/a")} | {server.ToolCallsPerRun:0.0} | ")
                + Invariant($"{Percent(server.FailedToolCallShare)} | {server.TokensPerRun:0} | ${server.CostPerTaskUncached:0.0000} | ")
                + Invariant($"{(server.CostPerSolvedUncached is { } solved ? $"${solved:0.0000}" : "–")} | ${server.CostActualPerTask:0.0000} | ${server.CostJudge:0.0000} | ")
                + Invariant($"{server.LlmMsPerRun / 1000:0.0} s | {server.ToolMsPerRun / 1000:0.00} s |"));
        }

        text.AppendLine().AppendLine(
            "Cost per task and per solved task are computed without prompt caching (cache tokens at the input price), "
            + "because caching favours servers with large tool definitions; the actual cost with caching is shown for reference. "
            + "Judge cost is not part of the server's cost.");
        text.AppendLine();
        Chart(text, "Cost per solved task without caching (US cents)", "US cents", metrics, server => server.CostPerSolvedUncached is { } cost ? Math.Round((double)cost * 100, 3) : 0, null);
    }

    private static void Errors(StringBuilder text, IReadOnlyList<ServerMetrics> metrics)
    {
        text.AppendLine("## Errors").AppendLine();
        // Fixed taxonomy first, then codes set by hand that the taxonomy does not list.
        var columns = ErrorColumns
            .Concat(metrics.SelectMany(server => server.Errors.Keys).Where(code => !ErrorColumns.Contains(code)).Distinct().Order(StringComparer.Ordinal))
            .ToList();
        text.AppendLine("| Server | Hallucination rate | " + string.Join(" | ", columns) + " | failed without code |");
        text.AppendLine("|---|---|" + string.Concat(columns.Select(_ => "---|")) + "---|");
        foreach (var server in metrics)
        {
            text.AppendLine(Invariant($"| {server.ServerId} | {Percent(server.HallucinationRate)} | ")
                + string.Join(" | ", columns.Select(code => server.Errors.GetValueOrDefault(code).ToString(CultureInfo.InvariantCulture)))
                + Invariant($" | {server.FailedWithoutCode} |"));
        }

        text.AppendLine().AppendLine(
            "Hallucination rate: share of answered runs with a clock time that appears in no tool result of the run; it does not decide "
            + "whether a task passed, so `hallucinated` is also counted for passed runs. The other codes are counted per run, and a run can have several. "
            + "\"Failed without code\": failed runs whose failure the scorer cannot classify, e.g. a judge verdict or a missed regex; such codes are set by hand.");
        text.AppendLine();
    }

    private static void Methodology(StringBuilder text, IReadOnlyList<RunManifest> manifests, IReadOnlyList<ServerMetrics> metrics)
    {
        var first = manifests[0];
        text.AppendLine("## Methodology").AppendLine();
        text.AppendLine("| Run | Started | Commit | Harness | Repeats | Seed |").AppendLine("|---|---|---|---|---|---|");
        foreach (var manifest in manifests)
        {
            text.AppendLine(Invariant(
                $"| `{manifest.RunId}` | {manifest.StartedAt.UtcDateTime:yyyy-MM-dd HH:mm} UTC | `{Short(manifest.GitSha)}`{(manifest.GitDirty == true ? " (dirty)" : "")} | {manifest.HarnessVersion} | {manifest.Repeats} | {manifest.Seed} |"));
        }

        text.AppendLine();
        text.AppendLine(Invariant($"- Model `{first.Model}`, effort `{first.Effort}`, max_tokens {first.MaxTokens}, tool-call limit {first.DefaultMaxToolCalls} unless a task sets its own, pause {first.PauseSeconds:0.#} s between runs."));
        text.AppendLine(Invariant($"- Task set `{first.TaskFile}` (SHA-256 `{Short(first.TaskSetSha256)}`){(first.Tasks is { Count: > 0 } only ? $", only {string.Join(", ", only)}" : "")}; system prompt template SHA-256 `{Short(first.SystemPromptTemplateSha256)}`, the same for every server, with the server's instructions in a fixed section."));
        if (first.JudgePromptSha256 is { } judge)
        {
            text.AppendLine(Invariant($"- Judge for ambiguous and bad input tasks: `{first.Model}`, effort `{first.Judge?.Effort}`, prompt SHA-256 `{Short(judge)}`, blind to the server; its check by review is in `bench/judge-review/agreement.md`."));
        }

        text.AppendLine(Invariant($"- Prices (USD per million tokens): input {first.Pricing.Input}, output {first.Pricing.Output}, cache write {first.Pricing.CacheWrite}, cache read {first.Pricing.CacheRead}."));
        text.AppendLine("- Oracle checks verify the named connection or departure against transport.opendata.ch, asked once per task from the reference time; grounding checks every clock time of the answer against the tool results.");
        text.AppendLine();
        text.AppendLine("| Server | Version | serverInfo | tools/list SHA-256 | Tool definition tokens | Covered | Not covered |").AppendLine("|---|---|---|---|---|---|---|");
        foreach (var server in Servers(manifests))
        {
            var hashes = manifests.SelectMany(manifest => manifest.Servers.Where(item => item.Id == server.Id).Select(item => item.ToolsHash)).Distinct().ToList();
            text.AppendLine(Invariant(
                $"| {server.Id} | {(server.Hosted ? "hosted" : server.Version)} | {server.ServerName} {server.ServerVersion} | ")
                + Invariant($"{string.Join(", ", hashes.Select(hash => $"`{Short(hash)}`"))}{(hashes.Count > 1 ? " (changed between runs)" : "")} | ")
                + Invariant($"{(server.ToolTokens is { } tokens ? tokens.ToString(CultureInfo.InvariantCulture) : "n/a")} | {string.Join(", ", server.CapabilitiesCovered)} | {(server.CapabilitiesNotCovered.Count == 0 ? "–" : string.Join(", ", server.CapabilitiesNotCovered))} |"));
        }

        text.AppendLine();
        text.AppendLine("Known limitations of the benchmark are listed in the README.");
        text.AppendLine();
    }

    private static void Chart(StringBuilder text, string title, string axis, IReadOnlyList<ServerMetrics> metrics, Func<ServerMetrics, double> value, double? max)
    {
        var values = metrics.Select(value).ToList();
        var highest = values.DefaultIfEmpty(0).Max();
        var top = max ?? (highest > 0 ? Math.Ceiling(highest * 1.2 * 100) / 100 : 1);
        text.AppendLine("```mermaid").AppendLine("xychart-beta");
        text.AppendLine(Invariant($"    title \"{title}\""));
        text.AppendLine("    x-axis [" + string.Join(", ", metrics.Select(server => $"\"{server.ServerId}\"")) + "]");
        text.AppendLine(Invariant($"    y-axis \"{axis}\" 0 --> {top}"));
        text.AppendLine("    bar [" + string.Join(", ", values.Select(v => v.ToString(CultureInfo.InvariantCulture))) + "]");
        text.AppendLine("```").AppendLine();
    }

    /// <summary>The servers of the runs in order of first appearance, with their entry of the first run that has them.</summary>
    private static List<ManifestServer> Servers(IReadOnlyList<RunManifest> manifests) =>
        [.. manifests.SelectMany(manifest => manifest.Servers).DistinctBy(server => server.Id)];

    private static string WithInterval(Estimate? estimate) =>
        estimate is null ? "–" : Invariant($"{estimate.Mean * 100:0} % ({estimate.Low * 100:0}–{estimate.High * 100:0})");

    private static string Percent(double? value) => value is { } v ? Invariant($"{v * 100:0} %") : "–";

    private static string Dates(IReadOnlyList<RunManifest> manifests)
    {
        var first = manifests.Min(manifest => manifest.StartedAt).UtcDateTime;
        var last = manifests.Max(manifest => manifest.FinishedAt ?? manifest.StartedAt).UtcDateTime;
        return first.Date == last.Date ? Invariant($"{first:yyyy-MM-dd}") : Invariant($"{first:yyyy-MM-dd} to {last:yyyy-MM-dd}");
    }

    private static string Short(string? hash) => hash is null ? "-" : hash[..Math.Min(12, hash.Length)];

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
