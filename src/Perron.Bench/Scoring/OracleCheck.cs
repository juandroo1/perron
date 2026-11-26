using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Perron.Bench.Scoring;

/// <summary>
/// Oracle checks, "verify, don't recompute" (AK-9.1; plan.md, decision log): the answer must name a connection or
/// departure that exists in the oracle's data, meets the task's constraints (the oracle query already carried them)
/// and is at most <c>tolerance_minutes</c> worse than the best one. Planned and forecast times both count. A connection
/// that starts or ends with a walk is also named by the first vehicle's departure or the last vehicle's arrival.
/// </summary>
public static partial class OracleCheck
{
    public static CheckResult Evaluate(int index, JsonElement check, OracleData? data, string? answer, DateTimeOffset reference)
    {
        var oracle = check.GetProperty("oracle").GetString()!;
        if (data is null)
        {
            return new(index, "oracle", Results.Unscored, $"{oracle}: no oracle data in the trace");
        }

        if (data.Error is not null)
        {
            return new(index, "oracle", Results.Unscored, $"{oracle}: oracle failed: {data.Error}");
        }

        var parameters = check.GetProperty("params");
        var tolerance = TimeSpan.FromMinutes(check.TryGetProperty("tolerance_minutes", out var t) ? t.GetInt32() : 0);
        var times = new HashSet<string>(ClockTimes.In(answer));

        return oracle switch
        {
            "earliest_arrival" => EarliestArrival(index, data, times, RelativeTime.Resolve(parameters.GetProperty("depart_after").GetString()!, reference), tolerance),
            "latest_departure" => LatestDeparture(index, data, times, RelativeTime.Resolve(parameters.GetProperty("arrive_before").GetString()!, reference), tolerance),
            "next_departure" => NextDeparture(index, data, times, RelativeTime.Resolve(parameters.GetProperty("after").GetString()!, reference), tolerance),
            "occupancy" => Occupancy(index, data, answer),
            "stop_candidates" => StopCandidates(index, data, answer),
            _ => new(index, "oracle", Results.Unscored, $"unknown oracle '{oracle}'"),
        };
    }

    private static CheckResult EarliestArrival(int index, OracleData data, HashSet<string> times, DateTimeOffset after, TimeSpan tolerance)
    {
        var valid = (data.Connections ?? []).Where(connection => connection.Departure >= after).ToList();
        if (valid.Count == 0)
        {
            return new(index, "oracle", Results.Unscored, "earliest_arrival: the oracle found no connection");
        }

        var best = valid.Min(connection => connection.Arrival);
        return Judge(index, "earliest_arrival", times, valid,
            named: connection => DepartureNamed(times, connection) && ArrivalNamed(times, connection),
            weakly: connection => ArrivalNamed(times, connection),
            goodEnough: connection => connection.Arrival <= best + tolerance,
            best: $"best arrival {ClockTimes.Of(best)}");
    }

    private static CheckResult LatestDeparture(int index, OracleData data, HashSet<string> times, DateTimeOffset before, TimeSpan tolerance)
    {
        var valid = (data.Connections ?? []).Where(connection => connection.Arrival <= before).ToList();
        if (valid.Count == 0)
        {
            return new(index, "oracle", Results.Unscored, "latest_departure: the oracle found no connection");
        }

        var best = valid.Max(connection => connection.Departure);
        return Judge(index, "latest_departure", times, valid,
            named: connection => DepartureNamed(times, connection) && ArrivalNamed(times, connection),
            weakly: connection => DepartureNamed(times, connection),
            goodEnough: connection => connection.Departure >= best - tolerance,
            best: $"latest departure {ClockTimes.Of(best)}");
    }

    private static CheckResult NextDeparture(int index, OracleData data, HashSet<string> times, DateTimeOffset after, TimeSpan tolerance)
    {
        var valid = (data.Departures ?? []).Where(departure => departure.Time >= after).ToList();
        if (valid.Count == 0)
        {
            return new(index, "oracle", Results.Unscored, "next_departure: the oracle found no departure");
        }

        var best = valid.Min(departure => departure.Time);
        return Judge(index, "next_departure", times, valid,
            named: departure => Named(times, departure.Time, departure.Forecast),
            weakly: _ => false,
            goodEnough: departure => departure.Time <= best + tolerance,
            best: $"first departure {ClockTimes.Of(best)}");
    }

