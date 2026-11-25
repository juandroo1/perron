using System.Globalization;
using System.Text.RegularExpressions;

namespace Perron.Transport;

/// <summary>Parses the API's duration format <c>00d00:56:00</c> (days, hours, minutes, seconds).</summary>
public static partial class ApiDuration
{
    public static TimeSpan? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var match = DurationPattern().Match(value.Trim());
        if (!match.Success)
        {
            throw new FormatException($"Unexpected duration format '{value}'.");
        }

        int Part(int group) => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
        return new TimeSpan(Part(1), Part(2), Part(3), Part(4));
    }

    [GeneratedRegex(@"^(\d+)d(\d{2}):(\d{2}):(\d{2})$")]
    private static partial Regex DurationPattern();
}
