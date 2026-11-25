using System.Reflection;

using ModelContextProtocol.Protocol;

using Perron.Mcp.Tools;
using Perron.Transport;

namespace Perron.Mcp;

/// <summary>MCP server registration shared by the HTTP and stdio transports.</summary>
public static class McpServerSetup
{
    public const string ServerName = "perron";

    public static IMcpServerBuilder AddPerronMcpServer(this IServiceCollection services)
    {
        services.AddTransportApiClient();

        return services
            .AddMcpServer(options => options.ServerInfo = new Implementation
            {
                Name = ServerName,
                Version = GetVersion(),
            })
            // Explicit collection: WithTools(typeof(X)) would bind to WithTools<T>(T target) with T = Type.
            .WithTools([typeof(FindStopsTool), typeof(PlanTripTool), typeof(GetDeparturesTool)])
            .WithRequestFilters(filters => filters.AddCallToolFilter(TransportErrorFilter.Wrap));
    }

    private static string GetVersion()
    {
        var assembly = typeof(McpServerSetup).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // Strip the "+<commit>" suffix the SDK appends to the informational version.
        return informational?.Split('+')[0] ?? assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
