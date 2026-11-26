# Perron Bench report

Runs `20261126-105446-full`, 2026-11-26. Suite `full` (25 tasks), model `claude-haiku-5-5`, effort `medium`, max_tokens 16000, 3 repeat(s) per task and server.

## Success

| Server | Coverage | Success, covered tasks (95 % CI) | Success, all tasks (95 % CI) | pass^3 | Scored runs | Pending | Unscored | Infra errors |
|---|---|---|---|---|---|---|---|---|
| perron-rich | 23/25 | 91 % (81–99) | 84 % (71–96) | 83 % | 69 | 0 | 0 | 0 |
| perron-minimal | 23/25 | 88 % (77–97) | 81 % (68–95) | 83 % | 69 | 0 | 0 | 0 |
| perron-json | 23/25 | 93 % (81–100) | 85 % (72–97) | 91 % | 69 | 0 | 0 | 0 |
| perron-many | 23/25 | 94 % (84–100) | 87 % (73–99) | 91 % | 69 | 0 | 0 | 0 |
| official | 23/25 | 87 % (74–99) | 80 % (65–93) | 83 % | 69 | 0 | 0 | 0 |
| spt | 25/25 | 92 % (81–100) | 92 % (81–100) | 88 % | 75 | 0 | 0 | 0 |

Success is the share of passed scored runs per task, averaged over tasks; the interval is a 95 % percentile bootstrap over tasks (2000 resamples, fixed seed). "All tasks" counts tasks a server does not cover as 0. pass^k: share of covered tasks whose k scored runs all passed. Pending (judge missing) and unscored (no oracle data) runs are not counted.

```mermaid
xychart-beta
    title "Success on covered tasks (%)"
    x-axis ["perron-rich", "perron-minimal", "perron-json", "perron-many", "official", "spt"]
    y-axis "Success (%)" 0 --> 100
    bar [91, 88, 93, 94, 87, 92]
```

## Paired comparison with `perron-rich`

| Server | Tasks compared | Difference in success (95 % CI) | Interval excludes 0 |
|---|---|---|---|
| perron-minimal | 23 | -3 pp (-12 to +4) | no |
| perron-json | 23 | +1 pp (-3 to +7) | no |
| perron-many | 23 | +3 pp (0 to +7) | no |
| official | 23 | -4 pp (-14 to +3) | no |
| spt | 23 | 0 pp (-4 to +4) | no |

Differences in percentage points on the tasks both servers cover and scored, bootstrapped over tasks. Only differences whose interval excludes 0 support a statement in the README.

## Cost and efficiency

| Server | Tool definition tokens | Tool calls per run | Failed tool calls | Tokens per run | Cost per task | Cost per solved task | Actual cost per task (cached) | Judge cost (total) | LLM time per run | Tool time per run |
|---|---|---|---|---|---|---|---|---|---|---|
| perron-rich | 2124 | 1.2 | 4 % | 6155 | $0.0008 | $0.0009 | $0.0004 | $0.0030 | 2.9 s | 0.22 s |
| perron-minimal | 1158 | 1.2 | 7 % | 4089 | $0.0006 | $0.0007 | $0.0004 | $0.0032 | 2.8 s | 0.16 s |
| perron-json | 2124 | 1.2 | 4 % | 6538 | $0.0009 | $0.0009 | $0.0005 | $0.0032 | 2.9 s | 0.19 s |
| perron-many | 3870 | 1.3 | 4 % | 10061 | $0.0012 | $0.0013 | $0.0006 | $0.0029 | 3.1 s | 0.16 s |
| official | 1386 | 1.3 | 0 % | 5518 | $0.0008 | $0.0009 | $0.0005 | $0.0033 | 3.4 s | 0.20 s |
| spt | 2063 | 1.4 | 0 % | 6893 | $0.0009 | $0.0010 | $0.0006 | $0.0030 | 3.3 s | 0.25 s |

Cost per task and per solved task are computed without prompt caching (cache tokens at the input price), because caching favours servers with large tool definitions; the actual cost with caching is shown for reference. Judge cost is not part of the server's cost.

