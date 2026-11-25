using System.Text.RegularExpressions;

using ModelContextProtocol;

using Perron.Transport;

namespace Perron.Mcp.Tools;

/// <summary>Turns a stop given as name or ID into a stop ID (AK-2.4). Shared by the tools that take stops.</summary>
public static partial class StopResolver
{
    /// <summary>Most candidates shown for an ambiguous or unknown name.</summary>
    public const int MaxCandidates = 8;

    /// <summary>
    /// Resolves <paramref name="input"/>. An ID is used as is; a name is searched with <c>/locations?type=station</c>
    /// (cached for 24 h) and matched with <see cref="StopNames.Match"/>.
    /// </summary>
    /// <param name="role">Parameter the input came from, e.g. <c>from</c>; used in messages.</param>
    /// <param name="stopSearchTool">The variant's tool for stop searches, named in messages.</param>
    /// <exception cref="McpException">No stop is named like the input.</exception>
    public static async Task<StopResolution> ResolveAsync(
        ITransportApiClient transport, string input, string role, string stopSearchTool, CancellationToken cancellationToken)
    {
        var trimmed = input.Trim();
        if (StopId().IsMatch(trimmed))
        {
            return new StopResolution.Resolved(role, trimmed, null);
        }

        var found = await transport.SearchLocationsAsync(
            LocationQuery.ByName(trimmed) with { Type = LocationType.Station }, cancellationToken);
        var candidates = found.Where(stop => !string.IsNullOrEmpty(stop.Id)).ToList();

        return StopNames.Match(trimmed, candidates) switch
        {
            StopMatch.Found(var stop) => new StopResolution.Resolved(role, stop.Id!, stop.Name),
            StopMatch.Ambiguous(var options) => new StopResolution.Ambiguous(role, trimmed, [.. options.Take(MaxCandidates)]),
            StopMatch.NotFound(var similar) => throw NotFound(trimmed, role, similar, stopSearchTool),
            _ => throw new InvalidOperationException("Unknown stop match."),
        };
    }

    /// <summary>Lines listing the candidates of an ambiguous name, for a tool result.</summary>
    public static IEnumerable<string> Describe(StopResolution.Ambiguous ambiguous) =>
    [
        $"\"{ambiguous.Input}\" ({ambiguous.Role}) matches several stops (ID | name | kind):",
        .. ambiguous.Candidates.Select(stop => $"{stop.Id} | {stop.Name} | {stop.Icon ?? "-"}"),
    ];

    private static McpException NotFound(string input, string role, IReadOnlyList<Location> similar, string stopSearchTool)
    {
        if (similar.Count == 0)
        {
            return new McpException(
                $"No stop found for \"{input}\" ({role}). Check the spelling, or search with {stopSearchTool} and use the stop's ID.");
        }

        var names = string.Join("; ", similar.Take(MaxCandidates).Select(stop => $"{stop.Id} {stop.Name}"));
        return new McpException(
            $"No stop is named \"{input}\" ({role}). Similar stops (ID name): {names}. "
            + $"Use one of these IDs if it is the right stop, or search with {stopSearchTool}.");
    }

    /// <summary>Stop IDs are numbers, e.g. 8503000 (Zürich HB) or 1200021 (a stop in Austria).</summary>
    [GeneratedRegex(@"^\d{6,9}$")]
    private static partial Regex StopId();
}

/// <summary>Outcome of <see cref="StopResolver.ResolveAsync"/>.</summary>
public abstract record StopResolution(string Role)
{
    /// <summary>The stop to query. <see cref="Name"/> is <c>null</c> when the input was an ID.</summary>
    public sealed record Resolved(string Role, string Id, string? Name) : StopResolution(Role)
    {
        /// <summary>"Basel SBB (8500010)", or just the ID.</summary>
        public string Display => Name is null ? Id : $"{Name} ({Id})";
    }

    /// <summary>The name could mean any of <see cref="Candidates"/>; the agent should ask or pick an ID.</summary>
    public sealed record Ambiguous(string Role, string Input, IReadOnlyList<Location> Candidates) : StopResolution(Role);
}
