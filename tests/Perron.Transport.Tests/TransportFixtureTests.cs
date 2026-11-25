using System.Text.Json;

namespace Perron.Transport.Tests;

/// <summary>
/// Guards the recorded transport.opendata.ch responses (tests/fixtures/transport-opendata) that later tests build on.
/// </summary>
public class TransportFixtureTests
{
    private static readonly string Directory = Path.Combine(AppContext.BaseDirectory, "fixtures", "transport-opendata");

    public static TheoryData<string> ManifestFiles()
    {
        var data = new TheoryData<string>();
        foreach (var entry in LoadManifest())
        {
            data.Add(entry.File);
        }

        return data;
    }

    [Fact]
    public void Manifest_ListsEveryFixtureFile_AndNothingElse()
    {
        var listed = LoadManifest().Select(entry => entry.File).Order(StringComparer.Ordinal).ToArray();
        var onDisk = System.IO.Directory.GetFiles(Directory)
            .Select(Path.GetFileName)
            .Where(name => name != "manifest.json")
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(listed);
        Assert.Equal(onDisk, listed);
    }

    [Theory]
    [MemberData(nameof(ManifestFiles))]
    public void Fixture_IsJson_WithTheShapeOfItsEndpointAndStatus(string file)
    {
        var entry = LoadManifest().Single(e => e.File == file);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory, file)));
        var root = document.RootElement;

        if (entry.Status >= 400)
        {
            var errors = root.GetProperty("errors");
            Assert.True(errors.GetArrayLength() > 0);
            Assert.False(string.IsNullOrWhiteSpace(errors[0].GetProperty("message").GetString()));
            return;
        }

        var listProperty = entry.Endpoint switch
        {
            "locations" => "stations",
            "connections" => "connections",
            "stationboard" => "stationboard",
            _ => throw new InvalidOperationException($"Unknown endpoint '{entry.Endpoint}'."),
        };
        Assert.Equal(JsonValueKind.Array, root.GetProperty(listProperty).ValueKind);
    }

    [Fact]
    public void AmbiguousName_HasSeveralCandidates_AndConnectionsPickOneSilently()
    {
        using var locations = Load("locations-buchs.json");
        var names = locations.RootElement.GetProperty("stations").EnumerateArray()
            .Select(station => station.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("Buchs AG", names);
        Assert.Contains("Buchs SG", names);

        // The behaviour AK-2.4 guards against: the API resolves "Buchs" to one station without saying so.
        using var connections = Load("connections-buchs-ambiguous.json");
        Assert.Equal("Buchs AG", connections.RootElement.GetProperty("from").GetProperty("name").GetString());
    }

    [Fact]
    public void UnknownStation_IsAnEmptyResult_NotAnError()
    {
        using var locations = Load("locations-unknown.json");
        Assert.Equal(0, locations.RootElement.GetProperty("stations").GetArrayLength());

        using var stationboard = Load("stationboard-unknown.json");
        Assert.Equal(JsonValueKind.Null, stationboard.RootElement.GetProperty("station").ValueKind);
    }

    [Fact]
    public void Times_UseAnOffsetWithoutColon()
    {
        using var connections = Load("connections-zurich-bern-now.json");
        var departure = connections.RootElement.GetProperty("connections")[0]
            .GetProperty("from").GetProperty("departure").GetString();

        Assert.Matches(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}[+-]\d{4}$", departure);
    }

    private static JsonDocument Load(string file) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Directory, file)));

    private static IReadOnlyList<ManifestEntry> LoadManifest() =>
        JsonSerializer.Deserialize<List<ManifestEntry>>(
            File.ReadAllText(Path.Combine(Directory, "manifest.json")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
        ?? throw new InvalidOperationException("Empty manifest.");

    private sealed record ManifestEntry(string File, string Endpoint, int Status);
}
