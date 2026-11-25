# Perron

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

Requirements: .NET 10 SDK, Docker.

```bash
cp .env.example .env
dotnet build -warnaserror
dotnet test
```

Docker setup, MCP client configuration and benchmark commands will follow.

## Results

*Pending.* The report will include methodology, model, server versions, run dates, confidence intervals and known limitations of the benchmark.

## How this project is built

Perron is developed spec-first: [`spec.md`](specs/001-perron/spec.md) defines what and why, [`plan.md`](specs/001-perron/plan.md) defines how (including a decision log), and [`tasks.md`](specs/001-perron/tasks.md) tracks implementation step by step.

## Data source

Timetable data: [opentransportdata.swiss](https://opentransportdata.swiss), via [transport.opendata.ch](https://transport.opendata.ch).

## License

[MIT](LICENSE)
