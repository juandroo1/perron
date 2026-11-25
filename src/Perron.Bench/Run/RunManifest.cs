using Perron.Bench.Settings;

namespace Perron.Bench.Run;

/// <summary>A server as it was during a run: what it reported and what the model saw of it.</summary>
public sealed record ManifestServer(
    string Id,
    string? Version,
    bool Hosted,
    string? ServerName,
    string? ServerVersion,
    string? ProtocolVersion,
    IReadOnlyList<string> Tools,
    string ToolsHash,
    long? ToolTokens,
    string InstructionsSha256,
    IReadOnlyList<string> CapabilitiesCovered,
    IReadOnlyList<string> CapabilitiesNotCovered);

/// <summary>
/// Run manifest (AK-8.2): everything needed to say what was measured. Written at the start of a run and again with
/// <see cref="FinishedAt"/> and the counts at its end.
/// </summary>
public sealed record RunManifest(
    string RunId,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string GitSha,
    bool? GitDirty,
    string HarnessVersion,
    string Suite,
    string TaskFile,
    string TaskSetSha256,
    int TaskCount,
    int Repeats,
    int Seed,
    string Model,
    string Effort,
    int MaxTokens,
    double PauseSeconds,
    int DefaultMaxToolCalls,
    ModelPricing Pricing,
    string SystemPromptTemplateSha256,
    IReadOnlyList<ManifestServer> Servers,
    IReadOnlyDictionary<string, int>? StatusCounts = null,
    decimal? TotalCostUsd = null);
