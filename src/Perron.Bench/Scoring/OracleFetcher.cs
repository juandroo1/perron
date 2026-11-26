using System.Text.Json;

using Perron.Bench.Tasks;
using Perron.Transport;

namespace Perron.Bench.Scoring;

/// <summary>
/// Asks transport.opendata.ch for the oracle checks of a task, once per task round and from its reference time
/// (AK-9.1). The queries carry the task's constraints (via, minimum transfer time, means of transport, line, towards),
/// so the stored data holds only valid entries. A failed query is stored as an error and leaves the check unscored.
/// </summary>
public sealed class OracleFetcher(ITransportApiClient transport, TimeProvider clock)
{
    private const int MaxConnections = ConnectionQuery.MaxLimit;
    private const int MaxDepartures = 30;

    public async Task<IReadOnlyList<OracleData>> FetchAsync(BenchTask task, DateTimeOffset reference, CancellationToken cancellationToken)
    {
        var result = new List<OracleData>();
        var checks = CheckDefinitions.Of(task.Checks.ValueKind == JsonValueKind.Undefined ? null : task.Checks);
        for (var index = 0; index < checks.Count; index++)
        {
            var check = checks[index];
            if (!check.TryGetProperty("type", out var type) || type.GetString() != "oracle")
            {
                continue;
            }

            var oracle = check.GetProperty("oracle").GetString()!;
            try
            {
                result.Add(await FetchAsync(index, oracle, check.GetProperty("params"), reference, cancellationToken));
            }
            catch (Exception ex) when (ex is TransportApiException or ArgumentException or FormatException or KeyNotFoundException or InvalidOperationException)
            {
                result.Add(new OracleData(index, oracle, clock.GetUtcNow(), Error: $"{ex.GetType().Name}: {ex.Message}"));
            }
        }

        return result;
    }

    private async Task<OracleData> FetchAsync(int index, string oracle, JsonElement parameters, DateTimeOffset reference, CancellationToken cancellationToken)
    {
        var fetched = clock.GetUtcNow();
        switch (oracle)
        {
            case "earliest_arrival":
            case "latest_departure":
                {
                    var arrival = oracle == "latest_departure";
                    var at = RelativeTime.Resolve(Text(parameters, arrival ? "arrive_before" : "depart_after")!, reference);
                    var answer = await transport.GetConnectionsAsync(
                        Connections(parameters, at, arrival, TransportTypes(parameters, "transport_types")), cancellationToken);
                    return new OracleData(index, oracle, fetched, Connections: [.. answer.Connections.Select(Connection).OfType<OracleConnection>()]);
                }

            case "next_departure":
                return new OracleData(index, oracle, fetched, Departures: await DeparturesAsync(parameters, reference, cancellationToken));

            case "occupancy":
                {
                    var at = RelativeTime.Resolve(Text(parameters, "depart_after")!, reference);
                    var answer = await transport.GetConnectionsAsync(new ConnectionQuery(Text(parameters, "from")!, Text(parameters, "to")!) { At = at, Limit = 4 }, cancellationToken);
                    var first = answer.Connections
                        .Where(connection => connection.From.Departure >= at)
                        .OrderBy(connection => connection.From.Departure)
                        .FirstOrDefault();
                    var level = parameters.GetProperty("class").GetInt32() == 1 ? first?.Occupancy1st : first?.Occupancy2nd;
                    return new OracleData(index, oracle, fetched, Occupancy: level);
                }

            case "stop_candidates":
                {
                    var stops = await transport.SearchLocationsAsync(
                        LocationQuery.ByName(Text(parameters, "name")!) with { Type = LocationType.Station }, cancellationToken);
                    return new OracleData(index, oracle, fetched, Candidates: [.. stops.Where(stop => stop.Id is not null).Select(stop => stop.Name)]);
                }

            default:
                throw new ArgumentException($"unknown oracle '{oracle}'");
        }
    }