    /// <summary>
    /// Passed if a fully named entry is good enough; if no entry is fully named, a weakly named one counts.
    /// Failed as <c>not_optimal</c> when the answer names valid entries that are all too late (or too early),
    /// as <c>wrong_time</c> when it names none, as <c>gave_up</c> when it names no time at all.
    /// </summary>
    private static CheckResult Judge<T>(
        int index, string oracle, HashSet<string> times, List<T> valid, Func<T, bool> named, Func<T, bool> weakly, Func<T, bool> goodEnough, string best)
    {
        if (times.Count == 0)
        {
            return new(index, "oracle", Results.Failed, $"{oracle}: the answer names no time ({best})", ErrorCodes.GaveUp);
        }

        var matches = valid.Where(named).ToList();
        var how = "named";
        if (matches.Count == 0)
        {
            matches = valid.Where(weakly).ToList();
            how = "named in part";
        }

        if (matches.Count == 0)
        {
            return new(index, "oracle", Results.Failed, $"{oracle}: no valid entry matches the times {string.Join(", ", times.Order())} ({best})", ErrorCodes.WrongTime);
        }

        return matches.Any(goodEnough)
            ? new(index, "oracle", Results.Passed, $"{oracle}: valid entry {how}, within tolerance ({best})")
            : new(index, "oracle", Results.Failed, $"{oracle}: valid entry {how}, but outside the tolerance ({best})", ErrorCodes.NotOptimal);
    }

    private static bool DepartureNamed(HashSet<string> times, OracleConnection connection) =>
        Named(times, connection.Departure, connection.DepartureForecast) || Named(times, connection.RideDeparture);

    private static bool ArrivalNamed(HashSet<string> times, OracleConnection connection) =>
        Named(times, connection.Arrival, connection.ArrivalForecast) || Named(times, connection.RideArrival);

    private static bool Named(HashSet<string> times, DateTimeOffset? time) =>
        time is { } t && times.Contains(ClockTimes.Of(t));

    private static bool Named(HashSet<string> times, DateTimeOffset planned, DateTimeOffset? forecast) =>
        times.Contains(ClockTimes.Of(planned)) || (forecast is { } f && times.Contains(ClockTimes.Of(f)));

    private static CheckResult Occupancy(int index, OracleData data, string? answer)
    {
        if (data.Occupancy is not (1 or 2 or 3) || data.Occupancy is not { } level)
        {
            return new(index, "oracle", Results.Unscored, "occupancy: the oracle has no forecast for this connection");
        }

        var named = OccupancyLevels(answer);
        var names = string.Join(", ", named);
        if (named.Count == 0)
        {
            return new(index, "oracle", Results.Failed, $"occupancy: the answer names no level (expected {level})", ErrorCodes.GaveUp);
        }

        return named.Contains(level)
            ? new(index, "oracle", Results.Passed, $"occupancy: level {level} named")
            : new(index, "oracle", Results.Failed, $"occupancy: expected level {level}, the answer names {names}");
    }

    /// <summary>Levels 1 (low) to 3 (high) named in the answer, in German, French or English, or as ●○○ symbols.</summary>
    public static IReadOnlySet<int> OccupancyLevels(string? answer)
    {
        var levels = new HashSet<int>();
        if (string.IsNullOrWhiteSpace(answer))
        {
            return levels;
        }

        if (Low().IsMatch(answer))
        {
            levels.Add(1);
        }

        if (Medium().IsMatch(answer))
        {
            levels.Add(2);
        }

        if (High().IsMatch(answer))
        {
            levels.Add(3);
        }

        return levels;
    }

    private static CheckResult StopCandidates(int index, OracleData data, string? answer)
    {
        var candidates = data.Candidates ?? [];
        if (candidates.Count < 2)
        {
            return new(index, "oracle", Results.Unscored, "stop_candidates: the oracle found fewer than two candidates");
        }

        var text = Normalize(answer ?? "");
        var named = candidates.Where(candidate => text.Contains(Normalize(candidate), StringComparison.Ordinal)).ToList();
        return named.Count >= 2
            ? new(index, "oracle", Results.Passed, $"stop_candidates: names {string.Join(", ", named)}")
            : new(index, "oracle", Results.Failed, $"stop_candidates: names {named.Count} of {candidates.Count} candidates, at least 2 needed");
    }

    /// <summary>Lower case without diacritics, single spaces.</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return Spaces().Replace(builder.ToString(), " ").Trim();
    }

    // Phrases that describe occupancy; deliberately narrow ("wenig" alone would also match "wenige Minuten").
    [GeneratedRegex(@"(?i)\b(niedrig\w*|gering\w*|wenig (belegt|ausgelastet|besetzt|los)|schwach (belegt|ausgelastet)|low|light|faible\w*|peu (charg\w+|fr(é|e)quent\w+|de monde))\b|●○○|\b1/3\b")]
    private static partial Regex Low();

    [GeneratedRegex(@"(?i)\b(mittel\w*|m(ä|ae|a)ssig\w*|moderat\w*|medium|moyen\w*)\b|●●○|\b2/3\b")]
    private static partial Regex Medium();

    [GeneratedRegex(@"(?i)\b(hoch|hohe\w*|stark (belegt|ausgelastet|besetzt)|voll(e|er|en|es)?|vollbesetzt|(ü|ue)berf(ü|ue)llt|nur steh\w*|stehpl(ä|ae)tze|high|heavy|full|busy|(é|e)lev(é|e)e?|tr(è|e)s charg\w+|bond(é|e)e?)\b|●●●|\b3/3\b")]
    private static partial Regex High();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
