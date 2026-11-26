using System.Globalization;
using System.Text;

using Perron.Transport;

namespace Perron.Mcp.Tools;

/// <summary>
/// Compares stop names the way people type them (ignoring case, accents and extra whitespace) and decides whether a
/// name search names one stop (plan.md, decision log: resolving stop names).
/// </summary>
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

    /// <summary>
    /// True if the stop is named like the query. Bilingual names count in each language:
    /// "Freiburg" matches "Fribourg/Freiburg".
    /// </summary>
    public static bool IsExactMatch(string query, Location stop)
    {
        var normalized = Normalize(query);
        return Variants(stop.Name).Any(variant => variant == normalized);
    }

    /// <summary>
    /// True if the stop's name (or one language of a bilingual name) starts with the query as a whole word:
    /// "Buchs" matches "Buchs AG" and "Buchs-Dällikon", "Gen" does not match "Genève".
    /// </summary>
    public static bool StartsWithQuery(string query, Location stop)
    {
        var normalized = Normalize(query);
        return Variants(stop.Name).Any(variant =>
            variant.StartsWith(normalized, StringComparison.Ordinal)
            && (variant.Length == normalized.Length || !char.IsLetterOrDigit(variant[normalized.Length])));
    }

    /// <summary>A stop named after a place ("Basel SBB"), not a stop within a place ("Basel, Aeschenplatz").</summary>
    public static bool IsPlaceLevel(Location stop) => !stop.Name.Contains(',', StringComparison.Ordinal);

    /// <summary>Decides which stop a name search means. <paramref name="candidates"/> are stops with an ID, best first.</summary>
    public static StopMatch Match(string query, IReadOnlyList<Location> candidates)
    {
        if (candidates.FirstOrDefault(stop => IsExactMatch(query, stop)) is { } exact)
        {
            return new StopMatch.Found(exact);
        }

        if (candidates.Count == 1)
        {
            return new StopMatch.Found(candidates[0]);
        }

        var starting = candidates.Where(stop => StartsWithQuery(query, stop)).ToList();
        if (starting.Count == 0)
        {
            return new StopMatch.NotFound(candidates);
        }

        var places = starting.Where(IsPlaceLevel).ToList();
        if (places.Count == 0)
        {
            return starting.Count == 1 ? new StopMatch.Found(starting[0]) : new StopMatch.Ambiguous(starting);
        }

        // A place with only stops within it ("Oberdorf BL, Station") is a candidate of its own unless a place-level
        // stop covers it ("Basel SBB" covers "Basel, Aeschenplatz"); its best stop stands for it.
        var uncovered = starting
            .Where(stop => !IsPlaceLevel(stop))
            .GroupBy(Place)
            .Where(group => !places.Any(place => StartsWithQuery(group.Key, place)))
            .Select(group => group.First())
            .ToHashSet();
        if (places.Count == 1 && uncovered.Count == 0)
        {
            return new StopMatch.Found(places[0]);
        }

        return new StopMatch.Ambiguous([.. starting.Where(stop => IsPlaceLevel(stop) || uncovered.Contains(stop))]);
    }

    /// <summary>True if a name search does not name one stop and the agent should ask which one is meant.</summary>
    public static bool IsAmbiguous(string query, IReadOnlyList<Location> candidates) =>
        Match(query, candidates) is StopMatch.Ambiguous;

    /// <summary>The place part of a stop name: "Oberdorf BL, Station" → "oberdorf bl".</summary>
    private static string Place(Location stop)
    {
        var normalized = Normalize(stop.Name);
        var comma = normalized.IndexOf(',', StringComparison.Ordinal);
        return comma < 0 ? normalized : normalized[..comma].TrimEnd();
    }

    /// <summary>The normalized name, and for a bilingual place ("Fribourg/Freiburg, Pl. Gare") each language on its own.</summary>
    private static IEnumerable<string> Variants(string name)
    {
        var normalized = Normalize(name);
        yield return normalized;

        var comma = normalized.IndexOf(',', StringComparison.Ordinal);
        var place = comma < 0 ? normalized : normalized[..comma];
        var rest = comma < 0 ? "" : normalized[comma..];
        var languages = place.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (languages.Length < 2)
        {
            yield break;
        }

        foreach (var language in languages)
        {
            yield return language + rest;
        }
    }
}

/// <summary>Outcome of <see cref="StopNames.Match"/>.</summary>
public abstract record StopMatch
{
    private StopMatch()
    {
    }

    /// <summary>The query names this stop.</summary>
    public sealed record Found(Location Stop) : StopMatch;

    /// <summary>The query could mean any of these stops.</summary>
    public sealed record Ambiguous(IReadOnlyList<Location> Candidates) : StopMatch;

    /// <summary>No stop is named like the query; <see cref="Similar"/> are the API's fuzzy hits (may be empty).</summary>
    public sealed record NotFound(IReadOnlyList<Location> Similar) : StopMatch;
}
