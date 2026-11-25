using Microsoft.Extensions.Options;

using Perron.Mcp.Results;
using Perron.Mcp.Tools;

namespace Perron.Mcp.Variants;

/// <summary>How the tools are cut (plan.md, section 4).</summary>
public enum ToolCut
{
    /// <summary>3 tools: find_stops, plan_trip, get_departures.</summary>
    Standard,

    /// <summary>7 finer tools with the same data and formats.</summary>
    Many,
}

/// <summary>How much the tool descriptions explain.</summary>
public enum DescriptionStyle
{
    Rich,
    Minimal,
}

/// <summary>How tool results are written.</summary>
public enum ResultFormat
{
    Compact,
    Json,
}

/// <summary>One variant of Perron MCP for the ablations (AK-5.1, AK-5.2).</summary>
public sealed record PerronVariant(ToolCut Tools, DescriptionStyle Descriptions, ResultFormat Format)
{
    public static PerronVariant Default { get; } = new(ToolCut.Standard, DescriptionStyle.Rich, ResultFormat.Compact);

    /// <summary>
    /// Name in <c>serverInfo</c>: <c>perron-rich</c> for the defaults, otherwise <c>perron-</c> plus the values that
    /// differ from the defaults, e.g. <c>perron-minimal</c>, <c>perron-json</c>.
    /// </summary>
    public string ServerName
    {
        get
        {
            List<string> parts = [];
            if (Tools != Default.Tools)
            {
                parts.Add(Tools.ToString().ToLowerInvariant());
            }

            if (Descriptions != Default.Descriptions)
            {
                parts.Add(Descriptions.ToString().ToLowerInvariant());
            }

            if (Format != Default.Format)
            {
                parts.Add(Format.ToString().ToLowerInvariant());
            }

            return "perron-" + (parts.Count == 0 ? "rich" : string.Join('-', parts));
        }
    }

    /// <summary>Writes a tool result in this variant's format.</summary>
    public string Render(ToolResult result) =>
        Format == ResultFormat.Json ? JsonRenderer.Render(result) : CompactRenderer.Render(result);

    /// <summary>The tool that searches stops by name, for hints.</summary>
    public string StopSearchTool => Tools == ToolCut.Many ? ManyTools.SearchStopsByName : FindStopsTool.Name;

    /// <summary>The tool (or tools) that plan connections, for hints.</summary>
    public string TripTools => Tools == ToolCut.Many ? "the connection tools" : PlanTripTool.Name;
}

/// <summary>Variant settings from the environment: <c>PERRON_TOOLS</c>, <c>PERRON_DESCRIPTIONS</c>, <c>PERRON_FORMAT</c>.</summary>
public sealed class PerronVariantOptions
{
    public const string ToolsVariable = "PERRON_TOOLS";
    public const string DescriptionsVariable = "PERRON_DESCRIPTIONS";
    public const string FormatVariable = "PERRON_FORMAT";

    public string Tools { get; set; } = "standard";

    public string Descriptions { get; set; } = "rich";

    public string Format { get; set; } = "compact";

    /// <summary>The variant; call only after validation.</summary>
    public PerronVariant ToVariant() => new(
        Enum.Parse<ToolCut>(Tools, ignoreCase: true),
        Enum.Parse<DescriptionStyle>(Descriptions, ignoreCase: true),
        Enum.Parse<ResultFormat>(Format, ignoreCase: true));
}

public sealed class PerronVariantOptionsValidator : IValidateOptions<PerronVariantOptions>
{
    public ValidateOptionsResult Validate(string? name, PerronVariantOptions options)
    {
        List<string> failures = [];
        Check<ToolCut>(PerronVariantOptions.ToolsVariable, options.Tools, failures);
        Check<DescriptionStyle>(PerronVariantOptions.DescriptionsVariable, options.Descriptions, failures);
        Check<ResultFormat>(PerronVariantOptions.FormatVariable, options.Format, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void Check<TEnum>(string variable, string value, List<string> failures)
        where TEnum : struct, Enum
    {
        if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed) || int.TryParse(value, out _))
        {
            var allowed = string.Join(", ", Enum.GetNames<TEnum>().Select(n => n.ToLowerInvariant()));
            failures.Add($"{variable} must be one of {allowed}, not \"{value}\".");
        }
    }
}
