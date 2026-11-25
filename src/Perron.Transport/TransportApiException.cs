namespace Perron.Transport;

/// <summary>Kinds of failure, so callers (the MCP tools) can tell the agent what to do.</summary>
public enum TransportApiErrorKind
{
    /// <summary>HTTP 400 for a date outside the timetable the API has loaded.</summary>
    OutsideTimetable,

    /// <summary>Other HTTP 400: the API does not accept the request.</summary>
    InvalidRequest,

    /// <summary>HTTP 429: the API is busy or throttles this client.</summary>
    Throttled,

    /// <summary>HTTP 5xx, timeout or network failure.</summary>
    Unavailable,
}

/// <summary>A failed call to transport.opendata.ch, after retries.</summary>
public sealed class TransportApiException : Exception
{
    public TransportApiException(TransportApiErrorKind kind, int? statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Kind = kind;
        StatusCode = statusCode;
    }

    public TransportApiErrorKind Kind { get; }

    /// <summary>HTTP status code, or <c>null</c> for timeouts and network failures.</summary>
    public int? StatusCode { get; }
}
