using System.Net;

namespace Perron.Transport.Tests;

public class ErrorAndResilienceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DateOutsideTimetable_IsOutsideTimetable_AndNotRetried()
    {
        using var fixture = new ClientFixture(FakeHandler.Fixture("connections-past-date.json", HttpStatusCode.BadRequest));

        var error = await Assert.ThrowsAsync<TransportApiException>(
            () => fixture.Client.GetConnectionsAsync(new ConnectionQuery("Zürich HB", "Bern"), Ct));

        Assert.Equal(TransportApiErrorKind.OutsideTimetable, error.Kind);
        Assert.Equal(400, error.StatusCode);
        Assert.Contains("outside of loaded timetable window", error.Message);
        Assert.Equal(1, fixture.Handler.Calls);
    }

    [Fact]
    public async Task OtherBadRequest_IsInvalidRequest()
    {
        using var fixture = new ClientFixture(FakeHandler.Always(
            () => Fixtures.Json("""{"errors":[{"message":"something else"}]}""", HttpStatusCode.BadRequest)));

        var error = await Assert.ThrowsAsync<TransportApiException>(
            () => fixture.Client.GetConnectionsAsync(new ConnectionQuery("A", "B"), Ct));

        Assert.Equal(TransportApiErrorKind.InvalidRequest, error.Kind);
        Assert.Equal("something else", error.Message);
    }

    [Fact]
    public async Task ServerError_IsUnavailable_AfterRetries()
    {
        using var fixture = new ClientFixture(
            FakeHandler.Fixture("stationboard-invalid-datetime.json", HttpStatusCode.InternalServerError));

        var error = await Assert.ThrowsAsync<TransportApiException>(
            () => fixture.Client.GetStationboardAsync(new StationboardQuery { Station = "Bern" }, Ct));

        Assert.Equal(TransportApiErrorKind.Unavailable, error.Kind);
        Assert.Equal(500, error.StatusCode);
        Assert.Equal(3, fixture.Handler.Calls); // first attempt + 2 retries
    }

    [Fact]
    public async Task Throttling_IsThrottled_AfterRetries()
    {
        // SYNTHETIC: no real 429 was recorded (throttling is not provoked, plan.md decision log).
        // Body shape follows the API's recorded error format.
        using var fixture = new ClientFixture(FakeHandler.Always(
            () => Fixtures.Json("""{"errors":[{"message":"Too many requests (synthetic test response)"}]}""", HttpStatusCode.TooManyRequests)));

        var error = await Assert.ThrowsAsync<TransportApiException>(
            () => fixture.Client.SearchLocationsAsync(LocationQuery.ByName("Bern"), Ct));

        Assert.Equal(TransportApiErrorKind.Throttled, error.Kind);
        Assert.Equal(429, error.StatusCode);
        Assert.Equal(3, fixture.Handler.Calls);
    }

    [Fact]
    public async Task TransientFailure_IsRetried_ThenSucceeds()
    {
        using var fixture = new ClientFixture(new FakeHandler((_, call, _) => Task.FromResult(call == 1
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : Fixtures.Response("locations-zurich-hb.json"))));

        var locations = await fixture.Client.SearchLocationsAsync(LocationQuery.ByName("Zürich HB"), Ct);

        Assert.Equal("Zürich HB", locations[0].Name);
        Assert.Equal(2, fixture.Handler.Calls);
    }

    [Fact]
    public async Task NetworkFailure_IsUnavailable()
    {
        using var fixture = new ClientFixture(
            new FakeHandler((_, _, _) => throw new HttpRequestException("connection refused")),
            options => options.MaxRetryAttempts = 1);

        var error = await Assert.ThrowsAsync<TransportApiException>(
            () => fixture.Client.SearchLocationsAsync(LocationQuery.ByName("Bern"), Ct));

        Assert.Equal(TransportApiErrorKind.Unavailable, error.Kind);
        Assert.Null(error.StatusCode);
    }

    [Fact]
    public async Task Timeout_IsUnavailable()
    {
        using var fixture = new ClientFixture(
            new FakeHandler(async (_, _, token) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), token);
                return Fixtures.Response("locations-zurich-hb.json");
            }),
            options =>
            {
                options.MaxRetryAttempts = 1;
                options.AttemptTimeout = TimeSpan.FromMilliseconds(200);
                options.TotalTimeout = TimeSpan.FromSeconds(1);
            },
            useFakeTime: false);

        var error = await Assert.ThrowsAsync<TransportApiException>(
            () => fixture.Client.SearchLocationsAsync(LocationQuery.ByName("Bern"), Ct));

        Assert.Equal(TransportApiErrorKind.Unavailable, error.Kind);
    }

    [Fact]
    public async Task CallerCancellation_IsNotWrapped()
    {
        using var fixture = new ClientFixture(new FakeHandler(async (_, _, token) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), token);
            return Fixtures.Response("locations-zurich-hb.json");
        }),
            useFakeTime: false);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Client.SearchLocationsAsync(LocationQuery.ByName("Bern"), cancellation.Token));
    }
}
