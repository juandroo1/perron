using System.Diagnostics.CodeAnalysis;

using ModelContextProtocol;

using Perron.Mcp.Results;
using Perron.Mcp.Variants;
using Perron.Transport;

namespace Perron.Mcp.Tools;

/// <summary>Tool <c>get_departures</c> (AK-3.1, AK-2.3, AK-2.4). Texts in <see cref="ToolTexts"/>.</summary>
public static class GetDeparturesTool
{
    public const string Name = "get_departures";
    public const int DefaultLimit = 10;
    public const int MaxLimit = 30;

    [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Parameter names are the tool's snake_case schema (plan.md, section 4).")]
    public static async Task<string> GetDeparturesAsync(
        ITransportApiClient transport,
        TimeProvider clock,
        PerronVariant variant,
        string stop,
        string? datetime = null,
        BoardType type = BoardType.Departure,
        TransportMode[]? transport_types = null,
        int limit = DefaultLimit,
        CancellationToken cancellationToken = default) =>
        variant.Render(await BoardAsync(
            transport, clock, variant, Name, stop, datetime, type, transport_types, limit, cancellationToken));

    /// <summary>Reads departures or arrivals; shared by the variants' board tools.</summary>
    /// <param name="tool">Name of the calling tool, for the hint after an ambiguous name.</param>
    public static async Task<ToolResult> BoardAsync(
        ITransportApiClient transport,
        TimeProvider clock,
        PerronVariant variant,
        string tool,
        string stop,
        string? datetime,
        BoardType type,
        TransportMode[]? transportTypes,
        int limit,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(stop))
        {
            throw new McpException("Give a stop, as a name (e.g. \"Bern\") or an ID (e.g. \"8507000\").");
        }

        if (limit is < 1 or > MaxLimit)
        {
            throw new McpException($"limit must be between 1 and {MaxLimit}.");
        }

        var at = string.IsNullOrWhiteSpace(datetime) ? (DateTimeOffset?)null : ToolText.ParseDateTime(datetime);

        var resolution = await StopResolver.ResolveAsync(transport, stop, "stop", variant.StopSearchTool, cancellationToken);
        if (resolution is StopResolution.Ambiguous ambiguous)
        {
            return new CandidatesResult([ambiguous], $"Ask which one is meant, then call {tool} again with its ID.");
        }

        var resolved = (StopResolution.Resolved)resolution;
        var board = await transport.GetStationboardAsync(
            new StationboardQuery
            {
                StationId = resolved.Id,
                At = at,
                Type = type == BoardType.Arrival ? StationboardType.Arrival : StationboardType.Departure,
                TransportTypes = [.. (transportTypes ?? []).Distinct().Select(mode => mode.ToTransportType())],
                Limit = limit,
            },
            cancellationToken);

        if (board.Station is null)
        {
            throw new McpException(
                $"No stop with ID \"{resolved.Id}\". Search with {variant.StopSearchTool} and use the stop's ID.");
        }

        var result = new BoardResult(
            new StopRef(resolved.Id, resolved.Name ?? board.Station.Name),
            type,
            at,
            clock.GetUtcNow(),
            [.. board.Entries.Select(entry => BoardEntryView.From(entry, board.Station, type))],
            null);

        return result.Entries.Count > 0
            ? result
            : result with
            {
                Hint = $"No {result.Kind.ToLowerInvariant()} at {result.Stop.Display} {result.FromText}. "
                    + "Try a later time or other means of transport.",
            };
    }
}
