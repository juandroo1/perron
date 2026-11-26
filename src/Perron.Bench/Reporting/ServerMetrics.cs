using Perron.Bench.Run;
using Perron.Bench.Scoring;
using Perron.Bench.Settings;

namespace Perron.Bench.Reporting;

/// <summary>The figures of one server in a report (AK-10.1).</summary>
public sealed record ServerMetrics(
    string ServerId,
    int Tasks,
    int CoveredTasks,
    Estimate? SuccessCovered,
    Estimate? SuccessAll,
    double? PassK,
    int K,
    int Scored,
    int Passed,
    int Pending,
    int Unscored,
    int InfraErrors,
    int Runs,
    long? ToolTokens,
    double ToolCallsPerRun,
    double FailedToolCallShare,
    double TokensPerRun,
    decimal CostPerTaskUncached,
    decimal? CostPerSolvedUncached,
    decimal CostActualPerTask,
    decimal CostJudge,
    double LlmMsPerRun,
    double ToolMsPerRun,
    double? HallucinationRate,
    int FailedWithoutCode,
    IReadOnlyDictionary<string, int> Errors)
{
    /// <summary>Per task: share of passed scored runs; <c>null</c> for tasks without scored runs.</summary>
    public required IReadOnlyDictionary<string, double?> TaskSuccess { get; init; }

    /// <summary>Tasks the server does not cover.</summary>
    public required IReadOnlySet<string> NotCovered { get; init; }

    public static ServerMetrics Of(string serverId, IReadOnlyList<string> tasks, IReadOnlyList<RunTrace> traces, int k, long? toolTokens, ModelPricing pricing)
    {
        var mine = traces.Where(trace => trace.ServerId == serverId).ToList();
        var notCovered = tasks.Where(task => mine.Where(trace => trace.TaskId == task).All(trace => trace.Status == RunStatus.NotCovered)).ToHashSet();
        var covered = tasks.Where(task => !notCovered.Contains(task)).ToList();

        // Success per task: share of passed runs among the scored ones (passed or failed).
        var taskSuccess = tasks.ToDictionary(
            task => task,
            task =>
            {
                var scored = mine.Where(trace => trace.TaskId == task && Outcome(trace) is Results.Passed or Results.Failed).ToList();
                return scored.Count == 0 ? (double?)null : scored.Count(trace => Outcome(trace) == Results.Passed) / (double)scored.Count;
            });

        var successCovered = covered.Select(task => taskSuccess[task]).OfType<double>().ToList();
        var successAll = tasks
            .Select(task => notCovered.Contains(task) ? 0 : taskSuccess[task])
            .OfType<double>()
            .ToList();

        // pass^k: share of covered tasks whose k scored runs all passed (k = repeats over all runs of the report).
        var withK = covered
            .Select(task => mine.Where(trace => trace.TaskId == task && Outcome(trace) is Results.Passed or Results.Failed).ToList())
            .Where(scored => scored.Count >= k && k > 0)
            .ToList();
        double? passK = withK.Count == 0 ? null : withK.Count(scored => scored.All(trace => Outcome(trace) == Results.Passed)) / (double)withK.Count;

        // Runs that reached the model: not excluded for coverage or infrastructure.
        var runs = mine.Where(trace => trace.Status is not (RunStatus.NotCovered or RunStatus.InfraError)).ToList();
        var calls = runs.SelectMany(trace => trace.Turns.SelectMany(turn => turn.ToolCalls)).Where(call => !call.OverLimit).ToList();
        var uncached = runs.Sum(trace => Uncached(trace.Totals.Usage, pricing));
        var passed = mine.Count(trace => Outcome(trace) == Results.Passed);
        var answered = runs.Where(trace => !string.IsNullOrWhiteSpace(trace.FinalAnswer)).ToList();

        return new ServerMetrics(
            serverId,
            tasks.Count,
            covered.Count,
            Statistics.Bootstrap(successCovered),
            Statistics.Bootstrap(successAll),
            passK,
            k,
            mine.Count(trace => Outcome(trace) is Results.Passed or Results.Failed),
            passed,
            mine.Count(trace => Outcome(trace) == Results.Pending),
            mine.Count(trace => Outcome(trace) == Results.Unscored),
            mine.Count(trace => trace.Status == RunStatus.InfraError),
            runs.Count,
            toolTokens,
            runs.Count == 0 ? 0 : runs.Average(trace => (double)trace.Totals.ToolCalls),
            calls.Count == 0 ? 0 : calls.Count(call => call.IsError) / (double)calls.Count,
            runs.Count == 0 ? 0 : runs.Average(trace => (double)Tokens(trace.Totals.Usage)),
            runs.Count == 0 ? 0 : uncached / runs.Count,
            passed == 0 ? null : uncached / passed,
            runs.Count == 0 ? 0 : runs.Sum(trace => trace.Totals.CostUsd) / runs.Count,
            mine.Sum(trace => trace.Judgements?.Sum(judgement => judgement.CostUsd) ?? 0),
            runs.Count == 0 ? 0 : runs.Average(trace => trace.Totals.LlmMs),
            runs.Count == 0 ? 0 : runs.Average(trace => trace.Totals.ToolMs),
            answered.Count == 0 ? null : answered.Count(trace => trace.Evaluation?.Grounding.Grounded == false) / (double)answered.Count,
            mine.Count(trace => Outcome(trace) == Results.Failed && (trace.Evaluation?.Errors ?? []).Count == 0),
            mine.SelectMany(trace => trace.Evaluation?.Errors ?? [])
                .GroupBy(code => code)
                .ToDictionary(group => group.Key, group => group.Count()))
        {
            TaskSuccess = taskSuccess,
            NotCovered = notCovered,
        };
    }

    /// <summary>Cost as if nothing came from the prompt cache: cache tokens at the input price (plan.md, section 6).</summary>
    public static decimal Uncached(TokenUsage usage, ModelPricing pricing) =>
        (((usage.Input + usage.CacheWrite + usage.CacheRead) * pricing.Input) + (usage.Output * pricing.Output)) / 1_000_000m;

    private static long Tokens(TokenUsage usage) => usage.Input + usage.CacheWrite + usage.CacheRead + usage.Output;

    private static string Outcome(RunTrace trace) => trace.Evaluation?.Outcome ?? Evaluator.Evaluate(trace).Outcome;
}
