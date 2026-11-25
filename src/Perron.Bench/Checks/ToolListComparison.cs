namespace Perron.Bench.Checks;

/// <summary>Difference between the expected and the actual tool names of a server.</summary>
public sealed record ToolListComparison(IReadOnlyList<string> Missing, IReadOnlyList<string> Unexpected)
{
    public bool Matches => Missing.Count == 0 && Unexpected.Count == 0;

    public static ToolListComparison Compare(IEnumerable<string> expected, IEnumerable<string> actual)
    {
        var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
        var actualSet = actual.ToHashSet(StringComparer.Ordinal);

        return new ToolListComparison(
            [.. expectedSet.Except(actualSet).Order(StringComparer.Ordinal)],
            [.. actualSet.Except(expectedSet).Order(StringComparer.Ordinal)]);
    }
}
