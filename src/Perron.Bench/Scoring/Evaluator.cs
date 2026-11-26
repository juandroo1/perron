using System.Text.Json;
using System.Text.RegularExpressions;

using Perron.Bench.Run;

namespace Perron.Bench.Scoring;

/// <summary>
/// Scores one run from its trace alone, without model or API (AK-9.1, AK-9.4, AK-9.6): every check of the task, the
/// grounding of the times in the answer, the outcome and the failure codes. A task passes if all its checks pass;
/// grounding does not change that, it is reported as hallucination rate.
/// </summary>
public static class Evaluator
{
    /// <summary>Bump when the scoring rules change, so rescored traces show which rules they were scored with.</summary>
    public const string Version = "1";

    private const int ToolErrorLoop = 3;

    public static Evaluation Evaluate(RunTrace trace)
    {
        var grounding = Ground(trace);

        if (trace.Status is RunStatus.InfraError or RunStatus.NotCovered)
        {
            return new Evaluation(
                Results.Excluded, [], grounding, trace.Status == RunStatus.InfraError ? [ErrorCodes.Infra] : [], Version);
        }

        var checks = CheckDefinitions.Of(trace.Checks)
            .Select((check, index) => Check(index, check, trace))
            .ToList();

        var outcome = checks.Count == 0 ? Results.Unscored
            : checks.Any(check => check.Result == Results.Failed) ? Results.Failed
            : checks.Any(check => check.Result == Results.Unscored) ? Results.Unscored
            : checks.Any(check => check.Result == Results.Pending) ? Results.Pending
            : Results.Passed;

        var errors = new List<string>();
        if (trace.Status == RunStatus.ToolLimit)
        {
            errors.Add(ErrorCodes.ToolLimit);
        }

        if (string.IsNullOrWhiteSpace(trace.FinalAnswer))
        {
            errors.Add(ErrorCodes.GaveUp);
        }

        if (!grounding.Grounded)
        {
            errors.Add(ErrorCodes.Hallucinated);
        }

        if (trace.Turns.SelectMany(turn => turn.ToolCalls).Count(call => call.IsError && !call.OverLimit) >= ToolErrorLoop)
        {
            errors.Add(ErrorCodes.ToolErrorLoop);
        }

        errors.AddRange(checks.Where(check => check.Result == Results.Failed && check.Code is not null).Select(check => check.Code!));

        return new Evaluation(outcome, checks, grounding, [.. errors.Distinct()], Version);
    }

    /// <summary>Times of the answer that appear in no tool result of the run, in the task or in the system prompt.</summary>
    public static GroundingResult Ground(RunTrace trace)
    {
        var times = ClockTimes.In(trace.FinalAnswer);
        var known = new HashSet<string>(ClockTimes.In(trace.Prompt));
        known.UnionWith(ClockTimes.In(trace.SystemPrompt));
        foreach (var call in trace.Turns.SelectMany(turn => turn.ToolCalls))
        {
            known.UnionWith(ClockTimes.InToolResult(call.Result));
        }

        return new GroundingResult(times, [.. times.Where(time => !known.Contains(time))]);
    }

    public static ScoreLine Line(RunTrace trace, Evaluation evaluation) => new(
        trace.RunId,
        trace.TaskId,
        trace.Category,
        trace.ServerId,
        trace.Repeat,
        trace.Status,
        evaluation.Outcome,
        evaluation.Grounding.Grounded,
        evaluation.Grounding.Ungrounded.Count,
        evaluation.Errors);

    private static CheckResult Check(int index, JsonElement check, RunTrace trace)
    {
        var type = check.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        try
        {
            return type switch
            {
                "regex" => RegexCheck(index, check, trace),
                "oracle" => OracleCheck.Evaluate(index, check, trace.Oracle?.FirstOrDefault(data => data.CheckIndex == index), trace.FinalAnswer, trace.ReferenceTime),
                "judge" => new CheckResult(index, "judge", Results.Pending, "judge checks follow in task 3.5"),
                _ => new CheckResult(index, type, Results.Unscored, $"unknown check type '{type}'"),
            };
        }
        catch (Exception ex) when (ex is FormatException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            return new CheckResult(index, type, Results.Unscored, $"check could not be evaluated: {ex.Message}");
        }
    }

    private static CheckResult RegexCheck(int index, JsonElement check, RunTrace trace)
    {
        var pattern = check.GetProperty("pattern").GetString()!;
        if (string.IsNullOrWhiteSpace(trace.FinalAnswer))
        {
            return new CheckResult(index, "regex", Results.Failed, "no answer", ErrorCodes.GaveUp);
        }

        return Regex.IsMatch(trace.FinalAnswer, pattern, RegexOptions.None, TimeSpan.FromSeconds(1))
            ? new CheckResult(index, "regex", Results.Passed, $"matches {pattern}")
            : new CheckResult(index, "regex", Results.Failed, $"does not match {pattern}", trace.Category == "stops" ? ErrorCodes.WrongStop : null);
    }
}
