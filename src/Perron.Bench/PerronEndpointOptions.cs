using Microsoft.Extensions.Options;

namespace Perron.Bench;

/// <summary>Where Perron Bench reaches the Perron MCP server (HTTP).</summary>
public sealed class PerronEndpointOptions
{
    public const string EnvironmentVariable = "PERRON_MCP_URL";
    public const string DefaultMcpUrl = "http://localhost:8080/mcp";

    public string McpUrl { get; set; } = DefaultMcpUrl;

    public Uri Endpoint => new(McpUrl, UriKind.Absolute);
}

public sealed class PerronEndpointOptionsValidator : IValidateOptions<PerronEndpointOptions>
{
    public ValidateOptionsResult Validate(string? name, PerronEndpointOptions options) =>
        Uri.TryCreate(options.McpUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{PerronEndpointOptions.EnvironmentVariable} must be an absolute http(s) URL, got '{options.McpUrl}'.");
}
