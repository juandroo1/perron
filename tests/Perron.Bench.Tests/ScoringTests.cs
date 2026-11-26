using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

using Perron.Bench.Run;
using Perron.Bench.Scoring;
using Perron.Bench.Settings;

namespace Perron.Bench.Tests;

/// <summary>
/// Scoring from stored traces (AK-9.1, AK-9.4, AK-9.6): golden traces with their expected outcome, failure codes and
/// check results (tests/Perron.Bench.Tests/Golden, written by a script from the transport.opendata.ch fixtures).
/// </summary>
public sealed class ScoringTests
{
    public static TheoryData<string> GoldenTraces() =>
        [.. JsonNode.Parse(File.ReadAllText(GoldenFile("expected.json")))!.AsObject().Select(pair => pair.Key)];

    [Theory]
    [MemberData(nameof(GoldenTraces))]
    public void GoldenTrace_IsScoredAsExpected(string name)
    {
        var expected = JsonNode.Parse(File.ReadAllText(GoldenFile("expected.json")))![name]!;
        var trace = Load(name);

        var evaluation = Evaluator.Evaluate(trace);

        Assert.Equal(expected["outcome"]!.GetValue<string>(), evaluation.Outcome);
        Assert.Equal(expected["errors"]!.AsArray().Select(code => code!.GetValue<string>()).Order(), evaluation.Errors.Order());
        Assert.Equal(expected["checks"]!.AsArray().Select(result => result!.GetValue<string>()), evaluation.Checks.Select(check => check.Result));
    }

    [Fact]
    public void Grounding_FindsTimesThatNoToolReturned()
    {
        var grounding = Evaluator.Ground(Load("earliest-wrong-time-hallucinated"));

        Assert.Equal(["08:15", "09:10"], grounding.Times);
        Assert.Equal(["08:15", "09:10"], grounding.Ungrounded);
        Assert.False(grounding.Grounded);
    }

    [Fact]
    public void Grounding_AcceptsUtcTimestampsInToolResults_AndTimesFromThePrompt()
    {
        var trace = Load("earliest-utc-tool-result") with { FinalAnswer = "Ab 08:00 gibt es den IC 8: 08:02 bis 08:58." };

        var grounding = Evaluator.Ground(trace);

        Assert.True(grounding.Grounded, string.Join(", ", grounding.Ungrounded));
    }

    [Fact]
    public void Details_ExplainTheVerdict()
    {
        var check = Assert.Single(Evaluator.Evaluate(Load("earliest-not-optimal")).Checks);

        Assert.Equal("earliest_arrival: valid entry named, but outside the tolerance (best arrival 08:58)", check.Detail);
        Assert.Equal(ErrorCodes.NotOptimal, check.Code);
    }

    [Fact]
    public void Trace_RoundTripsWithEvaluationAndOracleData()
    {
        var trace = Load("earliest-forecast");
        var scored = trace with { Evaluation = Evaluator.Evaluate(trace) };

        var copy = JsonSerializer.Deserialize(JsonSerializer.Serialize(scored, TraceJsonContext.Default.RunTrace), TraceJsonContext.Default.RunTrace)!;

        Assert.Equal(Results.Passed, copy.Evaluation!.Outcome);
        Assert.Equal(new DateTimeOffset(2026, 11, 26, 9, 0, 0, TimeSpan.FromHours(1)), copy.Oracle![0].Connections![0].ArrivalForecast);
    }

