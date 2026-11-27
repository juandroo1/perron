using System.Text.Json;

using Perron.Bench.Registry;
using Perron.Bench.Reporting;
using Perron.Bench.Run;
using Perron.Bench.Scoring;
using Perron.Bench.Settings;

namespace Perron.Bench.Tests;

/// <summary>Report of stored runs (US-10): statistics, figures per server, paired comparison, Markdown and CLI.</summary>
public sealed class ReportTests
{
    private static readonly ModelPricing Pricing = new(1m, 5m, 1.25m, 0.1m);
    private static readonly string[] Tasks = ["t1", "t2", "t3"];

    [Fact]
    public void Bootstrap_IsDeterministicAndContainsTheMean()
    {
        double[] values = [1, 0.5, 0, 1, 1, 0.5];

        var first = Statistics.Bootstrap(values)!;
        var second = Statistics.Bootstrap(values)!;

        Assert.Equal(first, second);
        Assert.Equal(4.0 / 6, first.Mean, 9);
        Assert.InRange(first.Mean, first.Low, first.High);
        Assert.True(first.Low < first.High);
        Assert.Equal(6, first.N);
    }

    [Fact]
    public void Bootstrap_HandlesEmptyAndSingleValues()
    {
        Assert.Null(Statistics.Bootstrap([]));
        Assert.Equal(new Estimate(0.5, 0.5, 0.5, 1), Statistics.Bootstrap([0.5]));
        Assert.False(Statistics.Bootstrap([0.5])!.ExcludesZero);
    }

    [Fact]
    public void Percentile_InterpolatesBetweenRanks()
    {
        double[] sorted = [0, 10, 20, 30, 40];

        Assert.Equal(0, Statistics.Percentile(sorted, 0));
        Assert.Equal(40, Statistics.Percentile(sorted, 1));
        Assert.Equal(20, Statistics.Percentile(sorted, 0.5));
        Assert.Equal(5, Statistics.Percentile(sorted, 0.125), 9);
    }

    [Fact]
    public void Metrics_CountSuccessCoverageAndPassK()
    {
        RunTrace[] traces =
        [
            Trace("t1", "a", 1, Results.Passed), Trace("t1", "a", 2, Results.Passed),
            Trace("t2", "a", 1, Results.Passed), Trace("t2", "a", 2, Results.Failed),
            Trace("t3", "a", 1, status: RunStatus.NotCovered), Trace("t3", "a", 2, status: RunStatus.NotCovered),
        ];

        var metrics = ServerMetrics.Of("a", Tasks, traces, 2, 1000, Pricing);

        Assert.Equal(2, metrics.CoveredTasks);
        Assert.Equal(["t3"], metrics.NotCovered);
        Assert.Equal(0.75, metrics.SuccessCovered!.Mean, 9);
        Assert.Equal(0.5, metrics.SuccessAll!.Mean, 9);
        Assert.Equal(0.5, metrics.PassK);
        Assert.Equal(2, metrics.K);
        Assert.Equal(4, metrics.Scored);
        Assert.Equal(3, metrics.Passed);
        Assert.Equal(4, metrics.Runs);
        Assert.Equal(1000, metrics.ToolTokens);
    }

    [Fact]
    public void Metrics_LeavePendingUnscoredAndInfraErrorsOutOfSuccess()
    {
        RunTrace[] traces =
        [
            Trace("t1", "a", 1, Results.Passed), Trace("t1", "a", 2, Results.Pending),
            Trace("t2", "a", 1, Results.Unscored), Trace("t2", "a", 2, Results.Unscored),
            Trace("t3", "a", 1, Results.Failed), Trace("t3", "a", 2, Results.Excluded, RunStatus.InfraError),
        ];

        var metrics = ServerMetrics.Of("a", Tasks, traces, 2, null, Pricing);

        Assert.Equal(3, metrics.CoveredTasks);
        Assert.Equal(1.0, metrics.TaskSuccess["t1"]);
        Assert.Null(metrics.TaskSuccess["t2"]);
        Assert.Equal(0.0, metrics.TaskSuccess["t3"]);
        Assert.Equal(0.5, metrics.SuccessCovered!.Mean, 9);
        Assert.Equal(2, metrics.SuccessCovered.N);
        Assert.Null(metrics.PassK);
        Assert.Equal(1, metrics.Pending);
        Assert.Equal(2, metrics.Unscored);
        Assert.Equal(1, metrics.InfraErrors);
        Assert.Equal(5, metrics.Runs);
    }

