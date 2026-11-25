using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using Perron.Transport;

namespace Perron.Bench.Run;

/// <summary>
/// The system prompt, identical for every server (AK-8.1; plan.md, decision log). Only the reference time and the
/// server's own instructions are filled in; both go into fixed places.
/// </summary>
public static class SystemPrompt
{
    public const string Template =
        """
        You help people with public transport in Switzerland, using the tools available to you.

        Current date and time: {reference_time} (Europe/Zurich). Dates such as "today" or "tomorrow" refer to this time.

        Answer briefly and with concrete times. Answer in the language of the question.

        ## Server instructions
        {server_instructions}
        """;

    /// <summary>SHA-256 of <see cref="Template"/> for the run manifest.</summary>
    public static string TemplateSha256 { get; } = Sha256(Template.ReplaceLineEndings("\n"));

    public static string Build(DateTimeOffset referenceTime, string? serverInstructions) =>
        Template.ReplaceLineEndings("\n")
            .Replace("{reference_time}", Format(referenceTime), StringComparison.Ordinal)
            .Replace(
                "{server_instructions}",
                string.IsNullOrWhiteSpace(serverInstructions) ? "(none)" : serverInstructions.Trim(),
                StringComparison.Ordinal);

    /// <summary>"Wed 25.11.2026 14:05".</summary>
    public static string Format(DateTimeOffset referenceTime) =>
        SwissTime.ToZurich(referenceTime).ToString("ddd dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture);

    /// <summary>The reference time of a task round: now in Europe/Zurich, on the minute.</summary>
    public static DateTimeOffset ReferenceTime(TimeProvider clock)
    {
        var now = SwissTime.ToZurich(clock.GetUtcNow());
        return new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, now.Offset);
    }

    public static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
