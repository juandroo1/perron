namespace Perron.Transport;

/// <summary>WGS84 coordinate. The API calls latitude <c>x</c> and longitude <c>y</c>.</summary>
public sealed record Coordinate(double Latitude, double Longitude);

/// <summary>A stop, address or point of interest. Addresses and points of interest have no <see cref="Id"/>.</summary>
/// <param name="Icon">Kind of stop as given by the API: <c>train</c>, <c>tram</c>, <c>bus</c> or <c>null</c>.</param>
/// <param name="DistanceMeters">Only set for searches by coordinate.</param>
public sealed record Location(string? Id, string Name, Coordinate? Coordinate, double? DistanceMeters, string? Icon);

/// <summary>Real-time forecast. Times in Europe/Zurich.</summary>
public sealed record Prognosis(DateTimeOffset? Arrival, DateTimeOffset? Departure, string? Platform);

/// <summary>A stop within a connection or stationboard entry. Times in Europe/Zurich.</summary>
/// <param name="DelayMinutes"><c>null</c> when no real-time data is available.</param>
public sealed record Checkpoint(
    Location? Station,
    DateTimeOffset? Arrival,
    DateTimeOffset? Departure,
    int? DelayMinutes,
    string? Platform,
    Prognosis? Prognosis);

/// <summary>A vehicle ride. <see cref="Number"/> has leading zeros removed (<c>EC000015</c> → <c>15</c>).</summary>
public sealed record Journey(string? Category, string? Number, string? Operator, string? Destination)
{
    /// <summary>Category and number, e.g. <c>IC 1</c>, <c>S 14</c>, <c>T 3</c>.</summary>
    public string? Line => LineName.Of(Category, Number);
}

/// <summary>A walk between stops.</summary>
public sealed record Walk(TimeSpan? Duration);

/// <summary>One part of a connection: either a <see cref="Journey"/> or a <see cref="Walk"/>.</summary>
public sealed record Section(Checkpoint Departure, Checkpoint Arrival, Journey? Journey, Walk? Walk);

/// <summary>A connection from A to B.</summary>
/// <param name="Products">Lines used, as given by the API, e.g. <c>IC 1</c>.</param>
public sealed record Connection(
    Checkpoint From,
    Checkpoint To,
    TimeSpan? Duration,
    int Transfers,
    IReadOnlyList<string> Products,
    IReadOnlyList<Section> Sections);

/// <summary>Result of a connection search.</summary>
/// <param name="ResolvedFrom">Station the API chose for the given origin (it picks one silently for ambiguous names).</param>
/// <param name="ResolvedTo">
/// Station the API chose for the destination, taken from the first connection: with two vias the API's top-level
/// <c>to</c> names the first via instead.
/// </param>
public sealed record ConnectionsResult(Location? ResolvedFrom, Location? ResolvedTo, IReadOnlyList<Connection> Connections);

/// <summary>One departure or arrival of a stationboard.</summary>
/// <param name="Destination">
/// Final destination of the vehicle, also for arrivals. The API does not provide the origin of an arriving vehicle.
/// </param>
public sealed record StationboardEntry(
    Checkpoint Stop,
    string? Category,
    string? Number,
    string? Operator,
    string? Destination)
{
    /// <summary>Category and number, e.g. <c>S 1</c>, <c>T 3</c>.</summary>
    public string? Line => LineName.Of(Category, Number);
}

/// <summary>Departures or arrivals of one station. <see cref="Station"/> is <c>null</c> for an unknown station.</summary>
public sealed record Stationboard(Location? Station, IReadOnlyList<StationboardEntry> Entries);

internal static class LineName
{
    public static string? Of(string? category, string? number) =>
        (string.IsNullOrWhiteSpace(category), string.IsNullOrWhiteSpace(number)) switch
        {
            (false, false) => $"{category} {number}",
            (false, true) => category,
            (true, false) => number,
            _ => null,
        };
}
