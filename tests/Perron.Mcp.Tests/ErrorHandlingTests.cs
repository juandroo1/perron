using System.Net;
using System.Text;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

using Perron.Mcp.Tools;
using Perron.Transport;

namespace Perron.Mcp.Tests;

/// <summary>
/// AK-4.2: failures of transport.opendata.ch become tool errors that say what to do, and the server keeps working.
/// Runs the real client (resilience, cache) behind Perron MCP over HTTP; the API is replaced by a scripted handler.
/// </summary>
public sealed class ErrorHandlingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TimetableMessage =
        "No timetable data for this date. transport.opendata.ch has the timetable from 15.11.2026 to 14.04.2027. "
        + "Ask for a date in that range; there is no data further ahead or in the past.";

    private const string ThrottledMessage =
        "transport.opendata.ch is busy and rejected the request (HTTP 429). "
        + "Wait about 30 seconds before trying again, and do not repeat the same request right away.";

    private const string UnavailableMessage =
        "transport.opendata.ch is not available right now (timeout, network or server error). Try again in a minute. "
        + "If it keeps failing, tell the user the timetable service is unavailable; do not guess times.";

    [Fact]
    public async Task OutsideTimetable_NamesTheWindow_AndTheServerKeepsWorking()
    {
        // First /connections answer: the recorded 400 for a date outside the timetable; then a normal answer.
        var handler = new ScriptedHandler((uri, connectionCalls) => connectionCalls == 1
            ? Fixture("connections-past-date.json", HttpStatusCode.BadRequest)
            : Fixture("connections-zurich-bern-tomorrow-0800.json"));
        await using var client = await ConnectAsync(handler);

        var failed = await CallAsync(client, "plan_trip", new() { ["from"] = "8503000", ["to"] = "8507000", ["datetime"] = "2020-01-01T08:00" });
        var next = await CallAsync(client, "plan_trip", new() { ["from"] = "8503000", ["to"] = "8507000", ["datetime"] = "2026-11-26T08:00" });

        AssertToolError(failed, TimetableMessage);
        Assert.NotEqual(true, next.IsError);
        Assert.StartsWith("Connections ", Text(next));
    }

    [Fact]
    public async Task Throttled_AsksToWait()
    {
        // Synthetic 429: the real API was not provoked (plan.md, decision log); body as documented, not recorded.
        var handler = new ScriptedHandler((_, _) => Json("""{"errors":[{"message":"Too Many Requests"}]}""", HttpStatusCode.TooManyRequests));
        await using var client = await ConnectAsync(handler);

        var result = await CallAsync(client, "get_departures", new() { ["stop"] = "8507000" });

        AssertToolError(result, ThrottledMessage);
    }

    [Fact]
    public async Task ServerError_IsRetriedOnce_ThenTellsTheAgentNotToGuess()
    {
        var handler = new ScriptedHandler((_, _) => Json("Service Unavailable", HttpStatusCode.ServiceUnavailable));
        await using var client = await ConnectAsync(handler);

        var result = await CallAsync(client, "find_stops", new() { ["query"] = "Bern" });

        AssertToolError(result, UnavailableMessage);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task NetworkFailure_IsUnavailable()
    {
        var handler = new ScriptedHandler((_, _) => throw new HttpRequestException("No such host is known."));
        await using var client = await ConnectAsync(handler);

        var result = await CallAsync(client, "get_departures", new() { ["stop"] = "8507000", ["type"] = "arrival" });

        AssertToolError(result, UnavailableMessage);
    }

    [Fact]
    public async Task OtherBadRequest_PassesTheApiMessageOn()
    {
        var handler = new ScriptedHandler((_, _) => Json("""{"errors":[{"message":"Invalid parameter via."}]}""", HttpStatusCode.BadRequest));
        await using var client = await ConnectAsync(handler);

        var result = await CallAsync(client, "plan_trip", new() { ["from"] = "8503000", ["to"] = "8507000", ["via"] = new[] { "8500218" } });

        AssertToolError(
            result,
            "transport.opendata.ch did not accept the request: Invalid parameter via. Check the stops, date and time, then try again.");
    }

    [Fact]
    public void OutsideTimetable_WithoutAWindow_StillExplains()
    {
        var message = TransportErrors.Message(new TransportApiException(TransportApiErrorKind.OutsideTimetable, 400, "outside of loaded timetable window"));

        Assert.StartsWith("No timetable data for this date. transport.opendata.ch has the timetable only for a few months around today.", message);
    }

    [Fact]
    public void LongApiMessages_AreShortened()
    {
        var message = TransportErrors.Message(new TransportApiException(TransportApiErrorKind.InvalidRequest, 400, new string('x', 500)));

        Assert.Contains(new string('x', 200) + " …. Check the stops", message);
        Assert.DoesNotContain(new string('x', 201), message);
    }

    private static void AssertToolError(CallToolResult result, string message)
    {
        Assert.True(result.IsError);
        Assert.Equal(message, Text(result));
    }

    private static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    private static HttpResponseMessage Fixture(string file, HttpStatusCode status = HttpStatusCode.OK) =>
        Json(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "transport-opendata", file)), status);

    private static HttpResponseMessage Json(string body, HttpStatusCode status) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static Task<CallToolResult> CallAsync(McpClient client, string tool, Dictionary<string, object?> arguments) =>
        client.CallToolAsync(tool, arguments, cancellationToken: TestContext.Current.CancellationToken).AsTask();

    private async Task<McpClient> ConnectAsync(ScriptedHandler handler)
    {
        var app = factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.ConfigureHttpClientDefaults(http => http.ConfigurePrimaryHttpMessageHandler(() => handler));

            // One retry without waiting keeps the tests fast but still runs the resilience pipeline.
            services.Configure<TransportApiOptions>(options =>
            {
                options.MaxRetryAttempts = 1;
                options.RetryBaseDelay = TimeSpan.Zero;
            });
        }));

        var http = app.CreateClient();
        var transport = new HttpClientTransport(
            new HttpClientTransportOptions { Endpoint = new Uri(http.BaseAddress!, "/mcp"), TransportMode = HttpTransportMode.StreamableHttp },
            http,
            null,
            true);
        return await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
    }

    /// <summary>Answers every request from a script; counts all calls and the calls to /connections.</summary>
    private sealed class ScriptedHandler(Func<Uri, int, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private int calls;
        private int connectionCalls;

        public int Calls => calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref calls);
            var uri = request.RequestUri!;
            var connections = uri.AbsolutePath.EndsWith("/connections", StringComparison.Ordinal)
                ? Interlocked.Increment(ref connectionCalls)
                : 0;
            return Task.FromResult(respond(uri, connections));
        }
    }
}
