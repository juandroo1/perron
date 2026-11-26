using System.Globalization;
using System.Text.Json;

using Microsoft.Extensions.Configuration;

using ModelContextProtocol.Client;

using Perron.Bench.Checks;
using Perron.Bench.Registry;
using Perron.Bench.Scoring;
using Perron.Bench.Settings;
using Perron.Bench.Tasks;

using McpTool = ModelContextProtocol.Protocol.Tool;

namespace Perron.Bench.Run;

/// <summary>Options of <c>bench run</c>.</summary>
/// <param name="Control">Use the control model of bench.json instead of the main model.</param>
public sealed record RunOptions(
    string Suite, IReadOnlyList<string> ServerIds, int Repeats, bool Control, bool DryRun, int? Seed, IReadOnlyList<string>? Tasks = null);

/// <summary>
/// <c>bench run</c> (AK-8.1 to AK-8.3): connects to the servers, then runs every task on every server and repeat in the
/// agent loop with the same settings. The reference time is frozen per task; the order of the servers is shuffled per
/// task and repeat (seed in the manifest). Tasks a server cannot cover are recorded without a model call. Writes
/// manifest, traces and runs.jsonl to bench/results/&lt;run-id&gt;/.
/// </summary>
public sealed class RunCommand(
    McpServerConnector connector, IToolTokenCounter tokenCounter, OracleFetcher oracles, IConfiguration configuration, TimeProvider clock)
{
    public async Task<int> RunAsync(
        BenchSettings settings,
        ServerRegistry registry,
        TaskSuite suite,
        string benchDirectory,
        RunOptions options,
        IModelClient? model,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var modelId = options.Control ? settings.ControlModel : settings.Model;
        var pricing = settings.PricingUsdPerMtok[modelId];

        var definitions = new List<ServerDefinition>();
        foreach (var server in options.ServerIds.Select(id => registry.Find(id)!))
        {
            var missing = ServerEnvironment.MissingKeys(server, configuration);
            if (missing.Count == 0)
            {
                definitions.Add(server);
            }
            else if (server.Optional)
            {
                await output.WriteLineAsync($"{server.Id}: skipped, missing {string.Join(", ", missing)} (optional server)");
            }
            else
            {
                await output.WriteLineAsync($"{server.Id}: missing {string.Join(", ", missing)}");
                return ExitCodes.UsageError;
            }
        }

        if (!options.DryRun && model is null)
        {
            await output.WriteLineAsync("ANTHROPIC_API_KEY is not set: bench run needs it (use --dry-run to estimate without it).");
            return ExitCodes.UsageError;
        }

        var sessions = new List<ServerSession>();
        try
        {
            foreach (var server in definitions)
            {
                try
                {
                    sessions.Add(await ConnectAsync(server, cancellationToken));
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    await output.WriteLineAsync($"{server.Id}: not reachable: {ex.GetType().Name}: {ex.Message}");
                    return ExitCodes.CheckFailed;
                }
            }

            var referenceForPrompt = SystemPrompt.ReferenceTime(clock);
            if (options.DryRun)
            {
                await DryRunAsync(settings, suite, options, modelId, pricing, sessions, referenceForPrompt, benchDirectory, output);
                return ExitCodes.Success;
            }

            return await RunTasksAsync(settings, suite, options, modelId, pricing, sessions, benchDirectory, model!, output, cancellationToken);
        }
        finally
        {
            foreach (var session in sessions)
            {
                await session.Client.DisposeAsync();
            }
        }
    }

    private async Task<int> RunTasksAsync(
        BenchSettings settings,
        TaskSuite suite,
        RunOptions options,
        string modelId,
        ModelPricing pricing,
        List<ServerSession> sessions,
        string benchDirectory,
        IModelClient model,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var started = clock.GetUtcNow();
        var seed = options.Seed ?? Random.Shared.Next();
        var random = new Random(seed);
        var runId = $"{started.UtcDateTime:yyyyMMdd-HHmmss}-{suite.Name}";
        var directory = Path.Combine(benchDirectory, "results", runId);
        Directory.CreateDirectory(Path.Combine(directory, "traces"));

        var (sha, dirty) = GitInfo.Read(configuration);
        var manifest = new RunManifest(
            runId,
            started,
            null,
            sha,
            dirty,
            GitInfo.HarnessVersion(),
            suite.Name,
            suite.File,
            suite.Sha256,
            suite.Tasks.Count,
            options.Repeats,
            seed,
            modelId,
            settings.Effort,
            settings.MaxTokens,
            settings.PauseSeconds,
            settings.DefaultMaxToolCalls,
            pricing,
            SystemPrompt.TemplateSha256,
            [.. sessions.Select(session => session.Manifest)],
            JudgePromptSha256: Judge.PromptSha256,
            Judge: settings.Judge,
            Tasks: options.Tasks);
        await WriteManifestAsync(directory, manifest);
        await output.WriteLineAsync($"Run {runId}: {suite.Tasks.Count} tasks × {sessions.Count} servers × {options.Repeats} repeats, model {modelId}, seed {seed}");

        var loop = new AgentLoop(model);

        // The judge uses the main model of bench.json, also when the agent runs with the control model (AK-9.2).
        var judge = new Judge(model, clock);
        var judgePricing = settings.PricingUsdPerMtok[settings.Model];
        var summaries = new List<RunSummary>();
        var pending = suite.Tasks.Count * sessions.Count * options.Repeats;
        foreach (var task in suite.Tasks)
        {
            var reference = SystemPrompt.ReferenceTime(clock);

            // The oracle is asked once per task round, from its reference time, and shared by all servers (AK-9.1).
            var anyCovered = sessions.Any(session => ServerCoverage.Missing(task.CapabilitiesRequired, session.Manifest.CapabilitiesCovered).Count == 0);
            IReadOnlyList<OracleData> oracle = anyCovered ? await oracles.FetchAsync(task, reference, cancellationToken) : [];

            for (var repeat = 1; repeat <= options.Repeats; repeat++)
            {
                foreach (var session in Shuffle(sessions, random))
                {
                    var request = new AgentRunRequest(
                        runId,
                        task,
                        session.Definition.Id,
                        repeat,
                        modelId,
                        settings.Effort,
                        settings.MaxTokens,
                        task.MaxToolCalls ?? settings.DefaultMaxToolCalls,
                        reference,
                        SystemPrompt.Build(reference, session.Instructions),
                        session.Tools,
                        pricing);

                    var missing = ServerCoverage.Missing(task.CapabilitiesRequired, session.Manifest.CapabilitiesCovered);
                    RunTrace trace;
                    if (missing.Count > 0)
                    {
                        trace = AgentLoop.NotCovered(request, missing);
                    }
                    else
                    {
                        trace = await loop.RunAsync(request, new McpToolInvoker(session.Client), cancellationToken);
                        if (trace.Status == RunStatus.InfraError)
                        {
                            // AK-9.5: one more attempt after the pause; if it fails again, the run is excluded from scoring.
                            await output.WriteLineAsync($"{task.Id} {session.Definition.Id} r{repeat}: {trace.StatusDetail}; retrying once");
                            await PauseAsync(settings, cancellationToken);
                            trace = await loop.RunAsync(request, new McpToolInvoker(session.Client), cancellationToken) with { Attempt = 2 };
                        }

                        trace = trace with { Oracle = oracle.Count > 0 ? oracle : null };
                        if (Judge.ChecksOf(trace).Count > 0)
                        {
                            trace = trace with { Judgements = await judge.JudgeAsync(trace, settings.Model, settings.Judge, judgePricing, cancellationToken) };
                        }
                    }

                    var evaluation = Evaluator.Evaluate(trace);
                    trace = trace with { Evaluation = evaluation };

                    await File.WriteAllTextAsync(
                        Path.Combine(directory, "traces", trace.FileName),
                        JsonSerializer.Serialize(trace, TraceJsonContext.Default.RunTrace) + "\n",
                        cancellationToken);
                    var summary = RunSummary.Of(trace);
                    summaries.Add(summary);
                    await File.AppendAllTextAsync(
                        Path.Combine(directory, "runs.jsonl"),
                        JsonSerializer.Serialize(summary, SummaryJsonContext.Default.RunSummary) + "\n",
                        cancellationToken);
                    await File.AppendAllTextAsync(
                        Path.Combine(directory, "scores.jsonl"),
                        JsonSerializer.Serialize(Evaluator.Line(trace, evaluation), SummaryJsonContext.Default.ScoreLine) + "\n",
                        cancellationToken);
                    await output.WriteLineAsync(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{task.Id} {session.Definition.Id} r{repeat}: {trace.Status}, {evaluation.Outcome}{Codes(evaluation)} ({trace.Totals.Turns} turns, {trace.Totals.ToolCalls} tool calls, ${trace.Totals.CostUsd:0.0000})"));

                    pending--;
                    if (trace.Totals.Turns > 0 && pending > 0)
                    {
                        // Spare the transport API between runs (CLAUDE.md, plan.md section 6).
                        await PauseAsync(settings, cancellationToken);
                    }
                }
            }
        }

        var counts = summaries.GroupBy(run => run.Status).OrderBy(group => group.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count());
        var total = summaries.Sum(run => run.CostUsd);
        await WriteManifestAsync(directory, manifest with { FinishedAt = clock.GetUtcNow(), StatusCounts = counts, TotalCostUsd = total });
        await output.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"Done: {string.Join(", ", counts.Select(pair => $"{pair.Key} {pair.Value}"))}; cost ${total:0.0000}; results in {directory}"));

        return counts.ContainsKey(RunStatus.InfraError) ? ExitCodes.CheckFailed : ExitCodes.Success;
    }

    private async Task DryRunAsync(
        BenchSettings settings,
        TaskSuite suite,
        RunOptions options,
        string modelId,
        ModelPricing pricing,
        List<ServerSession> sessions,
        DateTimeOffset reference,
        string benchDirectory,
        TextWriter output)
    {
        var history = DryRun.History(Path.Combine(benchDirectory, "results"));
        await output.WriteLineAsync(
            $"Dry run: suite {suite.Name} ({suite.Tasks.Count} tasks), {options.Repeats} repeats, model {modelId}; no model calls.");

        var estimates = new List<DryRunEstimate>();
        foreach (var session in sessions)
        {
            var covered = suite.Tasks
                .Where(task => ServerCoverage.Missing(task.CapabilitiesRequired, session.Manifest.CapabilitiesCovered).Count == 0)
                .ToList();
            var estimate = DryRun.Estimate(
                new DryRunServer(
                    session.Definition.Id,
                    ToolChars(session.Tools),
                    session.Manifest.ToolTokens,
                    SystemPrompt.Build(reference, session.Instructions),
                    covered),
                options.Repeats,
                pricing,
                settings.DryRunAssumptions,
                history);
            estimates.Add(estimate);
            await output.WriteLineAsync(string.Create(
                CultureInfo.InvariantCulture,
                $"{estimate.ServerId,-15} {estimate.Runs,4} runs ({suite.Tasks.Count - covered.Count} not covered) | {estimate.Turns:0.0} turns/run | {estimate.LlmCalls,5} model calls | input {estimate.InputTokens,9} | output {estimate.OutputTokens,8} | ${estimate.CostUsd:0.0000} | {estimate.Basis}{(estimate.Estimated ? ", tool tokens estimated" : "")}"));
        }

        await output.WriteLineAsync(string.Create(
            CultureInfo.InvariantCulture,
            $"Total: {estimates.Sum(estimate => estimate.Runs)} runs, {estimates.Sum(estimate => estimate.LlmCalls)} model calls, ${estimates.Sum(estimate => estimate.CostUsd):0.0000} (no prompt caching assumed; system prompt and tasks estimated at {DryRun.CharactersPerToken} characters per token)."));
    }

    private async Task<ServerSession> ConnectAsync(ServerDefinition server, CancellationToken cancellationToken)
    {
        var client = await connector.ConnectAsync(
            server, ServerEnvironment.Endpoint(server, configuration), ServerEnvironment.Process(server, configuration), cancellationToken);
        try
        {
            var tools = (await client.ListToolsAsync(cancellationToken: cancellationToken)).Select(tool => tool.ProtocolTool).ToList();
            long? toolTokens = tokenCounter.IsAvailable ? await tokenCounter.CountAsync(tools, cancellationToken) : null;
            var instructions = client.ServerInstructions;
            var (covered, notCovered) = ServerCoverage.Of(server, key => ServerEnvironment.IsSet(configuration, key));

            return new ServerSession(
                server,
                client,
                tools,
                instructions,
                new ManifestServer(
                    server.Id,
                    server.Version,
                    server.Hosted,
                    client.ServerInfo?.Name,
                    client.ServerInfo?.Version,
                    client.NegotiatedProtocolVersion,
                    [.. tools.Select(tool => tool.Name).Order(StringComparer.Ordinal)],
                    ToolListHash.Compute(tools),
                    toolTokens,
                    SystemPrompt.Sha256(instructions ?? ""),
                    covered,
                    [.. notCovered.Select(item => item.Name)]));
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    private Task PauseAsync(BenchSettings settings, CancellationToken cancellationToken) =>
        settings.PauseSeconds > 0 ? Task.Delay(TimeSpan.FromSeconds(settings.PauseSeconds), clock, cancellationToken) : Task.CompletedTask;

    private static string Codes(Evaluation evaluation) =>
        evaluation.Errors.Count == 0 ? "" : $" [{string.Join(", ", evaluation.Errors)}]";

    private static int ToolChars(IReadOnlyList<McpTool> tools) =>
        tools.Sum(tool => tool.Name.Length + (tool.Description?.Length ?? 0) + tool.InputSchema.GetRawText().Length);

    private static List<ServerSession> Shuffle(List<ServerSession> sessions, Random random)
    {
        var order = new List<ServerSession>(sessions);
        for (var i = order.Count - 1; i > 0; i--)
        {
            var j = random.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        return order;
    }

    private static Task WriteManifestAsync(string directory, RunManifest manifest) =>
        File.WriteAllTextAsync(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(manifest, TraceJsonContext.Default.RunManifest) + "\n");

    private sealed record ServerSession(
        ServerDefinition Definition, McpClient Client, IReadOnlyList<McpTool> Tools, string? Instructions, ManifestServer Manifest);
}
