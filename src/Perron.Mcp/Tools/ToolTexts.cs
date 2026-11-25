using Perron.Mcp.Variants;

namespace Perron.Mcp.Tools;

/// <summary>Description of a tool and its parameters in one style.</summary>
public sealed record ToolDescription(string Description, IReadOnlyDictionary<string, string> Parameters);

/// <summary>
/// Tool descriptions per style (plan.md, section 4). <c>rich</c> says when to use a tool, when not, and gives an
/// example; <c>minimal</c> keeps one sentence and only meaning, format and range of the parameters.
/// </summary>
public static class ToolTexts
{
    private const string DateTimeRich = "Local Swiss time, ISO 8601 without offset, e.g. \"2026-11-26T08:00\". Default: now.";
    private const string DateTimeMinimal = "Date and time, YYYY-MM-DDTHH:mm.";
    private const string TransportTypesRich = "Only use these means of transport, e.g. [\"train\"]. Default: all.";
    private const string TransportTypesMinimal = "Means of transport.";

    public static ToolDescription FindStops(DescriptionStyle style) => style == DescriptionStyle.Rich
        ? new(
            "Find public transport stops in Switzerland by name or near a coordinate. "
            + "Returns stop IDs, names, the kind of stop (train, tram, bus …) and coordinates; "
            + "a search near a coordinate also returns the straight-line distance. "
            + "Use it to turn a place name into a stop ID, to see which stops a name could mean "
            + "(e.g. \"Buchs\" matches Buchs AG, Buchs SG and others), or to find the stop nearest to a point. "
            + "It returns no connections or departures. "
            + "Example: query \"Luzern\" → 8505000 Luzern.",
            new Dictionary<string, string>
            {
                ["query"] = "Stop or place name, e.g. \"Zürich HB\", \"Genève\", \"Buchs\". Omit when searching near a coordinate.",
                ["latitude"] = "WGS84 latitude (e.g. 47.378177) to find the nearest stops. Give together with longitude, and no query.",
                ["longitude"] = "WGS84 longitude (e.g. 8.540192) to find the nearest stops. Give together with latitude, and no query.",
                ["limit"] = "Maximum number of stops, 1–10. Default 5.",
            })
        : new(
            "Find stops by name or coordinate.",
            new Dictionary<string, string>
            {
                ["query"] = "Stop name.",
                ["latitude"] = "Latitude.",
                ["longitude"] = "Longitude.",
                ["limit"] = "Maximum number of stops (1–10).",
            });

    public static ToolDescription PlanTrip(DescriptionStyle style) => style == DescriptionStyle.Rich
        ? new(
            "Plan public transport connections in Switzerland from one stop to another, optionally via up to two stops, "
            + "leaving after or arriving before a given time. "
            + "Returns connections with date, departure and arrival times, platforms, delays, duration, number of transfers, "
            + "and every leg with its line and the transfer time between legs. "
            + "Stops can be names or IDs from find_stops. "
            + "If a name matches several stops (e.g. \"Buchs\"), the result lists the candidates with their IDs instead of connections: "
            + "ask which one is meant, or call again with the right ID. "
            + "Times are local Swiss time (Europe/Zurich). "
            + "Example: from \"Zürich HB\", to \"Bern\", datetime \"2026-11-26T08:00\".",
            new Dictionary<string, string>
            {
                ["from"] = "Departure stop: name (e.g. \"Zürich HB\") or ID (e.g. \"8503000\").",
                ["to"] = "Arrival stop: name or ID.",
                ["via"] = "Up to 2 stops the connection must pass through, names or IDs, e.g. [\"Olten\"].",
                ["datetime"] = DateTimeRich,
                ["time_is"] = "\"departure\" (default): leave at or after datetime. \"arrival\": arrive at or before datetime.",
                ["transport_types"] = TransportTypesRich,
                ["limit"] = "Number of connections, 1–8. Default 4.",
            })
        : new(
            "Plan connections between two stops.",
            new Dictionary<string, string>
            {
                ["from"] = "Departure stop.",
                ["to"] = "Arrival stop.",
                ["via"] = "Stops to pass through (max. 2).",
                ["datetime"] = DateTimeMinimal,
                ["time_is"] = "Whether datetime is the departure or arrival time.",
                ["transport_types"] = TransportTypesMinimal,
                ["limit"] = "Number of connections (1–8).",
            });

