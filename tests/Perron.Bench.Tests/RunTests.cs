using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

using Anthropic.Models.Messages;

using Microsoft.Extensions.DependencyInjection;

using Perron.Bench.Run;
using Perron.Bench.Settings;

namespace Perron.Bench.Tests;

/// <summary>
/// <c>bench run</c> (AK-8.1 to AK-8.3) against Perron MCP in memory, with a scripted model: no LLM and no API calls.
/// </summary>
public sealed class RunTests(PerronMcpFactory server) : IClassFixture<PerronMcpFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 14, 5, 42, TimeSpan.FromHours(1));

    private const string Servers = """
        { "servers": [
          { "id": "alpha", "transport": "http", "url": "http://localhost/mcp", "version": "1",
            "capabilities": [{ "name": "stops", "reason": "r" }, { "name": "trips", "reason": "r" }, { "name": "departures", "reason": "r" }] },
          { "id": "beta", "transport": "http", "url": "http://localhost/mcp", "version": "1",
            "capabilities": [{ "name": "stops", "reason": "r" }] }
        ] }
        """;

    private const string Tasks = """
        {"id":"dev-001","prompt":"Welche ID hat Bern?","language":"de","category":"stops","capabilities_required":["stops"],"smoke":true,"checks":[{"type":"regex","pattern":"8507000"}],"max_tool_calls":4}
        {"id":"dev-002","prompt":"Wie komme ich nach Bern?","language":"de","category":"trip_simple","capabilities_required":["stops","trips"],"smoke":true,"checks":[{"type":"regex","pattern":"x"}],"max_tool_calls":4}
        {"id":"dev-003","prompt":"Abfahrten in Bern?","language":"de","category":"departures","capabilities_required":["stops","departures"],"smoke":false,"checks":[{"type":"regex","pattern":"x"}],"max_tool_calls":4}
        """;

    [Fact]
    public async Task Smoke_RunsEveryTaskOnEveryServer_AndWritesManifestTracesAndRuns()
    {
        using var bench = new TemporaryBench(Servers, Tasks);
        var model = ScriptedModel.LookUpBern();

        var (exitCode, output, _) = await RunAsync(bench, model, "run", "--suite", "smoke", "--seed", "7");

        Assert.Equal(ExitCodes.Success, exitCode);
        var runDirectory = Assert.Single(Directory.GetDirectories(Path.Combine(bench.Directory, "results")));
        Assert.EndsWith("20261125-130542-smoke", runDirectory);

        // 2 smoke tasks × 2 servers; beta cannot plan trips, so dev-002 is not covered there and costs no model call.
        var runs = File.ReadAllLines(Path.Combine(runDirectory, "runs.jsonl")).Select(line => JsonNode.Parse(line)!).ToList();
        Assert.Equal(4, runs.Count);
        Assert.Equal(3, runs.Count(run => run["status"]!.GetValue<string>() == RunStatus.Completed));
        var notCovered = Assert.Single(runs, run => run["status"]!.GetValue<string>() == RunStatus.NotCovered);
        Assert.Equal("dev-002", notCovered["task_id"]!.GetValue<string>());
        Assert.Equal("beta", notCovered["server_id"]!.GetValue<string>());
        Assert.Equal(6, model.Calls);
        Assert.Contains("Done: completed 3, not_covered 1;", output);

        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(runDirectory, "manifest.json")))!;
        Assert.Equal("smoke", manifest["suite"]!.GetValue<string>());
        Assert.Equal(Sha256(File.ReadAllBytes(Path.Combine(bench.Directory, "tasks", "dev.jsonl"))), manifest["task_set_sha256"]!.GetValue<string>());
        Assert.Equal(SystemPrompt.TemplateSha256, manifest["system_prompt_template_sha256"]!.GetValue<string>());
        Assert.Equal(7, manifest["seed"]!.GetValue<int>());
        Assert.Equal("claude-haiku-5-5", manifest["model"]!.GetValue<string>());
        Assert.Equal("medium", manifest["effort"]!.GetValue<string>());
        Assert.Equal("abc123", manifest["git_sha"]!.GetValue<string>());
        Assert.NotNull(manifest["finished_at"]);
        var alpha = manifest["servers"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "alpha")!;
        Assert.Matches("^[0-9a-f]{64}$", alpha["tools_hash"]!.GetValue<string>());
        Assert.Equal(["find_stops", "get_departures", "plan_trip"], alpha["tools"]!.AsArray().Select(tool => tool!.GetValue<string>()));
        Assert.Equal(["stops"], manifest["servers"]!.AsArray().Single(item => item!["id"]!.GetValue<string>() == "beta")!["capabilities_covered"]!.AsArray().Select(c => c!.GetValue<string>()));
    }

    [Fact]
    public async Task Trace_RecordsPromptTurnsToolCallsTokensAndCost()
    {
        using var bench = new TemporaryBench(Servers, Tasks);

        await RunAsync(bench, ScriptedModel.LookUpBern(), "run", "--suite", "smoke", "--servers", "alpha");

        var trace = ReadTrace(bench, "dev-001__alpha__r1.json");
        Assert.Equal(RunStatus.Completed, trace["status"]!.GetValue<string>());
        Assert.Equal("Welche ID hat Bern?", trace["prompt"]!.GetValue<string>());
        Assert.Equal(4, trace["max_tool_calls"]!.GetValue<int>());
        var systemPrompt = trace["system_prompt"]!.GetValue<string>();
        Assert.Contains("Current date and time: Wed 25.11.2026 14:05 (Europe/Zurich).", systemPrompt);
        Assert.EndsWith("## Server instructions\n(none)", systemPrompt);

        var turns = trace["turns"]!.AsArray();
        Assert.Equal(2, turns.Count);
        Assert.Equal("tool_use", turns[0]!["stop_reason"]!.GetValue<string>());
        Assert.Equal(["thinking", "tool_use"], turns[0]!["content"]!.AsArray().Select(block => block!["type"]!.GetValue<string>()));
        var call = Assert.Single(turns[0]!["tool_calls"]!.AsArray())!;
        Assert.Equal("find_stops", call["name"]!.GetValue<string>());
        Assert.Equal("Bern", call["arguments"]!["query"]!.GetValue<string>());
        Assert.Contains("8507000 | Bern | train", call["result"]!.GetValue<string>());
        Assert.False(call["is_error"]!.GetValue<bool>());
        Assert.Equal("The stop ID of Bern is 8507000.", trace["final_answer"]!.GetValue<string>());

        // Two turns of 1000 input, 100 output, 200 cache write and 300 cache read tokens at the Haiku prices of bench.json.
        var totals = trace["totals"]!;
        Assert.Equal(2, totals["turns"]!.GetValue<int>());
        Assert.Equal(1, totals["tool_calls"]!.GetValue<int>());
        Assert.Equal(2000, totals["usage"]!["input"]!.GetValue<long>());
        Assert.Equal(((2000 * 0.10m) + (200 * 0.50m) + (400 * 0.125m) + (600 * 0.01m)) / 1_000_000m, totals["cost_usd"]!.GetValue<decimal>());
        Assert.Null(trace["oracle"]);
    }

    [Fact]
    public async Task Loop_PassesThinkingBack_AndSendsTheSameSettingsEveryTurn()
    {
        using var bench = new TemporaryBench(Servers, Tasks);
        var model = ScriptedModel.LookUpBern();

        await RunAsync(bench, model, "run", "--suite", "smoke", "--servers", "alpha");

        var first = model.Requests[0];
        var second = model.Requests[1];
        Assert.Single(first.Messages);
        Assert.Equal(3, second.Messages.Count);
        Assert.Equal("claude-haiku-5-5", second.Model.Raw());
        Assert.Equal(16000, second.MaxTokens);
        Assert.Equal(3, second.Tools!.Count);
        Assert.Contains("\"signature\":\"sig-1\"", JsonSerializer.Serialize(second.Messages[1].Json));
        Assert.Contains("\"tool_result\"", JsonSerializer.Serialize(second.Messages[2].Json));
    }

    [Fact]
    public async Task ParallelToolCalls_AreAllRun_AndAnsweredInOneMessage()
    {
        using var bench = new TemporaryBench(Servers, Tasks);
        var model = ScriptedModel.TwoCallsThenAnswer();

        await RunAsync(bench, model, "run", "--suite", "smoke", "--servers", "alpha");

        var trace = ReadTrace(bench, "dev-001__alpha__r1.json");
        Assert.Equal(2, trace["turns"]!.AsArray()[0]!["tool_calls"]!.AsArray().Count);
        Assert.Equal(3, model.Requests[1].Messages.Count);
        Assert.Equal(2, JsonSerializer.Serialize(model.Requests[1].Messages[2].Json).Split("\"tool_result\"").Length - 1);
    }

    [Fact]
    public async Task ToolLimit_AnswersExtraCallsWithTheLimitMessage_AndStopsWhenTheModelKeepsCalling()
    {
        using var bench = new TemporaryBench(Servers, Tasks);

        await RunAsync(bench, ScriptedModel.AlwaysCallsTools(), "run", "--suite", "smoke", "--servers", "alpha");

        var trace = ReadTrace(bench, "dev-001__alpha__r1.json");
        Assert.Equal(RunStatus.ToolLimit, trace["status"]!.GetValue<string>());
        Assert.Equal(4, trace["totals"]!["tool_calls"]!.GetValue<int>());
        var calls = trace["turns"]!.AsArray().SelectMany(turn => turn!["tool_calls"]!.AsArray()).ToList();
        var overLimit = Assert.Single(calls, call => call!["over_limit"]!.GetValue<bool>())!;
        Assert.Equal("Tool call limit reached (4 calls). Answer now with the information you have.", overLimit["result"]!.GetValue<string>());
        Assert.Equal(6, trace["turns"]!.AsArray().Count);
    }

    [Theory]
    [InlineData("max_tokens", RunStatus.MaxTokens)]
    [InlineData("refusal", RunStatus.Refusal)]
    [InlineData("end_turn", RunStatus.Completed)]
    public async Task StopReason_SetsTheStatus(string stopReason, string status)
    {
        using var bench = new TemporaryBench(Servers, Tasks);

        await RunAsync(bench, ScriptedModel.AnswerAtOnce(stopReason), "run", "--suite", "smoke", "--servers", "alpha");

        Assert.Equal(status, ReadTrace(bench, "dev-001__alpha__r1.json")["status"]!.GetValue<string>());
    }

    [Fact]
    public async Task ModelFailure_IsAnInfrastructureError_AndTheRunExitsWithOne()
    {
        using var bench = new TemporaryBench(Servers, Tasks);

        var (exitCode, _, _) = await RunAsync(bench, ScriptedModel.Failing(), "run", "--suite", "smoke", "--servers", "alpha");

        Assert.Equal(ExitCodes.CheckFailed, exitCode);
        var trace = ReadTrace(bench, "dev-001__alpha__r1.json");
        Assert.Equal(RunStatus.InfraError, trace["status"]!.GetValue<string>());
        Assert.StartsWith("model: HttpRequestException", trace["status_detail"]!.GetValue<string>());
    }

    [Fact]
    public async Task Repeats_AndFullSuite_RunEveryTaskSeveralTimes()
    {
        using var bench = new TemporaryBench(Servers, Tasks);

        await RunAsync(bench, ScriptedModel.LookUpBern(), "run", "--suite", "full", "--servers", "alpha", "--repeats", "2");

        var runDirectory = Assert.Single(Directory.GetDirectories(Path.Combine(bench.Directory, "results")));
        Assert.Equal(6, File.ReadAllLines(Path.Combine(runDirectory, "runs.jsonl")).Length);
        Assert.True(File.Exists(Path.Combine(runDirectory, "traces", "dev-003__alpha__r2.json")));
    }

    [Fact]
    public async Task SameSeed_GivesTheSameServerOrder()
    {
        using var first = new TemporaryBench(Servers, Tasks);
        using var second = new TemporaryBench(Servers, Tasks);

        await RunAsync(first, ScriptedModel.LookUpBern(), "run", "--suite", "smoke", "--seed", "42", "--repeats", "3");
        await RunAsync(second, ScriptedModel.LookUpBern(), "run", "--suite", "smoke", "--seed", "42", "--repeats", "3");

        Assert.Equal(Order(first), Order(second));
    }

    [Fact]
    public async Task DryRun_EstimatesWithoutModelCalls_FromBenchJsonAssumptions()
    {
        using var bench = new TemporaryBench(Servers, Tasks);
        var model = ScriptedModel.LookUpBern();

        var (exitCode, output, _) = await RunAsync(bench, model, "run", "--suite", "smoke", "--dry-run");

        Assert.Equal(ExitCodes.Success, exitCode);
        Assert.Equal(0, model.Calls);
        Assert.False(Directory.Exists(Path.Combine(bench.Directory, "results")));
        Assert.Contains("alpha              2 runs (0 not covered) | 4.0 turns/run |     8 model calls", output);
        Assert.Contains("beta               1 runs (1 not covered) | 4.0 turns/run |     4 model calls", output);
        Assert.Contains("bench.json assumptions, tool tokens estimated", output);
        Assert.Contains("Total: 3 runs, 12 model calls", output);
    }

    [Fact]
    public async Task DryRun_IsCalibratedFromEarlierRuns()
    {
        using var bench = new TemporaryBench(Servers, Tasks);
        await RunAsync(bench, ScriptedModel.LookUpBern(), "run", "--suite", "smoke", "--servers", "alpha");

        var (_, output, _) = await RunAsync(bench, ScriptedModel.LookUpBern(), "run", "--suite", "smoke", "--servers", "alpha", "--dry-run");

        Assert.Contains("2.0 turns/run", output);
        Assert.Contains("calibrated from 2 earlier runs", output);
    }

    [Fact]
    public async Task WithoutApiKey_RunNeedsTheKey_ButDryRunWorks()
    {
        using var bench = new TemporaryBench(Servers, Tasks);

        var (exitCode, output, _) = await RunAsync(bench, null, "run", "--suite", "smoke");
        var (dryExit, _, _) = await RunAsync(bench, null, "run", "--suite", "smoke", "--dry-run");

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Contains("ANTHROPIC_API_KEY is not set", output);
        Assert.Equal(ExitCodes.Success, dryExit);
    }

    [Fact]
    public async Task UnknownSuite_IsAUsageError()
    {
        using var bench = new TemporaryBench(Servers, Tasks);

        var (exitCode, _, error) = await RunAsync(bench, ScriptedModel.LookUpBern(), "run", "--suite", "everything");

        Assert.Equal(ExitCodes.UsageError, exitCode);
        Assert.Contains("everything", error);
    }

    [Fact]
    public void SystemPrompt_IsTheTemplateWithTimeAndInstructions()
    {
        var prompt = SystemPrompt.Build(Now, "  Use stop IDs.  ");

        Assert.Equal(
            """
            You help people with public transport in Switzerland, using the tools available to you.

            Current date and time: Wed 25.11.2026 14:05 (Europe/Zurich). Dates such as "today" or "tomorrow" refer to this time.

            Answer briefly and with concrete times. Answer in the language of the question.

            ## Server instructions
            Use stop IDs.
            """.ReplaceLineEndings("\n"),
            prompt);
        Assert.Equal(new DateTimeOffset(2026, 11, 25, 14, 5, 0, TimeSpan.FromHours(1)), SystemPrompt.ReferenceTime(new FixedClock(Now)));
    }

    [Fact]
    public void BenchJson_OfTheRepository_IsValid_AndPricesBothModels()
    {
        var settings = BenchSettings.Load(Path.Combine(AppContext.BaseDirectory, "bench", BenchSettings.FileName));

        Assert.Equal("medium", settings.Effort);
        Assert.Equal(16000, settings.MaxTokens);
        Assert.True(settings.PauseSeconds > 0);
        Assert.Equal(0.10m, settings.PricingUsdPerMtok[settings.Model].Input);
        Assert.True(settings.PricingUsdPerMtok.ContainsKey(settings.ControlModel));
    }

    private static List<string> Order(TemporaryBench bench) =>
        [.. File.ReadAllLines(Path.Combine(Assert.Single(Directory.GetDirectories(Path.Combine(bench.Directory, "results"))), "runs.jsonl"))
            .Select(line => JsonNode.Parse(line)!["server_id"]!.GetValue<string>())];

    private static JsonNode ReadTrace(TemporaryBench bench, string file)
    {
        var runDirectory = Assert.Single(Directory.GetDirectories(Path.Combine(bench.Directory, "results")));
        return JsonNode.Parse(File.ReadAllText(Path.Combine(runDirectory, "traces", file)))!;
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(TemporaryBench bench, ScriptedModel? model, params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var configuration = new Dictionary<string, string?>
        {
            [BenchDirectoryOptions.EnvironmentVariable] = bench.Directory,
            [GitInfo.ShaVariable] = "abc123",
            ["ANTHROPIC_API_KEY"] = "",
        };

        var exitCode = await BenchCli.RunAsync(
            args,
            output,
            error,
            configuration,
            services =>
            {
                server.RouteBenchClientToServer(services);
                services.AddSingleton<TimeProvider>(new FixedClock(Now));
                if (model is not null)
                {
                    services.AddSingleton<IModelClient>(model);
                }
            },
            TestContext.Current.CancellationToken);

        return (exitCode, output.ToString(), error.ToString());
    }

    /// <summary>A bench directory with its own registry and tasks, bench.json of the repository without pauses.</summary>
    private sealed class TemporaryBench : IDisposable
    {
        public TemporaryBench(string servers, string tasks)
        {
            Directory = System.IO.Directory.CreateTempSubdirectory("perron-run-").FullName;
            System.IO.Directory.CreateDirectory(Path.Combine(Directory, "tasks"));
            File.WriteAllText(Path.Combine(Directory, "servers.json"), servers);
            File.WriteAllText(Path.Combine(Directory, "tasks", "dev.jsonl"), tasks.ReplaceLineEndings("\n") + "\n");
            var settings = JsonNode.Parse(
                File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "bench", BenchSettings.FileName)),
                documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip })!;
            settings["pause_seconds"] = 0;
            File.WriteAllText(Path.Combine(Directory, BenchSettings.FileName), settings.ToJsonString());
        }

        public string Directory { get; }

        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
}