    [Fact]
    public void Metrics_ComputeCostWithoutCaching()
    {
        var usage = new TokenUsage(1000, 400, 2000, 3000);
        RunTrace[] traces =
        [
            Trace("t1", "a", 1, Results.Passed, usage: usage, cost: 0.002m),
            Trace("t2", "a", 1, Results.Failed, usage: usage, cost: 0.002m),
        ];

        var metrics = ServerMetrics.Of("a", ["t1", "t2"], traces, 1, null, Pricing);

        // (1000 + 2000 + 3000) input tokens at 1 USD and 400 output tokens at 5 USD per million.
        Assert.Equal(0.008m, ServerMetrics.Uncached(usage, Pricing));
        Assert.Equal(0.008m, metrics.CostPerTaskUncached);
        Assert.Equal(0.016m, metrics.CostPerSolvedUncached);
        Assert.Equal(0.002m, metrics.CostActualPerTask);
        Assert.Equal(6400, metrics.TokensPerRun);
    }

    [Fact]
    public void Metrics_HaveNoCostPerSolvedTaskWithoutSolvedTasks()
    {
        var metrics = ServerMetrics.Of("a", ["t1"], [Trace("t1", "a", 1, Results.Failed)], 1, null, Pricing);

        Assert.Null(metrics.CostPerSolvedUncached);
    }

    [Fact]
    public void Metrics_CountErrorsHallucinationsAndFailuresWithoutCode()
    {
        RunTrace[] traces =
        [
            Trace("t1", "a", 1, Results.Passed, errors: [ErrorCodes.Hallucinated], ungrounded: ["10:01"]),
            Trace("t1", "a", 2, Results.Failed, errors: [ErrorCodes.WrongTime]),
            Trace("t2", "a", 1, Results.Failed),
            Trace("t2", "a", 2, Results.Failed, errors: [ErrorCodes.GaveUp], answer: null),
        ];

        var metrics = ServerMetrics.Of("a", ["t1", "t2"], traces, 2, null, Pricing);

        Assert.Equal(1.0 / 3, metrics.HallucinationRate!.Value, 9);
        Assert.Equal(1, metrics.Errors[ErrorCodes.Hallucinated]);
        Assert.Equal(1, metrics.Errors[ErrorCodes.WrongTime]);
        Assert.Equal(1, metrics.Errors[ErrorCodes.GaveUp]);
        Assert.Equal(1, metrics.FailedWithoutCode);
    }

    [Fact]
    public void Metrics_SeparateLlmAndToolTimeAndCountFailedToolCalls()
    {
        RunTrace[] traces =
        [
            Trace("t1", "a", 1, Results.Passed, llmMs: 3000, toolMs: 200, calls: [false, true]),
            Trace("t2", "a", 1, Results.Passed, llmMs: 1000, toolMs: 400, calls: [false, false]),
        ];

        var metrics = ServerMetrics.Of("a", ["t1", "t2"], traces, 1, null, Pricing);

        Assert.Equal(2000, metrics.LlmMsPerRun);
        Assert.Equal(300, metrics.ToolMsPerRun);
        Assert.Equal(2, metrics.ToolCallsPerRun);
        Assert.Equal(0.25, metrics.FailedToolCallShare);
    }