    [Fact]
    public async Task Rescore_ScoresStoredTracesAgain_AndWritesScoresJsonl()
    {
        var bench = Directory.CreateTempSubdirectory("perron-rescore-").FullName;
        try
        {
            var traces = Directory.CreateDirectory(Path.Combine(bench, "results", "golden", "traces")).FullName;
            foreach (var file in Directory.GetFiles(GoldenDirectory(), "*.json").Where(file => !file.EndsWith("expected.json", StringComparison.Ordinal)))
            {
                File.Copy(file, Path.Combine(traces, Path.GetFileName(file)));
            }

            var output = new StringWriter();
            var exitCode = await BenchCli.RunAsync(
                ["rescore", "golden"], output, new StringWriter(),
                new Dictionary<string, string?> { [BenchDirectoryOptions.EnvironmentVariable] = bench },
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(ExitCodes.Success, exitCode);
            var scores = File.ReadAllLines(Path.Combine(bench, "results", "golden", "scores.jsonl"));
            Assert.Equal(25, scores.Length);
            var stored = JsonNode.Parse(File.ReadAllText(Path.Combine(traces, "latest-passed.json")))!;
            Assert.Equal("passed", stored["evaluation"]!["outcome"]!.GetValue<string>());
            Assert.Equal(Evaluator.Version, stored["evaluation"]!["scorer_version"]!.GetValue<string>());
            Assert.Contains("golden: 25 traces rescored", output.ToString());
            Assert.Contains("perron-rich", output.ToString());
        }
        finally
        {
            Directory.Delete(bench, true);
        }
    }

    [Fact]
    public async Task Rescore_UnknownRun_IsAUsageError()
    {
        var bench = Directory.CreateTempSubdirectory("perron-rescore-").FullName;
        try
        {
            var error = new StringWriter();
            var exitCode = await BenchCli.RunAsync(
                ["rescore", "nope"], new StringWriter(), error,
                new Dictionary<string, string?> { [BenchDirectoryOptions.EnvironmentVariable] = bench },
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Equal(ExitCodes.UsageError, exitCode);
            Assert.Contains("Unknown run(s): nope", error.ToString());
        }
        finally
        {
            Directory.Delete(bench, true);
        }
    }

    [Theory]
    [InlineData("now", "2026-11-25T17:12:00+01:00")]
    [InlineData("+1d 08:00", "2026-11-26T08:00:00+01:00")]
    [InlineData("+2d 14:00", "2026-11-27T14:00:00+01:00")]
    [InlineData("next:mon 07:30", "2026-11-30T07:30:00+01:00")]
    [InlineData("next:wed 09:00", "2026-12-02T09:00:00+01:00")]
    public void RelativeTime_IsResolvedFromTheReferenceTime(string expression, string expected)
    {
        var reference = new DateTimeOffset(2026, 11, 25, 17, 12, 0, TimeSpan.FromHours(1)); // a Wednesday

        Assert.Equal(DateTimeOffset.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), RelativeTime.Resolve(expression, reference));
    }

    [Fact]
    public void RelativeTime_UsesSummerTimeForDatesInSummer()
    {
        var reference = new DateTimeOffset(2027, 3, 27, 12, 0, 0, TimeSpan.FromHours(1));

        Assert.Equal(TimeSpan.FromHours(2), RelativeTime.Resolve("+1d 08:00", reference).Offset);
    }

    [Theory]
    [InlineData("IC 8 ab 08:02, an 8:58.", "08:02,08:58")]
    [InlineData("Départ à 07h05, arrivée à 07h44.", "07:05,07:44")]
    [InlineData("Haltestelle 8507000, Gleis 12A-C, 1.5 km, 2026-11-26", "")]
    [InlineData("Am 26.11. um 23:59 und 00:01", "23:59,00:01")]
    public void ClockTimes_FindsTimesButNotOtherNumbers(string text, string expected)
    {
        Assert.Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries), ClockTimes.In(text));
    }

    [Theory]
    [InlineData("Der Zug ist mässig ausgelastet.", "2")]
    [InlineData("Le train est peu chargé.", "1")]
    [InlineData("1. Klasse ●○○, 2. Klasse ●●●", "1,3")]
    [InlineData("In wenigen Minuten fährt der vollständig erneuerte Zug ab.", "")]
    [InlineData("Expect a busy train; standing room only.", "3")]
    public void OccupancyLevels_AreReadFromWordsAndSymbols(string answer, string expected)
    {
        Assert.Equal(expected.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse), OracleCheck.OccupancyLevels(answer).Order());
    }

    private static RunTrace Load(string name) =>
        JsonSerializer.Deserialize(File.ReadAllText(GoldenFile($"{name}.json")), TraceJsonContext.Default.RunTrace)!;

    private static string GoldenFile(string name) => Path.Combine(GoldenDirectory(), name);

    private static string GoldenDirectory([CallerFilePath] string callerFile = "") =>
        Path.Combine(Path.GetDirectoryName(callerFile)!, "Golden");
}
