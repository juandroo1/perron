using System.Globalization;
using System.Text;

using Perron.Transport;

namespace Perron.Mcp.Tools;

/// <summary>Compares stop names the way people type them: ignoring case, accents and extra whitespace.</summary>
public static class StopNames
{
    /// <summary>Lower case, without diacritics, single spaces. "  Zürich   HB" → "zurich hb".</summary>
    public static string Normalize(string name)
    {
        var decomposed = name.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            builder.Append(char.ToLowerInvariant(c));
            lastWasSpace = false;
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>True if a stop's name is the query itself (see <see cref="Normalize"/>).</summary>
    public static bool IsExactMatch(string query, Location stop) => Normalize(stop.Name) == Normalize(query);

    /// <summary>
    /// True if a name search is ambiguous: several stops were found and none is named exactly like the query
    /// ("Buchs" → Buchs AG, Buchs SG …). "Zürich HB" is not ambiguous, even though Zürich, Sihlpost/HB is found too.
    /// </summary>
    public static bool IsAmbiguous(string query, IReadOnlyCollection<Location> stops) =>
        stops.Count > 1 && !stops.Any(stop => IsExactMatch(query, stop));
}
