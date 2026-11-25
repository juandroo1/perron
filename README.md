# Perron

[![CI](https://github.com/juandroo1/perron/actions/workflows/ci.yml/badge.svg)](https://github.com/juandroo1/perron/actions/workflows/ci.yml)

**How much does tool design (number of tools, descriptions, result format) affect how well an agent solves tasks?**

Perron answers this for Swiss public transport, a domain with several existing MCP servers and live data:

1. **Perron MCP:** an MCP server in .NET for Swiss public transport (stops, connections, departures), based on [transport.opendata.ch](https://transport.opendata.ch).
2. **Perron Bench:** an eval harness in .NET that runs the same agent tasks against several MCP servers (Perron and existing ones) and compares task success, tool calls, tokens, cost and latency.

The goal is an honest, reproducible comparison, not a leaderboard Perron has to top.

> **Status:** early development. Nothing below is usable yet; results will be published here once the benchmark has run.

## Repository layout

```
src/
  Perron.Transport/   client for transport.opendata.ch, models, caching
  Perron.Mcp/         MCP server: tools, variants, transports, /health
  Perron.Bench/       harness: server registry, agent loop, checks, oracle, report
tests/                unit and integration tests (no live API calls)
bench/                server registry, task sets, results
specs/001-perron/     spec, plan and tasks (spec-driven development)
```

## Getting started

Requirements: .NET SDK 10.0.404 (pinned in `global.json`), Docker.

```bash
cp .env.example .env
dotnet build -warnaserror
dotnet test
```

### Run the MCP server locally

```bash
dotnet run --project src/Perron.Mcp                 # Streamable HTTP on http://localhost:8080/mcp, health on /health
dotnet run --project src/Perron.Mcp -- --stdio      # stdio transport
npx @modelcontextprotocol/inspector                 # inspect and call tools manually
```

Perron speaks both MCP protocol paths: the `initialize` handshake (revisions up to 2025-11-25) and per-request metadata with `server/discover` (2026-07-28). Over HTTP it runs stateless.

Tools (all read-only):

| Tool | What it does |
|---|---|
| `find_stops` | Finds stops by name or near a coordinate and returns ID, name, kind of stop (train, tram, bus …) and coordinates, plus the distance for a coordinate search. An ambiguous name such as "Buchs" lists the candidates (Buchs AG, Buchs SG …) with a hint to ask which one is meant. |
| `plan_trip` | Plans connections from one stop to another, optionally via up to two stops, leaving after or arriving before a time (local Swiss time). Each connection shows date, departure and arrival, duration, transfers, and every leg with line, direction, platforms, delays and transfer time. Stops can be names or IDs; an ambiguous name returns the candidates with IDs instead of connections. |
| `get_departures` | Shows the next departures or arrivals at one stop (name or ID), from now or a given time, optionally only some means of transport. Each entry shows time, delay, line, final destination and platform. Arrivals show the final destination too; the API does not provide where a vehicle comes from. |

Tool texts and results are in English. Every result ends with the attribution "Fahrplandaten: opentransportdata.swiss".

### Use with Claude Code and Claude Desktop

**Claude Code** reads `.mcp.json` in this repository, which points to Perron MCP from Docker Compose:

```bash
docker compose up -d --wait        # Perron MCP on http://127.0.0.1:8080/mcp
claude                             # in the repository; approve the project server "perron" once
```

`/mcp` in Claude Code shows the server and its tools. Without this repository, add it once for your user:

```bash
claude mcp add --transport http perron http://localhost:8080/mcp
```

**Claude Desktop** starts local servers itself over stdio. Build the image once (`docker compose build mcp`), then add Perron to `claude_desktop_config.json` (Windows: `%APPDATA%\Claude\claude_desktop_config.json`, macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`) and restart Claude Desktop:

```json
{
  "mcpServers": {
    "perron": {
      "command": "docker",
      "args": ["run", "-i", "--rm", "perron-mcp:dev", "--stdio"]
    }
  }
}
```

This needs Docker but no .NET. With the .NET SDK instead, run `dotnet publish src/Perron.Mcp -c Release -o publish/perron` and use `"command": "dotnet", "args": ["<repository>/publish/perron/Perron.Mcp.dll", "--stdio"]`.

To try a variant, pass its settings, e.g. `"args": ["run", "-i", "--rm", "-e", "PERRON_TOOLS=many", "perron-mcp:dev", "--stdio"]`.

### Variants

For the benchmark ablations the same server runs in variants, chosen by environment variables (invalid values stop the start):

| Variable | Values | Default |
|---|---|---|
| `PERRON_TOOLS` | `standard` (3 tools) or `many` (7 finer tools with the same data and formats) | `standard` |
| `PERRON_DESCRIPTIONS` | `rich` (when to use a tool, when not, example) or `minimal` (one sentence, parameter meaning, format and range) | `rich` |
| `PERRON_FORMAT` | `compact` (short text) or `json` (the same fields as JSON) | `compact` |

Each variant reports its own name in `serverInfo`: `perron-rich` for the defaults, otherwise `perron-` plus the values that differ, e.g. `perron-minimal` or `perron-json`. Tool errors are plain text in every variant.

The tool cut `many` offers `search_stops_by_name`, `search_stops_near`, `get_connections_departing`, `get_connections_arriving`, `get_connections_via`, `get_departures` and `get_arrivals`.

Docker Compose runs the variants of the benchmark next to `mcp` (perron-rich) in the profile `bench`, reachable only inside the Compose network:

| Service | Variant | URL inside Compose |
|---|---|---|
| `mcp` | `perron-rich` | `http://mcp:8080/mcp` (also `http://127.0.0.1:8080/mcp` on the host) |
| `mcp-minimal` | `perron-minimal` | `http://mcp-minimal:8080/mcp` |
| `mcp-json` | `perron-json` | `http://mcp-json:8080/mcp` |
| `mcp-many` | `perron-many` | `http://mcp-many:8080/mcp` |

```bash
docker compose --profile bench up -d --wait mcp mcp-minimal mcp-json mcp-many
docker compose --profile bench run --rm bench check --servers perron-many
```

Errors are written for the agent, so it knows what to do next:

- **Invalid input** (unreadable date, unknown stop, too many via stops …): a tool error with a concrete hint, e.g. similar stop names.
- **Nothing found** (no connections, empty board, no stop for a search): a normal result with a hint, e.g. try another time.
- **API failures** (date outside the loaded timetable, throttling, timeouts, server errors): a tool error that says what happened and what to do, e.g. the timetable window or "wait about 30 seconds". Perron retries server errors and timeouts before giving up.

### Run with Docker

Docker is the reference environment.

```bash
cp .env.example .env                                  # once; optional until API keys are needed
docker compose up -d --build                          # Perron MCP on http://127.0.0.1:8080/mcp
docker compose ps                                     # mcp should be "healthy"
docker compose --profile bench run --rm bench check --servers perron-rich --expect-tools find_stops,get_departures,plan_trip   # connect, list tools
docker compose down
```

Images run as a non-root user and the port is bound to `127.0.0.1` only.

The `bench` image also contains uv 0.13.7, CPython 3.12.16 and the third-party stdio servers `spt` and `stm`, each in its own virtual environment under `/opt/mcp/<id>`. Their dependencies are pinned with version and hash in `bench/python/<id>.txt` (resolved as of each server's release day; the command to regenerate a file is in its header) and installed at build time; nothing is downloaded when a server starts.

`bench check` reads the server registry `bench/servers.json` and, for each server (`--servers all` or a list of IDs), connects over HTTP or stdio, lists the tools and prints:

- the status: `OK`, `CHANGED` (a hosted server whose `tools/list` differs from the hash in the registry; not a failure), `SKIPPED` (an optional server without its key) or `FAIL`;
- `serverInfo`, protocol version and tool names;
- the first 12 characters of the SHA-256 of `tools/list` (name, title, description, input schema, annotations);
- the tokens of the tool definitions, measured with `count_tokens` and the model from `bench/bench.json` (`n/a` without `ANTHROPIC_API_KEY`);
- the capabilities that count as covered, and those that are not for lack of a key.

| ID | Server | Transport | Capabilities |
|---|---|---|---|
| `perron-rich`, `perron-minimal`, `perron-json`, `perron-many` | Perron MCP and its variants | HTTP (compose services) | stops, trips, departures |
| `official` | transport.opendata.ch/mcp (hosted) | HTTP | stops, trips, departures |
| `spt` | swiss-public-transport-mcp 0.1.10 | stdio (bench image) | stops, trips, departures, occupancy |
| `stm` | swiss-transport-mcp 0.5.0, optional (`STM_TRANSPORT_API_KEY`) | stdio (bench image) | stops, trips, departures; occupancy and disruptions only with their own keys |

The reasons for each capability are in `bench/servers.json`. It exits with `0` if no server fails (and, with `--expect-tools`, every server offers exactly the expected tools), `1` if a check fails and `2` on usage or configuration errors. Outside Docker, `perron-rich` uses `PERRON_MCP_URL` (default `http://localhost:8080/mcp`); the variants and the stdio servers are only reachable inside Docker Compose:

```bash
dotnet run --project src/Perron.Bench -- check --servers perron-rich,official
```

CI (GitHub Actions) runs build, format check and tests, builds both images with layer cache and runs this check against the `mcp` container. MCP client configuration and benchmark commands will follow.

## Results

*Pending.* The report will include methodology, model, server versions, run dates, confidence intervals and known limitations of the benchmark.

Known limitations so far:

- **Task sets written by Claude.** The development set (`bench/tasks/dev.jsonl`) was written by Claude in the working session, the held-out test set by a separate Claude subagent without that session's context; both were spot-checked by a human. The working session never reads the test set. Tasks written by the same model family that solves them may favour it, and the author of the development set knew the planned Perron tool design.
- **Third-party Python servers** run with their dependencies as of their release date (`uv --exclude-newer`), because one of them no longer starts with today's dependencies.

## How this project is built

Perron is developed spec-first: [`spec.md`](specs/001-perron/spec.md) defines what and why, [`plan.md`](specs/001-perron/plan.md) defines how (including a decision log), and [`tasks.md`](specs/001-perron/tasks.md) tracks implementation step by step.

## Data source

Timetable data: [opentransportdata.swiss](https://opentransportdata.swiss), via [transport.opendata.ch](https://transport.opendata.ch).

## License

[MIT](LICENSE)
