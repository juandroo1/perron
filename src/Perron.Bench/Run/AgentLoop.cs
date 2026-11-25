using System.Diagnostics;
using System.Text.Json;

using Anthropic;
using Anthropic.Models.Messages;

using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Perron.Bench.Checks;
using Perron.Bench.Settings;

using McpTool = ModelContextProtocol.Protocol.Tool;

namespace Perron.Bench.Run;

/// <summary>The LLM behind the agent loop; replaced by a scripted model in tests.</summary>
public interface IModelClient
{
    Task<Message> CreateAsync(MessageCreateParams request, CancellationToken cancellationToken);
}

/// <summary>Claude over the Messages API.</summary>
public sealed class AnthropicModelClient(AnthropicClient client) : IModelClient
{
    public Task<Message> CreateAsync(MessageCreateParams request, CancellationToken cancellationToken) =>
        client.Messages.Create(request, cancellationToken: cancellationToken);
}

/// <summary>Result of a tool call as it goes back to the model.</summary>
public sealed record ToolOutcome(string Text, bool IsError);

/// <summary>Calls the tools of one server.</summary>
public interface IToolInvoker
{
    Task<ToolOutcome> CallAsync(string name, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken);
}

/// <summary>
/// Calls an MCP tool and turns the result into text the same way for every server (plan.md, section 6): the text
/// content, otherwise the structured content as JSON. Protocol errors (e.g. an unknown tool) go back to the model as
/// tool errors; transport failures propagate and end the run as an infrastructure error.
/// </summary>
public sealed class McpToolInvoker(McpClient client) : IToolInvoker
{
    public async Task<ToolOutcome> CallAsync(
        string name, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        CallToolResult result;
        try
        {
            result = await client.CallToolAsync(
                name, arguments.ToDictionary(pair => pair.Key, pair => (object?)pair.Value), cancellationToken: cancellationToken);
        }
        catch (McpException ex)
        {
            return new ToolOutcome(ex.Message, true);
        }

        return new ToolOutcome(ResultText(result), result.IsError == true);
    }

    public static string ResultText(CallToolResult result)
    {
        var texts = result.Content.OfType<TextContentBlock>().Select(block => block.Text).ToList();
        if (texts.Count > 0)
        {
            return string.Join("\n", texts);
        }

        if (result.StructuredContent is { } structured)
        {
            return JsonSerializer.Serialize(structured, McpJsonUtilities.DefaultOptions);
        }

        return result.Content.Count == 0 ? "" : "[non-text content]";
    }
}

/// <summary>What one run needs: the task, the server's tools and the fixed settings (AK-8.1).</summary>
public sealed record AgentRunRequest(
    string RunId,
    Tasks.BenchTask Task,
    string ServerId,
    int Repeat,
    string Model,
    string Effort,
    int MaxTokens,
    int MaxToolCalls,
    DateTimeOffset ReferenceTime,
    string SystemPrompt,
    IReadOnlyList<McpTool> Tools,
    ModelPricing Pricing);

/// <summary>
/// The agent loop (AK-8.1): asks the model, runs the tools it calls, returns all results of a turn in one message, and
/// stops at end of turn, at the tool-call limit, at max_tokens, at a refusal or at an infrastructure error. Thinking
/// blocks go back unchanged. Every step is recorded in the trace (AK-8.2).
/// </summary>
public sealed class AgentLoop(IModelClient model)
{
    public const string LimitMessage = "Tool call limit reached ({0} calls). Answer now with the information you have.";

