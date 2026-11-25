using System.Text.Json;
using System.Text.Json.Nodes;

using ModelContextProtocol.Server;

using Perron.Mcp.Variants;

namespace Perron.Mcp.Tools;

/// <summary>One tool of a variant: name, title, method and texts per description style.</summary>
public sealed record ToolDefinition(string Name, string Title, Delegate Method, Func<DescriptionStyle, ToolDescription> Texts);

/// <summary>Builds the tools of a variant (AK-5.1). All tools are read-only and query an outside service.</summary>
public static class ToolCatalog
{
    public static IReadOnlyList<ToolDefinition> Definitions(PerronVariant variant) => variant.Tools switch
    {
        ToolCut.Standard =>
        [
            new(FindStopsTool.Name, "Find stops", FindStopsTool.FindStopsAsync, ToolTexts.FindStops),
            new(PlanTripTool.Name, "Plan trip", PlanTripTool.PlanTripAsync, ToolTexts.PlanTrip),
            new(GetDeparturesTool.Name, "Get departures", GetDeparturesTool.GetDeparturesAsync, ToolTexts.GetDepartures),
        ],
        ToolCut.Many =>
        [
            new(ManyTools.SearchStopsByName, "Search stops by name", ManyTools.SearchStopsByNameAsync, ToolTexts.SearchStopsByName),
            new(ManyTools.SearchStopsNear, "Search stops near a point", ManyTools.SearchStopsNearAsync, ToolTexts.SearchStopsNear),
            new(ManyTools.GetConnectionsDeparting, "Connections leaving after", ManyTools.GetConnectionsDepartingAsync, ToolTexts.GetConnectionsDeparting),
            new(ManyTools.GetConnectionsArriving, "Connections arriving by", ManyTools.GetConnectionsArrivingAsync, ToolTexts.GetConnectionsArriving),
            new(ManyTools.GetConnectionsVia, "Connections via stops", ManyTools.GetConnectionsViaAsync, ToolTexts.GetConnectionsVia),
            new(ManyTools.GetDepartures, "Get departures", ManyTools.GetDeparturesAsync, ToolTexts.GetDeparturesMany),
            new(ManyTools.GetArrivals, "Get arrivals", ManyTools.GetArrivalsAsync, ToolTexts.GetArrivals),
        ],
        _ => throw new ArgumentOutOfRangeException(nameof(variant), variant.Tools, "Unknown tool cut."),
    };

    public static IEnumerable<McpServerTool> Create(PerronVariant variant, IServiceProvider services) =>
        Definitions(variant).Select(definition => Create(definition, variant.Descriptions, services));

    private static McpServerTool Create(ToolDefinition definition, DescriptionStyle style, IServiceProvider services)
    {
        var texts = definition.Texts(style);
        var tool = McpServerTool.Create(definition.Method, new McpServerToolCreateOptions
        {
            Name = definition.Name,
            Title = definition.Title,
            Description = texts.Description,
            ReadOnly = true,
            Idempotent = true,
            Destructive = false,
            OpenWorld = true,
            Services = services,
        });

        tool.ProtocolTool.InputSchema = WithParameterDescriptions(tool.ProtocolTool.InputSchema, definition.Name, texts.Parameters);
        return tool;
    }

    /// <summary>Sets the description of every parameter; each parameter must have a text in every style.</summary>
    private static JsonElement WithParameterDescriptions(
        JsonElement schema, string tool, IReadOnlyDictionary<string, string> descriptions)
    {
        var node = JsonNode.Parse(schema.GetRawText())!.AsObject();
        var properties = node["properties"]?.AsObject() ?? [];

        var names = properties.Select(property => property.Key).ToList();
        var missing = names.Except(descriptions.Keys).Concat(descriptions.Keys.Except(names)).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Tool {tool}: parameter texts do not match the parameters ({string.Join(", ", missing)}).");
        }

        foreach (var name in names)
        {
            properties[name]!.AsObject()["description"] = descriptions[name];
        }

        using var document = JsonDocument.Parse(node.ToJsonString());
        return document.RootElement.Clone();
    }
}
