using System.Globalization;
using System.Text.RegularExpressions;

using Perron.Transport;

namespace Perron.Bench.Scoring;

/// <summary>Relative times of oracle parameters (plan.md, section 6), resolved against the reference time.</summary>
public static partial class RelativeTime
{
    /// <summary><c>now</c>, <c>+&lt;n&gt;d HH:mm</c> or <c>next:&lt;mon..sun&gt; HH:mm</c> → local Swiss time.</summary>
    /// <exception cref="FormatException">Not one of the three forms.</exception>
    public static DateTimeOffset Resolve(string expression, DateTimeOffset reference)
    {
        var local = SwissTime.ToZurich(reference);
        if (expression == "now")
        {
            return local;
        }

        var match = Expression().Match(expression);
        if (!match.Success)
        {
            throw new FormatException($"'{expression}' is not now, +<n>d HH:mm or next:<day> HH:mm.");
        }

        var time = new TimeSpan(int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture), 0);
        DateTime day;
        if (match.Groups["days"].Success)
        {
            day = local.Date.AddDays(int.Parse(match.Groups["days"].Value, CultureInfo.InvariantCulture));
        }
        else
        {
            var target = match.Groups["weekday"].Value switch
            {
                "mon" => DayOfWeek.Monday,
                "tue" => DayOfWeek.Tuesday,
                "wed" => DayOfWeek.Wednesday,
                "thu" => DayOfWeek.Thursday,
                "fri" => DayOfWeek.Friday,
                "sat" => DayOfWeek.Saturday,
                _ => DayOfWeek.Sunday,
            };
            var ahead = ((int)target - (int)local.DayOfWeek + 7) % 7;
            day = local.Date.AddDays(ahead == 0 ? 7 : ahead);
        }

        var wall = day + time;
        return new DateTimeOffset(wall, SwissTime.Zone.GetUtcOffset(wall));
    }

    [GeneratedRegex(@"^(?:\+(?<days>\d+)d|next:(?<weekday>mon|tue|wed|thu|fri|sat|sun)) (?<hour>[01]\d|2[0-3]):(?<minute>[0-5]\d)$")]
    private static partial Regex Expression();
}

/// <summary>Clock times in text, normalized to "HH:MM".</summary>
public static partial class ClockTimes
{
    /// <summary>"8:05", "08:05" and the French "08h05"; not part of a longer number or date.</summary>
    public static IReadOnlyList<string> In(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        return
        [
            .. Clock().Matches(text)
                .Select(match => $"{int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture):00}:{match.Groups["minute"].Value}")
                .Distinct(),
        ];
    }

    /// <summary>
    /// Clock times plus the local Swiss time of every ISO 8601 timestamp with an offset or "Z", so a tool result in UTC
    /// still grounds the local time the model reports.
    /// </summary>
    public static IReadOnlySet<string> InToolResult(string? text)
    {
        var times = new HashSet<string>(In(text));
        if (string.IsNullOrEmpty(text))
        {
            return times;
        }

        foreach (Match match in Iso().Matches(text))
        {
            if (DateTimeOffset.TryParse(match.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant))
            {
                times.Add(Of(instant));
            }
            else if (TryParseApiTime(match.Value) is { } api)
            {
                // transport.opendata.ch style: offset without colon ("+0100").
                times.Add(Of(api));
            }
        }

        return times;
    }

    private static DateTimeOffset? TryParseApiTime(string value)
    {
        try
        {
            return SwissTime.ParseApiTimeOrNull(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>"HH:MM" in Europe/Zurich.</summary>
    public static string Of(DateTimeOffset instant) => SwissTime.ToZurich(instant).ToString("HH:mm", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"(?<![\d.:])(?<hour>[01]?\d|2[0-3])[:h](?<minute>[0-5]\d)(?![\d])")]
    private static partial Regex Clock();

    [GeneratedRegex(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:?\d{2})")]
    private static partial Regex Iso();
}
