using Perron.Mcp;

if (args.Contains("--healthcheck"))
{
    // One-shot probe for the Docker healthcheck; Docker handles retries, so no resilience pipeline here.
    using var probeClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
    return await HealthProbe.RunAsync(probeClient, HealthProbe.DefaultUrl);
}

if (args.Contains("--stdio"))
{
    var hostBuilder = Host.CreateApplicationBuilder(args);

    // stdout carries the MCP protocol; all logs must go to stderr.
    hostBuilder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

    hostBuilder.Services
        .AddPerronMcpServer()
        .WithStdioServerTransport();

    await hostBuilder.Build().RunAsync();
    return 0;
}

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddPerronMcpServer()
    .WithHttpTransport();

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapMcp("/mcp");
app.MapHealthChecks("/health");

await app.RunAsync();
return 0;