    public async Task<RunTrace> RunAsync(AgentRunRequest request, IToolInvoker tools, CancellationToken cancellationToken)
    {
        List<MessageParam> messages = [new UserMessageParam { Content = request.Task.Prompt }];
        List<ToolUnion> toolDefinitions = [.. request.Tools.Select(tool => (ToolUnion)AnthropicToolTokenCounter.ToAnthropicTool(tool))];

        var turns = new List<TraceTurn>();
        var toolCalls = 0;
        var limitAnnounced = false;
        string status = RunStatus.ToolLimit;
        string? detail = null;
        string? finalAnswer = null;

        // A run needs at most one turn per allowed call, one after the limit message and the final answer.
        for (var number = 1; number <= request.MaxToolCalls + 2; number++)
        {
            var parameters = new MessageCreateParams
            {
                Model = request.Model,
                MaxTokens = request.MaxTokens,
                System = request.SystemPrompt,
                Messages = [.. messages],
                OutputConfig = new OutputConfig { Effort = EffortOf(request.Effort) },
                CacheControl = new CacheControlEphemeral(),
            };
            if (toolDefinitions.Count > 0)
            {
                parameters = parameters with { Tools = toolDefinitions };
            }

            var started = Stopwatch.GetTimestamp();
            Message response;
            try
            {
                response = await model.CreateAsync(parameters, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                status = RunStatus.InfraError;
                detail = $"model: {ex.GetType().Name}: {ex.Message}";
                break;
            }

            var llmMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var stopReason = response.StopReason?.Raw();
            var (assistantContent, blocks, toolUses) = Read(response);
            var text = string.Join("\n", blocks.Where(block => block.Type == "text").Select(block => block.Text));
            var turnCalls = new List<TraceToolCall>();
            var usage = UsageOf(response);

            if (stopReason == "tool_use" && toolUses.Count > 0)
            {
                if (limitAnnounced)
                {
                    turns.Add(new TraceTurn(number, llmMs, stopReason, usage, blocks, turnCalls));
                    status = RunStatus.ToolLimit;
                    detail = "the model kept calling tools after the limit message";
                    finalAnswer = string.IsNullOrWhiteSpace(text) ? null : text;
                    break;
                }

                List<ContentBlockParam> results = [];
                foreach (var toolUse in toolUses)
                {
                    var arguments = JsonSerializer.SerializeToElement(toolUse.Input, AgentJsonContext.Default.IReadOnlyDictionaryStringJsonElement);
                    if (toolCalls >= request.MaxToolCalls)
                    {
                        var message = string.Format(System.Globalization.CultureInfo.InvariantCulture, LimitMessage, request.MaxToolCalls);
                        turnCalls.Add(new TraceToolCall(toolUse.ID, toolUse.Name, arguments, message, true, 0, true));
                        results.Add(new ToolResultBlockParam { ToolUseID = toolUse.ID, Content = message, IsError = true });
                        limitAnnounced = true;
                        continue;
                    }

                    toolCalls++;
                    var callStarted = Stopwatch.GetTimestamp();
                    ToolOutcome outcome;
                    try
                    {
                        outcome = await tools.CallAsync(toolUse.Name, toolUse.Input, cancellationToken);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        turns.Add(new TraceTurn(number, llmMs, stopReason, usage, blocks, turnCalls));
                        status = RunStatus.InfraError;
                        detail = $"tool {toolUse.Name}: {ex.GetType().Name}: {ex.Message}";
                        return Trace(request, status, detail, turns, finalAnswer);
                    }

                    var callMs = Stopwatch.GetElapsedTime(callStarted).TotalMilliseconds;
                    turnCalls.Add(new TraceToolCall(toolUse.ID, toolUse.Name, arguments, outcome.Text, outcome.IsError, callMs, false));
                    results.Add(new ToolResultBlockParam { ToolUseID = toolUse.ID, Content = outcome.Text, IsError = outcome.IsError });
                }

                turns.Add(new TraceTurn(number, llmMs, stopReason, usage, blocks, turnCalls));
                messages.Add(new AssistantMessageParam { Content = assistantContent });
                messages.Add(new UserMessageParam { Content = results });
                continue;
            }

            turns.Add(new TraceTurn(number, llmMs, stopReason, usage, blocks, turnCalls));
            finalAnswer = string.IsNullOrWhiteSpace(text) ? null : text;
            (status, detail) = stopReason switch
            {
                "max_tokens" => (RunStatus.MaxTokens, null),
                "refusal" => (RunStatus.Refusal, response.StopDetails?.ToString()),
                _ => (RunStatus.Completed, (string?)null),
            };
            break;
        }

        return Trace(request, status, detail, turns, finalAnswer);
    }

    /// <summary>A trace for a task the server cannot cover: no model call (AK-9.3).</summary>
    public static RunTrace NotCovered(AgentRunRequest request, IReadOnlyList<string> missing) =>
        Trace(request, RunStatus.NotCovered, $"missing capabilities: {string.Join(", ", missing)}", [], null);

    private static RunTrace Trace(AgentRunRequest request, string status, string? detail, List<TraceTurn> turns, string? finalAnswer)
    {
        var usage = turns.Aggregate(TokenUsage.Zero, (sum, turn) => sum.Add(turn.Usage));
        var calls = turns.SelectMany(turn => turn.ToolCalls).ToList();
        var totals = new TraceTotals(
            turns.Count,
            calls.Count(call => !call.OverLimit),
            usage,
            usage.Cost(request.Pricing),
            turns.Sum(turn => turn.LlmMs),
            calls.Sum(call => call.DurationMs),
            calls.Where(call => !call.OverLimit).Sum(call => (long)call.Result.Length));

        return new RunTrace(
            request.RunId,
            request.Task.Id,
            request.ServerId,
            request.Repeat,
            request.Model,
            status,
            detail,
            request.ReferenceTime,
            request.MaxToolCalls,
            request.SystemPrompt,
            request.Task.Prompt,
            turns,
            finalAnswer,
            totals);
    }

    /// <summary>The assistant turn as request blocks (unchanged, thinking with signature), as trace blocks, and its tool calls.</summary>
    private static (List<ContentBlockParam> Content, List<TraceBlock> Blocks, List<ToolUseBlock> ToolUses) Read(Message response)
    {
        List<ContentBlockParam> content = [];
        List<TraceBlock> blocks = [];
        List<ToolUseBlock> toolUses = [];
        foreach (var block in response.Content)
        {
            if (block.TryPickText(out var text))
            {
                content.Add(new TextBlockParam { Text = text.Text });
                blocks.Add(new TraceBlock("text", text.Text));
            }
            else if (block.TryPickThinking(out var thinking))
            {
                content.Add(new ThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
                blocks.Add(new TraceBlock("thinking", thinking.Thinking));
            }
            else if (block.TryPickRedactedThinking(out var redacted))
            {
                content.Add(new RedactedThinkingBlockParam { Data = redacted.Data });
                blocks.Add(new TraceBlock("redacted_thinking"));
            }
            else if (block.TryPickToolUse(out var toolUse))
            {
                content.Add(new ToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
                blocks.Add(new TraceBlock(
                    "tool_use",
                    ToolUseId: toolUse.ID,
                    Name: toolUse.Name,
                    Input: JsonSerializer.SerializeToElement(toolUse.Input, AgentJsonContext.Default.IReadOnlyDictionaryStringJsonElement)));
                toolUses.Add(toolUse);
            }
        }

        return (content, blocks, toolUses);
    }

    private static TokenUsage UsageOf(Message response) => new(
        response.Usage.InputTokens,
        response.Usage.OutputTokens,
        response.Usage.CacheCreationInputTokens ?? 0,
        response.Usage.CacheReadInputTokens ?? 0);

    private static Effort EffortOf(string effort) => effort switch
    {
        "low" => Effort.Low,
        "medium" => Effort.Medium,
        "high" => Effort.High,
        "max" => Effort.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, "Unsupported effort."),
    };
}

[System.Text.Json.Serialization.JsonSerializable(typeof(IReadOnlyDictionary<string, JsonElement>))]
internal sealed partial class AgentJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
