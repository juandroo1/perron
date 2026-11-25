using System.CommandLine;

using Anthropic;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Perron.Bench.Checks;
using Perron.Bench.Registry;
using Perron.Bench.Settings;

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
            Description = $"Servers to check: '{AllServers}' or a list of IDs from bench/servers.json.",
            AllowMultipleArgumentsPerToken = true,
            DefaultValueFactory = _ => [AllServers],
        };

        var expectToolsOption = new Option<string[]>("--expect-tools")
        {
            Description = "Expected tool names (exact set); the check fails if a server's tools differ.",
            AllowMultipleArgumentsPerToken = true,
        };

        var check = new Command("check", "Connect to the MCP servers of the registry, list their tools, hash tools/list and count the tokens of the tool definitions.");
        check.Options.Add(serversOption);
        check.Options.Add(expectToolsOption);
        check.SetAction(async (parseResult, cancellationToken) =>
        {
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
                var config = host.Services.GetRequiredService<IConfiguration>();
                var directory = host.Services.GetRequiredService<IOptions<BenchDirectoryOptions>>().Value;
                ServerRegistry registry;
                BenchSettings settings;
                try
                {
                    registry = ServerRegistry.Load(directory.File(ServerRegistry.FileName));
                    settings = BenchSettings.Load(directory.File(BenchSettings.FileName));
                }
                catch (InvalidDataException ex)
                {
                    await error.WriteLineAsync($"Invalid configuration: {ex.Message}");
                    return ExitCodes.UsageError;
                }

                var requested = SplitValues(parseResult.GetValue(serversOption));
                var known = registry.Servers.Select(server => server.Id).ToList();
                var unknown = requested.Where(id => id != AllServers && !known.Contains(id)).ToList();
                if (unknown.Count > 0)
                {
                    await error.WriteLineAsync(
                        $"Unknown server(s): {string.Join(", ", unknown)}. Servers in bench/servers.json: {string.Join(", ", known)}.");
                    return ExitCodes.UsageError;
                }

                List<string> serverIds = requested.Contains(AllServers) ? known : [.. requested.Distinct()];
                foreach (var server in serverIds.Select(id => registry.Find(id)!))
                {
                    if (server.UrlEnv is { } variable && config[variable] is { } url && !string.IsNullOrWhiteSpace(url)
                        && !(Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"))
                    {
                        await error.WriteLineAsync($"Invalid configuration: {variable} must be an absolute http(s) URL, got '{url}'.");
                        return ExitCodes.UsageError;
                    }
                }

                var tokenCounter = host.Services.GetService<IToolTokenCounter>() ?? CreateTokenCounter(config, settings);
                var command = ActivatorUtilities.CreateInstance<CheckCommand>(host.Services, tokenCounter);
                return await command.RunAsync(
                    registry, serverIds, SplitValues(parseResult.GetValue(expectToolsOption)), output, cancellationToken);
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

        builder.Services.AddOptions<BenchDirectoryOptions>()
            .Configure<IConfiguration>((options, config) =>
            {
                var directory = config[BenchDirectoryOptions.EnvironmentVariable];
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    options.Directory = directory.Trim();
                }
            });

        builder.Services.AddHttpClient(McpServerCheck.HttpClientName).AddStandardResilienceHandler();
        builder.Services.AddTransient<McpServerCheck>();

        configureServices?.Invoke(builder.Services);

        return builder.Build();
    }

    /// <summary>count_tokens with the benchmark model when ANTHROPIC_API_KEY is set, otherwise "n/a" (AK-7.2).</summary>
    private static IToolTokenCounter CreateTokenCounter(IConfiguration configuration, BenchSettings settings)
    {
        var key = configuration["ANTHROPIC_API_KEY"];
        return string.IsNullOrWhiteSpace(key)
            ? new UnavailableToolTokenCounter()
            : new AnthropicToolTokenCounter(new AnthropicClient { ApiKey = key.Trim() }, settings.Model);
    }

    private static List<string> SplitValues(string[]? values) =>
        [.. (values ?? []).SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))];
}