    [Fact]
    public void Paired_ComparesOnTasksBothServersScored()
    {
        RunTrace[] traces =
        [
            Trace("t1", "ref", 1, Results.Failed), Trace("t2", "ref", 1, Results.Failed), Trace("t3", "ref", 1, Results.Passed),
            Trace("t1", "b", 1, Results.Passed), Trace("t2", "b", 1, Results.Passed), Trace("t3", "b", 1, status: RunStatus.NotCovered),
            Trace("t1", "c", 1, Results.Failed), Trace("t2", "c", 1, Results.Passed), Trace("t3", "c", 1, Results.Passed),
        ];
        var metrics = new[] { "ref", "b", "c" }.Select(id => ServerMetrics.Of(id, Tasks, traces, 1, null, Pricing)).ToList();

        var paired = ReportBuilder.Paired(metrics, "ref");

        var b = Assert.Single(paired, pair => pair.ServerId == "b");
        Assert.Equal(2, b.Tasks);
        Assert.Equal(1.0, b.Difference!.Mean);
        Assert.True(b.Difference.ExcludesZero);
        var c = Assert.Single(paired, pair => pair.ServerId == "c");
        Assert.Equal(3, c.Tasks);
        Assert.Equal(1.0 / 3, c.Difference!.Mean, 9);
        Assert.False(c.Difference.ExcludesZero);
        Assert.Empty(ReportBuilder.Paired(metrics, "missing"));
    }

    [Fact]
    public void Incompatible_RejectsRunsWithAnotherModelEffortMaxTokensOrTaskSet()
    {
        var first = Manifest("r1");

        Assert.Null(ReportCommand.Incompatible([first, Manifest("r2")]));
        Assert.Contains("model", ReportCommand.Incompatible([first, Manifest("r2") with { Model = "claude-sonnet-5-5" }]));
        Assert.Contains("effort", ReportCommand.Incompatible([first, Manifest("r2") with { Effort = "high" }]));
        Assert.Contains("max_tokens", ReportCommand.Incompatible([first, Manifest("r2") with { MaxTokens = 8000 }]));
        Assert.Contains("task sets", ReportCommand.Incompatible([first, Manifest("r2") with { TaskSetSha256 = "other" }]));
    }

    [Fact]
    public void Build_WritesAllSectionsChartsAndMethodology()
    {
        RunTrace[] traces =
        [
            Trace("t1", "perron-rich", 1, Results.Passed), Trace("t2", "perron-rich", 1, Results.Failed),
            Trace("t1", "official", 1, Results.Passed), Trace("t2", "official", 1, Results.Passed),
        ];

        var (markdown, metrics) = ReportBuilder.Build([Manifest("r1")], traces, "perron-rich");

        Assert.Equal(["perron-rich", "official"], metrics.Select(server => server.ServerId));
        Assert.Contains("## Success", markdown);
        Assert.Contains("## Paired comparison with `perron-rich`", markdown);
        Assert.Contains("## Cost and efficiency", markdown);
        Assert.Contains("## Errors", markdown);
        Assert.Contains("## Methodology", markdown);
        Assert.Contains("| perron-rich | 2/2 | 50 % (0–100) |", markdown);
        Assert.Contains("| official | 2 | +50 pp", markdown);
        Assert.Equal(2, CountOf(markdown, "```mermaid\nxychart-beta"));
        Assert.Contains("    x-axis [\"perron-rich\", \"official\"]", markdown);
        Assert.Contains("    bar [50, 100]", markdown);
        Assert.Contains("model `claude-haiku-5-5`, effort `medium`, max_tokens 16000", markdown);
        Assert.Contains("| official | hosted | transport.opendata.ch 2c28784a | `hash-officia` |", markdown);
        Assert.Contains("Fahrplandaten: opentransportdata.swiss", markdown);
        Assert.DoesNotContain("\r", markdown);
    }

    [Fact]
    public void Build_NamesTheJudgeModelOfTheVerdicts_AlsoInARunWithTheControlModel()
    {
        var manifest = Manifest("r1") with { Model = "claude-sonnet-5-5", JudgePromptSha256 = "judge-hash" };
        var judged = Trace("t1", "perron-rich", 1, Results.Passed) with
        {
            Judgements = [new Judgement(0, "claude-haiku-5-5", "pass", "ok", new TokenUsage(10, 5, 0, 0), 0.0001m, 100, DateTimeOffset.UnixEpoch)],
        };

        var (markdown, _) = ReportBuilder.Build([manifest], [judged], "perron-rich");

        Assert.Contains("- Model `claude-sonnet-5-5`", markdown);
        Assert.Contains("- Judge for ambiguous and bad input tasks: `claude-haiku-5-5`, effort", markdown);
    }