    public static ToolDescription GetDepartures(DescriptionStyle style) => style == DescriptionStyle.Rich
        ? new(
            "Show the next departures (or arrivals) at one public transport stop in Switzerland, from now or from a given time. "
            + "Returns for each the date and time, delay, line, final destination and platform. "
            + "The stop can be a name or an ID from find_stops. "
            + "If a name matches several stops (e.g. \"Buchs\"), the result lists the candidates with their IDs instead: "
            + "ask which one is meant, or call again with the right ID. "
            + "Arrivals also show the vehicle's final destination, not where it comes from. "
            + "To check whether a vehicle stops at a certain station, or to get from A to B, use plan_trip. "
            + "Times are local Swiss time (Europe/Zurich). "
            + "Example: stop \"Bern\", datetime \"2026-11-26T08:00\", transport_types [\"train\"].",
            new Dictionary<string, string>
            {
                ["stop"] = "Stop name (e.g. \"Bern\", \"Zürich, Central\") or ID (e.g. \"8507000\").",
                ["datetime"] = DateTimeRich,
                ["type"] = "\"departure\" (default) or \"arrival\".",
                ["transport_types"] = TransportTypesRich,
                ["limit"] = "Number of departures, 1–30. Default 10. A few more may follow if they leave in the same minute as the last one.",
            })
        : new(
            "Get departures or arrivals at a stop.",
            new Dictionary<string, string>
            {
                ["stop"] = "Stop.",
                ["datetime"] = DateTimeMinimal,
                ["type"] = "Departures or arrivals.",
                ["transport_types"] = TransportTypesMinimal,
                ["limit"] = "Number of entries (1–30).",
            });

    // Tool cut "many": the same parameters and wording where they overlap with "standard".
    private const string FromRich = "Departure stop: name (e.g. \"Zürich HB\") or ID (e.g. \"8503000\").";
    private const string ToRich = "Arrival stop: name or ID.";
    private const string ConnectionLimitRich = "Number of connections, 1–8. Default 4.";
    private const string ConnectionLimitMinimal = "Number of connections (1–8).";
    private const string StopLimitRich = "Maximum number of stops, 1–10. Default 5.";
    private const string StopLimitMinimal = "Maximum number of stops (1–10).";
    private const string StopRich = "Stop name (e.g. \"Bern\", \"Zürich, Central\") or ID (e.g. \"8507000\").";

    private const string ConnectionsReturn =
        "Returns connections with date, departure and arrival times, platforms, delays, duration, number of transfers, "
        + "and every leg with its line and the transfer time between legs. "
        + "Stops can be names or IDs from search_stops_by_name. "
        + "If a name matches several stops, the result lists the candidates with their IDs instead: "
        + "ask which one is meant, or call again with the right ID. ";

    private const string BoardReturn =
        "Returns for each the date and time, delay, line, final destination and platform. "
        + "The stop can be a name or an ID from search_stops_by_name. "
        + "If a name matches several stops (e.g. \"Buchs\"), the result lists the candidates with their IDs instead: "
        + "ask which one is meant, or call again with the right ID. ";

    public static ToolDescription SearchStopsByName(DescriptionStyle style) => style == DescriptionStyle.Rich
        ? new(
            "Find public transport stops in Switzerland by name. "
            + "Returns stop IDs, names, the kind of stop (train, tram, bus …) and coordinates. "
            + "Use it to turn a place name into a stop ID, or to see which stops a name could mean "
            + "(e.g. \"Buchs\" matches Buchs AG, Buchs SG and others). "
            + "For stops near a point, use search_stops_near. "
            + "Example: query \"Luzern\" → 8505000 Luzern.",
            new Dictionary<string, string>
            {
                ["query"] = "Stop or place name, e.g. \"Zürich HB\", \"Genève\", \"Buchs\".",
                ["limit"] = StopLimitRich,
            })
        : new(
            "Find stops by name.",
            new Dictionary<string, string> { ["query"] = "Stop name.", ["limit"] = StopLimitMinimal });

    public static ToolDescription SearchStopsNear(DescriptionStyle style) => style == DescriptionStyle.Rich
        ? new(
            "Find the public transport stops in Switzerland nearest to a coordinate. "
            + "Returns stop IDs, names, the kind of stop, coordinates and the straight-line distance. "
            + "Example: latitude 47.378177, longitude 8.540192 → 8503000 Zürich HB, 1 m.",
            new Dictionary<string, string>
            {
                ["latitude"] = "WGS84 latitude, e.g. 47.378177.",
                ["longitude"] = "WGS84 longitude, e.g. 8.540192.",
                ["limit"] = StopLimitRich,
            })
        : new(
            "Find stops near a coordinate.",
            new Dictionary<string, string> { ["latitude"] = "Latitude.", ["longitude"] = "Longitude.", ["limit"] = StopLimitMinimal });

    public static ToolDescription GetConnectionsDeparting(DescriptionStyle style) => style == DescriptionStyle.Rich
        ? new(
            "Plan public transport connections in Switzerland that leave at or after a given time (default: now). "
            + ConnectionsReturn
            + "To arrive by a given time use get_connections_arriving; to pass through certain stops use get_connections_via. "
            + "Times are local Swiss time (Europe/Zurich). "
            + "Example: from \"Zürich HB\", to \"Bern\", datetime \"2026-11-26T08:00\".",
            new Dictionary<string, string>
            {
                ["from"] = FromRich,
                ["to"] = ToRich,
                ["datetime"] = DateTimeRich,
                ["transport_types"] = TransportTypesRich,
                ["limit"] = ConnectionLimitRich,
            })
        : new(
            "Get connections leaving after a time.",
            new Dictionary<string, string>
            {
                ["from"] = "Departure stop.",
                ["to"] = "Arrival stop.",
                ["datetime"] = DateTimeMinimal,
                ["transport_types"] = TransportTypesMinimal,
                ["limit"] = ConnectionLimitMinimal,
            });

