using System.CommandLine;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Perron.Bench.Checks;

namespace Perron.Bench;

public static class ExitCodes
{
    public const int Success = 0;
    public const int CheckFailed = 1;
    public const int UsageError = 2;
}

/// <summary>Command-line entry point of Perron Bench.</summary>
public static class BenchCli
{
    private const string AllServers = "all";

    /// <param name="args">Command-line arguments.</param>
    /// <param name="output">Receives the command result.</param>
    /// <param name="error">Receives usage and configuration errors.</param>
    /// <param name="configuration">Extra configuration values (override environment variables), mainly for tests.</param>
    /// <param name="configureServices">Service overrides, mainly for tests.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?>? configuration = null,
        Action<IServiceCollection>? configureServices = null,
        CancellationToken cancellationToken = default)
    {
        var root = BuildCommands(output, error, configuration, configureServices);
        var parseResult = root.Parse(args);

        if (parseResult.Errors.Count > 0)
        {
            foreach (var parseError in parseResult.Errors)
            {
                await error.WriteLineAsync(parseError.Message);
            }

            await error.WriteLineAsync("Run with --help for usage.");
            return ExitCodes.UsageError;
        }

        return await parseResult.InvokeAsync(
            new InvocationConfiguration { Output = output, Error = error }, cancellationToken);
    }

    private static RootCommand BuildCommands(
        TextWriter output,
        TextWriter error,
        IReadOnlyDictionary<string, string?>? configuration,
        Action<IServiceCollection>? configureServices)
    {
        var serversOption = new Option<string[]>("--servers")
        {
            Description = $"Servers to check: '{AllServers}' or a list of IDs (until task 3.1 only: {string.Join(", ", CheckCommand.KnownServers)}).",
            AllowMultipleArgumentsPerToken = true,
            DefaultValueFactory = _ => [AllServers],
        };

        var expectToolsOption = new Option<string[]>("--expect-tools")
        {
            Description = "Expected tool names (exact set); the check fails if the server's tools differ.",
            AllowMultipleArgumentsPerToken = true,
        };

        var check = new Command("check", "Connect to MCP servers and list their tools.");
        check.Options.Add(serversOption);
        check.Options.Add(expectToolsOption);
        check.SetAction(async (parseResult, cancellationToken) =>
        {
            var requested = SplitValues(parseResult.GetValue(serversOption));
            var unknown = requested.Where(id => id != AllServers && !CheckCommand.KnownServers.Contains(id)).ToList();
            if (unknown.Count > 0)
            {
                await error.WriteLineAsync(
                    $"Unknown server(s): {string.Join(", ", unknown)}. " +
                    $"Until the server registry exists (task 3.1) only these are available: {string.Join(", ", CheckCommand.KnownServers)}.");
                return ExitCodes.UsageError;
            }

            List<string> serverIds = requested.Contains(AllServers) ? [.. CheckCommand.KnownServers] : [.. requested.Distinct()];

            using var host = BuildHost(configuration, configureServices);
            try
            {
                await host.StartAsync(cancellationToken);
            }
            catch (OptionsValidationException ex)
            {
                await error.WriteLineAsync($"Invalid configuration: {string.Join(" ", ex.Failures)}");
                return ExitCodes.UsageError;
            }

            try
            {
                var command = host.Services.GetRequiredService<CheckCommand>();
                return await command.RunAsync(
                    serverIds, SplitValues(parseResult.GetValue(expectToolsOption)), output, cancellationToken);
            }
            finally
            {
                await host.StopAsync(CancellationToken.None);
            }
        });

        var root = new RootCommand("Perron Bench: runs agent tasks against MCP servers and compares the results.");
        root.Subcommands.Add(check);
        return root;
    }

    private static IHost BuildHost(
        IReadOnlyDictionary<string, string?>? configuration,
        Action<IServiceCollection>? configureServices)
    {
        // Command-line arguments are parsed by System.CommandLine, not by the host.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = [] });

        if (configuration is not null)
        {
            builder.Configuration.AddInMemoryCollection(configuration);
        }

        // stdout carries the command result; logs go to stderr, and only from Warning up.
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services.AddOptions<PerronEndpointOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                var url = config[PerronEndpointOptions.EnvironmentVariable];
                if (!string.IsNullOrWhiteSpace(url))
                {
                    options.McpUrl = url;
                }
            })
            .ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<PerronEndpointOptions>, PerronEndpointOptionsValidator>();

        builder.Services.AddHttpClient(McpServerCheck.HttpClientName).AddStandardResilienceHandler();
        builder.Services.AddTransient<McpServerCheck>();
        builder.Services.AddTransient<CheckCommand>();

        configureServices?.Invoke(builder.Services);

        return builder.Build();
    }

    private static List<string> SplitValues(string[]? values) =>
        [.. (values ?? []).SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))];
}
