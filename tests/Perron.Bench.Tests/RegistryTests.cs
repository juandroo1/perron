using System.Text.Json;

using Perron.Bench.Checks;
using Perron.Bench.Registry;
using Perron.Bench.Settings;

namespace Perron.Bench.Tests;

/// <summary>bench/servers.json (AK-7.1, AK-9.3) and bench/bench.json.</summary>
public sealed class RegistryTests
{
    private static readonly string BenchDirectory = Path.Combine(AppContext.BaseDirectory, "bench");
    private static readonly string Fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures", "foreign-servers");

    [Fact]
    public void Registry_LoadsAndListsTheServersOfSpecSection5()
    {
        var registry = ServerRegistry.Load(Path.Combine(BenchDirectory, ServerRegistry.FileName));

        Assert.Equal(
            ["perron-rich", "perron-minimal", "perron-json", "perron-many", "official", "spt", "stm"],
            registry.Servers.Select(server => server.Id));
        Assert.Equal(["stm"], registry.Servers.Where(server => server.Optional).Select(server => server.Id));
        Assert.Equal(["official"], registry.Servers.Where(server => server.Hosted).Select(server => server.Id));
    }

    [Fact]
    public void Official_ToolsHash_IsTheHashOfTheRecordedToolsList()
    {
        var registry = ServerRegistry.Load(Path.Combine(BenchDirectory, ServerRegistry.FileName));
        using var snapshot = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixtures, "official.json")));

        var hash = ToolListHash.Compute(snapshot.RootElement.GetProperty("tools"));
        Assert.True(hash == registry.Find("official")!.ToolsHash, $"tools_hash of official in servers.json must be {hash}");
    }

    [Theory]
    [InlineData("perron-rich", "stops,trips,departures")]
    [InlineData("official", "stops,trips,departures")]
    [InlineData("spt", "stops,trips,departures,occupancy")]
    [InlineData("stm", "stops,trips,departures,occupancy,disruptions")]
    public void Capabilities_AreFixedWithReasons(string id, string capabilities)
    {
        var server = ServerRegistry.Load(Path.Combine(BenchDirectory, ServerRegistry.FileName)).Find(id)!;

        Assert.Equal(capabilities.Split(','), server.Capabilities.Select(capability => capability.Name));
        Assert.All(server.Capabilities, capability => Assert.False(string.IsNullOrWhiteSpace(capability.Reason)));
    }

    [Fact]
    public void Stm_NeedsItsKey_AndExtraKeysForOccupancyAndDisruptions()
    {
        var stm = ServerRegistry.Load(Path.Combine(BenchDirectory, ServerRegistry.FileName)).Find("stm")!;

        Assert.Equal(["STM_TRANSPORT_API_KEY"], stm.EnvRequired);
        Assert.Equal("STM_TRANSPORT_API_KEY", stm.Env!["TRANSPORT_API_KEY"]);
        Assert.Equal(["STM_OCCUPANCY_API_KEY"], stm.Capabilities.Single(c => c.Name == "occupancy").RequiresEnv);
        Assert.Equal(["STM_SIRI_SX_API_KEY"], stm.Capabilities.Single(c => c.Name == "disruptions").RequiresEnv);
    }

    [Fact]
    public void BenchSettings_NameTheModels()
    {
        var settings = BenchSettings.Load(Path.Combine(BenchDirectory, BenchSettings.FileName));

        Assert.Equal("claude-haiku-5-5", settings.Model);
        Assert.Equal("claude-sonnet-5-5", settings.ControlModel);
    }

    public static TheoryData<string, string> BrokenServers() => new()
    {
        { """{ "id": "a", "transport": "http", "version": "1", "capabilities": [{ "name": "stops", "reason": "r" }] }""", "http servers need an absolute http(s) url" },
        { """{ "id": "a", "transport": "stdio", "version": "1", "capabilities": [{ "name": "stops", "reason": "r" }] }""", "stdio servers need a command" },
        { """{ "id": "a", "transport": "ws", "version": "1", "capabilities": [{ "name": "stops", "reason": "r" }] }""", "transport must be" },
        { """{ "id": "A b", "transport": "stdio", "command": "x", "version": "1", "capabilities": [{ "name": "stops", "reason": "r" }] }""", "id must be lower case" },
        { """{ "id": "a", "transport": "http", "url": "https://x/mcp", "hosted": true, "version": "1", "capabilities": [{ "name": "stops", "reason": "r" }] }""", "hosted servers cannot pin a version" },
        { """{ "id": "a", "transport": "http", "url": "https://x/mcp", "hosted": true, "capabilities": [{ "name": "stops", "reason": "r" }] }""", "hosted servers need tools_hash" },
        { """{ "id": "a", "transport": "stdio", "command": "x", "capabilities": [{ "name": "stops", "reason": "r" }] }""", "need a fixed version" },
        { """{ "id": "a", "transport": "stdio", "command": "x", "version": "1", "capabilities": [] }""", "capabilities must not be empty" },
        { """{ "id": "a", "transport": "stdio", "command": "x", "version": "1", "capabilities": [{ "name": "weather", "reason": "r" }] }""", "unknown capability 'weather'" },
        { """{ "id": "a", "transport": "stdio", "command": "x", "version": "1", "capabilities": [{ "name": "stops", "reason": " " }] }""", "capability 'stops' needs a reason" },
        { """{ "id": "a", "transport": "stdio", "command": "x", "version": "1", "env_required": ["lower"], "capabilities": [{ "name": "stops", "reason": "r" }] }""", "'lower' is not an environment variable name" },
    };

    [Theory]
    [MemberData(nameof(BrokenServers))]
    public void BrokenRegistry_IsRejected_WithTheRule(string server, string rule)
    {
        var path = Path.Combine(Path.GetTempPath(), $"servers-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, $$"""{ "servers": [ {{server}} ] }""");
        try
        {
            var error = Assert.Throws<InvalidDataException>(() => ServerRegistry.Load(path));
            Assert.Contains(rule, error.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DuplicateIds_AreRejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"servers-{Guid.NewGuid():N}.json");
        const string Server = """{ "id": "a", "transport": "stdio", "command": "x", "version": "1", "capabilities": [{ "name": "stops", "reason": "r" }] }""";
        File.WriteAllText(path, $$"""{ "servers": [ {{Server}}, {{Server}} ] }""");
        try
        {
            Assert.Contains("Server id 'a' is used more than once", Assert.Throws<InvalidDataException>(() => ServerRegistry.Load(path)).Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
