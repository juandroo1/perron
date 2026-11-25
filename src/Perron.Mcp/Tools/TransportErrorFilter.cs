using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

using Perron.Transport;

namespace Perron.Mcp.Tools;

/// <summary>
/// Call-tool filter for every tool (and every variant): a failed call to transport.opendata.ch becomes a tool error
/// with a message that tells the agent what to do (<see cref="TransportErrors"/>), instead of the SDK's generic
/// "An error occurred invoking …" (AK-4.2).
/// </summary>
public static partial class TransportErrorFilter
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Wrap(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (context, cancellationToken) =>
        {
            try
            {
                return await next(context, cancellationToken);
            }
            catch (TransportApiException error)
            {
                // Tool name, kind and status only: no user input in the log.
                var logger = context.Services?.GetService<ILoggerFactory>()?.CreateLogger(typeof(TransportErrorFilter));
                if (logger is not null)
                {
                    LogTransportError(logger, context.Params?.Name, error.Kind, error.StatusCode);
                }

                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = TransportErrors.Message(error) }],
                };
            }
        };

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tool {Tool} failed: transport.opendata.ch {Kind} (HTTP {StatusCode})")]
    private static partial void LogTransportError(ILogger logger, string? tool, TransportApiErrorKind kind, int? statusCode);
}
