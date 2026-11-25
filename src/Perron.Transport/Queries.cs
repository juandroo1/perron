namespace Perron.Transport;

/// <summary>Location types of <c>/locations</c>.</summary>
public enum LocationType
{
    All,
    Station,
    Poi,
    Address,
}

/// <summary>Means of transport accepted by <c>transportations[]</c>.</summary>
public enum TransportType
{
    Train,
    Tram,
    Bus,
    Ship,
    Cableway,
}

/// <summary>Kind of stationboard.</summary>
public enum StationboardType
{
    Departure,
    Arrival,
}

/// <summary>Search for locations by name or near a coordinate.</summary>
public sealed record LocationQuery
{
    private LocationQuery()
    {
    }

    public string? Name { get; private init; }

    public Coordinate? Near { get; private init; }

    public LocationType Type { get; init; } = LocationType.All;

    public static LocationQuery ByName(string name) => new() { Name = name };

    public static LocationQuery ByCoordinate(Coordinate coordinate) => new() { Near = coordinate };
}

/// <summary>Search for connections from <see cref="From"/> to <see cref="To"/> (names, IDs, addresses or coordinates).</summary>
public sealed record ConnectionQuery(string From, string To)
{
    public const int MaxVia = 2;
    public const int MinLimit = 1;
    public const int MaxLimit = 16;
    public const int MinPage = -10;
    public const int MaxPage = 10;

    public IReadOnlyList<string> Via { get; init; } = [];

    /// <summary>Departure (or arrival) instant; sent to the API as Europe/Zurich local time. Default: now.</summary>
    public DateTimeOffset? At { get; init; }

    public bool IsArrivalTime { get; init; }

    public IReadOnlyList<TransportType> TransportTypes { get; init; } = [];

    public int? Limit { get; init; }

    /// <summary>Zero-based page; negative pages go back in time.</summary>
    public int? Page { get; init; }
}

/// <summary>Departures or arrivals of a station, by name (<see cref="Station"/>) or ID (<see cref="StationId"/>).</summary>
public sealed record StationboardQuery
{
    public string? Station { get; init; }

    public string? StationId { get; init; }

    /// <summary>Start instant; sent to the API as Europe/Zurich local time. Default: now.</summary>
    public DateTimeOffset? At { get; init; }

    public StationboardType Type { get; init; } = StationboardType.Departure;

    public IReadOnlyList<TransportType> TransportTypes { get; init; } = [];

    /// <summary>Soft limit: the API returns all entries that share the time of the last one.</summary>
    public int? Limit { get; init; }
}
