using ModelContextProtocol;

using Perron.Mcp.Results;
using Perron.Mcp.Variants;
using Perron.Transport;

namespace Perron.Mcp.Tools;

/// <summary>Tool <c>find_stops</c> (AK-1.1, AK-1.2). Texts in <see cref="ToolTexts"/>.</summary>
public static class FindStopsTool
{
    public const string Name = "find_stops";
    public const int DefaultLimit = 5;
    public const int MaxLimit = 10;

    public static async Task<string> FindStopsAsync(
        ITransportApiClient transport,
        PerronVariant variant,
        string? query = null,
        double? latitude = null,
        double? longitude = null,
        int limit = DefaultLimit,
        CancellationToken cancellationToken = default) =>
        variant.Render(await SearchAsync(transport, variant, query, latitude, longitude, limit, cancellationToken));

    /// <summary>Searches stops by name or near a coordinate; shared by the variants' stop tools.</summary>
    public static async Task<StopsResult> SearchAsync(
        ITransportApiClient transport,
        PerronVariant variant,
        string? query,
        double? latitude,
        double? longitude,
        int limit,
        CancellationToken cancellationToken)
    {
        var search = Validate(query, latitude, longitude, limit);

        var found = await transport.SearchLocationsAsync(search, cancellationToken);
        var candidates = found.Where(stop => !string.IsNullOrEmpty(stop.Id)).ToList();
        var stops = candidates.Take(limit).ToList();

        if (search.Name is { } name)
        {
            var hint = stops.Count == 0
                ? $"No stop found for \"{name}\". Check the spelling, or try a shorter name or the name of the town."
                : StopNames.IsAmbiguous(name, candidates)
                    ? $"Several stops match \"{name}\". Ask which one is meant, or use its ID in {variant.TripTools}."
                    : null;
            return new StopsResult(name, null, stops, hint);
        }

        var near = search.Near!;
        var noStop = stops.Count == 0
            ? $"No stop found near {ToolText.Degrees(near.Latitude)}, {ToolText.Degrees(near.Longitude)}. "
                + "Check the coordinates (WGS84, latitude first); stops are only known in and around Switzerland."
            : null;
        return new StopsResult(null, near, stops, noStop);
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
}
