namespace Perron.Bench.Registry;

/// <summary>Which capabilities of a server count as covered with the keys at hand (AK-9.3).</summary>
public static class ServerCoverage
{
    /// <param name="isSet">Whether a key (name in <c>.env</c>) is set.</param>
    /// <returns>Covered capability names, and for the others the keys they lack.</returns>
    public static (IReadOnlyList<string> Covered, IReadOnlyList<(string Name, IReadOnlyList<string> MissingKeys)> NotCovered) Of(
        ServerDefinition server, Func<string, bool> isSet)
    {
        var covered = new List<string>();
        var notCovered = new List<(string, IReadOnlyList<string>)>();
        foreach (var capability in server.Capabilities)
        {
            var missing = (capability.RequiresEnv ?? []).Where(key => !isSet(key)).ToList();
            if (missing.Count == 0)
            {
                covered.Add(capability.Name);
            }
            else
            {
                notCovered.Add((capability.Name, missing));
            }
        }

        return (covered, notCovered);
    }

    /// <summary>Capabilities a task requires that the server does not cover.</summary>
    public static IReadOnlyList<string> Missing(IReadOnlyList<string> required, IReadOnlyList<string> covered) =>
        [.. required.Where(capability => !covered.Contains(capability))];
}
