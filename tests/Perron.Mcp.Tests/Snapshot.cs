using System.Runtime.CompilerServices;

namespace Perron.Mcp.Tests;

/// <summary>
/// Compares a tool result with <c>Snapshots/&lt;name&gt;.txt</c> next to this file. On a difference (or a missing
/// snapshot) the actual text is written to <c>&lt;name&gt;.received.txt</c> for review; rename it to accept it.
/// </summary>
public static class Snapshot
{
    public static void Match(string name, string actual, [CallerFilePath] string callerFile = "")
    {
        var directory = Path.Combine(Path.GetDirectoryName(callerFile)!, "Snapshots");
        var expectedPath = Path.Combine(directory, $"{name}.txt");
        var receivedPath = Path.Combine(directory, $"{name}.received.txt");

        var expected = File.Exists(expectedPath) ? File.ReadAllText(expectedPath).ReplaceLineEndings("\n") : null;
        if (expected == actual)
        {
            File.Delete(receivedPath);
            return;
        }

        System.IO.Directory.CreateDirectory(directory);
        File.WriteAllText(receivedPath, actual);
        Assert.Fail(expected is null
            ? $"Snapshot {name}.txt is missing; wrote {name}.received.txt for review."
            : $"Snapshot {name}.txt differs; wrote {name}.received.txt.{Environment.NewLine}Expected:{Environment.NewLine}{expected}{Environment.NewLine}Actual:{Environment.NewLine}{actual}");
    }
}
