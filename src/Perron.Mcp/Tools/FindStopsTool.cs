using System.ComponentModel;

using ModelContextProtocol;
using ModelContextProtocol.Server;

using Perron.Transport;

namespace Perron.Mcp.Tools;

/// <summary>Tool <c>find_stops</c> (AK-1.1, AK-1.2).</summary>
[McpServerToolType]
public static class FindStopsTool
{
    public const string Name = "find_stops";
    public const int DefaultLimit = 5;
    public const int MaxLimit = 10;

    [McpServerTool(
        Name = Name,
        Title = "Find stops",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = true)]
    [Description(
        "Find public transport stops in Switzerland by name or near a coordinate. "
        + "Returns stop IDs, names, the kind of stop (train, tram, bus …) and coordinates; "
        + "a search near a coordinate also returns the straight-line distance. "
        + "Use it to turn a place name into a stop ID, to see which stops a name could mean "
        + "(e.g. \"Buchs\" matches Buchs AG, Buchs SG and others), or to find the stop nearest to a point. "
        + "It returns no connections or departures. "
        + "Example: query \"Luzern\" → 8505000 Luzern.")]
    public static async Task<string> FindStopsAsync(
        ITransportApiClient transport,
        [Description("Stop or place name, e.g. \"Zürich HB\", \"Genève\", \"Buchs\". Omit when searching near a coordinate.")]
        string? query = null,
        [Description("WGS84 latitude (e.g. 47.378177) to find the nearest stops. Give together with longitude, and no query.")]
        double? latitude = null,
        [Description("WGS84 longitude (e.g. 8.540192) to find the nearest stops. Give together with latitude, and no query.")]
        double? longitude = null,
        [Description("Maximum number of stops, 1–10. Default 5.")]
        int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var search = Validate(query, latitude, longitude, limit);

        var found = await transport.SearchLocationsAsync(search, cancellationToken);
        var stops = found.Where(stop => !string.IsNullOrEmpty(stop.Id)).Take(limit).ToList();

        var lines = search.Name is { } name ? FormatByName(name, stops) : FormatNear(search.Near!, stops);
        lines.Add(ToolText.SourceLine);

        // Fixed '\n' so the result is the same on every platform.
        return string.Join('\n', lines);
    }

    private static LocationQuery Validate(string? query, double? latitude, double? longitude, int limit)
    {
        if (limit is < 1 or > MaxLimit)
        {
            throw new McpException($"limit must be between 1 and {MaxLimit}.");
        }

        var hasQuery = !string.IsNullOrWhiteSpace(query);
        var hasCoordinate = latitude is not null || longitude is not null;

        if (hasQuery && hasCoordinate)
        {
            throw new McpException("Give either a query or latitude and longitude, not both.");
        }

        if (hasQuery)
        {
            return LocationQuery.ByName(query!.Trim()) with { Type = LocationType.Station };
        }

        if (!hasCoordinate)
        {
            throw new McpException(
                "Give a query (stop or place name, e.g. \"Bern\") or both latitude and longitude (e.g. 47.378177 and 8.540192).");
        }

        if (latitude is not { } lat || longitude is not { } lon)
        {
            throw new McpException("Give both latitude and longitude, e.g. 47.378177 and 8.540192.");
        }

        if (!double.IsFinite(lat) || !double.IsFinite(lon) || lat is < -90 or > 90 || lon is < -180 or > 180)
        {
            throw new McpException(
                "latitude must be between -90 and 90 and longitude between -180 and 180 (WGS84 degrees, latitude first), "
                + "e.g. 47.378177 and 8.540192 for Zürich HB.");
        }

        return LocationQuery.ByCoordinate(new Coordinate(lat, lon)) with { Type = LocationType.Station };
    }

    private static List<string> FormatByName(string query, IReadOnlyList<Location> stops)
    {
        if (stops.Count == 0)
        {
            return [$"No stop found for \"{query}\". Check the spelling, or try a shorter name or the name of the town."];
        }

        List<string> lines =
        [
            $"Stops for \"{query}\" (ID | name | kind | latitude, longitude):",
            .. stops.Select(stop => Line(stop, withDistance: false)),
        ];

        if (StopNames.IsAmbiguous(query, stops))
        {
            lines.Add($"Several stops match \"{query}\" and none exactly. Ask which one is meant, or use the ID of the right one.");
        }

        return lines;
    }

    private static List<string> FormatNear(Coordinate point, IReadOnlyList<Location> stops)
    {
        var where = $"{ToolText.Degrees(point.Latitude)}, {ToolText.Degrees(point.Longitude)}";
        if (stops.Count == 0)
        {
            return [$"No stop found near {where}. Check the coordinates (WGS84, latitude first); stops are only known in and around Switzerland."];
        }

        return
        [
            $"Stops near {where} (ID | name | kind | latitude, longitude | distance):",
            .. stops.Select(stop => Line(stop, withDistance: true)),
        ];
    }

    private static string Line(Location stop, bool withDistance)
    {
        var coordinate = stop.Coordinate is { } c ? $"{ToolText.Degrees(c.Latitude)}, {ToolText.Degrees(c.Longitude)}" : "-";
        var line = $"{stop.Id} | {stop.Name} | {stop.Icon ?? "-"} | {coordinate}";
        return withDistance ? $"{line} | {(stop.DistanceMeters is { } d ? ToolText.Meters(d) : "-")}" : line;
    }
}
