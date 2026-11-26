using System.Net;
using System.Text;
using System.Text.Json;
using System.Web;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Perron.Bench.Scoring;
using Perron.Bench.Tasks;
using Perron.Transport;

namespace Perron.Bench.Tests;

/// <summary>Oracle queries from the reference time with the task's constraints (AK-9.1), answered from fixtures.</summary>
public sealed class OracleFetcherTests
{
    private static readonly DateTimeOffset Reference = new(2026, 11, 25, 17, 12, 0, TimeSpan.FromHours(1));

    [Fact]
    public async Task EarliestArrival_QueriesFromTheReferenceTime_WithAllConstraints()
    {
        using var api = new Api();

        var data = Assert.Single(await api.FetchAsync("""
            [{"type":"regex","pattern":"x"},
             {"type":"oracle","oracle":"earliest_arrival","tolerance_minutes":1,
              "params":{"from":"Zürich HB","to":"Bern","depart_after":"+1d 08:00","via":["Olten"],"min_transfer_minutes":10,"transport_types":["train"]}}]
            """));

        Assert.Equal(1, data.CheckIndex);
        Assert.Null(data.Error);
        Assert.Equal(4, data.Connections!.Count);
        Assert.Equal(new DateTimeOffset(2026, 11, 26, 8, 2, 0, TimeSpan.FromHours(1)), data.Connections[0].Departure);
        Assert.Equal(new DateTimeOffset(2026, 11, 26, 8, 58, 0, TimeSpan.FromHours(1)), data.Connections[0].Arrival);
        Assert.Equal(["IC 8"], data.Connections[0].Lines);

        var query = HttpUtility.ParseQueryString(Assert.Single(api.Requests).Query);
        Assert.Equal("2026-11-26", query["date"]);
        Assert.Equal("08:00", query["time"]);
        Assert.Equal("Olten", query["via"]);
        Assert.Equal("10", query["minTransferTime"]);
        Assert.Equal("train", query["transportations[]"]);
        Assert.Equal("16", query["limit"]);
        Assert.Null(query["isArrivalTime"]);
    }

