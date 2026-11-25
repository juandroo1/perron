using System.Globalization;
using System.Text.RegularExpressions;

using Perron.Transport;

namespace Perron.Mcp.Tools;

/// <summary>Tells the agent what a failed call to transport.opendata.ch means and what to do next (AK-4.2).</summary>
public static partial class TransportErrors
{
    private const int MaxApiMessageLength = 200;

    public static string Message(TransportApiException error) => error.Kind switch
    {
        TransportApiErrorKind.OutsideTimetable => OutsideTimetable(error.Message),
        TransportApiErrorKind.InvalidRequest =>
            $"transport.opendata.ch did not accept the request: {Shorten(error.Message)}. Check the stops, date and time, then try again.",
        TransportApiErrorKind.Throttled =>
            "transport.opendata.ch is busy and rejected the request (HTTP 429). "
            + "Wait about 30 seconds before trying again, and do not repeat the same request right away.",
        _ =>
            "transport.opendata.ch is not available right now (timeout, network or server error). Try again in a minute. "
            + "If it keeps failing, tell the user the timetable service is unavailable; do not guess times.",
    };

    /// <summary>
    /// The API names its window like "[2026-11-15 00:00, 2027-04-15 00:00[": the end is excluded, so the last day with
    /// data is the day before.
    /// </summary>
    private static string OutsideTimetable(string apiMessage)
    {
        var match = TimetableWindow().Match(apiMessage);
        if (match.Success
            && DateTime.TryParseExact(match.Groups["from"].Value, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from)
            && DateTime.TryParseExact(match.Groups["to"].Value, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
        {
            var lastDay = to.TimeOfDay == TimeSpan.Zero ? to.AddDays(-1) : to;
            return "No timetable data for this date. transport.opendata.ch has the timetable from "
                + $"{from.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)} to {lastDay.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}. "
                + "Ask for a date in that range; there is no data further ahead or in the past.";
        }

        return "No timetable data for this date. transport.opendata.ch has the timetable only for a few months around today. "
            + "Ask for a date in that range; there is no data further ahead or in the past.";
    }

    private static string Shorten(string message)
    {
        var trimmed = message.Trim().TrimEnd('.');
        return trimmed.Length <= MaxApiMessageLength ? trimmed : trimmed[..MaxApiMessageLength] + " …";
    }

    [GeneratedRegex(@"window \[(?<from>\d{4}-\d{2}-\d{2} \d{2}:\d{2}), (?<to>\d{4}-\d{2}-\d{2} \d{2}:\d{2})\[")]
    private static partial Regex TimetableWindow();
}
