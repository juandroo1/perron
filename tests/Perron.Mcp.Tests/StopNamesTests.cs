using System.Text.Json;

using Perron.Mcp.Tools;
using Perron.Transport;

namespace Perron.Mcp.Tests;

/// <summary>The matching rule against real stop searches (tests/fixtures, type=station).</summary>
public sealed class StopNamesTests
{
    [Theory]
    [InlineData("Zürich HB", "locations-zurich-hb.json", "8503000")]
    [InlineData("zurich hb", "locations-zurich-hb.json", "8503000")]
    [InlineData("Basel", "locations-basel-station.json", "8500010")]
    [InlineData("Fribourg", "locations-fribourg-station.json", "8504100")]
    [InlineData("Freiburg", "locations-fribourg-station.json", "8504100")]
    [InlineData("Genève", "locations-geneve.json", "8501008")]
    [InlineData("Geneve", "locations-geneve.json", "8501008")]
    public void Name_ResolvesToOneStop(string query, string fixture, string id)
    {
        var match = StopNames.Match(query, Load(fixture));

        Assert.Equal(id, Assert.IsType<StopMatch.Found>(match).Stop.Id);
    }

    [Theory]
    [InlineData("Buchs", "locations-buchs-station.json", "8502123,8509404,8503527")]
    [InlineData("Zürich", "locations-zurich-station.json", "8503000,8503006,8503020,8503003,8503016,8503001")]
    public void AmbiguousName_ListsThePlaceLevelCandidates(string query, string fixture, string ids)
    {
        var match = StopNames.Match(query, Load(fixture));

        var candidates = Assert.IsType<StopMatch.Ambiguous>(match).Candidates;
        Assert.Equal(ids.Split(','), candidates.Select(stop => stop.Id));
    }

    [Theory]
    [InlineData("Genf", "locations-genf-station.json", 8)]
    [InlineData("Xyzzyplatz", "locations-unknown.json", 0)]
    public void NameWithoutAStopStartingWithIt_IsNotFound(string query, string fixture, int similar)
    {
        var match = StopNames.Match(query, Load(fixture));

        Assert.Equal(similar, Assert.IsType<StopMatch.NotFound>(match).Similar.Count);
    }

    [Theory]
    [InlineData("Zürich HB", "Zürich HB", true)]
    [InlineData("zurich  hb", "Zürich HB", true)]
    [InlineData("Geneve", "Genève", true)]
    [InlineData("Freiburg", "Fribourg/Freiburg", true)]
    [InlineData("Buchs", "Buchs AG", false)]
    public void IsExactMatch_IgnoresCaseAccentsAndSpaces_AndAcceptsEachLanguage(string query, string name, bool expected) =>
        Assert.Equal(expected, StopNames.IsExactMatch(query, Stop(name)));

    [Theory]
    [InlineData("Buchs", "Buchs-Dällikon", true)]
    [InlineData("Buchs", "Buchs SG, Bahnhof", true)]
    [InlineData("Freiburg", "Fribourg/Freiburg, Pl. Gare", true)]
    [InlineData("Gen", "Genève", false)]
    [InlineData("Genf", "Bern, Galgenfeld", false)]
    public void StartsWithQuery_NeedsAWholeWord(string query, string name, bool expected) =>
        Assert.Equal(expected, StopNames.StartsWithQuery(query, Stop(name)));

    [Fact]
    public void OneCandidate_IsFound_EvenIfNamedDifferently() =>
        Assert.IsType<StopMatch.Found>(StopNames.Match("Buchs", [Stop("Buchs AG")]));

    private static Location Stop(string name) => new("1", name, null, null, null);

    private static List<Location> Load(string fixture)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "transport-opendata", fixture);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return
        [
            .. document.RootElement.GetProperty("stations").EnumerateArray()
                .Where(stop => stop.GetProperty("id").ValueKind == JsonValueKind.String)
                .Select(stop => new Location(
                    stop.GetProperty("id").GetString(),
                    stop.GetProperty("name").GetString()!,
                    null,
                    null,
                    stop.GetProperty("icon").GetString())),
        ];
    }
}
