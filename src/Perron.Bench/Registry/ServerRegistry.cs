using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Perron.Bench.Registry;

/// <summary>Capabilities a task can require and a server can offer (plan.md, section 6).</summary>
public static class Capabilities
{
    public static IReadOnlyList<string> All { get; } = ["stops", "trips", "departures", "occupancy", "disruptions"];
}

/// <summary>A capability of a server, with the reason it counts as covered (AK-9.3).</summary>
/// <param name="RequiresEnv">Keys (names in <c>.env</c>) without which the capability is not covered.</param>
public sealed record CapabilityDefinition(string Name, string Reason, IReadOnlyList<string>? RequiresEnv = null);

/// <summary>One MCP server of the benchmark (AK-7.1).</summary>
/// <param name="Url">For <c>http</c>: the endpoint.</param>
/// <param name="UrlEnv">For <c>http</c>: environment variable that overrides <see cref="Url"/>.</param>
/// <param name="Command">For <c>stdio</c>: the program to start.</param>
/// <param name="Env">For <c>stdio</c>: variables for the process, mapped from names in <c>.env</c> (process name → <c>.env</c> name).</param>
/// <param name="Version">Fixed version; <c>null</c> for hosted servers, which are tracked by <see cref="ToolsHash"/>.</param>
/// <param name="ToolsHash">For hosted servers: the <c>tools/list</c> hash measured when the registry was written.</param>
/// <param name="EnvRequired">Keys (names in <c>.env</c>) the server cannot start without.</param>
public sealed record ServerDefinition(
    string Id,
    string Transport,
    string? Url,
    string? UrlEnv,
    string? Command,
    IReadOnlyList<string>? Args,
    IReadOnlyDictionary<string, string>? Env,
    string? Version,
    bool Hosted,
    bool Optional,
    IReadOnlyList<string>? EnvRequired,
    string? ToolsHash,
    IReadOnlyList<CapabilityDefinition> Capabilities,
    string? Notes = null)
{
    public const string Http = "http";
    public const string Stdio = "stdio";
}

/// <summary>Contents of <c>bench/servers.json</c>.</summary>
public sealed record ServerRegistryFile(IReadOnlyList<ServerDefinition> Servers);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(ServerRegistryFile))]
internal sealed partial class RegistryJsonContext : JsonSerializerContext;

/// <summary>The servers of <c>bench/servers.json</c>, validated when loaded.</summary>
public sealed partial class ServerRegistry
{
    public const string FileName = "servers.json";

    private ServerRegistry(IReadOnlyList<ServerDefinition> servers) => Servers = servers;

    public IReadOnlyList<ServerDefinition> Servers { get; }

    public ServerDefinition? Find(string id) => Servers.FirstOrDefault(server => server.Id == id);

    /// <exception cref="InvalidDataException">The file is missing, not valid JSON or breaks a rule.</exception>
    public static ServerRegistry Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"Server registry not found: {path}.");
        }

        ServerRegistryFile? file;
        try
        {
            file = JsonSerializer.Deserialize(File.ReadAllText(path), RegistryJsonContext.Default.ServerRegistryFile);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{path} is not a valid server registry: {ex.Message}", ex);
        }

        var servers = file?.Servers ?? [];
        var errors = Validate(servers).ToList();
        if (errors.Count > 0)
        {
            throw new InvalidDataException($"{path}: {string.Join(" ", errors)}");
        }

        return new ServerRegistry(servers);
    }

    public static IEnumerable<string> Validate(IReadOnlyList<ServerDefinition> servers)
    {
        if (servers.Count == 0)
        {
            yield return "No servers.";
        }

        foreach (var duplicate in servers.GroupBy(server => server.Id).Where(group => group.Count() > 1))
        {
            yield return $"Server id '{duplicate.Key}' is used more than once.";
        }

        foreach (var server in servers)
        {
            foreach (var error in Validate(server))
            {
                yield return $"{server.Id}: {error}";
            }
        }
    }

    private static IEnumerable<string> Validate(ServerDefinition server)
    {
        if (!ServerId().IsMatch(server.Id ?? ""))
        {
            yield return "id must be lower case letters, digits and '-'.";
        }

        switch (server.Transport)
        {
            case ServerDefinition.Http:
                if (!Uri.TryCreate(server.Url, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
                {
                    yield return "http servers need an absolute http(s) url.";
                }

                if (server.Command is not null || server.Args is { Count: > 0 } || server.Env is { Count: > 0 })
                {
                    yield return "http servers take no command, args or env.";
                }

                break;

            case ServerDefinition.Stdio:
                if (string.IsNullOrWhiteSpace(server.Command))
                {
                    yield return "stdio servers need a command.";
                }

                if (server.Url is not null || server.UrlEnv is not null)
                {
                    yield return "stdio servers take no url.";
                }

                break;

            default:
                yield return $"transport must be '{ServerDefinition.Http}' or '{ServerDefinition.Stdio}'.";
                break;
        }

        if (server.Hosted)
        {
            if (server.Version is not null)
            {
                yield return "hosted servers cannot pin a version; record tools_hash instead.";
            }

            if (server.ToolsHash is null || !Sha256().IsMatch(server.ToolsHash))
            {
                yield return "hosted servers need tools_hash (64 lower-case hex digits).";
            }
        }
        else if (string.IsNullOrWhiteSpace(server.Version))
        {
            yield return "servers that are not hosted need a fixed version.";
        }

        var capabilities = server.Capabilities ?? [];
        if (capabilities.Count == 0)
        {
            yield return "capabilities must not be empty.";
        }

        foreach (var capability in capabilities)
        {
            if (!Capabilities.All.Contains(capability.Name))
            {
                yield return $"unknown capability '{capability.Name}' (known: {string.Join(", ", Capabilities.All)}).";
            }

            if (string.IsNullOrWhiteSpace(capability.Reason))
            {
                yield return $"capability '{capability.Name}' needs a reason.";
            }
        }

        foreach (var duplicate in capabilities.GroupBy(capability => capability.Name).Where(group => group.Count() > 1))
        {
            yield return $"capability '{duplicate.Key}' is listed more than once.";
        }

        var keys = (server.EnvRequired ?? [])
            .Concat(capabilities.SelectMany(capability => capability.RequiresEnv ?? []))
            .Concat(server.Env?.Values ?? []);
        foreach (var key in keys.Where(key => !EnvName().IsMatch(key)).Distinct())
        {
            yield return $"'{key}' is not an environment variable name.";
        }
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex ServerId();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();

    [GeneratedRegex("^[A-Z][A-Z0-9_]*$")]
    private static partial Regex EnvName();
}
