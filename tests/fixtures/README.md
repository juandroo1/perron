# Test fixtures

Real responses, recorded once and committed, so tests never call external services.
Fahrplandaten: opentransportdata.swiss (via transport.opendata.ch).

## `transport-opendata/`

Raw responses of `https://transport.opendata.ch/v1/` recorded on 2026-11-25 between about 16:10 and 16:13 UTC
(17:10 to 17:13 Europe/Zurich), one request every 3 seconds, User-Agent `perron-dev`.
"Tomorrow" in the requests is 2026-11-26. The `locations-*-station.json` files for tasks 2.2 and 2.3 were recorded later on 2026-11-25, also 3 seconds apart. Bodies are stored unchanged; the API escapes non-ASCII characters (`ü`).
`manifest.json` lists every file with its exact URL, HTTP status, content type, time and size.

| File | Endpoint | Status | Case |
|---|---|---|---|
| `locations-zurich-hb.json` | `locations` | 200 | Exact name |
| `locations-buchs.json` | `locations` | 200 | Ambiguous name, default type=all |
| `locations-buchs-station.json` | `locations` | 200 | Ambiguous name, stations only |
| `locations-geneve.json` | `locations` | 200 | French name with accent |
| `locations-coordinates.json` | `locations` | 200 | Coordinates near Zürich HB, type=all |
| `locations-coordinates-station.json` | `locations` | 200 | Coordinates, stations only |
| `locations-unknown.json` | `locations` | 200 | Unknown name |
| `locations-empty.json` | `locations` | 200 | No parameters |
| `locations-basel-station.json` | `locations` | 200 | Town with several main stations, stations only (task 2.2) |
| `locations-fribourg-station.json` | `locations` | 200 | Town name differs from station name, stations only (task 2.2) |
| `locations-genf-station.json` | `locations` | 200 | German name of a French-speaking town, stations only (task 2.2) |
| `locations-zurich-station.json` | `locations` | 200 | Town name only, several stations, stations only (task 2.2) |
| `locations-bern-station.json` | `locations` | 200 | Exact name of a main station, stations only (task 2.3) |
| `locations-zurich-central-station.json` | `locations` | 200 | Exact name of a tram stop, stations only (task 2.3) |
| `locations-oberdorf-station.json` | `locations` | 200 | Ambiguous name, one place only with stops within it (task 4.2, recorded 2026-11-26 17:48 UTC, User-Agent `perron-bench-check`) |
| `connections-interlaken-zermatt-walk-first.json` | `connections` | 200 | Connections that start with a walk, "tomorrow" is 2026-11-27 (task 4.2, recorded 2026-11-26 17:54 UTC, User-Agent `perron-bench-check`) |
| `connections-zurich-bern-now.json` | `connections` | 200 | Simple trip, now |
| `connections-zurich-bern-tomorrow-0800.json` | `connections` | 200 | Departure time tomorrow 08:00 |
| `connections-zurich-bern-arrive-0900.json` | `connections` | 200 | Arrival time tomorrow 09:00 |
| `connections-zurich-bern-page1.json` | `connections` | 200 | Next page |
| `connections-bern-zurich-via-olten.json` | `connections` | 200 | One via |
| `connections-lausanne-stgallen-via-two.json` | `connections` | 200 | Two vias, array notation |
| `connections-zurich-airport-train.json` | `connections` | 200 | Trains only |
| `connections-basel-lugano-transfer10.json` | `connections` | 200 | Minimum transfer time 10 min |
| `connections-geneve-lausanne.json` | `connections` | 200 | French names |
| `connections-buchs-ambiguous.json` | `connections` | 200 | Ambiguous from: which Buchs is chosen? |
| `connections-unknown-from.json` | `connections` | 200 | Unknown from |
| `connections-missing-to.json` | `connections` | 200 | Missing required parameter to |
| `connections-invalid-date.json` | `connections` | 400 | Invalid date |
| `connections-past-date.json` | `connections` | 400 | Date outside the loaded timetable years |
| `stationboard-bern-departures.json` | `stationboard` | 200 | Departures now |
| `stationboard-bern-arrivals.json` | `stationboard` | 200 | Arrivals now |
| `stationboard-zurich-central-tram.json` | `stationboard` | 200 | Tram only |
| `stationboard-bern-tomorrow-0800.json` | `stationboard` | 200 | Fixed datetime |
| `stationboard-by-id.json` | `stationboard` | 200 | By station id (Bern) |
| `stationboard-buchs-ambiguous.json` | `stationboard` | 200 | Ambiguous station name |
| `stationboard-unknown.json` | `stationboard` | 200 | Unknown station |
| `stationboard-invalid-datetime.json` | `stationboard` | 500 | Invalid datetime |

There is no recorded throttling response: the API was deliberately not provoked (plan.md, decision log).
Tests for throttling use a synthetic response, marked as such.

## `foreign-servers/`

`initialize` result and `tools/list` of the third-party MCP servers, recorded on 2026-11-25
(see spec.md, section 5). Reference for `bench check` and for the `tools/list` hash.

| File | Server | Version | Tools | Tool definitions (compact JSON) |
|---|---|---|---|---|
| `official.json` | transport.opendata.ch/mcp | hosted, `serverInfo.version` 2c28784a | 4 | 3,090 chars |
| `spt.json` | swiss-public-transport-mcp | 0.1.10 | 4 | 4,692 chars |
| `stm.json` | swiss-transport-mcp | 0.5.0 | 11 | 20,459 chars |

The Python servers were started in a throwaway container (`ghcr.io/astral-sh/uv:python3.12-bookworm-slim`) with the
start command stored in each file; nothing ran on the host.

Capability probes for task 3.1 (one call each, recorded on 2026-11-25 with the MCP Inspector CLI): does a connection
result include the occupancy forecast? `official-connections-probe.json` (`connections`, Zürich HB → Bern): no.
`spt-plan_journey-probe.json` (`plan_journey`, same trip, same throwaway container as above): yes
(`Occupancy: 1st ●●○ | 2nd ●●●`).
