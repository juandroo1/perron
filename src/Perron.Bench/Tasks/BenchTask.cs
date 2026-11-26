using System.Security.Cryptography;
using System.Text.Json;

namespace Perron.Bench.Tasks;

/// <summary>One task of <c>bench/tasks/*.jsonl</c> (plan.md, section 6). Checks stay raw JSON until task 3.4.</summary>
public sealed record BenchTask(
    string Id,
    string Prompt,
    string Language,
    string Category,
    IReadOnlyList<string> CapabilitiesRequired,
    bool Smoke,
    int? MaxToolCalls,
    JsonElement Checks);

/// <summary>A set of tasks for one run, with the SHA-256 of the file it came from (run manifest, AK-8.2).</summary>
public sealed record TaskSuite(string Name, string File, string Sha256, IReadOnlyList<BenchTask> Tasks);

public static class TaskSuites
{
    public const string Smoke = "smoke";
    public const string Full = "full";
    public const string Test = "test";

    public static IReadOnlyList<string> Names { get; } = [Smoke, Full, Test];

    /// <summary>The file a suite reads: <c>smoke</c> and <c>full</c> use the dev set, <c>test</c> the held-out set (task 4.3 only).</summary>
    public static string FileOf(string suite) => suite == Test ? "tasks/test.jsonl" : "tasks/dev.jsonl";

    /// <exception cref="InvalidDataException">The file is missing or a line is not a valid task.</exception>
    public static TaskSuite Load(string benchDirectory, string suite)
    {
        if (!Names.Contains(suite))
        {
            throw new InvalidDataException($"Unknown suite '{suite}' (known: {string.Join(", ", Names)}).");
        }

        var relative = FileOf(suite);
        var path = Path.Combine(benchDirectory, relative);
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"Task set not found: {path}.");
        }

        var bytes = File.ReadAllBytes(path);
        var tasks = Parse(bytes, relative);
        return new TaskSuite(
            suite,
            relative,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            suite == Smoke ? [.. tasks.Where(task => task.Smoke)] : tasks);
    }

    public static List<BenchTask> Parse(byte[] utf8, string name)
    {
        var text = System.Text.Encoding.UTF8.GetString(utf8);
        var tasks = new List<BenchTask>();
        var lineNumber = 0;
        foreach (var line in text.Split('\n'))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                tasks.Add(new BenchTask(
                    root.GetProperty("id").GetString()!,
                    root.GetProperty("prompt").GetString()!,
                    root.GetProperty("language").GetString()!,
                    root.GetProperty("category").GetString()!,
                    [.. root.GetProperty("capabilities_required").EnumerateArray().Select(capability => capability.GetString()!)],
                    root.TryGetProperty("smoke", out var smoke) && smoke.GetBoolean(),
                    root.TryGetProperty("max_tool_calls", out var max) && max.ValueKind == JsonValueKind.Number ? max.GetInt32() : null,
                    root.TryGetProperty("checks", out var checks) ? checks.Clone() : default));
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                // Name the line only: the content of the held-out set must not appear in output.
                throw new InvalidDataException($"{name} line {lineNumber} is not a valid task.", ex);
            }
        }

        return tasks;
    }
}