    [Fact]
    public void Build_MarksChangedToolListsAcrossRuns()
    {
        var second = Manifest("r2") with
        {
            Servers = [.. Manifest("r2").Servers.Select(server => server.Id == "official" ? server with { ToolsHash = "hash-changed" } : server)],
        };
        RunTrace[] traces = [Trace("t1", "perron-rich", 1, Results.Passed), Trace("t1", "official", 1, Results.Passed)];

        var (markdown, _) = ReportBuilder.Build([Manifest("r1"), second], traces, "perron-rich");

        Assert.Contains("`hash-officia`, `hash-changed` (changed between runs)", markdown);
        Assert.Contains("2 repeat(s) per task and server", markdown);
    }

    [Fact]
    public void Build_ListsSkippedServers()
    {
        var manifest = Manifest("r1") with { SkippedServers = [new SkippedServer("stm", "missing STM_TRANSPORT_API_KEY (optional server)")] };

        var (withSkipped, _) = ReportBuilder.Build([manifest], [Trace("t1", "perron-rich", 1, Results.Passed)], "perron-rich");
        var (without, _) = ReportBuilder.Build([Manifest("r1")], [Trace("t1", "perron-rich", 1, Results.Passed)], "perron-rich");

        Assert.Contains("Skipped servers (not part of the comparison):\n\n- stm: missing STM_TRANSPORT_API_KEY (optional server)\n", withSkipped);
        Assert.DoesNotContain("Skipped servers", without);
    }

    [Fact]
    public async Task Cli_WritesTheReportOfTheLatestRunByDefault()
    {
        using var bench = new TempBench();
        bench.AddRun(Manifest("20261126-080000-smoke"), [Trace("t1", "perron-rich", 1, Results.Failed)]);
        bench.AddRun(Manifest("20261126-090000-smoke"), [Trace("t1", "perron-rich", 1, Results.Passed)]);

        var (exitCode, output, _) = await RunAsync(bench.Directory, "report");

        Assert.Equal(ExitCodes.Success, exitCode);
        var report = Path.Combine(bench.Directory, "results", "20261126-090000-smoke", "report.md");
        Assert.Contains("Runs `20261126-090000-smoke`", await File.ReadAllTextAsync(report, TestContext.Current.CancellationToken));
        Assert.Contains("success 100 %", output);
        Assert.False(File.Exists(Path.Combine(bench.Directory, "results", "20261126-080000-smoke", "report.md")));
    }

    [Fact]
    public async Task Cli_CombinesRunsAndRejectsUnknownOrIncompatibleOnes()
    {
        using var bench = new TempBench();
        bench.AddRun(Manifest("20261126-080000-smoke"), [Trace("t1", "perron-rich", 1, Results.Failed)]);
        bench.AddRun(Manifest("20261126-090000-smoke"), [Trace("t1", "perron-rich", 1, Results.Passed)]);
        bench.AddRun(Manifest("20261126-100000-smoke") with { Model = "claude-sonnet-5-5" }, [Trace("t1", "perron-rich", 1, Results.Passed)]);

        var (combined, output, _) = await RunAsync(bench.Directory, "report", "20261126-090000-smoke", "20261126-080000-smoke");
        var (unknown, _, unknownError) = await RunAsync(bench.Directory, "report", "20990101-000000-smoke");
        var (incompatible, _, incompatibleError) = await RunAsync(bench.Directory, "report", "20261126-090000-smoke", "20261126-100000-smoke");

        Assert.Equal(ExitCodes.Success, combined);
        Assert.Contains("success 50 %", output);
        Assert.True(File.Exists(Path.Combine(bench.Directory, "results", "20261126-090000-smoke", "report-2-runs.md")));
        Assert.Equal(ExitCodes.UsageError, unknown);
        Assert.Contains("Unknown run", unknownError);
        Assert.Equal(ExitCodes.UsageError, incompatible);
        Assert.Contains("model", incompatibleError);
    }

