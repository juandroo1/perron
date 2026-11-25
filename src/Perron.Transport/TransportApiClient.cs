using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Perron.Transport;

/// <summary>Typed client for transport.opendata.ch (<c>/locations</c>, <c>/connections</c>, <c>/stationboard</c>).</summary>
public interface ITransportApiClient
{
    /// <summary>Locations matching a name or near a coordinate, best match first. Empty for unknown names.</summary>
    Task<IReadOnlyList<Location>> SearchLocationsAsync(LocationQuery query, CancellationToken cancellationToken = default);

    /// <summary>Connections from A to B. Empty for unknown stations.</summary>
    Task<ConnectionsResult> GetConnectionsAsync(ConnectionQuery query, CancellationToken cancellationToken = default);

    /// <summary>Departures or arrivals of a station. <see cref="Stationboard.Station"/> is null for unknown stations.</summary>
    Task<Stationboard> GetStationboardAsync(StationboardQuery query, CancellationToken cancellationToken = default);
}

public sealed partial class TransportApiClient(
    HttpClient httpClient,
    TransportCache cache,
    IOptions<TransportApiOptions> options,
    ILogger<TransportApiClient> logger) : ITransportApiClient
{
    private const string TimetableWindowMarker = "outside of loaded timetable window";

    public Task<IReadOnlyList<Location>> SearchLocationsAsync(LocationQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return GetAsync<LocationsResponseDto, IReadOnlyList<Location>>(
            "locations", BuildLocationsUrl(query), options.Value.LocationsCacheDuration,
            ApiJsonContext.Default.LocationsResponseDto, ApiMapper.Map, cancellationToken);
    }

    public Task<ConnectionsResult> GetConnectionsAsync(ConnectionQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return GetAsync<ConnectionsResponseDto, ConnectionsResult>(
            "connections", BuildConnectionsUrl(query), options.Value.TimetableCacheDuration,
            ApiJsonContext.Default.ConnectionsResponseDto, ApiMapper.Map, cancellationToken);
    }

    public Task<Stationboard> GetStationboardAsync(StationboardQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return GetAsync<StationboardResponseDto, Stationboard>(
            "stationboard", BuildStationboardUrl(query), options.Value.TimetableCacheDuration,
            ApiJsonContext.Default.StationboardResponseDto, ApiMapper.Map, cancellationToken);
    }

    internal static string BuildLocationsUrl(LocationQuery query)
    {
        var url = new QueryBuilder("locations");
        if (query.Near is { } near)
        {
            url.Add("x", near.Latitude.ToString(CultureInfo.InvariantCulture));
            url.Add("y", near.Longitude.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            if (string.IsNullOrWhiteSpace(query.Name))
            {
                throw new ArgumentException("A location query needs a name or a coordinate.", nameof(query));
            }

            url.Add("query", query.Name.Trim());
        }

        if (query.Type != LocationType.All)
        {
            url.Add("type", query.Type.ToString().ToLowerInvariant());
        }

        return url.ToString();
    }

    internal static string BuildConnectionsUrl(ConnectionQuery query)
    {
        RequireText(query.From, nameof(query.From));
        RequireText(query.To, nameof(query.To));
        if (query.Via.Count > ConnectionQuery.MaxVia || query.Via.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException($"At most {ConnectionQuery.MaxVia} non-empty via stops are allowed.", nameof(query));
        }

        RequireRange(query.Limit, ConnectionQuery.MinLimit, ConnectionQuery.MaxLimit, nameof(query.Limit));
        RequireRange(query.Page, ConnectionQuery.MinPage, ConnectionQuery.MaxPage, nameof(query.Page));

        var url = new QueryBuilder("connections");
        url.Add("from", query.From.Trim());
        url.Add("to", query.To.Trim());

        // One via as "via", several only in array notation (API documentation).
        var viaKey = query.Via.Count > 1 ? "via[]" : "via";
        foreach (var via in query.Via)
        {
            url.Add(viaKey, via.Trim());
        }

        if (query.At is { } at)
        {
            url.Add("date", SwissTime.FormatDate(at));
            url.Add("time", SwissTime.FormatTime(at));
        }

        if (query.IsArrivalTime)
        {
            url.Add("isArrivalTime", "1");
        }

        AddTransportTypes(url, query.TransportTypes);
        url.AddIfSet("limit", query.Limit);
        url.AddIfSet("page", query.Page);
        return url.ToString();
    }

    internal static string BuildStationboardUrl(StationboardQuery query)
    {
        if (string.IsNullOrWhiteSpace(query.Station) && string.IsNullOrWhiteSpace(query.StationId))
        {
            throw new ArgumentException("A stationboard query needs a station name or ID.", nameof(query));
        }

        RequireRange(query.Limit, 1, int.MaxValue, nameof(query.Limit));

        var url = new QueryBuilder("stationboard");
        if (!string.IsNullOrWhiteSpace(query.StationId))
        {
            url.Add("id", query.StationId.Trim());
        }
        else
        {
            url.Add("station", query.Station!.Trim());
        }

        url.AddIfSet("limit", query.Limit);
        AddTransportTypes(url, query.TransportTypes);
        if (query.At is { } at)
        {
            url.Add("datetime", $"{SwissTime.FormatDate(at)} {SwissTime.FormatTime(at)}");
        }

        if (query.Type == StationboardType.Arrival)
        {
            url.Add("type", "arrival");
        }

        return url.ToString();
    }

    private async Task<TResult> GetAsync<TDto, TResult>(
        string endpoint,
        string url,
        TimeSpan cacheDuration,
        JsonTypeInfo<TDto> typeInfo,
        Func<TDto, TResult> map,
        CancellationToken cancellationToken)
        where TResult : class
    {
        if (cache.TryGet<TResult>(url, out var cached))
        {
            LogCacheHit(logger, endpoint);
            return cached;
        }

        var started = Stopwatch.GetTimestamp();
        HttpResponseMessage response;
        try
        {
            response = await httpClient.GetAsync(url, cancellationToken);
        }
        catch (Exception ex) when (IsUnavailable(ex, cancellationToken))
        {
            LogFailure(logger, endpoint, ex.GetType().Name, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw new TransportApiException(
                TransportApiErrorKind.Unavailable, null, "transport.opendata.ch did not answer in time or could not be reached.", ex);
        }

        using (response)
        {
            LogResponse(logger, endpoint, (int)response.StatusCode, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                throw CreateError(response.StatusCode, body);
            }

            var dto = JsonSerializer.Deserialize(body, typeInfo)
                ?? throw new TransportApiException(
                    TransportApiErrorKind.Unavailable, (int)response.StatusCode, "transport.opendata.ch returned an empty response.");
            var result = map(dto);
            cache.Set(url, result, cacheDuration);
            return result;
        }
    }

    private static TransportApiException CreateError(HttpStatusCode status, string body)
    {
        var message = ReadErrorMessage(body) ?? $"HTTP {(int)status}";
        var kind = status switch
        {
            HttpStatusCode.BadRequest when message.Contains(TimetableWindowMarker, StringComparison.OrdinalIgnoreCase)
                => TransportApiErrorKind.OutsideTimetable,
            HttpStatusCode.BadRequest => TransportApiErrorKind.InvalidRequest,
            HttpStatusCode.TooManyRequests => TransportApiErrorKind.Throttled,
            _ when (int)status >= 500 => TransportApiErrorKind.Unavailable,
            _ => TransportApiErrorKind.InvalidRequest,
        };

        return new TransportApiException(kind, (int)status, message);
    }

    private static string? ReadErrorMessage(string body)
    {
        try
        {
            var error = JsonSerializer.Deserialize(body, ApiJsonContext.Default.ErrorResponseDto);
            var messages = error?.Errors?.Select(e => e.Message).OfType<string>().ToList();
            return messages is { Count: > 0 } ? string.Join(" ", messages) : null;
        }
        catch (JsonException)
        {
            return string.IsNullOrWhiteSpace(body) ? null : body.Trim();
        }
    }

    private static bool IsUnavailable(Exception ex, CancellationToken cancellationToken) =>
        ex is HttpRequestException or TimeoutRejectedException or BrokenCircuitException
        || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested);

    private static void AddTransportTypes(QueryBuilder url, IReadOnlyList<TransportType> types)
    {
        foreach (var type in types.Distinct())
        {
            url.Add("transportations[]", type.ToString().ToLowerInvariant());
        }
    }

    private static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} must not be empty.", name);
        }
    }

    private static void RequireRange(int? value, int min, int max, string name)
    {
        if (value is int actual && (actual < min || actual > max))
        {
            throw new ArgumentOutOfRangeException(name, actual, $"{name} must be between {min} and {max}.");
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "transport.opendata.ch /{Endpoint}: cache hit")]
    private static partial void LogCacheHit(ILogger logger, string endpoint);

    [LoggerMessage(Level = LogLevel.Information, Message = "transport.opendata.ch /{Endpoint}: HTTP {StatusCode} in {ElapsedMs:F0} ms")]
    private static partial void LogResponse(ILogger logger, string endpoint, int statusCode, double elapsedMs);

    [LoggerMessage(Level = LogLevel.Warning, Message = "transport.opendata.ch /{Endpoint}: failed with {ErrorType} after {ElapsedMs:F0} ms")]
    private static partial void LogFailure(ILogger logger, string endpoint, string errorType, double elapsedMs);

    /// <summary>Builds relative URLs with a stable parameter order, so equal queries share a cache key.</summary>
    private sealed class QueryBuilder(string path)
    {
        private readonly StringBuilder query = new();

        public void Add(string key, string value)
        {
            query.Append(query.Length == 0 ? '?' : '&')
                .Append(Uri.EscapeDataString(key))
                .Append('=')
                .Append(Uri.EscapeDataString(value));
        }

        public void AddIfSet(string key, int? value)
        {
            if (value is int actual)
            {
                Add(key, actual.ToString(CultureInfo.InvariantCulture));
            }
        }

        public override string ToString() => path + query;
    }
}
