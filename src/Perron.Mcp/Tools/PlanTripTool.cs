using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using Perron.Transport;

namespace Perron.Mcp.Tools;

/// <summary>Tool <c>plan_trip</c> (AK-2.1 to AK-2.4).</summary>
[McpServerToolType]
public static class PlanTripTool
{
    public const string Name = "plan_trip";
    public const int DefaultLimit = 4;
    public const int MaxLimit = 8;

    [McpServerTool(
        Name = Name,
        Title = "Plan trip",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = true)]
    [Description(
        "Plan public transport connections in Switzerland from one stop to another, optionally via up to two stops, "
        + "leaving after or arriving before a given time. "
        + "Returns connections with date, departure and arrival times, platforms, delays, duration, number of transfers, "
        + "and every leg with its line and the transfer time between legs. "
        + "Stops can be names or IDs from find_stops. "
        + "If a name matches several stops (e.g. \"Buchs\"), the result lists the candidates with their IDs instead of connections: "
        + "ask which one is meant, or call again with the right ID. "
        + "Times are local Swiss time (Europe/Zurich). "
        + "Example: from \"Zürich HB\", to \"Bern\", datetime \"2026-11-26T08:00\".")]
    [SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Parameter names are the tool's snake_case schema (plan.md, section 4).")]
    public static async Task<string> PlanTripAsync(
        ITransportApiClient transport,
        [Description("Departure stop: name (e.g. \"Zürich HB\") or ID (e.g. \"8503000\").")]
        string from,
        [Description("Arrival stop: name or ID.")]
        string to,
        [Description("Up to 2 stops the connection must pass through, names or IDs, e.g. [\"Olten\"].")]
        string[]? via = null,
        [Description("Local Swiss time, ISO 8601 without offset, e.g. \"2026-11-26T08:00\". Default: now.")]
        string? datetime = null,
        [Description("\"departure\" (default): leave at or after datetime. \"arrival\": arrive at or before datetime.")]
        TimeIs time_is = TimeIs.Departure,
        [Description("Only use these means of transport, e.g. [\"train\"]. Default: all.")]
        TransportMode[]? transport_types = null,
        [Description("Number of connections, 1–8. Default 4.")]
        int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var vias = (via ?? []).Where(stop => !string.IsNullOrWhiteSpace(stop)).ToList();
        Validate(from, to, vias, limit);
        var at = datetime is null || string.IsNullOrWhiteSpace(datetime) ? (DateTimeOffset?)null : ToolText.ParseDateTime(datetime);

        List<StopResolution> stops =
        [
            await StopResolver.ResolveAsync(transport, from, "from", cancellationToken),
            await StopResolver.ResolveAsync(transport, to, "to", cancellationToken),
        ];
        for (var i = 0; i < vias.Count; i++)
        {
            stops.Add(await StopResolver.ResolveAsync(transport, vias[i], vias.Count == 1 ? "via" : $"via {i + 1}", cancellationToken));
        }

        var ambiguous = stops.OfType<StopResolution.Ambiguous>().ToList();
        if (ambiguous.Count > 0)
        {
            return Ambiguous(ambiguous);
        }

        var resolved = stops.Cast<StopResolution.Resolved>().ToList();
        var query = new ConnectionQuery(resolved[0].Id, resolved[1].Id)
        {
            Via = [.. resolved.Skip(2).Select(stop => stop.Id)],
            At = at,
            IsArrivalTime = time_is == TimeIs.Arrival,
            TransportTypes = [.. (transport_types ?? []).Distinct().Select(mode => mode.ToTransportType())],
            Limit = limit,
        };

        var result = await transport.GetConnectionsAsync(query, cancellationToken);
        return Format(resolved, result, at, time_is);
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

    private static string Ambiguous(List<StopResolution.Ambiguous> ambiguous) =>
        string.Join('\n', (IEnumerable<string>)
        [
            .. ambiguous.SelectMany(StopResolver.Describe),
            "Ask which one is meant, then call plan_trip again with its ID.",
            ToolText.SourceLine,
        ]);

    private static string Format(
        List<StopResolution.Resolved> stops, ConnectionsResult result, DateTimeOffset? at, TimeIs timeIs)
    {
        var from = Display(stops[0], result.ResolvedFrom);
        var to = Display(stops[1], result.ResolvedTo);
        var via = stops.Count > 2
            ? " via " + string.Join(", ", stops.Skip(2).Select(stop => Display(stop, StopInResult(stop.Id, result))))
            : "";
        var when = at is not { } instant
            ? "leaving now"
            : timeIs == TimeIs.Arrival
                ? $"arriving by {ToolText.Date(instant)} {ToolText.Time(instant)}"
                : $"leaving after {ToolText.Date(instant)} {ToolText.Time(instant)}";

        List<string> lines;
        if (result.Connections.Count == 0)
        {
            lines =
            [
                $"No connections found from {from} to {to}{via}, {when}. "
                + "Try another time, fewer via stops or other means of transport.",
            ];
        }
        else
        {
            lines = [$"Connections {from} → {to}{via}, {when}:"];
            lines.AddRange(result.Connections.SelectMany((connection, index) => ConnectionFormat.Lines(connection, index + 1)));
        }

        lines.Add(ToolText.SourceLine);
        return string.Join('\n', lines);
    }

    /// <summary>A stop the connections pass, found by ID; gives a name to a via given as ID.</summary>
    private static Location? StopInResult(string id, ConnectionsResult result) =>
        result.Connections
            .SelectMany(connection => connection.Sections)
            .SelectMany(section => new[] { section.Departure.Station, section.Arrival.Station })
            .FirstOrDefault(station => station?.Id == id);

    /// <summary>The resolved name, or for an ID input the name the API reports.</summary>
    private static string Display(StopResolution.Resolved stop, Location? reported) =>
        stop.Name is null && reported?.Name is { } name ? $"{name} ({stop.Id})" : stop.Display;
}
