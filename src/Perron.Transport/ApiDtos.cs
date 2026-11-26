using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Perron.Transport;

// Shapes of the transport.opendata.ch JSON (only the fields Perron uses), verified in task 1.1.
// Mapped to the public models in Models.cs, where the API's quirks are cleaned up.

internal sealed record LocationsResponseDto(List<LocationDto>? Stations);

internal sealed record ConnectionsResponseDto(List<ConnectionDto>? Connections, LocationDto? From, LocationDto? To);

internal sealed record StationboardResponseDto(LocationDto? Station, List<StationboardEntryDto>? Stationboard);

internal sealed record ErrorResponseDto(List<ErrorItemDto>? Errors);

internal sealed record ErrorItemDto(string? Message);

internal sealed record CoordinateDto(double? X, double? Y);

internal sealed record LocationDto(string? Id, string? Name, CoordinateDto? Coordinate, double? Distance, string? Icon);

internal sealed record PrognosisDto(string? Platform, string? Arrival, string? Departure);

internal sealed record CheckpointDto(
    LocationDto? Station,
    string? Arrival,
    string? Departure,
    int? Delay,
    string? Platform,
    PrognosisDto? Prognosis);

internal sealed record JourneyDto(string? Category, string? Number, string? Operator, string? To);

internal sealed record WalkDto(int? Duration);

internal sealed record SectionDto(JourneyDto? Journey, WalkDto? Walk, CheckpointDto? Departure, CheckpointDto? Arrival);

internal sealed record ConnectionDto(
    CheckpointDto? From,
    CheckpointDto? To,
    string? Duration,
    int? Transfers,
    List<string>? Products,
    List<SectionDto>? Sections,
    int? Capacity1st,
    int? Capacity2nd);

internal sealed record StationboardEntryDto(
    CheckpointDto? Stop,
    string? Category,
    string? Number,
    string? Operator,
    string? To);

/// <summary>Source-generated JSON metadata: no reflection, so the client also works trimmed or with native AOT.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, NumberHandling = JsonNumberHandling.AllowReadingFromString)]
[JsonSerializable(typeof(LocationsResponseDto))]
[JsonSerializable(typeof(ConnectionsResponseDto))]
[JsonSerializable(typeof(StationboardResponseDto))]
[JsonSerializable(typeof(ErrorResponseDto))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;

internal static partial class ApiMapper
{
    public static IReadOnlyList<Location> Map(LocationsResponseDto dto) =>
        [.. (dto.Stations ?? []).Select(MapLocation).OfType<Location>()];

    public static ConnectionsResult Map(ConnectionsResponseDto dto)
    {
        var connections = (dto.Connections ?? []).Select(MapConnection).ToList();

        // With two vias the API's top-level "to" names the first via; the connections carry the real destination.
        var resolvedTo = connections.FirstOrDefault()?.To.Station ?? MapLocation(dto.To);

        return new ConnectionsResult(MapLocation(dto.From), resolvedTo, connections);
    }

    public static Stationboard Map(StationboardResponseDto dto) =>
        new(
            MapLocation(dto.Station),
            [.. (dto.Stationboard ?? []).Select(entry => new StationboardEntry(
                MapCheckpoint(entry.Stop),
                Clean(entry.Category),
                NormalizeNumber(entry.Number),
                Clean(entry.Operator),
                Clean(entry.To)))]);

    private static Connection MapConnection(ConnectionDto dto) =>
        new(
            MapCheckpoint(dto.From),
            MapCheckpoint(dto.To),
            ApiDuration.Parse(dto.Duration),
            dto.Transfers ?? 0,
            [.. (dto.Products ?? []).Select(product => Clean(product)).OfType<string>()],
            [.. (dto.Sections ?? []).Select(MapSection)],
            Occupancy(dto.Capacity1st),
            Occupancy(dto.Capacity2nd));

    /// <summary>The API's occupancy forecast: 1 low, 2 medium, 3 high; other values (-1, 0) mean none.</summary>
    private static int? Occupancy(int? level) => level is >= 1 and <= 3 ? level : null;

    private static Section MapSection(SectionDto dto) =>
        new(
            MapCheckpoint(dto.Departure),
            MapCheckpoint(dto.Arrival),
            dto.Journey is null
                ? null
                : new Journey(Clean(dto.Journey.Category), NormalizeNumber(dto.Journey.Number), Clean(dto.Journey.Operator), Clean(dto.Journey.To)),
            dto.Walk is null ? null : new Walk(dto.Walk.Duration is int seconds ? TimeSpan.FromSeconds(seconds) : null));

    private static Checkpoint MapCheckpoint(CheckpointDto? dto) =>
        dto is null
            ? new Checkpoint(null, null, null, null, null, null)
            : new Checkpoint(
                MapLocation(dto.Station),
                SwissTime.ParseApiTimeOrNull(dto.Arrival),
                SwissTime.ParseApiTimeOrNull(dto.Departure),
                dto.Delay,
                Clean(dto.Platform),
                dto.Prognosis is null
                    ? null
                    : new Prognosis(
                        SwissTime.ParseApiTimeOrNull(dto.Prognosis.Arrival),
                        SwissTime.ParseApiTimeOrNull(dto.Prognosis.Departure),
                        Clean(dto.Prognosis.Platform)));

    private static Location? MapLocation(LocationDto? dto) =>
        dto is null || string.IsNullOrWhiteSpace(dto.Name)
            ? null
            : new Location(
                Clean(dto.Id),
                Clean(dto.Name)!,
                dto.Coordinate is { X: double latitude, Y: double longitude } ? new Coordinate(latitude, longitude) : null,
                dto.Distance,
                Clean(dto.Icon));

    /// <summary>Trims and collapses inner whitespace (the API pads some operators, e.g. <c>"VBZ    F"</c>).</summary>
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : InnerWhitespace().Replace(value.Trim(), " ");

    /// <summary>Removes leading zeros of numeric train numbers (<c>000015</c> → <c>15</c>).</summary>
    private static string? NormalizeNumber(string? number)
    {
        var cleaned = Clean(number);
        if (cleaned is null || !cleaned.All(char.IsAsciiDigit))
        {
            return cleaned;
        }

        var trimmed = cleaned.TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex InnerWhitespace();
}
