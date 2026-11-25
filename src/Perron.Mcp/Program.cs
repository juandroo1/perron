using Perron.Mcp;

if (args.Contains("--stdio"))
{
    var hostBuilder = Host.CreateApplicationBuilder(args);

    // stdout carries the MCP protocol; all logs must go to stderr.
    hostBuilder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

    hostBuilder.Services
        .AddPerronMcpServer()
        .WithStdioServerTransport();

    await hostBuilder.Build().RunAsync();
    return;
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