    [Fact]
    public async Task ConnectionStartingWithAWalk_KeepsTheFirstVehiclesDeparture()
    {
        using var api = new Api(connections: "connections-interlaken-zermatt-walk-first.json");

        var data = Assert.Single(await api.FetchAsync("""
            [{"type":"oracle","oracle":"earliest_arrival","tolerance_minutes":1,"params":{"from":"Interlaken Ost","to":"Zermatt","depart_after":"+1d 13:00"}}]
            """));

        var best = data.Connections![0];
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 13, 10, 0, TimeSpan.FromHours(1)), best.Departure);
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 13, 21, 0, TimeSpan.FromHours(1)), best.RideDeparture);
        Assert.Null(best.RideArrival);
    }

    [Fact]
    public async Task EarliestArrival_WithoutAWalk_HasNoRideTimes()
    {
        using var api = new Api();

        var data = Assert.Single(await api.FetchAsync("""[{"type":"oracle","oracle":"earliest_arrival","tolerance_minutes":1,"params":{"from":"Zürich HB","to":"Bern","depart_after":"+1d 08:00"}}]"""));

        Assert.All(data.Connections!, connection => Assert.Null(connection.RideDeparture));
    }

    [Fact]
    public async Task LatestDeparture_AsksForArrivals()
    {
        using var api = new Api();

        await api.FetchAsync("""[{"type":"oracle","oracle":"latest_departure","tolerance_minutes":1,"params":{"from":"Bern","to":"Lausanne","arrive_before":"+1d 10:00"}}]""");

        var query = HttpUtility.ParseQueryString(Assert.Single(api.Requests).Query);
        Assert.Equal("1", query["isArrivalTime"]);
        Assert.Equal("10:00", query["time"]);
    }

    [Fact]
    public async Task NextDeparture_ReadsTheBoard_AndFiltersTheLine()
    {
        using var api = new Api();

        var data = Assert.Single(await api.FetchAsync("""[{"type":"oracle","oracle":"next_departure","tolerance_minutes":1,"params":{"stop":"Bern","after":"+1d 08:00","line":"IC 61","transport_type":"train"}}]"""));

        var departures = data.Departures!;
        Assert.Equal(["IC 61", "IC 61"], departures.Select(departure => departure.Line));
        Assert.Equal(new DateTimeOffset(2026, 11, 26, 8, 4, 0, TimeSpan.FromHours(1)), departures[0].Time);
        var query = HttpUtility.ParseQueryString(Assert.Single(api.Requests).Query);
        Assert.Equal("2026-11-26 08:00", query["datetime"]);
        Assert.Equal("train", query["transportations[]"]);
    }

    [Fact]
    public async Task NextDeparture_Towards_UsesDirectConnections()
    {
        using var api = new Api();

        var data = Assert.Single(await api.FetchAsync("""[{"type":"oracle","oracle":"next_departure","tolerance_minutes":1,"params":{"stop":"Zürich HB","after":"+1d 08:00","towards":"Bern"}}]"""));

        // The recorded connections Zürich HB → Bern: three direct, one with a transfer.
        Assert.Equal(["08:02", "08:06", "08:32"], data.Departures!.Select(departure => ClockTimes.Of(departure.Time)));
        Assert.EndsWith("/connections", Assert.Single(api.Requests).AbsolutePath);
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    public async Task Occupancy_IsTheForecastOfTheFirstConnection_ForTheClass(int travelClass, int level)
    {
        using var api = new Api();

        var data = Assert.Single(await api.FetchAsync($$$"""[{"type":"oracle","oracle":"occupancy","tolerance_minutes":0,"params":{"from":"Zürich HB","to":"Bern","depart_after":"+1d 08:00","class":{{{travelClass}}}}}]"""));

        Assert.Equal(level, data.Occupancy);
    }

    [Fact]
    public async Task StopCandidates_AreTheStationsOfTheName()
    {
        using var api = new Api();

        var data = Assert.Single(await api.FetchAsync("""[{"type":"oracle","oracle":"stop_candidates","tolerance_minutes":0,"params":{"name":"Buchs"}}]"""));

        Assert.Equal(["Buchs AG", "Buchs SG", "Buchs SG, Bahnhof"], data.Candidates!.Take(3));
        Assert.Contains("type=station", Assert.Single(api.Requests).Query);
    }

    [Fact]
    public async Task FailedQuery_IsStoredAsAnError()
    {
        using var api = new Api(HttpStatusCode.TooManyRequests);

        var data = Assert.Single(await api.FetchAsync("""[{"type":"oracle","oracle":"earliest_arrival","tolerance_minutes":1,"params":{"from":"A","to":"B","depart_after":"+1d 08:00"}}]"""));

        Assert.StartsWith("TransportApiException:", data.Error);
        Assert.Null(data.Connections);
    }

    [Theory]
    [InlineData("T 4", "4", true)]
    [InlineData("IC 8", "IC 8", true)]
    [InlineData("IC8", "IC 8", true)]
    [InlineData("IC 61", "IC 8", false)]
    [InlineData("S 14", "4", false)]
    [InlineData(null, null, true)]
    public void LineMatches_ComparesLineNames(string? actual, string? wanted, bool matches) =>
        Assert.Equal(matches, OracleFetcher.LineMatches(actual, wanted));

    /// <summary>The transport client as Perron Bench registers it, answered from fixtures; records the requests.</summary>
    private sealed class Api : IDisposable
    {
        private readonly ServiceProvider provider;

        public Api(HttpStatusCode status = HttpStatusCode.OK, string connections = "connections-zurich-bern-tomorrow-0800.json")
        {
            var handler = new Handler(this, status, connections);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddSingleton<TimeProvider>(new FixedClock(Reference));
            services.AddTransportApiClient(options =>
                {
                    options.MaxRetryAttempts = 1;
                    options.RetryBaseDelay = TimeSpan.Zero;
                })
                .ConfigurePrimaryHttpMessageHandler(() => handler);
            provider = services.BuildServiceProvider();
        }

        public List<Uri> Requests { get; } = [];

        public Task<IReadOnlyList<OracleData>> FetchAsync(string checks)
        {
            var task = new BenchTask("dev-x", "?", "de", "trip_simple", ["stops", "trips"], false, 8, JsonDocument.Parse(checks).RootElement.Clone());
            var fetcher = new OracleFetcher(provider.GetRequiredService<ITransportApiClient>(), new FixedClock(Reference));
            return fetcher.FetchAsync(task, Reference, TestContext.Current.CancellationToken);
        }

        public void Dispose() => provider.Dispose();

        private sealed class Handler(Api api, HttpStatusCode status, string connections) : HttpMessageHandler
        {
            private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "transport-opendata");

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var uri = request.RequestUri!;
                lock (api.Requests)
                {
                    api.Requests.Add(uri);
                }

                if (status != HttpStatusCode.OK)
                {
                    return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("""{"errors":[{"message":"busy"}]}""", Encoding.UTF8, "application/json") });
                }

                var file = uri.AbsolutePath switch
                {
                    var path when path.EndsWith("/locations", StringComparison.Ordinal) => "locations-buchs-station.json",
                    var path when path.EndsWith("/stationboard", StringComparison.Ordinal) => "stationboard-bern-tomorrow-0800.json",
                    _ => connections,
                };
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(File.ReadAllText(Path.Combine(Directory, file)), Encoding.UTF8, "application/json"),
                });
            }
        }
    }
}
