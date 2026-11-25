using System.Text.Json;
using System.Text.Json.Nodes;

using ModelContextProtocol;

using Perron.Bench.Checks;

using McpTool = ModelContextProtocol.Protocol.Tool;

namespace Perron.Bench.Tests;

/// <summary>The tools/list hash and the translation of MCP tools for count_tokens (AK-7.1, AK-7.2). No API calls.</summary>
public sealed class ToolDefinitionTests
{
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures", "foreign-servers");

    [Fact]
    public void Hash_IgnoresToolOrderAndKeyOrder()
    {
        var a = Tool("a", "First tool.", """{"type":"object","properties":{"x":{"type":"string"}}}""");
        var b = Tool("b", "Second tool.", """{"properties":{"y":{"type":"integer"}},"type":"object"}""");
        var bReordered = Tool("b", "Second tool.", """{"type":"object","properties":{"y":{"type":"integer"}}}""");

        Assert.Equal(ToolListHash.Compute([a, b]), ToolListHash.Compute([bReordered, a]));
        Assert.Matches("^[0-9a-f]{64}$", ToolListHash.Compute([a, b]));
    }

    [Fact]
    public void Hash_ChangesWithAnythingAModelSees()
    {
        var baseline = ToolListHash.Compute([Tool("a", "Find stops.", """{"type":"object"}""")]);

        Assert.NotEqual(baseline, ToolListHash.Compute([Tool("a", "Find stops!", """{"type":"object"}""")]));
        Assert.NotEqual(baseline, ToolListHash.Compute([Tool("b", "Find stops.", """{"type":"object"}""")]));
        Assert.NotEqual(baseline, ToolListHash.Compute([Tool("a", "Find stops.", """{"type":"object","required":["x"]}""")]));

        var annotated = Tool("a", "Find stops.", """{"type":"object"}""");
        annotated.Annotations = new() { ReadOnlyHint = true };
        Assert.NotEqual(baseline, ToolListHash.Compute([annotated]));
    }

    [Theory]
    [InlineData("official")]
    [InlineData("spt")]
    [InlineData("stm")]
    public void Hash_OfARecordedToolsList_IsStable(string server)
    {
        using var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, $"{server}.json")));
        var tools = snapshot.RootElement.GetProperty("tools");

        Assert.Equal(ToolListHash.Compute(tools), ToolListHash.Compute(tools));
    }

    [Fact]
    public void AnthropicTool_KeepsNameDescriptionAndTheFullSchema()
    {
        // stm's schemas use $defs, which the typed input schema of the SDK does not model.
        var tools = StmTools();
        var search = tools.Single(tool => tool.Name == "transport_search_stop");

        var converted = AnthropicToolTokenCounter.ToAnthropicTool(search);

        Assert.Equal("transport_search_stop", converted.RawData["name"].GetString());
        Assert.Equal(search.Description, converted.RawData["description"].GetString());
        var schema = converted.RawData["input_schema"];
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(search.InputSchema.GetRawText()), JsonNode.Parse(schema.GetRawText())));
        Assert.True(schema.TryGetProperty("$defs", out _));
    }

    [Fact]
    public void CountTokensRequest_HasOneMessage_AndAllTools()
    {
        var tools = StmTools();

        var withTools = AnthropicToolTokenCounter.Request("claude-haiku-5-5", tools);
        var without = AnthropicToolTokenCounter.Request("claude-haiku-5-5", []);

        Assert.Single(withTools.Messages);
        Assert.Equal(tools.Count, withTools.Tools!.Count);
        Assert.Null(without.Tools);
        Assert.Single(without.Messages);
    }

    private static List<McpTool> StmTools()
    {
        using var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "stm.json")));
        return
        [
            .. snapshot.RootElement.GetProperty("tools").EnumerateArray()
                .Select(tool => tool.Deserialize<McpTool>(McpJsonUtilities.DefaultOptions)!),
        ];
    }

    private static McpTool Tool(string name, string description, string schema) => new()
    {
        Name = name,
        Description = description,
        InputSchema = JsonDocument.Parse(schema).RootElement.Clone(),
    };
}
