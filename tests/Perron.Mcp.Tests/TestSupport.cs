using System.Net;
using System.Text;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Perron.Transport;

namespace Perron.Mcp.Tests;

/// <summary>Serves recorded transport.opendata.ch responses (tests/fixtures) and records every request. No network.</summary>
public sealed class FixtureHandler(Func<Uri, string> chooseFixture) : HttpMessageHandler
{
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "transport-opendata");

    public List<Uri> Requests { get; } = [];

    public static FixtureHandler Always(string fixture) => new(_ => fixture);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
        {
            Requests.Add(request.RequestUri!);
        }

        var body = File.ReadAllText(Path.Combine(Directory, chooseFixture(request.RequestUri!)));
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}

/// <summary>The transport client exactly as Perron MCP registers it, with a fixture handler.</summary>
public sealed class TransportFixture : IDisposable
{
    private readonly ServiceProvider provider;

    public TransportFixture(FixtureHandler handler)
    {
        Handler = handler;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddTransportApiClient().ConfigurePrimaryHttpMessageHandler(() => handler);
        provider = services.BuildServiceProvider(validateScopes: true);
    }

    public FixtureHandler Handler { get; }

    public ITransportApiClient Client => provider.GetRequiredService<ITransportApiClient>();

    public void Dispose() => provider.Dispose();
}

/// <summary>A clock that always shows the same instant.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
}
