using System.Globalization;

namespace Perron.Mcp.Tools;

/// <summary>Shared pieces of the <c>compact</c> result format (plan.md, section 4).</summary>
public static class ToolText
{
    /// <summary>Attribution required by the data source; last line of every result.</summary>
    public const string SourceLine = "Fahrplandaten: opentransportdata.swiss";

    /// <summary>WGS84 degrees with 5 decimals (about 1 m), independent of the server culture.</summary>
    public static string Degrees(double value) => value.ToString("0.00000", CultureInfo.InvariantCulture);

    public static string Meters(double value) => string.Create(CultureInfo.InvariantCulture, $"{value:0} m");
}