    public static ToolDescription GetConnectionsArriving(DescriptionStyle style) => style == DescriptionStyle.Rich
        ? new(
            "Plan public transport connections in Switzerland that arrive at or before a given time. "
            + ConnectionsReturn
            + "To leave after a given time use get_connections_departing; to pass through certain stops use get_connections_via. "
            + "Times are local Swiss time (Europe/Zurich). "
            + "Example: from \"Bern\", to \"Lausanne\", datetime \"2026-11-26T10:00\".",
            new Dictionary<string, string>
            {
                ["from"] = FromRich,
                ["to"] = ToRich,
                ["datetime"] = "Latest arrival, local Swiss time, ISO 8601 without offset, e.g. \"2026-11-26T10:00\".",
                ["transport_types"] = TransportTypesRich,
                ["limit"] = ConnectionLimitRich,
            })
        : new(
            "Get connections arriving by a time.",
            new Dictionary<string, string>
            {
                ["from"] = "Departure stop.",
                ["to"] = "Arrival stop.",
                ["datetime"] = DateTimeMinimal,
                ["transport_types"] = TransportTypesMinimal,
                ["limit"] = ConnectionLimitMinimal,
            });

    public static ToolDescription GetConnectionsVia(DescriptionStyle style) => style == DescriptionStyle.Rich
        ? new(
            "Plan public transport connections in Switzerland that pass through one or two given stops, "
            + "leaving after or arriving before a given time. "
            + ConnectionsReturn
            + "Without via stops use get_connections_departing or get_connections_arriving. "
            + "Times are local Swiss time (Europe/Zurich). "
            + "Example: from \"Zürich HB\", to \"Luzern\", via [\"Zug\"].",
            new Dictionary<string, string>
            {
                ["from"] = FromRich,
                ["to"] = ToRich,
                ["via"] = "1 or 2 stops the connection must pass through, names or IDs, e.g. [\"Olten\"].",
                ["datetime"] = DateTimeRich,
                ["time_is"] = "\"departure\" (default): leave at or after datetime. \"arrival\": arrive at or before datetime.",
                ["transport_types"] = TransportTypesRich,
                ["limit"] = ConnectionLimitRich,
            })
        : new(
            "Get connections via given stops.",
            new Dictionary<string, string>
            {
                ["from"] = "Departure stop.",
                ["to"] = "Arrival stop.",
                ["via"] = "Stops to pass through (1–2).",
                ["datetime"] = DateTimeMinimal,
                ["time_is"] = "Whether datetime is the departure or arrival time.",
                ["transport_types"] = TransportTypesMinimal,
                ["limit"] = ConnectionLimitMinimal,
            });

    public static ToolDescription GetDeparturesMany(DescriptionStyle style) => style == DescriptionStyle.Rich
        ? new(
            "Show the next departures at one public transport stop in Switzerland, from now or from a given time. "
            + BoardReturn
            + "For arrivals use get_arrivals. "
            + "To check whether a vehicle stops at a certain station, or to get from A to B, use the connection tools. "
            + "Times are local Swiss time (Europe/Zurich). "
            + "Example: stop \"Bern\", datetime \"2026-11-26T08:00\", transport_types [\"train\"].",
            new Dictionary<string, string>
            {
                ["stop"] = StopRich,
                ["datetime"] = DateTimeRich,
                ["transport_types"] = TransportTypesRich,
                ["limit"] = "Number of departures, 1–30. Default 10. A few more may follow if they leave in the same minute as the last one.",
            })
        : new(
            "Get departures at a stop.",
            new Dictionary<string, string>
            {
                ["stop"] = "Stop.",
                ["datetime"] = DateTimeMinimal,
                ["transport_types"] = TransportTypesMinimal,
                ["limit"] = "Number of departures (1–30).",
            });

    public static ToolDescription GetArrivals(DescriptionStyle style) => style == DescriptionStyle.Rich
        ? new(
            "Show the next arrivals at one public transport stop in Switzerland, from now or from a given time. "
            + BoardReturn
            + "Arrivals show the vehicle's final destination, not where it comes from. "
            + "For departures use get_departures. "
            + "To check whether a vehicle stops at a certain station, or to get from A to B, use the connection tools. "
            + "Times are local Swiss time (Europe/Zurich). "
            + "Example: stop \"Luzern\", datetime \"2026-11-26T18:00\", transport_types [\"train\"].",
            new Dictionary<string, string>
            {
                ["stop"] = StopRich,
                ["datetime"] = DateTimeRich,
                ["transport_types"] = TransportTypesRich,
                ["limit"] = "Number of arrivals, 1–30. Default 10. A few more may follow if they arrive in the same minute as the last one.",
            })
        : new(
            "Get arrivals at a stop.",
            new Dictionary<string, string>
            {
                ["stop"] = "Stop.",
                ["datetime"] = DateTimeMinimal,
                ["transport_types"] = TransportTypesMinimal,
                ["limit"] = "Number of arrivals (1–30).",
            });
}
