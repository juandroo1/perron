using System.CommandLine;

using Anthropic;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Perron.Bench.Checks;
using Perron.Bench.Registry;
using Perron.Bench.Run;
using Perron.Bench.Scoring;
using Perron.Bench.Settings;
using Perron.Bench.Tasks;
using Perron.Transport;

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
        static Option<string[]> ServersOption(string verb) => new("--servers")
        {
            Description = $"Servers to {verb}: '{AllServers}' or a list of IDs from bench/servers.json.",
            AllowMultipleArgumentsPerToken = true,
            DefaultValueFactory = _ => [AllServers],
        };

        // check
        var checkServers = ServersOption("check");
        var expectToolsOption = new Option<string[]>("--expect-tools")
        {
            Description = "Expected tool names (exact set); the check fails if a server's tools differ.",
            AllowMultipleArgumentsPerToken = true,
        };

        var check = new Command("check", "Connect to the MCP servers of the registry, list their tools, hash tools/list and count the tokens of the tool definitions.");
        check.Options.Add(checkServers);
        check.Options.Add(expectToolsOption);
        check.SetAction((parseResult, cancellationToken) => WithContextAsync(
            configuration, configureServices, error, parseResult.GetValue(checkServers), cancellationToken,
            context =>
            {
                var command = ActivatorUtilities.CreateInstance<CheckCommand>(context.Services, context.TokenCounter);
                return command.RunAsync(
                    context.Registry, context.ServerIds, SplitValues(parseResult.GetValue(expectToolsOption)), output, cancellationToken);
            }));

        // run
        var runServers = ServersOption("run the tasks on");
        var suiteOption = new Option<string>("--suite")
        {
            Description = $"Task suite: {TaskSuites.Smoke} (dev tasks marked smoke), {TaskSuites.Full} (all dev tasks) or {TaskSuites.Test} (held-out set, final comparison only).",
            DefaultValueFactory = _ => TaskSuites.Smoke,
        };
        suiteOption.AcceptOnlyFromAmong([.. TaskSuites.Names]);
        var repeatsOption = new Option<int>("--repeats")
        {
            Description = "Runs per task and server.",
            DefaultValueFactory = _ => 1,
        };
        var modelOption = new Option<string>("--model")
        {
            Description = "main (model of bench.json) or control (control_model).",
            DefaultValueFactory = _ => "main",
        };
        modelOption.AcceptOnlyFromAmong("main", "control");
        var dryRunOption = new Option<bool>("--dry-run") { Description = "Estimate model calls and cost without calling the model." };
        var seedOption = new Option<int?>("--seed") { Description = "Seed for the order of the servers (default: random, recorded in the manifest)." };

        var run = new Command("run", "Run the tasks of a suite on the servers in the agent loop and write traces to bench/results.");
        foreach (var option in new Option[] { suiteOption, runServers, repeatsOption, modelOption, dryRunOption, seedOption })
        {
            run.Options.Add(option);
        }

        run.SetAction((parseResult, cancellationToken) => WithContextAsync(
            configuration, configureServices, error, parseResult.GetValue(runServers), cancellationToken,
            async context =>
            {
                var repeats = parseResult.GetValue(repeatsOption);
                if (repeats < 1)
                {
                    await error.WriteLineAsync("--repeats must be at least 1.");
                    return ExitCodes.UsageError;
                }

                TaskSuite suite;
                try
                {
                    suite = TaskSuites.Load(context.Directory.Directory, parseResult.GetValue(suiteOption)!);
                }
                catch (InvalidDataException ex)
                {
                    await error.WriteLineAsync($"Invalid configuration: {ex.Message}");
                    return ExitCodes.UsageError;
                }

                var options = new RunOptions(
                    suite.Name,
                    context.ServerIds,
                    repeats,
                    parseResult.GetValue(modelOption) == "control",
                    parseResult.GetValue(dryRunOption),
                    parseResult.GetValue(seedOption));
                var model = context.Services.GetService<IModelClient>() ?? CreateModelClient(context.Configuration);
                var command = ActivatorUtilities.CreateInstance<RunCommand>(context.Services, context.TokenCounter);
                return await command.RunAsync(
                    context.Settings, context.Registry, suite, context.Directory.Directory, options, model, output, cancellationToken);
            }));

        // rescore
        var runsArgument = new Argument<string[]>("runs")
        {
            Description = "Run IDs under bench/results, or 'all' (default).",
            Arity = ArgumentArity.ZeroOrMore,
            DefaultValueFactory = _ => ["all"],
        };
        var rescore = new Command("rescore", "Score stored traces again with the current rules, without model or API calls.");
        rescore.Arguments.Add(runsArgument);
        rescore.SetAction(async (parseResult, cancellationToken) =>
        {
            using var host = BuildHost(configuration, configureServices);
            var directory = host.Services.GetRequiredService<IOptions<BenchDirectoryOptions>>().Value;
            var runs = SplitValues(parseResult.GetValue(runsArgument));
            return await RescoreCommand.RunAsync(directory.Directory, runs.Count == 0 ? ["all"] : runs, output, error, cancellationToken);
        });

        var root = new RootCommand("Perron Bench: runs agent tasks against MCP servers and compares the results.");
        root.Subcommands.Add(check);
        root.Subcommands.Add(run);
        root.Subcommands.Add(rescore);
        return root;
    }

    /// <summary>Starts the host, loads registry and settings, resolves the requested servers, then runs the command.</summary>
    private static async Task<int> WithContextAsync(
        IReadOnlyDictionary<string, string?>? configuration,
        Action<IServiceCollection>? configureServices,
        TextWriter error,
        string[]? servers,
        CancellationToken cancellationToken,
        Func<CommandContext, Task<int>> command)
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

            var requested = SplitValues(servers);
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
            return await command(new CommandContext(host.Services, config, directory, registry, settings, serverIds, tokenCounter));
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
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

        builder.Services.AddHttpClient(McpServerConnector.HttpClientName).AddStandardResilienceHandler();
        builder.Services.AddTransient<McpServerConnector>();
        builder.Services.AddTransient<McpServerCheck>();
        builder.Services.AddSingleton(TimeProvider.System);

        // transport.opendata.ch for the oracle checks (TRANSPORT_API_BASE), with the same resilience and cache as Perron.
        builder.Services.AddTransportApiClient();
        builder.Services.AddTransient<OracleFetcher>();

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

    /// <summary>Claude over the Messages API, or <c>null</c> without ANTHROPIC_API_KEY.</summary>
    private static AnthropicModelClient? CreateModelClient(IConfiguration configuration)
    {
        var key = configuration["ANTHROPIC_API_KEY"];
        return string.IsNullOrWhiteSpace(key) ? null : new AnthropicModelClient(new AnthropicClient { ApiKey = key.Trim() });
    }

    private static List<string> SplitValues(string[]? values) =>
        [.. (values ?? []).SelectMany(value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))];

    private sealed record CommandContext(
        IServiceProvider Services,
        IConfiguration Configuration,
        BenchDirectoryOptions Directory,
        ServerRegistry Registry,
        BenchSettings Settings,
        List<string> ServerIds,
        IToolTokenCounter TokenCounter);
}
