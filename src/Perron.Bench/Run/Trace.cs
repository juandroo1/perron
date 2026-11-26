using System.Text.Json;
using System.Text.Json.Serialization;

using Perron.Bench.Scoring;
using Perron.Bench.Settings;

namespace Perron.Bench.Run;

/// <summary>How a run ended (plan.md, decision log).</summary>
public static class RunStatus
{
    public const string Completed = "completed";
    public const string ToolLimit = "tool_limit";
    public const string MaxTokens = "max_tokens";
    public const string Refusal = "refusal";
    public const string InfraError = "infra_error";
    public const string NotCovered = "not_covered";
}

/// <summary>Tokens of one request or a whole run.</summary>
public sealed record TokenUsage(long Input, long Output, long CacheWrite, long CacheRead)
{
    public static TokenUsage Zero { get; } = new(0, 0, 0, 0);

    public TokenUsage Add(TokenUsage other) =>
        new(Input + other.Input, Output + other.Output, CacheWrite + other.CacheWrite, CacheRead + other.CacheRead);

    /// <summary>USD for this usage at the given prices (per million tokens).</summary>
    public decimal Cost(ModelPricing pricing) =>
        ((Input * pricing.Input) + (Output * pricing.Output) + (CacheWrite * pricing.CacheWrite) + (CacheRead * pricing.CacheRead)) / 1_000_000m;
}

/// <summary>A content block of an assistant turn. Thinking text is usually empty (display "omitted").</summary>
public sealed record TraceBlock(string Type, string? Text = null, string? ToolUseId = null, string? Name = null, JsonElement? Input = null);

/// <summary>One tool call: arguments as sent by the model, the result as returned to it, and the time it took.</summary>
/// <param name="OverLimit">The call exceeded max_tool_calls and was answered with the limit message, not executed.</param>
public sealed record TraceToolCall(
    string Id, string Name, JsonElement Arguments, string Result, bool IsError, double DurationMs, bool OverLimit);

/// <summary>One request to the model and what followed from it.</summary>
public sealed record TraceTurn(
    int Number,
    double LlmMs,
    string? StopReason,
    TokenUsage Usage,
    IReadOnlyList<TraceBlock> Content,
    IReadOnlyList<TraceToolCall> ToolCalls);

/// <summary>Totals of a run.</summary>
public sealed record TraceTotals(int Turns, int ToolCalls, TokenUsage Usage, decimal CostUsd, double LlmMs, double ToolMs, long ToolResultChars);

/// <summary>
/// Complete record of one run of one task on one server (AK-8.2): system prompt, task, every turn with its blocks,
/// tool calls with arguments, results and latency, tokens and cost; the task's checks, the oracle data of its round
/// and the evaluation (AK-9.x). <c>bench rescore</c> recomputes <see cref="Evaluation"/> from the rest (AK-9.6).
/// </summary>
/// <param name="Attempt">2 when the run was repeated after an infrastructure error (AK-9.5).</param>
public sealed record RunTrace(
    string RunId,
    string TaskId,
    string ServerId,
    int Repeat,
    string Model,
    string Status,
    string? StatusDetail,
    DateTimeOffset ReferenceTime,
    int MaxToolCalls,
    string SystemPrompt,
    string Prompt,
    IReadOnlyList<TraceTurn> Turns,
    string? FinalAnswer,
    TraceTotals Totals,
    string? Category = null,
    JsonElement? Checks = null,
    IReadOnlyList<OracleData>? Oracle = null,
    Evaluation? Evaluation = null,
    int Attempt = 1)
{
    public string FileName => $"{TaskId}__{ServerId}__r{Repeat}.json";
}

/// <summary>One line of <c>runs.jsonl</c>: the figures of a run for the report and for calibrating <c>--dry-run</c>.</summary>
public sealed record RunSummary(
    string RunId,
    string TaskId,
    string ServerId,
    int Repeat,
    string Model,
    string Status,
    int Turns,
    int ToolCalls,
    long InputTokens,
    long OutputTokens,
    long CacheWriteTokens,
    long CacheReadTokens,
    decimal CostUsd,
    double LlmMs,
    double ToolMs,
    long ToolResultChars)
{
    public static RunSummary Of(RunTrace trace) => new(
        trace.RunId,
        trace.TaskId,
        trace.ServerId,
        trace.Repeat,
        trace.Model,
        trace.Status,
        trace.Totals.Turns,
        trace.Totals.ToolCalls,
        trace.Totals.Usage.Input,
        trace.Totals.Usage.Output,
        trace.Totals.Usage.CacheWrite,
        trace.Totals.Usage.CacheRead,
        trace.Totals.CostUsd,
        trace.Totals.LlmMs,
        trace.Totals.ToolMs,
        trace.Totals.ToolResultChars);
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true)]
[JsonSerializable(typeof(RunTrace))]
[JsonSerializable(typeof(RunManifest))]
public sealed partial class TraceJsonContext : JsonSerializerContext;

/// <summary>runs.jsonl: one compact line per run.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower)]
[JsonSerializable(typeof(RunSummary))]
[JsonSerializable(typeof(ScoreLine))]
public sealed partial class SummaryJsonContext : JsonSerializerContext;
