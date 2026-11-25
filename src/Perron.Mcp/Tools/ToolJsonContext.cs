using System.Text.Json.Serialization;

namespace Perron.Mcp.Tools;

/// <summary>Source-generated JSON metadata for the few values Perron MCP serializes itself (AOT-safe).</summary>
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class ToolJsonContext : JsonSerializerContext;