/// <summary>A model that answers from a script; records every request. Usage per answer: 1000/100/200/300 tokens.</summary>
public sealed class ScriptedModel(Func<MessageCreateParams, Message> respond) : IModelClient
{
    private int calls;

    public int Calls => calls;

    public List<MessageCreateParams> Requests { get; } = [];

    public Task<Message> CreateAsync(MessageCreateParams request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        lock (Requests)
        {
            Requests.Add(request);
        }

        return Task.FromResult(respond(request));
    }

    /// <summary>First turn: thinking plus find_stops("Bern"); after the tool result: the answer.</summary>
    public static ScriptedModel LookUpBern() => new(request => request.Messages.Count == 1
        ? Answer("tool_use", Thinking("sig-1"), ToolUse("toolu_1", "find_stops", """{"query":"Bern"}"""))
        : Answer("end_turn", Text("The stop ID of Bern is 8507000.")));

    public static ScriptedModel TwoCallsThenAnswer() => new(request => request.Messages.Count == 1
        ? Answer("tool_use", ToolUse("toolu_1", "find_stops", """{"query":"Bern"}"""), ToolUse("toolu_2", "find_stops", """{"latitude":46.9488,"longitude":7.4391}"""))
        : Answer("end_turn", Text("Done.")));

    public static ScriptedModel AlwaysCallsTools() => new(request =>
        Answer("tool_use", ToolUse($"toolu_{request.Messages.Count}", "find_stops", """{"query":"Bern"}""")));

    public static ScriptedModel AnswerAtOnce(string stopReason) => new(_ => Answer(stopReason, Text("Partial")));

    public static ScriptedModel Failing() => new(_ => throw new HttpRequestException("connection refused"));

    private static string Thinking(string signature) => $$"""{"type":"thinking","thinking":"","signature":"{{signature}}"}""";

    private static string Text(string text) => $$"""{"type":"text","text":{{JsonSerializer.Serialize(text)}}}""";

    private static string ToolUse(string id, string name, string input) =>
        $$$"""{"type":"tool_use","id":"{{{id}}}","name":"{{{name}}}","input":{{{input}}},"caller":{"type":"direct"}}""";

    private static Message Answer(string stopReason, params string[] content)
    {
        var json = $$$"""
            {"id":"msg_test","type":"message","role":"assistant","model":"claude-haiku-5-5",
             "content":[{{{string.Join(",", content)}}}],"stop_reason":"{{{stopReason}}}","stop_sequence":null,
             "usage":{"input_tokens":1000,"output_tokens":100,"cache_creation_input_tokens":200,"cache_read_input_tokens":300}}
            """;
        return Message.FromRawUnchecked(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!);
    }
}

/// <summary>A clock that always shows the same instant.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
}
