using System.Web;

using ModelContextProtocol;

using Perron.Mcp.Tools;
using Perron.Mcp.Variants;
using Perron.Transport;

namespace Perron.Mcp.Tests;

public sealed class PlanTripToolTests
{
    /// <summary>
    /// Upper bound per connection block (up to two transfers) in the compact format, in characters: longest recorded
    /// block 315 characters plus about 20 % for delays and platform changes; ≈ 3.5 characters per token gives about
    /// 115 tokens. See plan.md, decision log (token cap per connection).
    /// </summary>
    private const int MaxCharactersPerConnection = 400;

    /// <summary>Snapshot cases: recorded /connections response, tool arguments as the fixture's request.</summary>
    public static TheoryData<string, string, string, string[]?, string?, TimeIs, TransportMode[]?, int> Cases() => new()
    {
        { "zurich-bern-tomorrow-0800", "Zürich HB", "8507000", null, "2026-11-26T08:00", TimeIs.Departure, null, 4 },
        { "zurich-bern-now", "8503000", "8507000", null, null, TimeIs.Departure, null, 4 },
        { "zurich-bern-arrive-0900", "8503000", "8507000", null, "2026-11-26T09:00", TimeIs.Arrival, null, 4 },
        { "bern-zurich-via-olten", "8507000", "8503000", ["8500218"], "2026-11-26T08:00", TimeIs.Departure, null, 3 },
        { "lausanne-stgallen-via-two", "8501120", "8506302", ["8507000", "Zürich HB"], "2026-11-26T08:00", TimeIs.Departure, null, 2 },
        { "zurich-airport-train", "8503000", "8503016", null, "2026-11-26T08:00", TimeIs.Departure, [TransportMode.Train], 3 },
        { "basel-lugano-transfer10", "Basel", "8505300", null, "2026-11-26T08:00", TimeIs.Departure, null, 3 },
        { "geneve-lausanne", "Genève", "8501120", null, "2026-11-26T08:00", TimeIs.Departure, null, 3 },
        { "buchs-ambiguous", "8502123", "8503000", null, "2026-11-26T08:00", TimeIs.Departure, null, 3 },
        { "unknown-from", "8507000", "8503000", null, "2026-11-26T08:00", TimeIs.Departure, null, 4 },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Result_MatchesSnapshot_AndStaysUnderTheTokenCap(
        string fixture, string from, string to, string[]? via, string? datetime, TimeIs timeIs, TransportMode[]? types, int limit)
    {
        using var transport = new TransportFixture(Router($"connections-{fixture}.json"));

        var result = await PlanAsync(transport, from, to, via, datetime, timeIs, types, limit);

        Snapshot.Match($"plan_trip-{fixture}", result);
        foreach (var block in ConnectionBlocks(result))
        {
            Assert.True(block.Length <= MaxCharactersPerConnection, $"{fixture}: connection block has {block.Length} characters:\n{block}");
        }
    }

    [Fact]
    public async Task Request_UsesResolvedIds_AndAllOptions()
    {
        using var transport = new TransportFixture(Router("connections-lausanne-stgallen-via-two.json"));

        await PlanAsync(transport, "8501120", "8506302", ["8507000", "Zürich HB"], "2026-11-26T08:00", TimeIs.Arrival, [TransportMode.Train, TransportMode.Bus, TransportMode.Train], 2);

        var request = Assert.Single(transport.Handler.Requests, uri => uri.AbsolutePath.EndsWith("/connections", StringComparison.Ordinal));
        var query = HttpUtility.ParseQueryString(request.Query);
        Assert.Equal("8501120", query["from"]);
        Assert.Equal("8506302", query["to"]);
        Assert.Equal("8507000,8503000", query["via[]"]);
        Assert.Equal("2026-11-26", query["date"]);
        Assert.Equal("08:00", query["time"]);
        Assert.Equal("1", query["isArrivalTime"]);
        Assert.Equal("train,bus", query["transportations[]"]);
        Assert.Equal("2", query["limit"]);
    }

    [Fact]
    public async Task Ids_NeedNoStopSearch()
    {
        using var transport = new TransportFixture(Router("connections-zurich-bern-now.json"));

        await PlanAsync(transport, "8503000", " 8507000 ");

        var request = Assert.Single(transport.Handler.Requests);
        Assert.EndsWith("/connections", request.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AmbiguousName_ReturnsCandidates_InsteadOfConnections()
    {
        using var transport = new TransportFixture(Router("connections-zurich-bern-now.json"));

        var result = await PlanAsync(transport, "Buchs", "8503000");

        Snapshot.Match("plan_trip-ambiguous-from", result);
        Assert.DoesNotContain(transport.Handler.Requests, uri => uri.AbsolutePath.EndsWith("/connections", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SeveralAmbiguousNames_AreListedTogether()
    {
        using var transport = new TransportFixture(Router("connections-zurich-bern-now.json"));

        var result = await PlanAsync(transport, "Buchs", "Zürich", ["Zürich HB"]);

        Assert.Contains("\"Buchs\" (from) matches several stops", result);
        Assert.Contains("\"Zürich\" (to) matches several stops", result);
        Assert.Contains("8503006 | Zürich Oerlikon | train", result);
        Assert.Single(result.Split('\n'), line => line.StartsWith("Ask which one is meant", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Xyzzyplatz", "No stop found for \"Xyzzyplatz\" (from)")]
    [InlineData("Genf", "No stop is named \"Genf\" (from). Similar stops (ID name): 8590049 Bern, Galgenfeld;")]
    public async Task UnknownName_IsAToolError_WithAHint(string from, string message)
    {
        using var transport = new TransportFixture(Router("connections-zurich-bern-now.json"));

        var error = await Assert.ThrowsAsync<McpException>(() => PlanAsync(transport, from, "8507000"));

        Assert.StartsWith(message, error.Message);
        Assert.DoesNotContain(transport.Handler.Requests, uri => uri.AbsolutePath.EndsWith("/connections", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UnknownName_SuggestsTheRightStop_AmongSimilarOnes()
    {
        using var transport = new TransportFixture(Router("connections-zurich-bern-now.json"));

        var error = await Assert.ThrowsAsync<McpException>(() => PlanAsync(transport, "Genf", "8507000"));

        Assert.Contains("8501008 Genève", error.Message);
    }

    [Theory]
    [InlineData("", "8507000", null, null, 4, "Give both from and to")]
    [InlineData("8503000", "  ", null, null, 4, "Give both from and to")]
    [InlineData("8503000", "8507000", "8500218,8500010,8505000", null, 4, "at most 2 via stops")]
    [InlineData("8503000", "8507000", null, "morgen 8 Uhr", 4, "is not a date and time")]
    [InlineData("8503000", "8507000", null, "2026-11-26", 4, "is not a date and time")]
    [InlineData("8503000", "8507000", null, null, 0, "limit must be between 1 and 8")]
    [InlineData("8503000", "8507000", null, null, 9, "limit must be between 1 and 8")]
    public async Task InvalidInput_IsAToolError_WithAHint_AndCallsNoApi(
        string from, string to, string? via, string? datetime, int limit, string hint)
    {
        using var transport = new TransportFixture(Router("connections-zurich-bern-now.json"));

        var error = await Assert.ThrowsAsync<McpException>(
            () => PlanAsync(transport, from, to, via?.Split(','), datetime, limit: limit));

        Assert.Contains(hint, error.Message);
        Assert.Empty(transport.Handler.Requests);
    }

    [Theory]
    [InlineData("2026-11-26T08:00", "2026-11-26T08:00:00+01:00")]
    [InlineData("2026-11-26 08:00", "2026-11-26T08:00:00+01:00")]
    [InlineData("2026-07-01T08:00:30", "2026-07-01T08:00:30+02:00")]
    [InlineData("2026-11-26T07:00:00Z", "2026-11-26T08:00:00+01:00")]
    [InlineData("2026-11-26T09:00:00+02:00", "2026-11-26T08:00:00+01:00")]
    public void DateTime_IsLocalSwissTime_OrConvertedToIt(string input, string expected) =>
        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), ToolText.ParseDateTime(input));

    /// <summary>Delays, platform changes, walks and arrivals after midnight are rare in the recorded responses.</summary>
    [Fact]
    public void Format_ShowsDelays_PlatformChanges_Walks_AndNextDayArrivals()
    {
        var zurich = new Location("8503000", "Zürich HB", null, null, "train");
        var central = new Location("8588078", "Zürich, Central", null, null, "tram");
        var bellevue = new Location("8591105", "Zürich, Bellevue", null, null, "tram");
        static DateTimeOffset At(int day, int hour, int minute) => new(2026, 11, day, hour, minute, 0, TimeSpan.FromHours(1));

        var connection = new Connection(
            new Checkpoint(zurich, null, At(26, 23, 40), 2, "31", new Prognosis(null, At(26, 23, 42), "32")),
            new Checkpoint(bellevue, At(27, 0, 5), null, null, null, null),
            TimeSpan.FromMinutes(25),
            1,
            ["S 4", "T 4"],
            [
                new Section(
                    new Checkpoint(zurich, null, At(26, 23, 40), 2, "31", new Prognosis(null, At(26, 23, 42), "32")),
                    new Checkpoint(zurich, At(26, 23, 48), null, 1, "21", null),
                    new Journey("S", "4", "SZU", "Sihlwald"),
                    null),
                new Section(
                    new Checkpoint(zurich, null, At(26, 23, 48), null, null, null),
                    new Checkpoint(central, At(26, 23, 53), null, null, null, null),
                    null,
                    new Walk(TimeSpan.FromMinutes(5))),
                new Section(
                    new Checkpoint(central, null, At(26, 23, 58), null, null, null),
                    new Checkpoint(bellevue, At(27, 0, 5), null, null, null, null),
                    new Journey("T", "4", "VBZ", "Zürich, Tiefenbrunnen"),
                    null),
            ]);

        var lines = ConnectionFormat.Lines(connection, 1).ToList();

        Assert.Equal(
            [
                "1) Thu 26.11.2026 23:40 → Fri 27.11. 00:05 | 25 min | 1 transfer",
                "   S 4 to Sihlwald: Zürich HB 23:40 pl. 31 (now 32) (+2 min) → Zürich HB 23:48 pl. 21 (+1 min)",
                "   walk 5 min to Zürich, Central",
                "   transfer 10 min incl. walk",
                "   T 4 to Zürich, Tiefenbrunnen: Zürich, Central 23:58 → Zürich, Bellevue Fri 27.11. 00:05",
            ],
            lines);
    }

    /// <summary>Routes /locations by query to the recorded stop searches and /connections to <paramref name="connections"/>.</summary>
    private static FixtureHandler Router(string connections) => new(uri =>
    {
        if (!uri.AbsolutePath.EndsWith("/locations", StringComparison.Ordinal))
        {
            return connections;
        }

        return HttpUtility.ParseQueryString(uri.Query)["query"] switch
        {
            "Zürich HB" => "locations-zurich-hb.json",
            "Zürich" => "locations-zurich-station.json",
            "Buchs" => "locations-buchs-station.json",
            "Basel" => "locations-basel-station.json",
            "Fribourg" => "locations-fribourg-station.json",
            "Genf" => "locations-genf-station.json",
            "Genève" => "locations-geneve.json",
            _ => "locations-unknown.json",
        };
    });

    private static IEnumerable<string> ConnectionBlocks(string result)
    {
        var block = new List<string>();
        foreach (var line in result.Split('\n'))
        {
            if (char.IsDigit(line.FirstOrDefault()) || !line.StartsWith(' '))
            {
                if (block.Count > 0)
                {
                    yield return string.Join('\n', block);
                }

                block = char.IsDigit(line.FirstOrDefault()) ? [line] : [];
                continue;
            }

            if (block.Count > 0)
            {
                block.Add(line);
            }
        }

        if (block.Count > 0)
        {
            yield return string.Join('\n', block);
        }
    }

    private static Task<string> PlanAsync(
        TransportFixture transport,
        string from,
        string to,
        string[]? via = null,
        string? datetime = null,
        TimeIs timeIs = TimeIs.Departure,
        TransportMode[]? types = null,
        int limit = PlanTripTool.DefaultLimit) =>
        PlanTripTool.PlanTripAsync(
            transport.Client, PerronVariant.Default, from, to, via, datetime, timeIs, types, limit, TestContext.Current.CancellationToken);
}
