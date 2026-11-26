using System.Text.Json;

namespace Perron.Bench.Scoring;

/// <summary>A connection as the oracle saw it (times in Europe/Zurich).</summary>
/// <param name="RideDeparture">Planned departure of the first vehicle when the connection starts with a walk; else <c>null</c>.</param>
/// <param name="RideArrival">Planned arrival of the last vehicle when the connection ends with a walk; else <c>null</c>.</param>
public sealed record OracleConnection(
    DateTimeOffset Departure,
    DateTimeOffset? DepartureForecast,
    DateTimeOffset Arrival,
    DateTimeOffset? ArrivalForecast,
    int Transfers,
    IReadOnlyList<string> Lines,
    DateTimeOffset? RideDeparture = null,
    DateTimeOffset? RideArrival = null);

/// <summary>A departure (or arrival) as the oracle saw it.</summary>
public sealed record OracleDeparture(DateTimeOffset Time, DateTimeOffset? Forecast, string? Line, string? Destination);

/// <summary>
/// What transport.opendata.ch answered for one oracle check, fetched once per task round from the reference time
/// (AK-9.1) and stored in the trace, so <c>bench rescore</c> needs no API (AK-9.6). Only entries that meet the task's
/// constraints are stored: the query itself carries via, minimum transfer time, means of transport, line and towards.
/// </summary>
/// <param name="CheckIndex">Index of the check in the task's <c>checks</c>.</param>
/// <param name="Error">Set when the oracle could not be asked; the check is then unscored.</param>
public sealed record OracleData(
    int CheckIndex,
    string Oracle,
    DateTimeOffset FetchedAt,
    string? Error = null,
    IReadOnlyList<OracleConnection>? Connections = null,
    IReadOnlyList<OracleDeparture>? Departures = null,
    int? Occupancy = null,
    IReadOnlyList<string>? Candidates = null);

/// <summary>Values of <see cref="CheckResult.Result"/> and <see cref="Evaluation.Outcome"/>.</summary>
public static class Results
{
    public const string Passed = "passed";
    public const string Failed = "failed";
    public const string Pending = "pending";
    public const string Unscored = "unscored";
    public const string Excluded = "excluded";
}

/// <summary>Failure codes of the error taxonomy (plan.md, section 6).</summary>
public static class ErrorCodes
{
    public const string Infra = "infra";
    public const string ToolLimit = "tool_limit";
    public const string GaveUp = "gave_up";
    public const string Hallucinated = "hallucinated";
    public const string NotOptimal = "not_optimal";
    public const string WrongTime = "wrong_time";
    public const string WrongStop = "wrong_stop";
    public const string ToolErrorLoop = "tool_error_loop";
}

/// <summary>Result of one check of a task.</summary>
/// <param name="Code">Failure code for a failed check, if one can be told automatically.</param>
public sealed record CheckResult(int Index, string Type, string Result, string Detail, string? Code = null);

/// <summary>Times (HH:MM) of the final answer and those found in no tool result, task or system prompt (AK-9.4).</summary>
public sealed record GroundingResult(IReadOnlyList<string> Times, IReadOnlyList<string> Ungrounded)
{
    public bool Grounded => Ungrounded.Count == 0;
}

/// <summary>The scoring of one run: outcome, checks, grounding and failure codes.</summary>
public sealed record Evaluation(
    string Outcome,
    IReadOnlyList<CheckResult> Checks,
    GroundingResult Grounding,
    IReadOnlyList<string> Errors,
    string ScorerVersion);

/// <summary>One line of <c>scores.jsonl</c>.</summary>
public sealed record ScoreLine(
    string RunId,
    string TaskId,
    string? Category,
    string ServerId,
    int Repeat,
    string Status,
    string Outcome,
    bool Grounded,
    int UngroundedTimes,
    IReadOnlyList<string> Errors);

/// <summary>Raw check definitions of a task, as stored in a trace.</summary>
public static class CheckDefinitions
{
    public static IReadOnlyList<JsonElement> Of(JsonElement? checks) =>
        checks is { ValueKind: JsonValueKind.Array } array ? [.. array.EnumerateArray()] : [];
}