    /// <summary>
    /// Departures (or arrivals) of a stop after a time, filtered by line and means of transport. With <c>towards</c>,
    /// the departures of direct connections to that stop, because the stationboard does not list the stops ahead.
    /// </summary>
    private async Task<IReadOnlyList<OracleDeparture>> DeparturesAsync(JsonElement parameters, DateTimeOffset reference, CancellationToken cancellationToken)
    {
        var stop = Text(parameters, "stop")!;
        var after = RelativeTime.Resolve(Text(parameters, "after")!, reference);
        var line = Text(parameters, "line");
        var types = TransportTypes(parameters, "transport_type");

        if (Text(parameters, "towards") is { } towards)
        {
            var answer = await transport.GetConnectionsAsync(
                new ConnectionQuery(stop, towards) { At = after, TransportTypes = types, Limit = MaxConnections }, cancellationToken);
            return
            [
                .. answer.Connections
                    .Where(connection => connection.Transfers == 0 && connection.From.Departure is not null)
                    .Select(connection => (Connection: connection, Journey: connection.Sections.FirstOrDefault(section => section.Journey is not null)?.Journey))
                    .Where(item => LineMatches(item.Journey?.Line, line))
                    .Select(item => new OracleDeparture(
                        SwissTime.ToZurich(item.Connection.From.Departure!.Value),
                        Zurich(item.Connection.From.Prognosis?.Departure),
                        item.Journey?.Line,
                        item.Journey?.Destination)),
            ];
        }

        var arrivals = Text(parameters, "type") == "arrival";
        var board = await transport.GetStationboardAsync(
            new StationboardQuery
            {
                Station = stop,
                At = after,
                Type = arrivals ? StationboardType.Arrival : StationboardType.Departure,
                TransportTypes = types,
                Limit = MaxDepartures,
            },
            cancellationToken);

        return
        [
            .. board.Entries
                .Where(entry => LineMatches(entry.Line, line))
                .Select(entry =>
                {
                    // For arrivals the API puts the time into "departure" and leaves "arrival" empty.
                    var time = arrivals ? entry.Stop.Arrival ?? entry.Stop.Departure : entry.Stop.Departure ?? entry.Stop.Arrival;
                    var forecast = arrivals ? entry.Stop.Prognosis?.Arrival ?? entry.Stop.Prognosis?.Departure : entry.Stop.Prognosis?.Departure;
                    return time is null ? null : new OracleDeparture(SwissTime.ToZurich(time.Value), Zurich(forecast), entry.Line, entry.Destination);
                })
                .OfType<OracleDeparture>(),
        ];
    }

    private static ConnectionQuery Connections(JsonElement parameters, DateTimeOffset at, bool arrival, IReadOnlyList<TransportType> types) =>
        new(Text(parameters, "from")!, Text(parameters, "to")!)
        {
            Via = parameters.TryGetProperty("via", out var via) ? [.. via.EnumerateArray().Select(stop => stop.GetString()!)] : [],
            At = at,
            IsArrivalTime = arrival,
            TransportTypes = types,
            MinTransferTime = parameters.TryGetProperty("min_transfer_minutes", out var minutes) ? minutes.GetInt32() : null,
            Limit = MaxConnections,
        };

    private static OracleConnection? Connection(Connection connection)
    {
        var departure = connection.From.Departure ?? connection.Sections.FirstOrDefault()?.Departure.Departure;
        var arrival = connection.To.Arrival ?? connection.Sections.LastOrDefault()?.Arrival.Arrival;
        if (departure is null || arrival is null)
        {
            return null;
        }

        return new OracleConnection(
            SwissTime.ToZurich(departure.Value),
            Zurich(connection.From.Prognosis?.Departure),
            SwissTime.ToZurich(arrival.Value),
            Zurich(connection.To.Prognosis?.Arrival),
            connection.Transfers,
            [.. connection.Sections.Select(section => section.Journey?.Line).OfType<string>()]);
    }

    /// <summary>"4" matches line "T 4", "IC 8" matches "IC 8"; no filter matches every line.</summary>
    public static bool LineMatches(string? actual, string? wanted)
    {
        if (string.IsNullOrWhiteSpace(wanted))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(actual))
        {
            return false;
        }

        var a = actual.Replace(" ", "", StringComparison.Ordinal);
        var w = wanted.Replace(" ", "", StringComparison.Ordinal);
        return string.Equals(a, w, StringComparison.OrdinalIgnoreCase)
            || actual.EndsWith(" " + wanted.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static List<TransportType> TransportTypes(JsonElement parameters, string name)
    {
        if (!parameters.TryGetProperty(name, out var value))
        {
            return [];
        }

        var names = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(item => item.GetString()!) : [value.GetString()!];
        return [.. names.Select(type => Enum.Parse<TransportType>(type, ignoreCase: true))];
    }

    private static string? Text(JsonElement parameters, string name) =>
        parameters.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static DateTimeOffset? Zurich(DateTimeOffset? instant) => instant is { } value ? SwissTime.ToZurich(value) : null;
}
