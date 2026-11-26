using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Web;

using Perron.Mcp.Tools;
using Perron.Mcp.Variants;

namespace Perron.Mcp.Tests;

/// <summary>
/// Format <c>json</c> (AK-5.1): snapshots, and the same values as <c>compact</c> for the same recorded answers.
/// </summary>
public sealed partial class JsonFormatTests
{
    private static readonly PerronVariant Json = PerronVariant.Default with { Format = ResultFormat.Json };
    private static readonly FixedClock RecordedAt = new(new DateTimeOffset(2026, 11, 25, 17, 12, 0, TimeSpan.FromHours(1)));

    private static readonly JsonSerializerOptions Pretty = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static TheoryData<string> Cases() =>
    [
        "find_stops-buchs",
        "find_stops-coordinates",
        "plan_trip-zurich-bern-now",
        "plan_trip-buchs-ambiguous",
        "plan_trip-bern-zurich-via-olten",
        "plan_trip-basel-lugano-transfer10",
        "plan_trip-ambiguous-from",
        "plan_trip-unknown-from",
        "get_departures-bern-arrivals",
        "get_departures-zurich-central-tram",
    ];

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Json_MatchesSnapshot(string name)
    {
        var json = await RunAsync(name, Json);

        // Indented for review; the tool itself writes one line.
        Assert.DoesNotContain('\n', json);
        Snapshot.Match($"json-{name}", JsonNode.Parse(json)!.ToJsonString(Pretty).ReplaceLineEndings("\n"), "json");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Json_HasTheSameValuesAsCompact(string name)
    {
        var compact = await RunAsync(name, PerronVariant.Default);
        var json = await RunAsync(name, Json);

        foreach (Match id in StopId().Matches(compact))
        {
            Assert.Contains($"\"{id.Value}\"", json);
        }

        foreach (Match time in ClockTime().Matches(compact))
        {
            Assert.Contains($"T{time.Value}\"", json);
        }

        foreach (Match platform in Platform().Matches(compact))
        {
            Assert.Contains($"\"platform\":\"{platform.Groups["planned"].Value}\"", json);
        }

        foreach (Match delay in Delay().Matches(compact))
        {
            Assert.Contains($"\"delay_min\":{delay.Groups["minutes"].Value}", json);
        }

        Assert.Equal(compact.Contains("Ask which one is meant", StringComparison.Ordinal), json.Contains("Ask which one is meant", StringComparison.Ordinal));
        Assert.EndsWith("\"source\":\"Fahrplandaten: opentransportdata.swiss\"}", json);
    }

    private static Task<string> RunAsync(string name, PerronVariant variant)
    {
        var token = TestContext.Current.CancellationToken;
        return name switch
        {
            "find_stops-buchs" => Find(variant, "Buchs", null, null),
            "find_stops-coordinates" => Find(variant, null, 47.378177, 8.540192),
            "plan_trip-zurich-bern-now" => Plan(variant, "connections-zurich-bern-now.json", "8503000", "8507000", null, null),
            "plan_trip-buchs-ambiguous" => Plan(variant, "connections-buchs-ambiguous.json", "8502123", "8503000", null, "2026-11-26T08:00"),
            "plan_trip-bern-zurich-via-olten" => Plan(variant, "connections-bern-zurich-via-olten.json", "8507000", "8503000", ["8500218"], "2026-11-26T08:00"),
            "plan_trip-basel-lugano-transfer10" => Plan(variant, "connections-basel-lugano-transfer10.json", "8500010", "8505300", null, "2026-11-26T08:00", 10),
            "plan_trip-ambiguous-from" => Plan(variant, "connections-zurich-bern-now.json", "Buchs", "8503000", null, null),
            "plan_trip-unknown-from" => Plan(variant, "connections-unknown-from.json", "8507000", "8503000", null, "2026-11-26T08:00"),
            "get_departures-bern-arrivals" => Board(variant, "stationboard-bern-arrivals.json", "Bern", BoardType.Arrival, null),
            "get_departures-zurich-central-tram" => Board(variant, "stationboard-zurich-central-tram.json", "Zürich, Central", BoardType.Departure, [TransportMode.Tram]),
            _ => throw new ArgumentException(name),
        };

        async Task<string> Find(PerronVariant v, string? query, double? latitude, double? longitude)
        {
            using var transport = new TransportFixture(Router(query is null ? "locations-coordinates-station.json" : "unused"));
            return await FindStopsTool.FindStopsAsync(transport.Client, v, query, latitude, longitude, 5, token);
        }

        async Task<string> Plan(PerronVariant v, string fixture, string from, string to, string[]? via, string? datetime, int? minTransfer = null)
        {
            using var transport = new TransportFixture(Router(fixture));
            return await PlanTripTool.PlanTripAsync(transport.Client, v, from, to, via, datetime, min_transfer_minutes: minTransfer, cancellationToken: token);
        }

        async Task<string> Board(PerronVariant v, string fixture, string stop, BoardType type, TransportMode[]? types)
        {
            using var transport = new TransportFixture(Router(fixture));
            return await GetDeparturesTool.GetDeparturesAsync(transport.Client, RecordedAt, v, stop, null, type, types, 10, token);
        }
    }

    /// <summary>Stop searches by query; every other request gets <paramref name="other"/>.</summary>
    private static FixtureHandler Router(string other) => new(uri =>
    {
        if (!uri.AbsolutePath.EndsWith("/locations", StringComparison.Ordinal))
        {
            return other;
        }

        return HttpUtility.ParseQueryString(uri.Query)["query"] switch
        {
            null => other,
            "Buchs" => "locations-buchs-station.json",
            "Bern" => "locations-bern-station.json",
            "Zürich, Central" => "locations-zurich-central-station.json",
            _ => "locations-unknown.json",
        };
    });

    [GeneratedRegex(@"\b\d{7}\b")]
    private static partial Regex StopId();

    [GeneratedRegex(@"\b\d{2}:\d{2}\b")]
    private static partial Regex ClockTime();

    [GeneratedRegex(@"pl\. (?<planned>[^ \n]+)")]
    private static partial Regex Platform();

    [GeneratedRegex(@"\(\+(?<minutes>\d+) min\)")]
    private static partial Regex Delay();
}
