# Perron Bench report

Runs `20261126-090526-full`, 2026-11-26. Suite `full` (5 tasks), model `claude-haiku-5-5`, effort `medium`, max_tokens 16000, 2 repeat(s) per task and server.

## Success

| Server | Coverage | Success, covered tasks (95 % CI) | Success, all tasks (95 % CI) | pass^2 | Scored runs | Pending | Unscored | Infra errors |
|---|---|---|---|---|---|---|---|---|
| perron-rich | 5/5 | 90 % (70–100) | 90 % (70–100) | 80 % | 10 | 0 | 0 | 0 |
| perron-minimal | 5/5 | 80 % (40–100) | 80 % (40–100) | 80 % | 10 | 0 | 0 | 0 |
| perron-json | 5/5 | 70 % (30–100) | 70 % (30–100) | 60 % | 10 | 0 | 0 | 0 |
| perron-many | 5/5 | 100 % (100–100) | 100 % (100–100) | 100 % | 10 | 0 | 0 | 0 |
| official | 5/5 | 90 % (70–100) | 90 % (70–100) | 80 % | 10 | 0 | 0 | 0 |
| spt | 5/5 | 90 % (70–100) | 90 % (70–100) | 80 % | 10 | 0 | 0 | 0 |

Success is the share of passed scored runs per task, averaged over tasks; the interval is a 95 % percentile bootstrap over tasks (2000 resamples, fixed seed). "All tasks" counts tasks a server does not cover as 0. pass^k: share of covered tasks whose k scored runs all passed. Pending (judge missing) and unscored (no oracle data) runs are not counted.

```mermaid
xychart-beta
    title "Success on covered tasks (%)"
    x-axis ["perron-rich", "perron-minimal", "perron-json", "perron-many", "official", "spt"]
    y-axis "Success (%)" 0 --> 100
    bar [90, 80, 70, 100, 90, 90]
```

## Paired comparison with `perron-rich`

| Server | Tasks compared | Difference in success (95 % CI) | Interval excludes 0 |
|---|---|---|---|
| perron-minimal | 5 | -10 pp (-60 to +30) | no |
| perron-json | 5 | -20 pp (-40 to 0) | no |
| perron-many | 5 | +10 pp (0 to +30) | no |
| official | 5 | 0 pp (0 to 0) | no |
| spt | 5 | 0 pp (-30 to +30) | no |

Differences in percentage points on the tasks both servers cover and scored, bootstrapped over tasks. Only differences whose interval excludes 0 support a statement in the README.

## Cost and efficiency

| Server | Tool definition tokens | Tool calls per run | Failed tool calls | Tokens per run | Cost per task | Cost per solved task | Actual cost per task (cached) | Judge cost (total) | LLM time per run | Tool time per run |
|---|---|---|---|---|---|---|---|---|---|---|
| perron-rich | 2124 | 1.7 | 12 % | 7181 | $0.0010 | $0.0011 | $0.0006 | $0.0021 | 3.6 s | 0.29 s |
| perron-minimal | 1158 | 1.1 | 27 % | 3772 | $0.0006 | $0.0007 | $0.0004 | $0.0019 | 3.2 s | 0.23 s |
| perron-json | 2124 | 1.9 | 11 % | 8334 | $0.0011 | $0.0016 | $0.0007 | $0.0017 | 3.9 s | 0.29 s |
| perron-many | 3870 | 1.9 | 11 % | 11700 | $0.0014 | $0.0014 | $0.0008 | $0.0020 | 4.0 s | 0.33 s |
| official | 1386 | 2.1 | 0 % | 6725 | $0.0010 | $0.0011 | $0.0007 | $0.0021 | 4.1 s | 0.21 s |
| spt | 2063 | 2.7 | 0 % | 11422 | $0.0015 | $0.0017 | $0.0010 | $0.0024 | 5.3 s | 0.34 s |

Cost per task and per solved task are computed without prompt caching (cache tokens at the input price), because caching favours servers with large tool definitions; the actual cost with caching is shown for reference. Judge cost is not part of the server's cost.

```mermaid
xychart-beta
    title "Cost per solved task without caching (US cents)"
    x-axis ["perron-rich", "perron-minimal", "perron-json", "perron-many", "official", "spt"]
    y-axis "US cents" 0 --> 0.21
    bar [0.109, 0.072, 0.162, 0.145, 0.111, 0.171]
```

## Errors

| Server | Hallucination rate | infra | tool_limit | gave_up | hallucinated | not_optimal | wrong_time | wrong_stop | tool_error_loop | failed without code |
|---|---|---|---|---|---|---|---|---|---|---|
| perron-rich | 0 % | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| perron-minimal | 0 % | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 2 |
| perron-json | 20 % | 0 | 0 | 0 | 2 | 0 | 0 | 0 | 0 | 1 |
| perron-many | 0 % | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| official | 0 % | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 1 |
| spt | 10 % | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 1 |

Hallucination rate: share of answered runs with a clock time that appears in no tool result of the run; it does not decide whether a task passed, so `hallucinated` is also counted for passed runs. The other codes are counted per run, and a run can have several. "Failed without code": failed runs whose failure the scorer cannot classify, e.g. a judge verdict or a missed regex; such codes are set by hand.

## Methodology

| Run | Started | Commit | Harness | Repeats | Seed |
|---|---|---|---|---|---|
| `20261126-090526-full` | 2026-11-26 09:05 UTC | `02deb702dba6` | 1.0.0 | 2 | 546789015 |

- Model `claude-haiku-5-5`, effort `medium`, max_tokens 16000, tool-call limit 8 unless a task sets its own, pause 3 s between runs.
- Task set `tasks/dev.jsonl` (SHA-256 `dd064694d0d2`), only dev-017, dev-018, dev-019, dev-020, dev-021; system prompt template SHA-256 `026578e015b7`, the same for every server, with the server's instructions in a fixed section.
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

Known limitations of the benchmark are listed in the README.

Fahrplandaten: opentransportdata.swiss (via transport.opendata.ch).