    [Fact]
    public async Task Cli_FailsWithoutRuns()
    {
        using var bench = new TempBench();

        var (exitCode, _, error) = await RunAsync(bench.Directory, "report");

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Contains("No runs", error);
    }

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(string benchDirectory, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await BenchCli.RunAsync(
            args,
            output,
            error,
            new Dictionary<string, string?> { [BenchDirectoryOptions.EnvironmentVariable] = benchDirectory },
            cancellationToken: TestContext.Current.CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    private static int CountOf(string text, string value) => (text.Length - text.Replace(value, "", StringComparison.Ordinal).Length) / value.Length;

    private static RunTrace Trace(
        string task,
        string server,
        int repeat,
        string? outcome = null,
        string status = RunStatus.Completed,
        TokenUsage? usage = null,
        decimal cost = 0.001m,
        string[]? errors = null,
        string[]? ungrounded = null,
        string? answer = "Answer",
        double llmMs = 1000,
        double toolMs = 100,
        bool[]? calls = null)
    {
        var toolCalls = (calls ?? [false])
            .Select((isError, index) => new TraceToolCall($"c{index}", "plan_trip", JsonDocument.Parse("{}").RootElement, "result", isError, 10, false))
            .ToList();
        var tokens = usage ?? new TokenUsage(100, 50, 0, 0);
        var excluded = status is RunStatus.NotCovered or RunStatus.InfraError;
        var evaluation = new Evaluation(
            outcome ?? (excluded ? Results.Excluded : Results.Passed),
            [],
            new GroundingResult(ungrounded ?? [], ungrounded ?? []),
            errors ?? (status == RunStatus.InfraError ? [ErrorCodes.Infra] : []),
            Evaluator.Version);
        return new RunTrace(
            "run", task, server, repeat, "claude-haiku-5-5", status, null, DateTimeOffset.UnixEpoch, 8, "system", "prompt",
            excluded ? [] : [new TraceTurn(1, llmMs, "end_turn", tokens, [], toolCalls)],
            excluded ? null : answer,
            excluded
                ? new TraceTotals(0, 0, new TokenUsage(0, 0, 0, 0), 0, 0, 0, 0)
                : new TraceTotals(1, toolCalls.Count, tokens, cost, llmMs, toolMs, 6),
            Evaluation: evaluation);
    }

    private static RunManifest Manifest(string runId) => new(
        runId,
        new DateTimeOffset(2026, 11, 26, 8, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 11, 26, 8, 30, 0, TimeSpan.Zero),
        "0123456789abcdef",
        false,
        "1.0.0",
        "smoke",
        "tasks/dev.jsonl",
        "taskset-hash",
        2,
        1,
        42,
        "claude-haiku-5-5",
        "medium",
        16000,
        3,
        8,
        Pricing,
        "prompt-hash",
        [Server("perron-rich", hosted: false), Server("official", hosted: true)]);

    private static ManifestServer Server(string id, bool hosted) => new(
        id,
        hosted ? null : "repository",
        hosted,
        hosted ? "transport.opendata.ch" : id,
        hosted ? "2c28784a" : "1.0.0",
        "2025-06-18",
        ["plan_trip"],
        $"hash-{id}",
        1000,
        "instructions-hash",
        ["stops", "trips", "departures"],
        []);

    /// <summary>A temporary bench directory with runs under results/.</summary>
    private sealed class TempBench : IDisposable
    {
        private readonly DirectoryInfo directory = System.IO.Directory.CreateTempSubdirectory("perron-report-");

        public string Directory => directory.FullName;

        public void AddRun(RunManifest manifest, IReadOnlyList<RunTrace> traces)
        {
            var run = Path.Combine(Directory, "results", manifest.RunId);
            System.IO.Directory.CreateDirectory(Path.Combine(run, "traces"));
            File.WriteAllText(Path.Combine(run, "manifest.json"), JsonSerializer.Serialize(manifest, TraceJsonContext.Default.RunManifest));
            foreach (var trace in traces)
            {
                var stored = trace with { RunId = manifest.RunId };
                File.WriteAllText(Path.Combine(run, "traces", stored.FileName), JsonSerializer.Serialize(stored, TraceJsonContext.Default.RunTrace));
            }
        }

        public void Dispose() => directory.Delete(recursive: true);
    }
}
