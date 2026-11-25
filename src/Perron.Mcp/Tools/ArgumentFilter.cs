using System.Text.Json;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Perron.Mcp.Tools;

/// <summary>
/// Call-tool filter for every tool: makes argument mistakes recoverable (AK-4.1).
/// A number where the schema asks for a string (e.g. <c>"stop": 8505000</c>) is passed on as text; any other argument
/// that cannot be bound (wrong type, unknown enum value) becomes a tool error with a hint instead of the SDK's
/// generic "An error occurred invoking …". The input schema itself stays unchanged.
/// </summary>
public static class ArgumentFilter
{
    public const string InvalidArgumentsMessage =
        "An argument has the wrong type or value. Stop names, stop IDs and dates are strings "
        + "(e.g. \"8503000\", \"2026-11-26T08:00\"); time_is, type and transport_types take only the values listed in the tool's schema. "
        + "Check the arguments against the schema and call again.";

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Wrap(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            if (context.Params is { Arguments: { } arguments } && context.MatchedPrimitive is McpServerTool tool)
            {
                NumbersToStrings(arguments, tool.ProtocolTool.InputSchema);
            }

            try
            {
                return await next(context, cancellationToken);
            }
            catch (JsonException)
            {
                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = InvalidArgumentsMessage }],
                };
            }
        };

    /// <summary>Replaces numbers by their text where the schema property (or its array items) is a string.</summary>
    public static void NumbersToStrings(IDictionary<string, JsonElement> arguments, JsonElement schema)
    {
        if (!schema.TryGetProperty("properties", out var properties))
        {
            return;
        }

        foreach (var name in arguments.Keys.ToList())
        {
            if (!properties.TryGetProperty(name, out var property))
            {
                continue;
            }

            var value = arguments[name];
            if (value.ValueKind == JsonValueKind.Number && AllowsString(property))
            {
                arguments[name] = JsonSerializer.SerializeToElement(value.GetRawText(), ToolJsonContext.Default.String);
            }
            else if (value.ValueKind == JsonValueKind.Array
                && property.TryGetProperty("items", out var items)
                && AllowsString(items)
                && value.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.Number))
            {
                string?[] texts =
                [
                    .. value.EnumerateArray().Select(item => item.ValueKind switch
                    {
                        JsonValueKind.Number => item.GetRawText(),
                        JsonValueKind.String => item.GetString(),
                        _ => null,
                    }),
                ];
                if (texts.All(text => text is not null))
                {
                    arguments[name] = JsonSerializer.SerializeToElement(texts, ToolJsonContext.Default.StringArray);
                }
            }
        }
    }

    /// <summary>True if the schema's type is "string" or contains "string" (e.g. ["string", "null"]) and has no enum.</summary>
    private static bool AllowsString(JsonElement schema)
    {
        if (schema.TryGetProperty("enum", out _) || !schema.TryGetProperty("type", out var type))
        {
            return false;
        }

        return type.ValueKind switch
        {
            JsonValueKind.String => type.GetString() == "string",
            JsonValueKind.Array => type.EnumerateArray().Any(t => t.GetString() == "string"),
            _ => false,
        };
    }
}
