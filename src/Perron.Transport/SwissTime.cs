using System.Globalization;
using System.Text.RegularExpressions;

namespace Perron.Transport;

/// <summary>Explicit Europe/Zurich time handling. No implicit time zones anywhere in Perron.</summary>
public static partial class SwissTime
{
    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich");

    /// <summary>The same instant, expressed with the Europe/Zurich offset.</summary>
    public static DateTimeOffset ToZurich(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone);

    /// <summary>Date part (<c>yyyy-MM-dd</c>) of the instant in Europe/Zurich.</summary>
    public static string FormatDate(DateTimeOffset instant) =>
        ToZurich(instant).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Time part (<c>HH:mm</c>) of the instant in Europe/Zurich.</summary>
    public static string FormatTime(DateTimeOffset instant) =>
        ToZurich(instant).ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses an API time like <c>2026-11-25T17:32:00+0100</c> (offset without colon) or with <c>+01:00</c>,
    /// and returns it in Europe/Zurich.
    /// </summary>
    public static DateTimeOffset ParseApiTime(string value)
    {
        var normalized = OffsetWithoutColon().Replace(value.Trim(), "$1:$2");
        var parsed = DateTimeOffset.ParseExact(
            normalized, "yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture, DateTimeStyles.None);
        return ToZurich(parsed);
    }

    /// <summary>Like <see cref="ParseApiTime"/>, but <c>null</c> for null or empty input.</summary>
    public static DateTimeOffset? ParseApiTimeOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : ParseApiTime(value);

    [GeneratedRegex(@"([+-]\d{2})(\d{2})$")]
    private static partial Regex OffsetWithoutColon();
}