```mermaid
xychart-beta
    title "Cost per solved task without caching (US cents)"
    x-axis ["perron-rich", "perron-minimal", "perron-json", "perron-many", "official", "spt"]
    y-axis "US cents" 0 --> 0.16
    bar [0.09, 0.068, 0.093, 0.129, 0.092, 0.102]
```

## Errors

| Server | Hallucination rate | infra | tool_limit | gave_up | hallucinated | not_optimal | wrong_time | wrong_stop | tool_error_loop | failed without code |
|---|---|---|---|---|---|---|---|---|---|---|
| perron-rich | 0 % | 0 | 0 | 0 | 0 | 4 | 0 | 0 | 0 | 2 |
| perron-minimal | 1 % | 0 | 0 | 0 | 1 | 2 | 0 | 0 | 0 | 6 |
| perron-json | 0 % | 0 | 0 | 0 | 0 | 3 | 0 | 0 | 0 | 2 |
| perron-many | 0 % | 0 | 0 | 0 | 0 | 3 | 0 | 0 | 0 | 1 |
| official | 0 % | 0 | 0 | 0 | 0 | 3 | 3 | 0 | 0 | 3 |
| spt | 4 % | 0 | 0 | 0 | 3 | 3 | 0 | 0 | 0 | 3 |

Hallucination rate: share of answered runs with a clock time that appears in no tool result of the run; it does not decide whether a task passed, so `hallucinated` is also counted for passed runs. The other codes are counted per run, and a run can have several. "Failed without code": failed runs whose failure the scorer cannot classify, e.g. a judge verdict or a missed regex; such codes are set by hand.

## Methodology

| Run | Started | Commit | Harness | Repeats | Seed |
|---|---|---|---|---|---|
| `20261126-105446-full` | 2026-11-26 10:54 UTC | `ba853a5cdf10` | 1.0.0 | 3 | 1560330073 |

- Model `claude-haiku-5-5`, effort `medium`, max_tokens 16000, tool-call limit 8 unless a task sets its own, pause 3 s between runs.
- Task set `tasks/dev.jsonl` (SHA-256 `dd064694d0d2`); system prompt template SHA-256 `026578e015b7`, the same for every server, with the server's instructions in a fixed section.
- Judge for ambiguous and bad input tasks: `claude-haiku-5-5`, effort `medium`, prompt SHA-256 `792656604e81`, blind to the server; its check by review is in `bench/judge-review/agreement.md`.
- Prices (USD per million tokens): input 0.10, output 0.50, cache write 0.125, cache read 0.01.
- Oracle checks verify the named connection or departure against transport.opendata.ch, asked once per task from the reference time; grounding checks every clock time of the answer against the tool results.

| Server | Version | serverInfo | tools/list SHA-256 | Tool definition tokens | Covered | Not covered |
|---|---|---|---|---|---|---|
| perron-rich | repository | perron-rich 1.0.0 | `33ae1692d9fe` | 2124 | stops, trips, departures | – |
| perron-minimal | repository | perron-minimal 1.0.0 | `d78bbb5487c3` | 1158 | stops, trips, departures | – |
| perron-json | repository | perron-json 1.0.0 | `33ae1692d9fe` | 2124 | stops, trips, departures | – |
| perron-many | repository | perron-many 1.0.0 | `5f57e0d9dfd9` | 3870 | stops, trips, departures | – |
| official | hosted | transport.opendata.ch 2c28784a | `f899f0f82cbb` | 1386 | stops, trips, departures | – |
| spt | swiss-public-transport-mcp 0.1.10 | Swiss Public Transport 1.27.1 | `4052b02f3b2b` | 2063 | stops, trips, departures, occupancy | – |

Skipped servers (not part of the comparison):

- stm: missing STM_TRANSPORT_API_KEY (optional server)

Known limitations of the benchmark are listed in the README.

Fahrplandaten: opentransportdata.swiss (via transport.opendata.ch).
