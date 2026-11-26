using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Perron.Bench.Run;
using Perron.Bench.Settings;

namespace Perron.Bench.Scoring;

/// <summary>
/// The judge outside of <c>bench run</c> and its review by hand (AK-9.2): <c>bench judge</c> asks for missing verdicts,
/// <c>bench judge-export</c> writes a review sheet without server and judge verdict, <c>bench judge-agreement</c>
/// compares the human verdicts with the judge's.
/// </summary>
public static class JudgeReview
{
    public const string HumanVerdictColumn = "human_verdict";
    private const char Separator = ';';

    /// <summary><c>bench judge</c>: verdicts for judge checks that have none (or all, with <paramref name="force"/>), then rescoring.</summary>
    public static async Task<int> JudgeAsync(
        string benchDirectory,
        IReadOnlyList<string> runIds,
        bool force,
        Judge judge,
        BenchSettings settings,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var (runs, unknown) = TraceStore.Select(benchDirectory, runIds);
        if (unknown.Count > 0)
        {
            await error.WriteLineAsync($"Unknown run(s): {string.Join(", ", unknown)}. Runs: {RescoreCommand.Available(benchDirectory)}.");
            return ExitCodes.UsageError;
        }

        var pricing = settings.PricingUsdPerMtok[settings.Model];
        decimal cost = 0;
        var failures = 0;
        foreach (var runId in runs)
        {
            var traces = TraceStore.Load(benchDirectory, runId);
            for (var i = 0; i < traces.Count; i++)
            {
                var trace = traces[i].Trace;
                var checks = Judge.ChecksOf(trace);
                var complete = checks.All(check => trace.Judgements?.Any(judgement => judgement.CheckIndex == check.Index && judgement.Error is null) == true);
                if (checks.Count == 0 || (complete && !force))
                {
                    continue;
                }

                var judgements = await judge.JudgeAsync(trace, settings.Model, settings.Judge, pricing, cancellationToken);
                traces[i] = traces[i] with { Trace = trace with { Judgements = judgements } };
                cost += judgements.Sum(judgement => judgement.CostUsd);
                failures += judgements.Count(judgement => judgement.Error is not null);
                await output.WriteLineAsync(
                    $"{runId} {trace.TaskId} {trace.ServerId} r{trace.Repeat}: {string.Join(", ", judgements.Select(judgement => judgement.Verdict ?? $"error ({judgement.Error})"))}");
            }

            var lines = await TraceStore.ScoreAsync(benchDirectory, runId, traces, cancellationToken);
            await RescoreCommand.WriteSummaryAsync(lines, output);
        }

        await output.WriteLineAsync(string.Create(CultureInfo.InvariantCulture, $"Judge cost: ${cost:0.0000} (model {settings.Model})"));
        return failures > 0 ? ExitCodes.CheckFailed : ExitCodes.Success;
    }

