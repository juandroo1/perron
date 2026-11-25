using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using ModelContextProtocol.Protocol;

namespace Perron.Bench.Checks;

/// <summary>
/// SHA-256 of a server's <c>tools/list</c> (plan.md, decision log): the canonical JSON (object keys sorted, no
/// whitespace) of name, title, description, input schema and annotations of every tool, sorted by name.
/// Any change a model could notice changes the hash.
/// </summary>
public static class ToolListHash
{
    public static string Compute(IEnumerable<Tool> tools)
    {
        var list = new JsonArray([.. tools
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .Select(tool => (JsonNode?)Canonical(new JsonObject
            {
                ["name"] = tool.Name,
                ["title"] = tool.Title,
                ["description"] = tool.Description,
                ["inputSchema"] = JsonNode.Parse(tool.InputSchema.GetRawText()),
                ["annotations"] = tool.Annotations is null ? null : JsonSerializer.SerializeToNode(tool.Annotations, ModelContextProtocol.McpJsonUtilities.DefaultOptions),
            }))]);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(list.ToJsonString()));
        return Convert.ToHexStringLower(bytes);
    }

    /// <summary>Hash of tools as stored in a <c>tools/list</c> snapshot (e.g. <c>tests/fixtures/foreign-servers</c>).</summary>
    public static string Compute(JsonElement toolsArray) =>
        Compute(toolsArray.EnumerateArray().Select(tool => tool.Deserialize<Tool>(ModelContextProtocol.McpJsonUtilities.DefaultOptions)!));

    /// <summary>Sorts object keys recursively and drops null values, so equal content gives equal text.</summary>
    private static JsonNode? Canonical(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .Where(property => property.Value is not null)
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => KeyValuePair.Create(property.Key, Canonical(property.Value)))),
        JsonArray array => new JsonArray([.. array.Select(Canonical)]),
        null => null,
        _ => node.DeepClone(),
    };
}
