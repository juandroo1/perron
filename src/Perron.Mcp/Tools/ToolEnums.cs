using System.Text.Json.Serialization;

using Perron.Transport;

namespace Perron.Mcp.Tools;

/// <summary>Whether a tool's <c>datetime</c> is the earliest departure or the latest arrival.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TimeIs>))]
public enum TimeIs
{
    [JsonStringEnumMemberName("departure")]
    Departure,

    [JsonStringEnumMemberName("arrival")]
    Arrival,
}

/// <summary>Departures or arrivals of a stop.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BoardType>))]
public enum BoardType
{
    [JsonStringEnumMemberName("departure")]
    Departure,

    [JsonStringEnumMemberName("arrival")]
    Arrival,
}

/// <summary>Means of transport as tools accept them (plan.md, section 4: the API's values).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TransportMode>))]
public enum TransportMode
{
    [JsonStringEnumMemberName("train")]
    Train,

    [JsonStringEnumMemberName("tram")]
    Tram,

    [JsonStringEnumMemberName("bus")]
    Bus,

    [JsonStringEnumMemberName("ship")]
    Ship,

    [JsonStringEnumMemberName("cableway")]
    Cableway,
}

public static class TransportModeExtensions
{
    public static TransportType ToTransportType(this TransportMode mode) => mode switch
    {
        TransportMode.Train => TransportType.Train,
        TransportMode.Tram => TransportType.Tram,
        TransportMode.Bus => TransportType.Bus,
        TransportMode.Ship => TransportType.Ship,
        TransportMode.Cableway => TransportType.Cableway,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown means of transport."),
    };
}
