var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// MCP endpoint (/mcp), ping tool and /health follow in task 0.2.
app.MapGet("/", () => "Perron MCP");

app.Run();
