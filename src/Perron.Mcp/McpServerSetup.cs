using System.Reflection;

using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Perron.Mcp.Tools;
using Perron.Mcp.Variants;
using Perron.Transport;

namespace Perron.Mcp;

/// <summary>MCP server registration shared by the HTTP and stdio transports.</summary>
public static class McpServerSetup
{
    public static IMcpServerBuilder AddPerronMcpServer(this IServiceCollection services)
    {
        services.AddTransportApiClient();

        // Variant from PERRON_TOOLS, PERRON_DESCRIPTIONS, PERRON_FORMAT; invalid values stop the start (AK-5.1).
        services.AddOptions<PerronVariantOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                options.Tools = Setting(configuration, PerronVariantOptions.ToolsVariable) ?? options.Tools;
                options.Descriptions = Setting(configuration, PerronVariantOptions.DescriptionsVariable) ?? options.Descriptions;
                options.Format = Setting(configuration, PerronVariantOptions.FormatVariable) ?? options.Format;
            })
            .ValidateOnStart();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidateOptions<PerronVariantOptions>, PerronVariantOptionsValidator>());
        services.AddSingleton(provider => provider.GetRequiredService<IOptions<PerronVariantOptions>>().Value.ToVariant());

        // Name and tools depend on the variant, which is known once the configuration is read.
        services.AddOptions<McpServerOptions>()
            .Configure<IServiceProvider>((options, provider) =>
            {
                var variant = provider.GetRequiredService<PerronVariant>();
                options.ServerInfo = new Implementation { Name = variant.ServerName, Version = GetVersion() };
                options.ToolCollection ??= new McpServerPrimitiveCollection<McpServerTool>();
                foreach (var tool in ToolCatalog.Create(variant, provider))
                {
                    options.ToolCollection.Add(tool);
                }
            });

        return services
            .AddMcpServer()
            .WithRequestFilters(filters => filters
                .AddCallToolFilter(ArgumentFilter.Wrap)
                .AddCallToolFilter(TransportErrorFilter.Wrap));
    }

    private static string? Setting(IConfiguration configuration, string variable) =>
        string.IsNullOrWhiteSpace(configuration[variable]) ? null : configuration[variable]!.Trim();

    private static string GetVersion()
    {
        var assembly = typeof(McpServerSetup).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        // Strip the "+<commit>" suffix the SDK appends to the informational version.
        return informational?.Split('+')[0] ?? assembly.GetName().Version?.ToString() ?? "0.0.0";
    }
}
