using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Perron.Mcp.Variants;

namespace Perron.Mcp.Tests;

/// <summary>Variants over configuration (AK-5.1), their names (AK-5.2) and their tools/list snapshots (AK-4.3).</summary>
public sealed class VariantTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static TheoryData<string, string?, string?, string?> Variants() => new()
    {
        { "perron-rich", null, null, null },
        { "perron-minimal", null, "minimal", null },
        { "perron-json", null, null, "json" },
    };

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task ToolsList_MatchesSnapshot_AndServerInfoNamesTheVariant(
        string name, string? tools, string? descriptions, string? format)
    {
        await using var app = WithVariant(tools, descriptions, format);
        await using var client = await ConnectAsync(app);

        var list = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(name, client.ServerInfo?.Name);
        var snapshot = new JsonArray([.. list.OrderBy(tool => tool.Name, StringComparer.Ordinal).Select(tool => (JsonNode?)new JsonObject
        {
            ["name"] = tool.Name,
            ["title"] = tool.ProtocolTool.Title,
            ["description"] = tool.Description,
            ["annotations"] = JsonSerializer.SerializeToNode(tool.ProtocolTool.Annotations),
            ["inputSchema"] = JsonNode.Parse(tool.ProtocolTool.InputSchema.GetRawText()),
        })]);
        Snapshot.Match($"tools_list-{name}", snapshot.ToJsonString(Pretty).ReplaceLineEndings("\n"), "json");
    }

    [Theory]
    [MemberData(nameof(Variants))]
    public async Task EveryParameter_HasADescription(string name, string? tools, string? descriptions, string? format)
    {
        await using var app = WithVariant(tools, descriptions, format);
        await using var client = await ConnectAsync(app);

        var list = await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        foreach (var tool in list)
        {
            Assert.False(string.IsNullOrWhiteSpace(tool.Description), $"{name}: {tool.Name} needs a description");
            foreach (var parameter in tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject())
            {
                Assert.True(
                    parameter.Value.TryGetProperty("description", out var text) && !string.IsNullOrWhiteSpace(text.GetString()),
                    $"{name}: {tool.Name}.{parameter.Name} needs a description");
            }
        }
    }

    [Fact]
    public async Task Minimal_IsShorterThanRich_ButKeepsFormatsAndRanges()
    {
        await using var rich = WithVariant(null, null, null);
        await using var minimal = WithVariant(null, "minimal", null);
        await using var richClient = await ConnectAsync(rich);
        await using var minimalClient = await ConnectAsync(minimal);

        var richTools = await richClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);
        var minimalTools = await minimalClient.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(richTools.Select(tool => tool.Name).Order(), minimalTools.Select(tool => tool.Name).Order());
        foreach (var tool in minimalTools)
        {
            var richText = richTools.Single(candidate => candidate.Name == tool.Name).Description!;
            Assert.True(tool.Description!.Length * 4 < richText.Length, $"{tool.Name}: minimal should be much shorter");
            Assert.DoesNotContain("Example", tool.Description, StringComparison.OrdinalIgnoreCase);
        }

        var planTrip = minimalTools.Single(tool => tool.Name == "plan_trip").ProtocolTool.InputSchema.GetProperty("properties");
        Assert.Equal("Date and time, YYYY-MM-DDTHH:mm.", planTrip.GetProperty("datetime").GetProperty("description").GetString());
        Assert.Equal("Number of connections (1–8).", planTrip.GetProperty("limit").GetProperty("description").GetString());
    }

    [Fact]
    public async Task JsonVariant_AnswersWithJson()
    {
        var handler = FixtureHandler.Always("locations-buchs-station.json");
        await using var app = WithVariant(null, null, "json", handler);
        await using var client = await ConnectAsync(app);

        var result = await client.CallToolAsync(
            "find_stops",
            new Dictionary<string, object?> { ["query"] = "Buchs", ["limit"] = 2 },
            cancellationToken: TestContext.Current.CancellationToken);

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        var json = JsonNode.Parse(text)!;
        Assert.Equal("Buchs", json["query"]!.GetValue<string>());
        Assert.Equal("8502123", json["stops"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("Fahrplandaten: opentransportdata.swiss", json["source"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("PERRON_TOOLS", "huge", "PERRON_TOOLS must be one of standard, not \"huge\".")]
    [InlineData("PERRON_DESCRIPTIONS", "verbose", "PERRON_DESCRIPTIONS must be one of rich, minimal, not \"verbose\".")]
    [InlineData("PERRON_FORMAT", "1", "PERRON_FORMAT must be one of compact, json, not \"1\".")]
    public void InvalidSetting_StopsTheStart(string variable, string value, string message)
    {
        using var app = factory.WithWebHostBuilder(builder => builder.UseSetting(variable, value));

        var error = Record.Exception(() => app.CreateClient());

        var validation = error as OptionsValidationException
            ?? (error as AggregateException)?.InnerExceptions.OfType<OptionsValidationException>().FirstOrDefault();
        Assert.NotNull(validation);
        Assert.Contains(message, validation.Failures);
    }

    [Theory]
    [InlineData("standard", "rich", "compact", "perron-rich")]
    [InlineData("Standard", "MINIMAL", "compact", "perron-minimal")]
    [InlineData("standard", "minimal", "json", "perron-minimal-json")]
    public void ServerName_ListsTheValuesThatDifferFromTheDefaults(string tools, string descriptions, string format, string name)
    {
        var options = new PerronVariantOptions { Tools = tools, Descriptions = descriptions, Format = format };

        Assert.True(new PerronVariantOptionsValidator().Validate(null, options).Succeeded);
        Assert.Equal(name, options.ToVariant().ServerName);
    }

    private WebApplicationFactory<Program> WithVariant(string? tools, string? descriptions, string? format, FixtureHandler? handler = null) =>
        factory.WithWebHostBuilder(builder =>
        {
            Set(builder, PerronVariantOptions.ToolsVariable, tools);
            Set(builder, PerronVariantOptions.DescriptionsVariable, descriptions);
            Set(builder, PerronVariantOptions.FormatVariable, format);
            if (handler is not null)
            {
                builder.ConfigureTestServices(services =>
                    services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => handler)));
            }
        });

    private static void Set(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder, string variable, string? value)
    {
        if (value is not null)
        {
            builder.UseSetting(variable, value);
        }
    }

    private static async Task<McpClient> ConnectAsync(WebApplicationFactory<Program> app)
    {
        var http = app.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http,
            null,
            true);
        return await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }
}
