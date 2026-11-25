using System.Text.Json;
using System.Text.Json.Serialization;

namespace Perron.Bench.Settings;

/// <summary>Where Perron Bench finds <c>servers.json</c> and <c>bench.json</c>: <c>BENCH_DIR</c>, default <c>bench</c>.</summary>
public sealed class BenchDirectoryOptions
{
    public const string EnvironmentVariable = "BENCH_DIR";
    public const string DefaultDirectory = "bench";

    public string Directory { get; set; } = DefaultDirectory;

    public string File(string name) => Path.Combine(Directory, name);
}

/// <summary>
/// The versioned benchmark methodology in <c>bench/bench.json</c> (plan.md, section 6). Task 3.1 uses the models;
/// effort, max_tokens, pauses and the dry-run assumptions follow in task 3.3.
/// </summary>
public sealed record BenchSettings(string Model, string ControlModel)
{
    public const string FileName = "bench.json";

    /// <exception cref="InvalidDataException">The file is missing or incomplete.</exception>
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

        if (settings is null || string.IsNullOrWhiteSpace(settings.Model) || string.IsNullOrWhiteSpace(settings.ControlModel))
        {
            throw new InvalidDataException($"{path} needs model and control_model.");
        }

        return settings;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(BenchSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
