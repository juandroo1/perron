using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

using Anthropic.Models.Messages;

using Microsoft.Extensions.DependencyInjection;

using Perron.Bench.Run;
using Perron.Bench.Scoring;
using Perron.Bench.Settings;

namespace Perron.Bench.Tests;

/// <summary>LLM-as-judge (AK-9.2) with a scripted model: verdicts, blindness, storage, review sheet and agreement.</summary>
public sealed class JudgeTests(PerronMcpFactory server) : IClassFixture<PerronMcpFactory>
{
    private static readonly JudgeSettings Settings = new("medium", 2000);
    private static readonly ModelPricing Pricing = new(0.10m, 0.50m, 0.125m, 0.01m);
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.FromHours(1)));

    [Fact]
    public void Request_IsBlind_AndAsksForAStructuredVerdict()
    {
        var request = Judge.Request("claude-haiku-5-5", Settings, "Wann fährt der Zug ab Buchs?", "Passed if it asks which Buchs.", "Meinen Sie Buchs AG oder Buchs SG?");

        var message = JsonNode.Parse(JsonSerializer.Serialize(request.Messages[0].Json))!["content"]!.GetValue<string>();
        Assert.Contains("<task>\nWann fährt der Zug ab Buchs?\n</task>", message);
        Assert.Contains("<rubric>\nPassed if it asks which Buchs.\n</rubric>", message);
        Assert.Contains("<answer>\nMeinen Sie Buchs AG oder Buchs SG?\n</answer>", message);
        Assert.DoesNotContain("perron", message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("server", Judge.SystemPrompt.Replace("which system produced", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2000, request.MaxTokens);
        Assert.NotNull(request.OutputConfig?.Format);
        Assert.Matches("^[0-9a-f]{64}$", Judge.PromptSha256);
    }

    [Theory]
    [InlineData("pass", "passed")]
    [InlineData("fail", "failed")]
    public async Task Verdict_IsStored_AndScoresTheCheck(string verdict, string outcome)
    {
        var model = new JudgeModel($$"""{"verdict":"{{verdict}}","reason":"Asks which Buchs is meant."}""");
        var trace = Golden("judge-pending");

        var judgements = await new Judge(model, Clock).JudgeAsync(trace, "claude-haiku-5-5", Settings, Pricing, TestContext.Current.CancellationToken);

        var judgement = Assert.Single(judgements);
        Assert.Equal(verdict, judgement.Verdict);
        Assert.Equal("Asks which Buchs is meant.", judgement.Reason);
        Assert.Equal(((500 * 0.10m) + (40 * 0.50m)) / 1_000_000m, judgement.CostUsd);
        var evaluation = Evaluator.Evaluate(trace with { Judgements = judgements });
        Assert.Equal(outcome, evaluation.Outcome);
        Assert.Equal("judge: Asks which Buchs is meant.", Assert.Single(evaluation.Checks).Detail);
    }

    [Fact]
    public async Task EmptyAnswer_FailsWithoutAModelCall()
    {
        var model = new JudgeModel("""{"verdict":"pass","reason":"x"}""");

        var judgements = await new Judge(model, Clock).JudgeAsync(Golden("judge-pending") with { FinalAnswer = null }, "claude-haiku-5-5", Settings, Pricing, TestContext.Current.CancellationToken);

        Assert.Equal(Judge.Fail, Assert.Single(judgements).Verdict);
        Assert.Equal(0, model.Calls);
    }

    [Theory]
    [InlineData("infra_error")]
    [InlineData("not_covered")]
    public async Task ExcludedRuns_AreNotJudged(string status)
    {
        var model = new JudgeModel("""{"verdict":"pass","reason":"x"}""");

        var judgements = await new Judge(model, Clock).JudgeAsync(Golden("judge-pending") with { Status = status }, "claude-haiku-5-5", Settings, Pricing, TestContext.Current.CancellationToken);

        Assert.Empty(judgements);
        Assert.Equal(0, model.Calls);
    }

    [Theory]
    [InlineData("not json", "no JSON verdict")]
    [InlineData("""{"verdict":"maybe","reason":"x"}""", "invalid verdict 'maybe'")]
    [InlineData(null, "HttpRequestException")]
    public async Task UnusableJudgeAnswer_LeavesTheCheckUnscored(string? answer, string error)
    {
        var model = new JudgeModel(answer);

        var judgements = await new Judge(model, Clock).JudgeAsync(Golden("judge-pending"), "claude-haiku-5-5", Settings, Pricing, TestContext.Current.CancellationToken);

        var judgement = Assert.Single(judgements);
        Assert.Contains(error, judgement.Error);
        Assert.Equal(Results.Unscored, Evaluator.Evaluate(Golden("judge-pending") with { Judgements = judgements }).Outcome);
    }

    [Fact]
    public async Task Run_JudgesAfterTheAgent_AndRecordsTheJudge()
    {
        const string Servers = """
            { "servers": [ { "id": "alpha", "transport": "http", "url": "http://localhost/mcp", "version": "1",
              "capabilities": [{ "name": "stops", "reason": "r" }, { "name": "trips", "reason": "r" }] } ] }
            """;
        const string Tasks = """
            {"id":"dev-017","prompt":"Wann fährt morgen um 08:00 der nächste Zug von Buchs nach Zürich HB?","language":"de","category":"ambiguous","capabilities_required":["stops","trips"],"smoke":true,"checks":[{"type":"judge","rubric":"Passed if it asks which Buchs is meant."}],"max_tool_calls":4}
            {"id":"dev-001","prompt":"Welche ID hat Bern?","language":"de","category":"stops","capabilities_required":["stops"],"smoke":true,"checks":[{"type":"regex","pattern":"8507000"}],"max_tool_calls":4}
            """;
        using var bench = new Bench(Servers, Tasks);
        var model = new JudgeModel("""{"verdict":"pass","reason":"It asks which Buchs is meant."}""", agentAnswer: "Welches Buchs meinen Sie: Buchs AG oder Buchs SG?");

        var (exitCode, output) = await RunCliAsync(bench, model, "run", "--suite", "smoke", "--tasks", "dev-017");

        Assert.Equal(ExitCodes.Success, exitCode);
        var run = Assert.Single(Directory.GetDirectories(Path.Combine(bench.Directory, "results")));
        Assert.Single(Directory.GetFiles(Path.Combine(run, "traces")));
        var trace = JsonNode.Parse(File.ReadAllText(Path.Combine(run, "traces", "dev-017__alpha__r1.json")))!;
        Assert.Equal("pass", trace["judgements"]![0]!["verdict"]!.GetValue<string>());
        Assert.Equal("passed", trace["evaluation"]!["outcome"]!.GetValue<string>());
        var manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(run, "manifest.json")))!;
        Assert.Equal(Judge.PromptSha256, manifest["judge_prompt_sha256"]!.GetValue<string>());
        Assert.Equal(["dev-017"], manifest["tasks"]!.AsArray().Select(task => task!.GetValue<string>()));
        Assert.True(JsonNode.Parse(File.ReadAllLines(Path.Combine(run, "runs.jsonl"))[0])!["judge_cost_usd"]!.GetValue<decimal>() > 0);
        Assert.Contains("dev-017 alpha r1: completed, passed", output);
    }

    [Fact]
    public async Task Run_UnknownTask_IsAUsageError()
    {
        using var bench = new Bench(
            """{ "servers": [ { "id": "alpha", "transport": "http", "url": "http://localhost/mcp", "version": "1", "capabilities": [{ "name": "stops", "reason": "r" }] } ] }""",
            """{"id":"dev-001","prompt":"?","language":"de","category":"stops","capabilities_required":["stops"],"smoke":true,"checks":[{"type":"regex","pattern":"x"}],"max_tool_calls":4}""");

        var (exitCode, _) = await RunCliAsync(bench, new JudgeModel("{}"), "run", "--suite", "smoke", "--tasks", "dev-999");

        Assert.Equal(ExitCodes.UsageError, exitCode);
    }

    [Fact]
    public async Task JudgeCommand_FillsMissingVerdicts_ThenSkipsThem_UnlessForced()
    {
        using var bench = Bench.WithGoldenRun("judge-pending", "regex-passed");
        var model = new JudgeModel("""{"verdict":"fail","reason":"It chooses one Buchs silently."}""");

        var (first, output) = await RunCliAsync(bench, model, "judge");
        var callsAfterFirst = model.Calls;
        await RunCliAsync(bench, model, "judge");
        var callsAfterSecond = model.Calls;
        await RunCliAsync(bench, model, "judge", "--force");

        Assert.Equal(ExitCodes.Success, first);
        Assert.Equal(1, callsAfterFirst);
        Assert.Equal(1, callsAfterSecond);
        Assert.Equal(2, model.Calls);
        Assert.Contains("golden judge-pending perron-rich r1: fail", output);
        var trace = JsonNode.Parse(File.ReadAllText(Path.Combine(bench.Directory, "results", "golden", "traces", "judge-pending.json")))!;
        Assert.Equal("failed", trace["evaluation"]!["outcome"]!.GetValue<string>());
        Assert.Equal(2, File.ReadAllLines(Path.Combine(bench.Directory, "results", "golden", "scores.jsonl")).Length);
    }

    [Fact]
    public async Task Export_HidesServerAndVerdict_AndAgreementComparesWithTheJudge()
    {
        using var bench = Bench.WithGoldenRun("judge-pending");
        await RunCliAsync(bench, new JudgeModel("""{"verdict":"pass","reason":"It asks which Buchs is meant."}"""), "judge");

        var (exportCode, _) = await RunCliAsync(bench, null, "judge-export");
        var sheetPath = Path.Combine(bench.Directory, "judge-review", "review.csv");
        var sheet = File.ReadAllText(sheetPath);

        Assert.Equal(ExitCodes.Success, exportCode);
        Assert.StartsWith("﻿\"id\";\"task_id\";\"category\";\"prompt\";\"rubric\";\"answer\";\"human_verdict\";\"note\"", sheet);
        Assert.DoesNotContain("perron-rich", sheet);
        Assert.DoesNotContain("It asks which Buchs is meant.", sheet);
        Assert.DoesNotContain("\"pass\"", sheet);

        // The reviewer disagrees.
        var rows = JudgeReview.ParseCsv(sheet);
        rows[1][6] = "nicht bestanden";
        rows[1][7] = "Lists only two of three; fine; but I say fail";
        File.WriteAllText(sheetPath, string.Join("\r\n", rows.Select(row => string.Join(";", row.Select(field => "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"")))));

        var (agreementCode, output) = await RunCliAsync(bench, null, "judge-agreement", sheetPath);

        Assert.Equal(ExitCodes.Success, agreementCode);
        var report = File.ReadAllText(Path.Combine(bench.Directory, "judge-review", "agreement.md"));
        Assert.Contains("Agreement: 0/1 (0.0 %), Cohen's κ = 0.00.", report);
        Assert.Contains("| judge pass | 0 | 1 |", report);
        Assert.Contains("| judge-pending | pass | fail | It asks which Buchs is meant. |", report);
        Assert.Contains("Agreement: 0/1", output);
    }

    [Fact]
    public void Kappa_IsCohensKappa()
    {
        // 50 verdicts: observed agreement 0.7, chance agreement 0.5 → κ 0.4.
        Assert.Equal(0.4, JudgeReview.Kappa(20, 5, 10, 15)!.Value, 6);
        Assert.Equal(1.0, JudgeReview.Kappa(10, 0, 0, 10)!.Value, 6);
        Assert.Null(JudgeReview.Kappa(5, 0, 0, 0));
    }

    [Fact]
    public void Sheet_IsParsedWithQuotesSemicolonsAndLineBreaks()
    {
        var rows = JudgeReview.ParseCsv("﻿\"id\";\"answer\"\r\n\"j-1\";\"Ja; \"\"Buchs AG\"\"\nund Buchs SG\"\r\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal(["j-1", "Ja; \"Buchs AG\"\nund Buchs SG"], rows[1]);
    }

    [Theory]
    [InlineData("pass", "pass")]
    [InlineData(" Bestanden ", "pass")]
    [InlineData("nicht bestanden", "fail")]
    [InlineData("F", "fail")]
    [InlineData("", null)]
    [InlineData("vielleicht", null)]
    public void HumanVerdict_IsNormalized(string value, string? expected) =>
        Assert.Equal(expected, JudgeReview.Normalize(value));

    private static RunTrace Golden(string name) =>
        JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(GoldenDirectory(), $"{name}.json")), TraceJsonContext.Default.RunTrace)!;

    private static string GoldenDirectory([CallerFilePath] string callerFile = "") => Path.Combine(Path.GetDirectoryName(callerFile)!, "Golden");

    private async Task<(int ExitCode, string Output)> RunCliAsync(Bench bench, IModelClient? model, params string[] args)
    {
        var output = new StringWriter();
        var exitCode = await BenchCli.RunAsync(
            args,
            output,
            new StringWriter(),
            new Dictionary<string, string?> { [BenchDirectoryOptions.EnvironmentVariable] = bench.Directory, [GitInfo.ShaVariable] = "abc123", ["ANTHROPIC_API_KEY"] = "" },
            services =>
            {
                server.RouteBenchClientToServer(services);
                services.AddSingleton<TimeProvider>(Clock);
                services.AddHttpClient("ITransportApiClient").ConfigurePrimaryHttpMessageHandler(() => new TransportFixtureHandler());
                if (model is not null)
                {
                    services.AddSingleton(model);
                }
            },
            TestContext.Current.CancellationToken);
        return (exitCode, output.ToString());
    }

    /// <summary>
    /// Answers judge requests (they carry an output format) with <paramref name="judgeAnswer"/> (null: throws), and agent
    /// requests with <paramref name="agentAnswer"/>; usage 500 input and 40 output tokens.
    /// </summary>
    private sealed class JudgeModel(string? judgeAnswer, string agentAnswer = "Done.") : IModelClient
    {
        private int calls;

        public int Calls => calls;

        public Task<Message> CreateAsync(MessageCreateParams request, CancellationToken cancellationToken)
        {
            if (request.OutputConfig?.Format is null)
            {
                return Task.FromResult(Answer(agentAnswer));
            }

            Interlocked.Increment(ref calls);
            return judgeAnswer is null ? throw new HttpRequestException("judge unreachable") : Task.FromResult(Answer(judgeAnswer));
        }

        private static Message Answer(string text)
        {
            var json = $$$"""
                {"id":"msg_judge","type":"message","role":"assistant","model":"claude-haiku-5-5",
                 "content":[{"type":"text","text":{{{JsonSerializer.Serialize(text)}}}}],"stop_reason":"end_turn","stop_sequence":null,
                 "usage":{"input_tokens":500,"output_tokens":40,"cache_creation_input_tokens":0,"cache_read_input_tokens":0}}
                """;
            return Message.FromRawUnchecked(JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!);
        }
    }

    /// <summary>A bench directory with the repository's bench.json (no pauses) and the given registry and tasks.</summary>
    private sealed class Bench : IDisposable
    {
        public Bench(string servers, string tasks)
        {
            Directory = System.IO.Directory.CreateTempSubdirectory("perron-judge-").FullName;
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

        /// <summary>A stored run "golden" with copies of golden traces.</summary>
        public static Bench WithGoldenRun(params string[] traces)
        {
            var bench = new Bench("""{ "servers": [] }""", "");
            var directory = System.IO.Directory.CreateDirectory(Path.Combine(bench.Directory, "results", "golden", "traces")).FullName;
            foreach (var trace in traces)
            {
                File.Copy(Path.Combine(GoldenDirectory(), $"{trace}.json"), Path.Combine(directory, $"{trace}.json"));
            }

            return bench;
        }

        public void Dispose() => System.IO.Directory.Delete(Directory, true);
    }
}
