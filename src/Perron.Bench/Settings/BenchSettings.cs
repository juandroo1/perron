using System.Text.Json;
using System.Text.Json.Serialization;

namespace Perron.Bench.Settings;

/// <summary>Where Perron Bench finds <c>servers.json</c>, <c>bench.json</c>, tasks and results: <c>BENCH_DIR</c>, default <c>bench</c>.</summary>
public sealed class BenchDirectoryOptions
{
    public const string EnvironmentVariable = "BENCH_DIR";
    public const string DefaultDirectory = "bench";

    public string Directory { get; set; } = DefaultDirectory;

    public string File(string name) => Path.Combine(Directory, name);
}

/// <summary>USD per million tokens of one model.</summary>
public sealed record ModelPricing(decimal Input, decimal Output, decimal CacheWrite, decimal CacheRead);

/// <summary>Fixed assumptions of <c>--dry-run</c> before earlier runs exist (AK-8.3).</summary>
public sealed record DryRunAssumptions(double Turns, double ToolResultTokens, double OutputTokensPerTurn);

/// <summary>The versioned benchmark methodology in <c>bench/bench.json</c> (plan.md, section 6; AK-8.1).</summary>
public sealed record BenchSettings(
    string Model,
    string ControlModel,
    string Effort,
    int MaxTokens,
    double PauseSeconds,
    int DefaultMaxToolCalls,
    IReadOnlyDictionary<string, ModelPricing> PricingUsdPerMtok,
    DryRunAssumptions DryRunAssumptions)
{
    public const string FileName = "bench.json";

    public static IReadOnlyList<string> EffortLevels { get; } = ["low", "medium", "high", "max"];

    /// <exception cref="InvalidDataException">The file is missing, not valid or incomplete.</exception>
    public static BenchSettings Load(string path)
    {
        if (!System.IO.File.Exists(path))
        {
            throw new InvalidDataException($"Benchmark settings not found: {path}.");
        }

        BenchSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize(System.IO.File.ReadAllText(path), SettingsJsonContext.Default.BenchSettings);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path} is not valid: {ex.Message}", ex);
        }

        var errors = settings is null ? ["empty file"] : Validate(settings).ToList();
        if (errors.Count > 0)
        {
            throw new InvalidDataException($"{path}: {string.Join(" ", errors)}");
        }

        return settings!;
    }

    public static IEnumerable<string> Validate(BenchSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Model) || string.IsNullOrWhiteSpace(settings.ControlModel))
        {
            yield return "model and control_model are required.";
        }

        if (!EffortLevels.Contains(settings.Effort))
        {
            yield return $"effort must be one of {string.Join(", ", EffortLevels)}.";
        }

        if (settings.MaxTokens < 1)
        {
            yield return "max_tokens must be positive.";
        }

        if (settings.PauseSeconds < 0)
        {
            yield return "pause_seconds must not be negative.";
        }

        if (settings.DefaultMaxToolCalls < 1)
        {
            yield return "default_max_tool_calls must be positive.";
        }

        foreach (var model in new[] { settings.Model, settings.ControlModel }.Where(model => model is not null))
        {
            if (settings.PricingUsdPerMtok?.ContainsKey(model) != true)
            {
                yield return $"pricing_usd_per_mtok needs prices for {model}.";
            }
        }

        if (settings.DryRunAssumptions is not { Turns: > 0, ToolResultTokens: >= 0, OutputTokensPerTurn: > 0 })
        {
            yield return "dry_run_assumptions needs turns, tool_result_tokens and output_tokens_per_turn.";
        }
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(BenchSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
