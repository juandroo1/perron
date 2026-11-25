using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;

using Perron.Bench.Checks;
using Perron.Bench.Registry;

namespace Perron.Bench.Tests;

/// <summary>
/// The third-party Python servers in the bench image are pinned completely (plan.md, decision log): every package
/// in bench/python/&lt;id&gt;.txt has an exact version and SHA-256 hashes, and the server package matches the registry.
/// </summary>
public sealed partial class PythonServerPinTests
{
    [Theory]
    [InlineData("spt", "swiss-public-transport-mcp", "0.1.10", "2026-05-16T00:00:00Z")]
    [InlineData("stm", "swiss-transport-mcp", "0.5.0", "2026-09-27T00:00:00Z")]
    public void Requirements_PinEveryPackage_WithHashes(string server, string package, string version, string excludeNewer)
    {
        var text = File.ReadAllText(RepositoryFile($"bench/python/{server}.txt"));
        var requirements = Requirement().Matches(text).ToList();

        Assert.NotEmpty(requirements);
        Assert.Equal(Regex.Matches(text, @"(?m)^[a-z0-9][a-z0-9._-]*==").Count, requirements.Count);
        Assert.All(requirements, requirement => Assert.Contains("--hash=sha256:", requirement.Groups["hashes"].Value));
        Assert.Contains(requirements, requirement => requirement.Groups["name"].Value == package && requirement.Groups["version"].Value == version);
        Assert.DoesNotMatch(@"(?m)^[A-Za-z0-9_.\-\[\]]+\s*(>=|<=|~=|>|<)", text);
        Assert.Contains($"--exclude-newer {excludeNewer}", text);

        var registry = ServerRegistry.Load(RepositoryFile("bench/servers.json"));
        Assert.Equal($"{package} {version}", registry.Find(server)!.Version);
        Assert.Equal($"/opt/mcp/{server}/bin/{package}", registry.Find(server)!.Command);
    }

    /// <summary>
    /// The servers installed from these files offer exactly the tools recorded in task 1.1 (hash measured with
    /// <c>bench check</c> in the bench image on 2026-11-25).
    /// </summary>
    [Theory]
    [InlineData("spt", "4052b02f3b2b")]
    [InlineData("stm", "ee893ae9e2e1")]
    public void RecordedToolsList_HasTheHashOfTheInstalledServer(string server, string hashPrefix)
    {
        using var snapshot = JsonDocument.Parse(File.ReadAllText(RepositoryFile($"tests/fixtures/foreign-servers/{server}.json")));

        Assert.StartsWith(hashPrefix, ToolListHash.Compute(snapshot.RootElement.GetProperty("tools")));
    }

    [Fact]
    public void Dockerfile_InstallsEveryStdioServer_WithHashesRequired()
    {
        var dockerfile = File.ReadAllText(RepositoryFile("src/Perron.Bench/Dockerfile"));
        var stdio = ServerRegistry.Load(RepositoryFile("bench/servers.json")).Servers.Where(server => server.Transport == ServerDefinition.Stdio);

        Assert.Contains("--require-hashes --no-deps", dockerfile);
        Assert.Contains("FROM ghcr.io/astral-sh/uv:0.13.7", dockerfile);
        foreach (var server in stdio)
        {
            Assert.Matches($@"for server in [a-z ]*\b{server.Id}\b", dockerfile);
        }
    }

    private static string RepositoryFile(string path, [CallerFilePath] string callerFile = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFile)!, "..", "..", path));

    /// <summary>"name==version \ --hash=sha256:… …" up to the next requirement.</summary>
    [GeneratedRegex(@"(?m)^(?<name>[a-z0-9][a-z0-9._-]*)==(?<version>[^\s\\;]+)(?<hashes>(?:[^\n]*\n\s+--hash=sha256:[0-9a-f]{64})+)")]
    private static partial Regex Requirement();
}
