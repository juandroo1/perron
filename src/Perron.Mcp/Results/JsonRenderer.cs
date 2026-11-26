using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

using Perron.Mcp.Tools;
using Perron.Transport;

namespace Perron.Mcp.Results;

/// <summary>
/// The <c>json</c> format: the same fields as <see cref="CompactRenderer"/>, serialized compactly (snake_case, no
/// nulls, characters such as "ü" unescaped, local Swiss times as "2026-11-26T08:02"). plan.md, decision log.
/// </summary>
public static class JsonRenderer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false,
    };

    public static string Render(ToolResult result)
    {
        var json = result switch
        {
            StopsResult stops => Stops(stops),
            CandidatesResult candidates => Candidates(candidates),
            TripsResult trips => Trips(trips),
            BoardResult board => Board(board),
            _ => throw new InvalidOperationException($"Unknown result {result.GetType().Name}."),
        };

        json["source"] = ToolText.SourceLine;
        return json.ToJsonString(Options);
    }

    private static JsonObject Stops(StopsResult result)
    {
        var json = new JsonObject();
        if (result.Near is { } near)
        {
            json["near"] = new JsonObject { ["latitude"] = Degrees(near.Latitude), ["longitude"] = Degrees(near.Longitude) };
        }
        else
        {
            json["query"] = result.Query;
        }

        json["stops"] = new JsonArray([.. result.Stops.Select(stop => (JsonNode?)Stop(stop, withDistance: result.Near is not null))]);
        Add(json, "hint", result.Hint);
        return json;
    }

    private static JsonObject Stop(Location stop, bool withDistance)
    {
        var json = new JsonObject { ["id"] = stop.Id, ["name"] = stop.Name };
        Add(json, "kind", stop.Icon);
        if (stop.Coordinate is { } coordinate)
        {
            json["latitude"] = Degrees(coordinate.Latitude);
            json["longitude"] = Degrees(coordinate.Longitude);
        }

        if (withDistance && stop.DistanceMeters is { } distance)
        {
            json["distance_m"] = (int)Math.Round(distance);
        }

        return json;
    }

    private static JsonObject Candidates(CandidatesResult result) => new()
    {
        ["ambiguous"] = new JsonArray([.. result.Ambiguous.Select(group => (JsonNode?)new JsonObject
        {
            ["input"] = group.Input,
            ["role"] = group.Role,
            ["candidates"] = new JsonArray([.. group.Candidates.Select(stop =>
            {
                var candidate = new JsonObject { ["id"] = stop.Id, ["name"] = stop.Name };
                Add(candidate, "kind", stop.Icon);
                return (JsonNode?)candidate;
            })]),
        })]),
        ["hint"] = result.Hint,
    };

    private static JsonObject Trips(TripsResult result)
    {
        var json = new JsonObject { ["from"] = StopRef(result.From), ["to"] = StopRef(result.To) };
        if (result.Via.Count > 0)
        {
            json["via"] = new JsonArray([.. result.Via.Select(stop => (JsonNode?)StopRef(stop))]);
        }

        json["time_is"] = result.TimeIs == TimeIs.Arrival ? "arrival" : "departure";
        json["datetime"] = result.At is { } at ? LocalTime(at) : "now";
        if (result.MinTransferMinutes is { } minutes)
        {
            json["min_transfer_minutes"] = minutes;
        }

        json["connections"] = new JsonArray([.. result.Connections.Select(connection => (JsonNode?)Connection(connection))]);
        Add(json, "hint", result.Hint);
        return json;
    }

    private static JsonObject Connection(ConnectionView connection)
    {
        var json = new JsonObject();
        if (connection.Day is { } day)
        {
            json["weekday"] = Weekday(day);
        }

        Add(json, "departure", connection.Departure is { } departure ? LocalTime(departure) : null);
        Add(json, "arrival", connection.Arrival is { } arrival ? LocalTime(arrival) : null);
        if (connection.Duration is { } duration)
        {
            json["duration_min"] = (int)Math.Round(duration.TotalMinutes);
        }

        json["transfers"] = connection.Transfers;
        json["legs"] = new JsonArray([.. connection.Legs.Select(leg => (JsonNode?)Leg(leg))]);
        return json;
    }

    private static JsonObject Leg(LegView leg)
    {
        switch (leg)
        {
            case RideLeg ride:
                var json = new JsonObject { ["line"] = ride.Line };
                Add(json, "direction", ride.Direction);
                json["from"] = Point(ride.From);
                json["to"] = Point(ride.To);
                return json;

            case WalkLeg walk:
                var walking = new JsonObject();
                if (walk.Duration is { } duration)
                {
                    walking["walk_min"] = (int)Math.Round(duration.TotalMinutes);
                }
                else
                {
                    walking["walk"] = true;
                }

                Add(walking, "to", walk.To);
                return walking;

            case TransferLeg transfer:
                var changing = new JsonObject { ["transfer_min"] = (int)Math.Round(transfer.Duration.TotalMinutes) };
                if (transfer.IncludesWalk)
                {
                    changing["incl_walk"] = true;
                }
                else
                {
                    Add(changing, "at", transfer.At);
                }

                return changing;

            default:
                throw new InvalidOperationException("Unknown leg.");
        }
    }

    private static JsonObject Board(BoardResult result)
    {
        var json = new JsonObject
        {
            ["stop"] = StopRef(result.Stop),
            ["type"] = result.Type == BoardType.Arrival ? "arrivals" : "departures",
        };
        if (result.At is { } at)
        {
            json["from"] = LocalTime(at);
        }
        else
        {
            json["from"] = "now";
            json["date"] = SwissTime.ToZurich(result.Now).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        json["entries"] = new JsonArray([.. result.Entries.Select(entry =>
        {
            var item = Point(entry.Point);
            item.Remove("stop");
            item["line"] = entry.Line;
            Add(item, "destination", entry.Destination);
            if (entry.EndsHere)
            {
                item["ends_here"] = true;
            }

            return (JsonNode?)item;
        })]);
        Add(json, "hint", result.Hint);
        return json;
    }

    private static JsonObject Point(StopPoint point)
    {
        var json = new JsonObject();
        Add(json, "stop", point.Stop);
        Add(json, "time", point.Time is { } time ? LocalTime(time) : null);
        Add(json, "platform", point.Platform);
        Add(json, "platform_now", point.PlatformNow);
        if (point.DelayMinutes is { } delay)
        {
            json["delay_min"] = delay;
        }

        return json;
    }

    private static JsonObject StopRef(StopRef stop)
    {
        var json = new JsonObject { ["id"] = stop.Id };
        Add(json, "name", stop.Name);
        return json;
    }

    private static void Add(JsonObject json, string name, string? value)
    {
        if (value is not null)
        {
            json[name] = value;
        }
    }

    private static string LocalTime(DateTimeOffset instant) =>
        SwissTime.ToZurich(instant).ToString("yyyy-MM-dd'T'HH:mm", CultureInfo.InvariantCulture);

    private static string Weekday(DateTimeOffset instant) =>
        SwissTime.ToZurich(instant).ToString("ddd", CultureInfo.InvariantCulture);

    private static double Degrees(double value) => Math.Round(value, 5);
}
