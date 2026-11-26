using System.Diagnostics;
using System.Text.Json;

using Anthropic.Models.Messages;

using Perron.Bench.Run;
using Perron.Bench.Settings;

namespace Perron.Bench.Scoring;

/// <summary>A verdict of the LLM-as-judge on one judge check, stored in the trace.</summary>
/// <param name="Verdict"><c>pass</c> or <c>fail</c>; <c>null</c> when <see cref="Error"/> is set.</param>
/// <param name="Error">The judge could not be asked or gave no valid verdict; the check is then unscored.</param>
public sealed record Judgement(
    int CheckIndex,
    string Model,
    string? Verdict,
    string? Reason,
    TokenUsage Usage,
    decimal CostUsd,
    double LlmMs,
    DateTimeOffset JudgedAt,
    string? Error = null);

/// <summary>
/// LLM-as-judge for <c>ambiguous</c> and <c>bad_input</c> tasks (AK-9.2; plan.md, decision log). It sees only the
/// task, the rubric and the final answer: no server ID and no tool results, which would reveal the server's format.
/// The verdict comes as structured output. An empty answer fails without a model call.
/// </summary>
public sealed class Judge(IModelClient model, TimeProvider clock)
{
    public const string Pass = "pass";
    public const string Fail = "fail";

    public const string SystemPrompt =
        "You grade one answer of a public transport assistant for Switzerland against a rubric. "
        + "Judge only by the rubric, not by style, length or language. You do not know which system produced the answer "
        + "and cannot check facts; decide from the answer itself. Reply with JSON: verdict \"pass\" or \"fail\", "
        + "and a reason of one or two sentences in English.";

    /// <summary>SHA-256 of the judge's system prompt and message template, for the run manifest.</summary>
    public static string PromptSha256 { get; } = Run.SystemPrompt.Sha256(SystemPrompt + "\n" + Message("{task}", "{rubric}", "{answer}"));

    /// <summary>Indices of the judge checks of a trace and their rubrics.</summary>
    public static IReadOnlyList<(int Index, string Rubric)> ChecksOf(RunTrace trace) =>
        [.. CheckDefinitions.Of(trace.Checks)
            .Select((check, index) => (Check: check, Index: index))
            .Where(item => item.Check.TryGetProperty("type", out var type) && type.GetString() == "judge")
            .Select(item => (item.Index, item.Check.TryGetProperty("rubric", out var rubric) ? rubric.GetString() ?? "" : ""))];

    /// <summary>Verdicts for every judge check of the trace; none for runs that are excluded from scoring.</summary>
    public async Task<IReadOnlyList<Judgement>> JudgeAsync(
        RunTrace trace, string modelId, JudgeSettings settings, ModelPricing pricing, CancellationToken cancellationToken)
    {
        if (trace.Status is RunStatus.InfraError or RunStatus.NotCovered)
        {
            return [];
        }

        var judgements = new List<Judgement>();
        foreach (var (index, rubric) in ChecksOf(trace))
        {
            if (string.IsNullOrWhiteSpace(trace.FinalAnswer))
            {
                judgements.Add(new Judgement(index, modelId, Fail, "The assistant gave no answer.", TokenUsage.Zero, 0, 0, clock.GetUtcNow()));
                continue;
            }

            judgements.Add(await AskAsync(index, trace.Prompt, rubric, trace.FinalAnswer, modelId, settings, pricing, cancellationToken));
        }

        return judgements;
    }

    /// <summary>The request to the judge; public for tests.</summary>
    public static MessageCreateParams Request(string modelId, JudgeSettings settings, string task, string rubric, string answer) => new()
    {
        Model = modelId,
        MaxTokens = settings.MaxTokens,
        System = SystemPrompt,
        Messages = [new UserMessageParam { Content = Message(task, rubric, answer) }],
        OutputConfig = new OutputConfig
        {
            Effort = AgentLoop.EffortOf(settings.Effort),
            Format = new JsonOutputFormat { Schema = VerdictSchema() },
        },
    };

    private async Task<Judgement> AskAsync(
        int index, string task, string rubric, string answer, string modelId, JudgeSettings settings, ModelPricing pricing, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        Message response;
        try
        {
            response = await model.CreateAsync(Request(modelId, settings, task, rubric, answer), cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return new Judgement(index, modelId, null, null, TokenUsage.Zero, 0, Stopwatch.GetElapsedTime(started).TotalMilliseconds, clock.GetUtcNow(), $"{ex.GetType().Name}: {ex.Message}");
        }

        var ms = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var usage = new TokenUsage(
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            response.Usage.CacheCreationInputTokens ?? 0,
            response.Usage.CacheReadInputTokens ?? 0);
        var text = string.Concat(response.Content.Select(block => block.TryPickText(out var t) ? t.Text : ""));

        try
        {
            using var json = JsonDocument.Parse(text);
            var verdict = json.RootElement.GetProperty("verdict").GetString();
            var reason = json.RootElement.TryGetProperty("reason", out var r) ? r.GetString() : null;
            return verdict is Pass or Fail
                ? new Judgement(index, modelId, verdict, reason, usage, usage.Cost(pricing), ms, clock.GetUtcNow())
                : new Judgement(index, modelId, null, reason, usage, usage.Cost(pricing), ms, clock.GetUtcNow(), $"invalid verdict '{verdict}'");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return new Judgement(index, modelId, null, null, usage, usage.Cost(pricing), ms, clock.GetUtcNow(), $"no JSON verdict (stop reason {response.StopReason?.Raw()})");
        }
    }

    private static string Message(string task, string rubric, string answer) =>
        $"Task given to the assistant:\n<task>\n{task}\n</task>\n\nRubric:\n<rubric>\n{rubric}\n</rubric>\n\n"
        + $"The assistant's final answer:\n<answer>\n{answer}\n</answer>\n\nDoes the answer pass the rubric?";

    private static Dictionary<string, JsonElement> VerdictSchema() => new()
    {
        ["type"] = JsonSerializer.SerializeToElement("object", JudgeJsonContext.Default.String),
        ["properties"] = JsonDocument.Parse("""{"verdict":{"type":"string","enum":["pass","fail"]},"reason":{"type":"string"}}""").RootElement.Clone(),
        ["required"] = JsonDocument.Parse("""["verdict","reason"]""").RootElement.Clone(),
        ["additionalProperties"] = JsonDocument.Parse("false").RootElement.Clone(),
    };
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class JudgeJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
