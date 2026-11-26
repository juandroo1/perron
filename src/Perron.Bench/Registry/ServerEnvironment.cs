using Microsoft.Extensions.Configuration;

namespace Perron.Bench.Registry;

/// <summary>Resolves what a registry server needs from the environment (<c>.env</c> via configuration).</summary>
public static class ServerEnvironment
{
    public static bool IsSet(IConfiguration configuration, string key) => !string.IsNullOrWhiteSpace(configuration[key]);

    /// <summary>Required keys that are not set.</summary>
    public static IReadOnlyList<string> MissingKeys(ServerDefinition server, IConfiguration configuration) =>
        [.. (server.EnvRequired ?? []).Where(key => !IsSet(configuration, key))];

    /// <summary>For HTTP servers: the URL from <see cref="ServerDefinition.UrlEnv"/> if set, else the registry's.</summary>
    public static Uri? Endpoint(ServerDefinition server, IConfiguration configuration) =>
        server.Transport != ServerDefinition.Http
            ? null
            : new Uri(
                server.UrlEnv is { } variable && IsSet(configuration, variable) ? configuration[variable]!.Trim() : server.Url!,
                UriKind.Absolute);

    /// <summary>Variables for a stdio server, taken from the environment under their names in <c>.env</c>.</summary>
    public static Dictionary<string, string?> Process(ServerDefinition server, IConfiguration configuration) =>
        (server.Env ?? new Dictionary<string, string>())
            .Where(mapping => IsSet(configuration, mapping.Value))
            .ToDictionary(mapping => mapping.Key, mapping => configuration[mapping.Value]);
}
