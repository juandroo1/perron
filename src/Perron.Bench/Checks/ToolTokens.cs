using System.Text.Json;

using Anthropic;
using Anthropic.Models.Messages;

using McpTool = ModelContextProtocol.Protocol.Tool;

namespace Perron.Bench.Checks;

/// <summary>Counts how many input tokens a server's tool definitions add to a request (AK-7.2).</summary>
public interface IToolTokenCounter
{
    /// <summary>False without an API key: the size is reported as "n/a".</summary>
    bool IsAvailable { get; }

    /// <returns>The tokens, or <c>null</c> when counting is not available.</returns>
    Task<long?> CountAsync(IReadOnlyList<McpTool> tools, CancellationToken cancellationToken);
}

/// <summary>Used without <c>ANTHROPIC_API_KEY</c> (e.g. in CI).</summary>
public sealed class UnavailableToolTokenCounter : IToolTokenCounter
{
    public bool IsAvailable => false;

    public Task<long?> CountAsync(IReadOnlyList<McpTool> tools, CancellationToken cancellationToken) =>
        Task.FromResult<long?>(null);
}

/// <summary>
/// <c>count_tokens</c> with the benchmark model, once with and once without the tools; the difference is what the
/// tool definitions cost in every request (plan.md, decision log).
/// </summary>
public sealed class AnthropicToolTokenCounter(AnthropicClient client, string model) : IToolTokenCounter
{
    public bool IsAvailable => true;

    public async Task<long?> CountAsync(IReadOnlyList<McpTool> tools, CancellationToken cancellationToken)
    {
        var withTools = await client.Messages.CountTokens(Request(model, tools), cancellationToken: cancellationToken);
        var without = await client.Messages.CountTokens(Request(model, []), cancellationToken: cancellationToken);
        return withTools.InputTokens - without.InputTokens;
    }

    /// <summary>The request both counts use: one short user message, plus the tools if any.</summary>
    public static MessageCountTokensParams Request(string model, IReadOnlyList<McpTool> tools)
    {
        var request = new MessageCountTokensParams
        {
            Model = model,
            Messages = [new UserMessageParam { Content = "Hello" }],
        };

        return tools.Count == 0 ? request : request with { Tools = [.. tools.Select(ToAnthropicTool)] };
    }

    /// <summary>
    /// An MCP tool in the Messages API format: name, description and the input schema unchanged (including
    /// <c>$defs</c> and other keywords the typed schema does not model).
    /// </summary>
    public static Tool ToAnthropicTool(McpTool tool)
    {
        var raw = new Dictionary<string, JsonElement>
        {
            ["name"] = JsonSerializer.SerializeToElement(tool.Name, ToolTokensJsonContext.Default.String),
            ["input_schema"] = tool.InputSchema.Clone(),
        };
        if (!string.IsNullOrEmpty(tool.Description))
        {
            raw["description"] = JsonSerializer.SerializeToElement(tool.Description, ToolTokensJsonContext.Default.String);
        }

        return Tool.FromRawUnchecked(raw);
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class ToolTokensJsonContext : System.Text.Json.Serialization.JsonSerializerContext;
