namespace Perron.Bench.Tests;

public class AssemblySmokeTests
{
    [Fact]
    public void Assembly_IsReferencedAndLoads()
    {
        var name = typeof(AssemblyMarker).Assembly.GetName().Name;

        Assert.Equal("Perron.Bench", name);
    }
}
