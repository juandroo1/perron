using Perron.Mcp.Tools;
using Perron.Transport;

namespace Perron.Mcp.Results;

/// <summary>
/// What a tool found, before it is written as <c>compact</c> text or <c>json</c>. Both formats are rendered from these
/// models, so they carry the same fields (AK-5.1). Hints are full sentences for the agent and identical in both formats.
/// </summary>
public abstract record ToolResult;

/// <summary>A stop as given by the user (ID) or resolved from a name.</summary>
public sealed record StopRef(string Id, string? Name)
{
    /// <summary>"Basel SBB (8500010)", or just the ID.</summary>
    public string Display => Name is null ? Id : $"{Name} ({Id})";
}

/// <summary>Result of a stop search by name (<see cref="Query"/>) or near a coordinate (<see cref="Near"/>).</summary>
/// <param name="Hint">Set when nothing was found or the name is ambiguous.</param>
public sealed record StopsResult(string? Query, Coordinate? Near, IReadOnlyList<Location> Stops, string? Hint) : ToolResult;

/// <summary>Stops given by name that could mean several stops; the tool did not run.</summary>
public sealed record CandidatesResult(IReadOnlyList<StopResolution.Ambiguous> Ambiguous, string Hint) : ToolResult;

/// <summary>Result of a connection search.</summary>
/// <param name="At">Requested time; <c>null</c> means now.</param>
/// <param name="MinTransferMinutes">Requested minimum transfer time; <c>null</c> means the timetable's.</param>
/// <param name="Hint">Set when no connections were found.</param>
public sealed record TripsResult(
    StopRef From,
    StopRef To,
    IReadOnlyList<StopRef> Via,
    DateTimeOffset? At,
    TimeIs TimeIs,
    int? MinTransferMinutes,
    IReadOnlyList<ConnectionView> Connections,
    string? Hint) : ToolResult
{
    /// <summary>"leaving now", "leaving after Thu 26.11.2026 08:00", "arriving by …".</summary>
    public string When => At is not { } instant
        ? "leaving now"
        : TimeIs == TimeIs.Arrival
            ? $"arriving by {ToolText.Date(instant)} {ToolText.Time(instant)}"
            : $"leaving after {ToolText.Date(instant)} {ToolText.Time(instant)}";

    public string ViaText => Via.Count > 0 ? " via " + string.Join(", ", Via.Select(stop => stop.Display)) : "";

    /// <summary>", transfers of at least 10 min" or empty.</summary>
    public string TransferText => MinTransferMinutes is { } minutes ? $", transfers of at least {minutes} min" : "";
}

/// <summary>Departures or arrivals of one stop.</summary>
/// <param name="At">Requested time; <c>null</c> means now.</param>
/// <param name="Now">When the board was asked for; gives the date for "now".</param>
/// <param name="Hint">Set when the board is empty.</param>
public sealed record BoardResult(
    StopRef Stop,
    BoardType Type,
    DateTimeOffset? At,
    DateTimeOffset Now,
    IReadOnlyList<BoardEntryView> Entries,
    string? Hint) : ToolResult
{
    /// <summary>The day entries are compared with to decide whether they need their own date.</summary>
    public DateTimeOffset Day => At ?? Now;

    public string Kind => Type == BoardType.Arrival ? "Arrivals" : "Departures";

    public string FromText => At is { } instant
        ? $"from {ToolText.Date(instant)} {ToolText.Time(instant)}"
        : $"from now ({ToolText.Date(Now)})";
}

/// <summary>A stop within a leg or board entry: time, platform (and changed platform) and delay.</summary>
public sealed record StopPoint(string? Stop, DateTimeOffset? Time, string? Platform, string? PlatformNow, int? DelayMinutes)
{
    public static StopPoint From(Checkpoint checkpoint, DateTimeOffset? time)
    {
        var planned = string.IsNullOrWhiteSpace(checkpoint.Platform) ? null : checkpoint.Platform;
        var forecast = string.IsNullOrWhiteSpace(checkpoint.Prognosis?.Platform) ? null : checkpoint.Prognosis!.Platform;
        return new StopPoint(
            checkpoint.Station?.Name,
            time,
            planned ?? forecast,
            planned is not null && forecast is not null && forecast != planned ? forecast : null,
            checkpoint.DelayMinutes is > 0 ? checkpoint.DelayMinutes : null);
    }
}

