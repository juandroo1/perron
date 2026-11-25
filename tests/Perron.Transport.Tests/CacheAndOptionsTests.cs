using System.Net;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Perron.Transport.Tests;

public class CacheAndOptionsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Timetable_IsCachedFor30Seconds()
    {
        using var fixture = new ClientFixture(FakeHandler.Fixture("stationboard-bern-departures.json"));
        var query = new StationboardQuery { Station = "Bern", Limit = 10 };

        await fixture.Client.GetStationboardAsync(query, Ct);
        fixture.Time.Advance(TimeSpan.FromSeconds(29));
        await fixture.Client.GetStationboardAsync(query, Ct);
        Assert.Equal(1, fixture.Handler.Calls);

        fixture.Time.Advance(TimeSpan.FromSeconds(2));
        await fixture.Client.GetStationboardAsync(query, Ct);
        Assert.Equal(2, fixture.Handler.Calls);
    }

    [Fact]
    public async Task Locations_AreCachedFor24Hours()
    {
        using var fixture = new ClientFixture(FakeHandler.Fixture("locations-zurich-hb.json"));
        var query = LocationQuery.ByName("Zürich HB");

        await fixture.Client.SearchLocationsAsync(query, Ct);
        fixture.Time.Advance(TimeSpan.FromHours(23));
        await fixture.Client.SearchLocationsAsync(query, Ct);
        Assert.Equal(1, fixture.Handler.Calls);

        fixture.Time.Advance(TimeSpan.FromHours(2));
        await fixture.Client.SearchLocationsAsync(query, Ct);
        Assert.Equal(2, fixture.Handler.Calls);
    }

    [Fact]
    public async Task DifferentQueries_AreCachedSeparately()
    {
        using var fixture = new ClientFixture(FakeHandler.Fixture("connections-zurich-bern-now.json"));

        await fixture.Client.GetConnectionsAsync(new ConnectionQuery("Zürich HB", "Bern"), Ct);
        await fixture.Client.GetConnectionsAsync(new ConnectionQuery("Zürich HB", "Bern") { Limit = 2 }, Ct);
        await fixture.Client.GetConnectionsAsync(new ConnectionQuery("Zürich HB", "Bern"), Ct);

        Assert.Equal(2, fixture.Handler.Calls);
    }

    [Fact]
    public async Task Errors_AreNotCached()
    {
        using var fixture = new ClientFixture(
            new FakeHandler((_, call, _) => Task.FromResult(call <= 2
                ? new HttpResponseMessage(HttpStatusCode.BadGateway)
                : Fixtures.Response("locations-zurich-hb.json"))),
            options => options.MaxRetryAttempts = 1);
        var query = LocationQuery.ByName("Zürich HB");

        await Assert.ThrowsAsync<TransportApiException>(() => fixture.Client.SearchLocationsAsync(query, Ct));
        var locations = await fixture.Client.SearchLocationsAsync(query, Ct);

        Assert.NotEmpty(locations);
        Assert.Equal(3, fixture.Handler.Calls); // 2 failed attempts, then a fresh request instead of a cached error
    }

    [Fact]
    public async Task BaseAddress_ComesFromTransportApiBase()
    {
        using var fixture = new ClientFixture(
            FakeHandler.Fixture("locations-zurich-hb.json"),
            configuration: new() { [TransportApiOptions.BaseAddressVariable] = "https://example.test/api/v1/" });

        await fixture.Client.SearchLocationsAsync(LocationQuery.ByName("Zürich HB"), Ct);

        Assert.Equal("https://example.test/api/v1/locations?query=Z%C3%BCrich%20HB", fixture.Handler.Requests[0].AbsoluteUri);
    }

    [Fact]
    public void Requests_CarryAUserAgentWithoutPersonalData()
    {
        using var fixture = new ClientFixture(FakeHandler.Fixture("locations-zurich-hb.json"));
        var http = fixture.Services.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(ITransportApiClient));

        Assert.StartsWith("Perron/", http.DefaultRequestHeaders.UserAgent.ToString());
    }

    [Theory]
    [InlineData("https://transport.opendata.ch/v1")] // no trailing slash
    [InlineData("not a url")]
    [InlineData("ftp://transport.opendata.ch/v1/")]
    public void InvalidBaseAddress_FailsValidationAtStart(string baseAddress)
    {
        using var fixture = new ClientFixture(
            FakeHandler.Fixture("locations-zurich-hb.json"),
            configuration: new() { [TransportApiOptions.BaseAddressVariable] = baseAddress });

        // Start-up reports all failing options together (the resilience options read the transport options too).
        var error = Assert.ThrowsAny<Exception>(() => fixture.Services.GetRequiredService<IStartupValidator>().Validate());
        var failures = error is AggregateException aggregate ? aggregate.InnerExceptions : [error];
        Assert.All(failures, failure => Assert.IsType<OptionsValidationException>(failure));
        Assert.Contains("TRANSPORT_API_BASE", failures[0].Message);
    }

    [Fact]
    public void InvalidTimeouts_FailValidation()
    {
        var result = new TransportApiOptionsValidator().Validate(null, new TransportApiOptions
        {
            AttemptTimeout = TimeSpan.FromSeconds(10),
            TotalTimeout = TimeSpan.FromSeconds(5),
        });

        Assert.True(result.Failed);
    }
}
