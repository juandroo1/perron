using System.Diagnostics.CodeAnalysis;

using ModelContextProtocol;

using Perron.Mcp.Variants;
using Perron.Transport;

namespace Perron.Mcp.Tools;

/// <summary>
/// Tool cut <c>many</c> (plan.md, section 4): the same data and formats as <c>standard</c>, spread over 7 finer tools.
/// Each tool calls the shared core of the standard tools. Texts in <see cref="ToolTexts"/>.
/// </summary>
[SuppressMessage("Style", "IDE1006:Naming Styles", Justification = "Parameter names are the tools' snake_case schema (plan.md, section 4).")]
public static class ManyTools
{
    public const string SearchStopsByName = "search_stops_by_name";
    public const string SearchStopsNear = "search_stops_near";
    public const string GetConnectionsDeparting = "get_connections_departing";
    public const string GetConnectionsArriving = "get_connections_arriving";
    public const string GetConnectionsVia = "get_connections_via";
    public const string GetDepartures = GetDeparturesTool.Name;
    public const string GetArrivals = "get_arrivals";

    public static async Task<string> SearchStopsByNameAsync(
        ITransportApiClient transport,
        PerronVariant variant,
        string query,
        int limit = FindStopsTool.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new McpException("Give a stop or place name, e.g. \"Bern\".");
        }

        return variant.Render(await FindStopsTool.SearchAsync(transport, variant, query, null, null, limit, cancellationToken));
    }

    public static async Task<string> SearchStopsNearAsync(
        ITransportApiClient transport,
        PerronVariant variant,
        double latitude,
        double longitude,
        int limit = FindStopsTool.DefaultLimit,
        CancellationToken cancellationToken = default) =>
        variant.Render(await FindStopsTool.SearchAsync(transport, variant, null, latitude, longitude, limit, cancellationToken));

    public static async Task<string> GetConnectionsDepartingAsync(
        ITransportApiClient transport,
        PerronVariant variant,
        string from,
        string to,
        string? datetime = null,
        TransportMode[]? transport_types = null,
        int? min_transfer_minutes = null,
        int limit = PlanTripTool.DefaultLimit,
        CancellationToken cancellationToken = default) =>
        variant.Render(await PlanTripTool.PlanAsync(
            transport, variant, GetConnectionsDeparting, from, to, null, datetime, TimeIs.Departure, transport_types, min_transfer_minutes, limit, cancellationToken));

    public static async Task<string> GetConnectionsArrivingAsync(
        ITransportApiClient transport,
        PerronVariant variant,
        string from,
        string to,
        string datetime,
        TransportMode[]? transport_types = null,
        int? min_transfer_minutes = null,
        int limit = PlanTripTool.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(datetime))
        {
            throw new McpException("Give datetime, the latest arrival in local Swiss time, e.g. \"2026-11-26T10:00\".");
        }

        return variant.Render(await PlanTripTool.PlanAsync(
            transport, variant, GetConnectionsArriving, from, to, null, datetime, TimeIs.Arrival, transport_types, min_transfer_minutes, limit, cancellationToken));
    }

    public static async Task<string> GetConnectionsViaAsync(
        ITransportApiClient transport,
        PerronVariant variant,
        string from,
        string to,
        string[] via,
        string? datetime = null,
        TimeIs time_is = TimeIs.Departure,
        TransportMode[]? transport_types = null,
        int? min_transfer_minutes = null,
        int limit = PlanTripTool.DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (via is null || !via.Any(stop => !string.IsNullOrWhiteSpace(stop)))
        {
            throw new McpException($"Give 1 or 2 via stops, e.g. [\"Olten\"]. Without via stops, use {GetConnectionsDeparting} or {GetConnectionsArriving}.");
        }

        return variant.Render(await PlanTripTool.PlanAsync(
            transport, variant, GetConnectionsVia, from, to, via, datetime, time_is, transport_types, min_transfer_minutes, limit, cancellationToken));
    }

    public static async Task<string> GetDeparturesAsync(
        ITransportApiClient transport,
        TimeProvider clock,
        PerronVariant variant,
        string stop,
        string? datetime = null,
        TransportMode[]? transport_types = null,
        int limit = GetDeparturesTool.DefaultLimit,
        CancellationToken cancellationToken = default) =>
        variant.Render(await GetDeparturesTool.BoardAsync(
            transport, clock, variant, GetDepartures, stop, datetime, BoardType.Departure, transport_types, limit, cancellationToken));

    public static async Task<string> GetArrivalsAsync(
        ITransportApiClient transport,
        TimeProvider clock,
        PerronVariant variant,
        string stop,
        string? datetime = null,
        TransportMode[]? transport_types = null,
        int limit = GetDeparturesTool.DefaultLimit,
        CancellationToken cancellationToken = default) =>
        variant.Render(await GetDeparturesTool.BoardAsync(
            transport, clock, variant, GetArrivals, stop, datetime, BoardType.Arrival, transport_types, limit, cancellationToken));
}
