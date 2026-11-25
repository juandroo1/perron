using Perron.Bench.Checks;

namespace Perron.Bench.Tests;

public class ToolListComparisonTests
{
    [Fact]
    public void Compare_SameSetInAnyOrder_Matches()
    {
        var comparison = ToolListComparison.Compare(["b", "a"], ["a", "b"]);

        Assert.True(comparison.Matches);
        Assert.Empty(comparison.Missing);
        Assert.Empty(comparison.Unexpected);
    }

    [Fact]
    public void Compare_ReportsMissingAndUnexpected_Sorted()
    {
        var comparison = ToolListComparison.Compare(["find_stops", "plan_trip"], ["ping", "plan_trip", "debug"]);

        Assert.False(comparison.Matches);
        Assert.Equal(["find_stops"], comparison.Missing);
        Assert.Equal(["debug", "ping"], comparison.Unexpected);
    }

    [Fact]
    public void Compare_IsCaseSensitive()
    {
        var comparison = ToolListComparison.Compare(["Ping"], ["ping"]);

        Assert.False(comparison.Matches);
    }
}
