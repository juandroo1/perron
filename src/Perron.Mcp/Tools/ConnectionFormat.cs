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

    public static IEnumerable<string> Lines(Connection connection, int number)
    {
        var departure = connection.From.Departure ?? connection.Sections.FirstOrDefault()?.Departure.Departure;
        var arrival = connection.To.Arrival ?? connection.Sections.LastOrDefault()?.Arrival.Arrival;
        var day = departure ?? arrival;

        var duration = connection.Duration ?? (departure is { } d && arrival is { } a ? a - d : null);
        var transfers = connection.Transfers == 1 ? "1 transfer" : $"{connection.Transfers} transfers";
        yield return $"{number}) {(day is { } date ? ToolText.Date(date) + " " : "")}{At(departure, day)} → {At(arrival, day)}"
            + $" | {(duration is { } span ? ToolText.Duration(span) : "?")} | {transfers}";

        DateTimeOffset? lastJourneyArrival = null;
        string? lastJourneyStop = null;
        var walkedSinceLastJourney = false;
        foreach (var section in connection.Sections)
        {
            if (section.Journey is not { } journey)
            {
                walkedSinceLastJourney = true;
                yield return Indent + Walk(section);
                continue;
            }

            if (lastJourneyArrival is { } previous && section.Departure.Departure is { } next)
            {
                var minutes = ToolText.Duration(next - previous);
                yield return Indent + (walkedSinceLastJourney
                    ? $"transfer {minutes} incl. walk"
                    : $"transfer at {lastJourneyStop} {minutes}");
            }

            var line = journey.Line ?? "ride";
            var direction = journey.Destination is { } destination ? $" to {destination}" : "";
            yield return $"{Indent}{line}{direction}: {Stop(section.Departure, section.Departure.Departure, day)}"
                + $" → {Stop(section.Arrival, section.Arrival.Arrival, day)}";

            lastJourneyArrival = section.Arrival.Arrival;
            lastJourneyStop = section.Arrival.Station?.Name;
            walkedSinceLastJourney = false;
        }
    }

    private static string Walk(Section section)
    {
        var duration = section.Walk?.Duration
            ?? (section.Departure.Departure is { } from && section.Arrival.Arrival is { } to ? to - from : null);
        var target = section.Arrival.Station?.Name is { } name ? $" to {name}" : "";
        return duration is { } span ? $"walk {ToolText.Duration(span)}{target}" : $"walk{target}";
    }

    /// <summary>"Olten 08:30 pl. 7 (+3 min)", with "pl. 7 (now 8)" when the forecast platform differs.</summary>
    private static string Stop(Checkpoint checkpoint, DateTimeOffset? time, DateTimeOffset? day)
    {
        var text = $"{checkpoint.Station?.Name ?? "?"} {At(time, day)}";

        var planned = checkpoint.Platform;
        var forecast = checkpoint.Prognosis?.Platform;
        if (!string.IsNullOrWhiteSpace(planned))
        {
            text += $" pl. {planned}";
            if (!string.IsNullOrWhiteSpace(forecast) && forecast != planned)
            {
                text += $" (now {forecast})";
            }
        }
        else if (!string.IsNullOrWhiteSpace(forecast))
        {
            text += $" pl. {forecast}";
        }

        if (checkpoint.DelayMinutes is > 0 and var delay)
        {
            text += $" (+{delay} min)";
        }

        return text;
    }

    /// <summary>"08:04", or "Thu 27.11. 00:12" on a later day than <paramref name="day"/>.</summary>
    private static string At(DateTimeOffset? time, DateTimeOffset? day)
    {
        if (time is not { } instant)
        {
            return "?";
        }

        var sameDay = day is { } reference
            && SwissTime.ToZurich(reference).Date == SwissTime.ToZurich(instant).Date;
        return sameDay ? ToolText.Time(instant) : $"{ToolText.ShortDate(instant)} {ToolText.Time(instant)}";
    }
}
