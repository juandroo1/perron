using System.Diagnostics;
using System.Reflection;

using Microsoft.Extensions.Configuration;

namespace Perron.Bench.Run;

/// <summary>Version information for the run manifest (AK-8.2).</summary>
public static class GitInfo
{
    /// <summary>Set at image build time (build arg <c>GIT_SHA</c>); outside Docker read from git.</summary>
    public const string ShaVariable = "GIT_SHA";

    public static (string Sha, bool? Dirty) Read(IConfiguration configuration)
    {
        var configured = configuration[ShaVariable];
        if (!string.IsNullOrWhiteSpace(configured) && configured != "unknown")
        {
            return (configured.Trim(), null);
        }

        var sha = Git("rev-parse HEAD");
        if (sha is null)
        {
            return ("unknown", null);
        }

        var status = Git("status --porcelain");
        return (sha, status is null ? null : status.Length > 0);
    }

    public static string HarnessVersion()
    {
        var assembly = typeof(GitInfo).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
    }

    private static string? Git(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("git", arguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);
            return process.ExitCode == 0 ? output : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
