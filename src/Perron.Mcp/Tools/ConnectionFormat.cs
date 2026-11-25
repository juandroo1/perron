using Perron.Mcp.Results;
using Perron.Transport;

namespace Perron.Mcp.Tools;

/// <summary>
/// One connection in the <c>compact</c> format (plan.md, section 4; AK-2.2, AK-2.3):
/// a summary line with date, departure, arrival, duration and transfers, then one line per leg with line, direction,
/// stops, times, platforms and delays, and the transfer time between legs.
/// </summary>
public static class ConnectionFormat
{
    private const string Indent = "   ";

    public static IEnumerable<string> Lines(Connection connection, int number) => Lines(ConnectionView.From(connection), number);

    public static IEnumerable<string> Lines(ConnectionView connection, int number)
    {
        var day = connection.Day;
        var transfers = connection.Transfers == 1 ? "1 transfer" : $"{connection.Transfers} transfers";
        yield return $"{number}) {(day is { } date ? ToolText.Date(date) + " " : "")}"
            + $"{ToolText.TimeOnDay(connection.Departure, day)} → {ToolText.TimeOnDay(connection.Arrival, day)}"
            + $" | {(connection.Duration is { } total ? ToolText.Duration(total) : "?")} | {transfers}";

        foreach (var leg in connection.Legs)
        {
            yield return Indent + leg switch
            {
                RideLeg ride => $"{ride.Line}{(ride.Direction is { } direction ? $" to {direction}" : "")}: "
                    + $"{Point(ride.From, day)} → {Point(ride.To, day)}",
                WalkLeg walk => (walk.Duration is { } span ? $"walk {ToolText.Duration(span)}" : "walk")
                    + (walk.To is { } to ? $" to {to}" : ""),
                TransferLeg transfer => transfer.IncludesWalk
                    ? $"transfer {ToolText.Duration(transfer.Duration)} incl. walk"
                    : $"transfer at {transfer.At} {ToolText.Duration(transfer.Duration)}",
                _ => throw new InvalidOperationException("Unknown leg."),
            };
        }
    }

    /// <summary>"Olten 08:30 pl. 7 (+3 min)", with "pl. 7 (now 8)" when the forecast platform differs.</summary>
    public static string Point(StopPoint point, DateTimeOffset? day) =>
        $"{point.Stop ?? "?"} {ToolText.TimeOnDay(point.Time, day)}{Platform(point)}{ToolText.Delay(point.DelayMinutes)}";

    /// <summary>" pl. 7", " pl. 7 (now 8)", or "" without a platform.</summary>
    public static string Platform(StopPoint point) =>
        point.Platform is null ? "" : $" pl. {point.Platform}{(point.PlatformNow is { } now ? $" (now {now})" : "")}";
}
