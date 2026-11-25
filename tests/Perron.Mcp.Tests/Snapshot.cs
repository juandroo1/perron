using System.Runtime.CompilerServices;

namespace Perron.Mcp.Tests;

/// <summary>
/// Compares a tool result with <c>Snapshots/&lt;name&gt;.txt</c> next to this file. On a difference (or a missing
/// snapshot) the actual text is written to <c>&lt;name&gt;.received.txt</c> for review; rename it to accept it.
/// </summary>
public static class Snapshot
{
    public static void Match(string name, string actual, string extension = "txt", [CallerFilePath] string callerFile = "")
    {
        var directory = Path.Combine(Path.GetDirectoryName(callerFile)!, "Snapshots");
        var expectedPath = Path.Combine(directory, $"{name}.{extension}");
        var receivedPath = Path.Combine(directory, $"{name}.received.{extension}");

        var expected = File.Exists(expectedPath) ? File.ReadAllText(expectedPath).ReplaceLineEndings("\n") : null;
        if (expected == actual)
        {
            File.Delete(receivedPath);
            return;
        }

        System.IO.Directory.CreateDirectory(directory);
        File.WriteAllText(receivedPath, actual);
        Assert.Fail(expected is null
            ? $"Snapshot {name}.{extension} is missing; wrote {name}.received.{extension} for review."
            : $"Snapshot {name}.{extension} differs; wrote {name}.received.{extension}.{Environment.NewLine}Expected:{Environment.NewLine}{expected}{Environment.NewLine}Actual:{Environment.NewLine}{actual}");
    }
}
