using System.Net;
using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Perron.Transport.Tests;

/// <summary>Answers requests from a delegate and records every request URI. No network.</summary>
public sealed class FakeHandler(Func<HttpRequestMessage, int, CancellationToken, Task<HttpResponseMessage>> respond)
    : HttpMessageHandler
{
    private int calls;

    public List<Uri> Requests { get; } = [];

    public int Calls => calls;

    public static FakeHandler Always(Func<HttpResponseMessage> response) =>
        new((_, _, _) => Task.FromResult(response()));

    public static FakeHandler Fixture(string file, HttpStatusCode status = HttpStatusCode.OK) =>
        Always(() => Fixtures.Response(file, status));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request.RequestUri!);
        }

        var call = Interlocked.Increment(ref calls);
        return await respond(request, call, cancellationToken);
    }
}

public static class Fixtures
{
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "transport-opendata");

    public static string Read(string file) => File.ReadAllText(Path.Combine(Directory, file));

    public static HttpResponseMessage Response(string file, HttpStatusCode status = HttpStatusCode.OK) =>
        Json(Read(file), status);

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

/// <summary>Builds the client exactly as production does (DI, resilience, cache), with a fake handler and clock.</summary>
public sealed class ClientFixture : IDisposable
{
    private readonly ServiceProvider provider;

    public ClientFixture(
        FakeHandler handler,
        Action<TransportApiOptions>? configure = null,
        Dictionary<string, string?>? configuration = null,
        bool useFakeTime = true)
    {
        Handler = handler;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(configuration ?? []).Build());
        if (useFakeTime)
        {
            // Also drives the resilience pipeline; tests with real timeouts opt out.
            services.AddSingleton<TimeProvider>(Time);
        }

        services.AddTransportApiClient(options =>
            {
                // No real waiting between retries in tests.
                options.RetryBaseDelay = TimeSpan.Zero;
                configure?.Invoke(options);
            })
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        provider = services.BuildServiceProvider(validateScopes: true);
    }

    public FakeHandler Handler { get; }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 11, 25, 16, 0, 0, TimeSpan.Zero));

    public ITransportApiClient Client => provider.GetRequiredService<ITransportApiClient>();

    public IServiceProvider Services => provider;

    public void Dispose() => provider.Dispose();
}
