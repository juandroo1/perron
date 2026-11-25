using System.Diagnostics.CodeAnalysis;

using ModelContextProtocol;

using Perron.Mcp.Results;
using Perron.Mcp.Variants;
using Perron.Transport;

namespace Perron.Mcp.Tools;

/// <summary>Tool <c>plan_trip</c> (AK-2.1 to AK-2.4). Texts in <see cref="ToolTexts"/>.</summary>
public static class PlanTripTool
{
    public const string Name = "plan_trip";
    public const int DefaultLimit = 4;
    public const int MaxLimit = 8;

    [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Parameter names are the tool's snake_case schema (plan.md, section 4).")]
    public static async Task<string> PlanTripAsync(
        ITransportApiClient transport,
        PerronVariant variant,
        string from,
        string to,
        string[]? via = null,
        string? datetime = null,
        TimeIs time_is = TimeIs.Departure,
        TransportMode[]? transport_types = null,
        int limit = DefaultLimit,
        CancellationToken cancellationToken = default) =>
        variant.Render(await PlanAsync(
            transport, variant, Name, from, to, via, datetime, time_is, transport_types, limit, cancellationToken));

    /// <summary>Plans connections; shared by the variants' connection tools.</summary>
    /// <param name="tool">Name of the calling tool, for the hint after ambiguous names.</param>
    public static async Task<ToolResult> PlanAsync(
        ITransportApiClient transport,
        PerronVariant variant,
        string tool,
        string from,
        string to,
        string[]? via,
        string? datetime,
        TimeIs timeIs,
        TransportMode[]? transportTypes,
        int limit,
        CancellationToken cancellationToken)
    {
        var vias = (via ?? []).Where(stop => !string.IsNullOrWhiteSpace(stop)).ToList();
        Validate(from, to, vias, limit);
        var at = string.IsNullOrWhiteSpace(datetime) ? (DateTimeOffset?)null : ToolText.ParseDateTime(datetime);

        List<StopResolution> stops =
        [
            await StopResolver.ResolveAsync(transport, from, "from", variant.StopSearchTool, cancellationToken),
            await StopResolver.ResolveAsync(transport, to, "to", variant.StopSearchTool, cancellationToken),
        ];
        for (var i = 0; i < vias.Count; i++)
        {
            var role = vias.Count == 1 ? "via" : $"via {i + 1}";
            stops.Add(await StopResolver.ResolveAsync(transport, vias[i], role, variant.StopSearchTool, cancellationToken));
        }

        var ambiguous = stops.OfType<StopResolution.Ambiguous>().ToList();
        if (ambiguous.Count > 0)
        {
            return new CandidatesResult(ambiguous, $"Ask which one is meant, then call {tool} again with its ID.");
        }

        var resolved = stops.Cast<StopResolution.Resolved>().ToList();
        var query = new ConnectionQuery(resolved[0].Id, resolved[1].Id)
        {
            Via = [.. resolved.Skip(2).Select(stop => stop.Id)],
            At = at,
            IsArrivalTime = timeIs == TimeIs.Arrival,
            TransportTypes = [.. (transportTypes ?? []).Distinct().Select(mode => mode.ToTransportType())],
            Limit = limit,
        };

        var answer = await transport.GetConnectionsAsync(query, cancellationToken);
        var result = new TripsResult(
            new StopRef(resolved[0].Id, resolved[0].Name ?? answer.ResolvedFrom?.Name),
            new StopRef(resolved[1].Id, resolved[1].Name ?? answer.ResolvedTo?.Name),
            [.. resolved.Skip(2).Select(stop => new StopRef(stop.Id, stop.Name ?? StopInResult(stop.Id, answer)?.Name))],
            at,
            timeIs,
            [.. answer.Connections.Select(ConnectionView.From)],
            null);

        return result.Connections.Count > 0
            ? result
            : result with
            {
                Hint = $"No connections found from {result.From.Display} to {result.To.Display}{result.ViaText}, {result.When}. "
                    + "Try another time, fewer via stops or other means of transport.",
            };
    }

    private static void Validate(string from, string to, List<string> vias, int limit)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
        {
            throw new McpException("Give both from and to, as stop names (e.g. \"Zürich HB\") or IDs (e.g. \"8503000\").");
        }

        if (vias.Count > ConnectionQuery.MaxVia)
        {
            throw new McpException($"Give at most {ConnectionQuery.MaxVia} via stops.");
        }

        if (limit is < 1 or > MaxLimit)
        {
            throw new McpException($"limit must be between 1 and {MaxLimit}.");
        }
    }

    /// <summary>A stop the connections pass, found by ID; gives a name to a via given as ID.</summary>
    private static Location? StopInResult(string id, ConnectionsResult result) =>
        result.Connections
            .SelectMany(connection => connection.Sections)
            .SelectMany(section => new[] { section.Departure.Station, section.Arrival.Station })
            .FirstOrDefault(station => station?.Id == id);
}
