using Perron.Mcp.Tools;
using Perron.Transport;

namespace Perron.Mcp.Results;

/// <summary>The <c>compact</c> text format (plan.md, section 4): short lines, IDs for follow-up calls, source line last.</summary>
public static class CompactRenderer
{
    public static string Render(ToolResult result)
    {
        IEnumerable<string> lines = result switch
        {
            StopsResult stops => Stops(stops),
            CandidatesResult candidates => Candidates(candidates),
            TripsResult trips => Trips(trips),
            BoardResult board => Board(board),
            _ => throw new InvalidOperationException($"Unknown result {result.GetType().Name}."),
        };

        // Fixed '\n' so the result is the same on every platform.
        return string.Join('\n', lines.Append(ToolText.SourceLine));
    }

    private static IEnumerable<string> Stops(StopsResult result)
    {
        if (result.Stops.Count == 0)
        {
            yield return result.Hint ?? "No stops found.";
            yield break;
        }

        var near = result.Near is not null;
        yield return near
            ? $"Stops near {Degrees(result.Near!)} (ID | name | kind | latitude, longitude | distance):"
            : $"Stops for \"{result.Query}\" (ID | name | kind | latitude, longitude):";

        foreach (var stop in result.Stops)
        {
            var coordinate = stop.Coordinate is { } c ? Degrees(c) : "-";
            var line = $"{stop.Id} | {stop.Name} | {stop.Icon ?? "-"} | {coordinate}";
            yield return near ? $"{line} | {(stop.DistanceMeters is { } d ? ToolText.Meters(d) : "-")}" : line;
        }

        if (result.Hint is { } hint)
        {
            yield return hint;
        }
    }

    private static IEnumerable<string> Candidates(CandidatesResult result) =>
        [.. result.Ambiguous.SelectMany(StopResolver.Describe), result.Hint];

    private static IEnumerable<string> Trips(TripsResult result)
    {
        if (result.Connections.Count == 0)
        {
            yield return result.Hint ?? "No connections found.";
            yield break;
        }

        yield return $"Connections {result.From.Display} → {result.To.Display}{result.ViaText}, {result.When}{result.TransferText}:";
        var number = 0;
        foreach (var line in result.Connections.SelectMany(connection => ConnectionFormat.Lines(connection, ++number)))
        {
            yield return line;
        }
    }

    private static IEnumerable<string> Board(BoardResult result)
    {
        if (result.Entries.Count == 0)
        {
            yield return result.Hint ?? "Nothing found.";
            yield break;
        }

        yield return $"{result.Kind} at {result.Stop.Display} {result.FromText}:";
        foreach (var entry in result.Entries)
        {
            var direction = entry.Destination is not { } destination
                ? ""
                : result.Type == BoardType.Departure
                    ? $" to {destination}"
                    : entry.EndsHere ? " ends here" : $" continues to {destination}";
            yield return $"{ToolText.TimeOnDay(entry.Point.Time, result.Day)}{ToolText.Delay(entry.Point.DelayMinutes)} "
                + $"{entry.Line}{direction}{ConnectionFormat.Platform(entry.Point)}";
        }
    }

    private static string Degrees(Coordinate coordinate) =>
        $"{ToolText.Degrees(coordinate.Latitude)}, {ToolText.Degrees(coordinate.Longitude)}";
}