/// <summary>One part of a connection as shown to the agent.</summary>
public abstract record LegView;

public sealed record RideLeg(string Line, string? Direction, StopPoint From, StopPoint To) : LegView;

public sealed record WalkLeg(TimeSpan? Duration, string? To) : LegView;

/// <summary>Time between arriving with one ride and leaving with the next.</summary>
/// <param name="At">Stop of the transfer; <c>null</c> when a walk lies between the rides.</param>
public sealed record TransferLeg(TimeSpan Duration, string? At, bool IncludesWalk) : LegView;

/// <summary>A connection as shown to the agent (AK-2.2).</summary>
public sealed record ConnectionView(
    DateTimeOffset? Departure,
    DateTimeOffset? Arrival,
    TimeSpan? Duration,
    int Transfers,
    IReadOnlyList<LegView> Legs)
{
    /// <summary>The connection's date: departure, or arrival if the departure is unknown.</summary>
    public DateTimeOffset? Day => Departure ?? Arrival;

    public static ConnectionView From(Connection connection)
    {
        var departure = connection.From.Departure ?? connection.Sections.FirstOrDefault()?.Departure.Departure;
        var arrival = connection.To.Arrival ?? connection.Sections.LastOrDefault()?.Arrival.Arrival;
        var duration = connection.Duration ?? (departure is { } d && arrival is { } a ? a - d : null);

        List<LegView> legs = [];
        DateTimeOffset? lastRideArrival = null;
        string? lastRideStop = null;
        var walkedSinceLastRide = false;
        foreach (var section in connection.Sections)
        {
            if (section.Journey is not { } journey)
            {
                walkedSinceLastRide = true;
                var walkDuration = section.Walk?.Duration
                    ?? (section.Departure.Departure is { } from && section.Arrival.Arrival is { } to ? to - from : null);
                legs.Add(new WalkLeg(walkDuration, section.Arrival.Station?.Name));
                continue;
            }

            if (lastRideArrival is { } previous && section.Departure.Departure is { } next)
            {
                legs.Add(new TransferLeg(next - previous, walkedSinceLastRide ? null : lastRideStop, walkedSinceLastRide));
            }

            legs.Add(new RideLeg(
                journey.Line ?? "ride",
                journey.Destination,
                StopPoint.From(section.Departure, section.Departure.Departure),
                StopPoint.From(section.Arrival, section.Arrival.Arrival)));

            lastRideArrival = section.Arrival.Arrival;
            lastRideStop = section.Arrival.Station?.Name;
            walkedSinceLastRide = false;
        }

        return new ConnectionView(departure, arrival, duration, connection.Transfers, legs);
    }
}

/// <summary>One departure or arrival as shown to the agent (AK-3.1).</summary>
/// <param name="EndsHere">For arrivals: the vehicle's final destination is this stop.</param>
public sealed record BoardEntryView(StopPoint Point, string Line, string? Destination, bool EndsHere)
{
    public static BoardEntryView From(StationboardEntry entry, Location? station, BoardType type)
    {
        // For arrivals the API puts the time into "departure" and leaves "arrival" empty.
        var time = type == BoardType.Arrival
            ? entry.Stop.Arrival ?? entry.Stop.Departure
            : entry.Stop.Departure ?? entry.Stop.Arrival;
        var endsHere = type == BoardType.Arrival
            && entry.Destination is { } destination
            && station is not null
            && StopNames.Normalize(destination) == StopNames.Normalize(station.Name);
        return new BoardEntryView(StopPoint.From(entry.Stop, time), entry.Line ?? "?", entry.Destination, endsHere);
    }
}
