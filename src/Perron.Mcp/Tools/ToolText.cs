using System.Globalization;

using ModelContextProtocol;

using Perron.Transport;

namespace Perron.Mcp.Tools;

/// <summary>Shared pieces of the <c>compact</c> result format (plan.md, section 4) and of tool input.</summary>
public static class ToolText
{
    /// <summary>Attribution required by the data source; last line of every result.</summary>
    public const string SourceLine = "Fahrplandaten: opentransportdata.swiss";

    private static readonly string[] LocalFormats =
        ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"];

    /// <summary>WGS84 degrees with 5 decimals (about 1 m), independent of the server culture.</summary>
    public static string Degrees(double value) => value.ToString("0.00000", CultureInfo.InvariantCulture);

    public static string Meters(double value) => string.Create(CultureInfo.InvariantCulture, $"{value:0} m");

    /// <summary>"Wed 26.11.2026" in Europe/Zurich.</summary>
    public static string Date(DateTimeOffset instant) =>
        SwissTime.ToZurich(instant).ToString("ddd dd.MM.yyyy", CultureInfo.InvariantCulture);

    /// <summary>"Thu 27.11." in Europe/Zurich, for times on a later day than the connection's date.</summary>
    public static string ShortDate(DateTimeOffset instant) =>
        SwissTime.ToZurich(instant).ToString("ddd dd.MM.", CultureInfo.InvariantCulture);

    /// <summary>"08:04" in Europe/Zurich.</summary>
    public static string Time(DateTimeOffset instant) =>
        SwissTime.ToZurich(instant).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>"08:04", or "Thu 27.11. 00:12" on a later day than <paramref name="day"/>; "?" if unknown.</summary>
    public static string TimeOnDay(DateTimeOffset? time, DateTimeOffset? day)
    {
        if (time is not { } instant)
        {
            return "?";
        }

        var sameDay = day is { } reference
            && SwissTime.ToZurich(reference).Date == SwissTime.ToZurich(instant).Date;
        return sameDay ? Time(instant) : $"{ShortDate(instant)} {Time(instant)}";
    }

    /// <summary>" pl. 7", " pl. 7 (now 8)" when the forecast platform differs, or "" without a platform.</summary>
    public static string Platform(Checkpoint checkpoint)
    {
        var planned = checkpoint.Platform;
        var forecast = checkpoint.Prognosis?.Platform;
        if (!string.IsNullOrWhiteSpace(planned))
        {
            return !string.IsNullOrWhiteSpace(forecast) && forecast != planned
                ? $" pl. {planned} (now {forecast})"
                : $" pl. {planned}";
        }

        return string.IsNullOrWhiteSpace(forecast) ? "" : $" pl. {forecast}";
    }

    /// <summary>" (+3 min)" for a delay, "" without delay or real-time data.</summary>
    public static string Delay(int? minutes) => minutes is > 0 and var delay ? $" (+{delay} min)" : "";

    /// <summary>"56 min", "1h 18min", "1h".</summary>
    public static string Duration(TimeSpan duration)
    {
        var minutes = (int)Math.Round(duration.TotalMinutes);
        return minutes < 60
            ? $"{minutes} min"
            : minutes % 60 == 0 ? $"{minutes / 60}h" : $"{minutes / 60}h {minutes % 60}min";
    }

    /// <summary>
    /// Parses a tool's <c>datetime</c>: local Swiss time ("2026-11-26T08:00"), or an ISO 8601 instant with offset,
    /// which is converted to Europe/Zurich.
    /// </summary>
    /// <exception cref="McpException">Not a date and time.</exception>
    public static DateTimeOffset ParseDateTime(string value)
    {
        var trimmed = value.Trim();
        if (DateTime.TryParseExact(trimmed, LocalFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
        {
            return new DateTimeOffset(local, SwissTime.Zone.GetUtcOffset(local));
        }

        if (trimmed.Length > 16
            && DateTimeOffset.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.None, out var instant)
            && (trimmed.EndsWith('Z') || trimmed[16..].Contains('+') || trimmed[16..].Contains('-')))
        {
            return SwissTime.ToZurich(instant);
        }

        throw new McpException(
            $"datetime \"{trimmed}\" is not a date and time. Use local Swiss time like \"2026-11-26T08:00\", or omit it for now.");
    }
}
