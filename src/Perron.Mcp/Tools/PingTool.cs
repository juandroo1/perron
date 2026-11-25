using System.ComponentModel;

using ModelContextProtocol.Server;

namespace Perron.Mcp.Tools;

/// <summary>Connectivity check. Temporary: removed in task 2.1 (see plan.md, decision log).</summary>
[McpServerToolType]
public static class PingTool
{
    [McpServerTool(
        Name = "ping",
        Title = "Ping",
        ReadOnly = true,
        Idempotent = true,
        Destructive = false,
        OpenWorld = false)]
    [Description("Checks that the Perron server is reachable. Returns \"pong\". Use only to test the connection; it returns no timetable data.")]
    public static string Ping() => "pong";
}
