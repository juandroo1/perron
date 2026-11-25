using System.ComponentModel;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using Perron.Transport;

namespace Perron.Mcp.Tools;

/// <summary>Tool <c>get_departures</c> (AK-3.1, AK-2.3, AK-2.4).</summary>
[McpServerToolType]
public static class GetDeparturesTool
{
    public const string Name = "get_departures";
    public const int DefaultLimit = 10;
    public const int MaxLimit = 30;

    [McpServerTool(
        Name = Name,
        Title = "Get departures",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = true)]
    [Description(
        "Show the next departures (or arrivals) at one public transport stop in Switzerland, from now or from a given time. "
        + "Returns for each the date and time, delay, line, final destination and platform. "
        + "The stop can be a name or an ID from find_stops. "
        + "If a name matches several stops (e.g. \"Buchs\"), the result lists the candidates with their IDs instead: "
        + "ask which one is meant, or call again with the right ID. "
        + "Arrivals also show the vehicle's final destination, not where it comes from. "
        + "To check whether a vehicle stops at a certain station, or to get from A to B, use plan_trip. "
        + "Times are local Swiss time (Europe/Zurich). "
        + "Example: stop \"Bern\", datetime \"2026-11-26T08:00\", transport_types [\"train\"].")]
    public static async Task<string> GetDeparturesAsync(
        ITransportApiClient transport,
        TimeProvider clock,
        [Description("Stop name (e.g. \"Bern\", \"Zürich, Central\") or ID (e.g. \"8507000\").")]
        string stop,
        [Description("Local Swiss time, ISO 8601 without offset, e.g. \"2026-11-26T08:00\". Default: now.")]
        string? datetime = null,
        [Description("\"departure\" (default) or \"arrival\".")]
        BoardType type = BoardType.Departure,
        [Description("Only use these means of transport, e.g. [\"train\"]. Default: all.")]
        TransportMode[]? transport_types = null,
        [Description("Number of departures, 1–30. Default 10. A few more may follow if they leave in the same minute as the last one.")]
        int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
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

        var resolution = await StopResolver.ResolveAsync(transport, stop, "stop", cancellationToken);
        if (resolution is StopResolution.Ambiguous ambiguous)
        {
            return string.Join('\n', (IEnumerable<string>)
            [
                .. StopResolver.Describe(ambiguous),
                "Ask which one is meant, then call get_departures again with its ID.",
                ToolText.SourceLine,
            ]);
        }

        var resolved = (StopResolution.Resolved)resolution;
        var board = await transport.GetStationboardAsync(
            new StationboardQuery
            {
                StationId = resolved.Id,
                At = at,
                Type = type == BoardType.Arrival ? StationboardType.Arrival : StationboardType.Departure,
                TransportTypes = [.. (transport_types ?? []).Distinct().Select(mode => mode.ToTransportType())],
                Limit = limit,
            },
            cancellationToken);

        if (board.Station is null)
        {
            throw new McpException($"No stop with ID \"{resolved.Id}\". Search with find_stops and use the stop's ID.");
        }

        return Format(resolved, board, at, clock.GetUtcNow(), type);
    }

    private static string Format(
        StopResolution.Resolved stop, Stationboard board, DateTimeOffset? at, DateTimeOffset now, BoardType type)
    {
        var station = stop.Name is null && board.Station?.Name is { } name ? $"{name} ({stop.Id})" : stop.Display;
        var kind = type == BoardType.Arrival ? "Arrivals" : "Departures";
        var day = at ?? now;
        var from = at is { } instant
            ? $"from {ToolText.Date(instant)} {ToolText.Time(instant)}"
            : $"from now ({ToolText.Date(now)})";

        List<string> lines;
        if (board.Entries.Count == 0)
        {
            lines = [$"No {kind.ToLowerInvariant()} at {station} {from}. Try a later time or other means of transport."];
        }
        else
        {
            lines = [$"{kind} at {station} {from}:"];
            lines.AddRange(board.Entries.Select(entry => Line(entry, board.Station, day, type)));
        }

        lines.Add(ToolText.SourceLine);
        return string.Join('\n', lines);
    }

    /// <summary>"08:04 (+3 min) IC 61 to Basel SBB pl. 5 (now 7)"; arrivals "… continues to Brig" or "… ends here".</summary>
    private static string Line(StationboardEntry entry, Location? station, DateTimeOffset day, BoardType type)
    {
        // For arrivals the API puts the time into "departure" and leaves "arrival" empty.
        var time = type == BoardType.Arrival
            ? entry.Stop.Arrival ?? entry.Stop.Departure
            : entry.Stop.Departure ?? entry.Stop.Arrival;
        var line = entry.Line ?? "?";

        string direction;
        if (entry.Destination is not { } destination)
        {
            direction = "";
        }
        else if (type == BoardType.Departure)
        {
            direction = $" to {destination}";
        }
        else
        {
            var endsHere = station is not null && StopNames.Normalize(destination) == StopNames.Normalize(station.Name);
            direction = endsHere ? " ends here" : $" continues to {destination}";
        }

        return $"{ToolText.TimeOnDay(time, day)}{ToolText.Delay(entry.Stop.DelayMinutes)} {line}{direction}{ToolText.Platform(entry.Stop)}";
    }
}