    /// <summary>
    /// <c>bench judge-export</c>: one row per judge verdict, with task, rubric and answer, but neither the server nor the
    /// judge's verdict, so the review by hand is not influenced. Semicolon-separated UTF-8 with BOM for Excel.
    /// </summary>
    public static async Task<int> ExportAsync(string benchDirectory, IReadOnlyList<string> runIds, string outputFile, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        var (runs, unknown) = TraceStore.Select(benchDirectory, runIds);
        if (unknown.Count > 0)
        {
            await error.WriteLineAsync($"Unknown run(s): {string.Join(", ", unknown)}. Runs: {RescoreCommand.Available(benchDirectory)}.");
            return ExitCodes.UsageError;
        }

        var rows = Verdicts(benchDirectory, runs)
            .OrderBy(item => item.Trace.TaskId, StringComparer.Ordinal)
            .ThenBy(item => item.Id, StringComparer.Ordinal)
            .Select(item => new[]
            {
                item.Id, item.Trace.TaskId, item.Trace.Category ?? "", item.Trace.Prompt, item.Rubric, item.Trace.FinalAnswer ?? "", "", "",
            })
            .ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFile))!);
        var text = new StringBuilder();
        text.Append(Row(["id", "task_id", "category", "prompt", "rubric", "answer", HumanVerdictColumn, "note"]));
        foreach (var row in rows)
        {
            text.Append(Row(row));
        }

        await File.WriteAllTextAsync(outputFile, text.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
        await output.WriteLineAsync($"{rows.Count} judge verdicts from {runs.Count} run(s) written to {outputFile}. Fill in {HumanVerdictColumn} (pass or fail), then run bench judge-agreement.");
        return ExitCodes.Success;
    }

    /// <summary><c>bench judge-agreement</c>: agreement and Cohen's κ between the review sheet and the judge (AK-9.2).</summary>
    public static async Task<int> AgreementAsync(string benchDirectory, string reviewFile, string reportFile, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        if (!File.Exists(reviewFile))
        {
            await error.WriteLineAsync($"Review file not found: {reviewFile}.");
            return ExitCodes.UsageError;
        }

        var sheet = ParseCsv(await File.ReadAllTextAsync(reviewFile, cancellationToken));
        if (sheet.Count == 0 || !sheet[0].Contains("id") || !sheet[0].Contains(HumanVerdictColumn))
        {
            await error.WriteLineAsync($"{reviewFile} needs the columns id and {HumanVerdictColumn}.");
            return ExitCodes.UsageError;
        }

        var header = sheet[0];
        int Column(string name) => Array.IndexOf(header, name);
        var judged = Verdicts(benchDirectory, TraceStore.Runs(benchDirectory)).ToDictionary(item => item.Id);

        var pairs = new List<(string Id, string TaskId, string Judge, string Human, string? Reason, string Note)>();
        var unlabeled = 0;
        var unknownIds = new List<string>();
        foreach (var row in sheet.Skip(1).Where(row => row.Length >= header.Length))
        {
            var id = row[Column("id")];
            var human = Normalize(row[Column(HumanVerdictColumn)]);
            if (human is null)
            {
                unlabeled++;
                continue;
            }

            if (!judged.TryGetValue(id, out var item))
            {
                unknownIds.Add(id);
                continue;
            }

            var note = Column("note") >= 0 ? row[Column("note")] : "";
            pairs.Add((id, item.Trace.TaskId, item.Judgement.Verdict!, human, item.Judgement.Reason, note));
        }

        var report = Report(reviewFile, pairs, unlabeled, unknownIds);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportFile))!);
        await File.WriteAllTextAsync(reportFile, report, cancellationToken);
        await output.WriteLineAsync(report.Split('\n').First(line => line.StartsWith("Agreement", StringComparison.Ordinal)));
        await output.WriteLineAsync($"Report written to {reportFile}.");
        return ExitCodes.Success;
    }

    /// <summary>Cohen's κ for two raters with two categories; <c>null</c> when chance agreement is 1.</summary>
    public static double? Kappa(int bothPass, int judgePassHumanFail, int judgeFailHumanPass, int bothFail)
    {
        double n = bothPass + judgePassHumanFail + judgeFailHumanPass + bothFail;
        if (n == 0)
        {
            return null;
        }

        var observed = (bothPass + bothFail) / n;
        var judgePass = (bothPass + judgePassHumanFail) / n;
        var humanPass = (bothPass + judgeFailHumanPass) / n;
        var chance = (judgePass * humanPass) + ((1 - judgePass) * (1 - humanPass));
        return chance >= 1 ? null : (observed - chance) / (1 - chance);
    }

    /// <summary>Rows of a semicolon-separated sheet with quoted fields (as written by Excel or by the export).</summary>
    public static List<string[]> ParseCsv(string text)
    {
        var rows = new List<string[]>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        text = text.TrimStart('﻿');
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == Separator)
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                fields.Add(field.ToString());
                field.Clear();
                if (fields.Any(value => value.Length > 0))
                {
                    rows.Add([.. fields]);
                }

                fields.Clear();
            }
            else
            {
                field.Append(c);
            }
        }

        fields.Add(field.ToString());
        if (fields.Any(value => value.Length > 0))
        {
            rows.Add([.. fields]);
        }

        return rows;
    }

    /// <summary>"pass"/"fail" from what a reviewer may type (also German), or <c>null</c> if empty or unclear.</summary>
    public static string? Normalize(string value) => value.Trim().ToLowerInvariant() switch
    {
        "pass" or "passed" or "p" or "bestanden" or "ja" or "yes" or "ok" => Judge.Pass,
        "fail" or "failed" or "f" or "nicht bestanden" or "nein" or "no" => Judge.Fail,
        _ => null,
    };

    /// <summary>Judge verdicts of the given runs with an opaque ID that reveals neither run nor server.</summary>
    private static IEnumerable<(string Id, RunTrace Trace, string Rubric, Judgement Judgement)> Verdicts(string benchDirectory, IReadOnlyList<string> runs)
    {
        foreach (var runId in runs)
        {
            foreach (var stored in TraceStore.Load(benchDirectory, runId))
            {
                var rubrics = Judge.ChecksOf(stored.Trace).ToDictionary(check => check.Index, check => check.Rubric);
                foreach (var judgement in stored.Trace.Judgements ?? [])
                {
                    if (judgement.Verdict is not null && rubrics.TryGetValue(judgement.CheckIndex, out var rubric))
                    {
                        yield return (OpaqueId(runId, stored.Trace, judgement.CheckIndex), stored.Trace, rubric, judgement);
                    }
                }
            }
        }
    }

    private static string OpaqueId(string runId, RunTrace trace, int checkIndex) =>
        "j-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{runId}/{trace.FileName}/{checkIndex}")))[..10];

    private static string Row(IEnumerable<string> fields) =>
        string.Join(Separator, fields.Select(field => "\"" + field.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"")) + "\r\n";

    private static string Report(string reviewFile, List<(string Id, string TaskId, string Judge, string Human, string? Reason, string Note)> pairs, int unlabeled, List<string> unknownIds)
    {
        int Count(string judge, string human) => pairs.Count(pair => pair.Judge == judge && pair.Human == human);
        var (pp, pf, fp, ff) = (Count(Judge.Pass, Judge.Pass), Count(Judge.Pass, Judge.Fail), Count(Judge.Fail, Judge.Pass), Count(Judge.Fail, Judge.Fail));
        var agree = pp + ff;
        var kappa = Kappa(pp, pf, fp, ff);
        var text = new StringBuilder();
        text.AppendLine("# Judge agreement (AK-9.2)").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Review sheet: `{Path.GetFileName(reviewFile)}`. Labeled verdicts: {pairs.Count} (unlabeled: {unlabeled}, unknown IDs: {unknownIds.Count}).").AppendLine();
        text.AppendLine(CultureInfo.InvariantCulture, $"Agreement: {agree}/{pairs.Count} ({(pairs.Count == 0 ? 0 : 100.0 * agree / pairs.Count):0.0} %), Cohen's κ = {(kappa is { } k ? k.ToString("0.00", CultureInfo.InvariantCulture) : "n/a")}.");
        text.AppendLine().AppendLine("| | human pass | human fail |").AppendLine("|---|---|---|");
        text.AppendLine(CultureInfo.InvariantCulture, $"| judge pass | {pp} | {pf} |");
        text.AppendLine(CultureInfo.InvariantCulture, $"| judge fail | {fp} | {ff} |");

        var disagreements = pairs.Where(pair => pair.Judge != pair.Human).ToList();
        text.AppendLine().AppendLine("## Disagreements").AppendLine();
        if (disagreements.Count == 0)
        {
            text.AppendLine("None.");
        }
        else
        {
            text.AppendLine("| ID | Task | Judge | Human | Judge's reason | Note |").AppendLine("|---|---|---|---|---|---|");
            foreach (var pair in disagreements)
            {
                text.AppendLine(CultureInfo.InvariantCulture, $"| {pair.Id} | {pair.TaskId} | {pair.Judge} | {pair.Human} | {Cell(pair.Reason)} | {Cell(pair.Note)} |");
            }
        }

        return text.ToString().ReplaceLineEndings("\n");
    }

    private static string Cell(string? text) => (text ?? "").Replace("|", "\\|", StringComparison.Ordinal).ReplaceLineEndings(" ");
}
