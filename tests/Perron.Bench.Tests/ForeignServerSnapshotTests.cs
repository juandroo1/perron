using System.Text.Json;

namespace Perron.Bench.Tests;

/// <summary>
/// Guards the recorded initialize/tools/list snapshots of the third-party servers (tests/fixtures/foreign-servers),
/// the reference for bench check and the tools/list hash (spec.md, section 5).
/// </summary>
public class ForeignServerSnapshotTests
{
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "foreign-servers");

    [Theory]
    [InlineData("official", 4, "find_stations")]
    [InlineData("spt", 4, "plan_journey")]
    [InlineData("stm", 11, "transport_trip_plan")]
    public void Snapshot_HasServerInfoAndTheRecordedTools(string server, int toolCount, string sampleTool)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory, $"{server}.json")));
        var root = document.RootElement;

        Assert.Equal(server, root.GetProperty("server").GetString());
        Assert.False(string.IsNullOrWhiteSpace(
            root.GetProperty("initialize").GetProperty("serverInfo").GetProperty("name").GetString()));

        var toolNames = root.GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString())
            .ToArray();
        Assert.Equal(toolCount, toolNames.Length);
        Assert.Equal(toolNames.Length, toolNames.Distinct().Count());
        Assert.Contains(sampleTool, toolNames);
    }

    [Theory]
    [InlineData("spt", "0.1.10", "--exclude-newer 2026-05-16T00:00:00Z")]
    [InlineData("stm", "0.5.0", "--exclude-newer 2026-09-27T00:00:00Z")]
    public void PythonServer_IsPinned_WithReleaseDateDependencies(string server, string version, string excludeNewer)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory, $"{server}.json")));
        var root = document.RootElement;

        Assert.Equal(version, root.GetProperty("version").GetString());
        var start = root.GetProperty("start").GetString();
        Assert.Contains(excludeNewer, start);
        Assert.Contains($"=={version}", start);
    }
}
