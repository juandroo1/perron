# Perron Bench report

Runs `20261126-201722-test`, 2026-11-26. Suite `test` (20 tasks), model `claude-sonnet-5-5`, effort `medium`, max_tokens 16000, 3 repeat(s) per task and server.

## Success

| Server | Coverage | Success, covered tasks (95 % CI) | Success, all tasks (95 % CI) | pass^3 | Scored runs | Pending | Unscored | Infra errors |
|---|---|---|---|---|---|---|---|---|
| perron-rich | 19/20 | 100 % (100–100) | 95 % (85–100) | 100 % | 57 | 0 | 0 | 0 |
| official | 19/20 | 95 % (86–100) | 90 % (78–100) | 89 % | 57 | 0 | 0 | 0 |
| spt | 20/20 | 97 % (92–100) | 97 % (92–100) | 90 % | 60 | 0 | 0 | 0 |

Success is the share of passed scored runs per task, averaged over tasks; the interval is a 95 % percentile bootstrap over tasks (2000 resamples, fixed seed). "All tasks" counts tasks a server does not cover as 0. pass^k: share of covered tasks whose k scored runs all passed. Pending (judge missing) and unscored (no oracle data) runs are not counted.

```mermaid
xychart-beta
    title "Success on covered tasks (%)"
    x-axis ["perron-rich", "official", "spt"]
    y-axis "Success (%)" 0 --> 100
    bar [100, 95, 97]
```

## Paired comparison with `perron-rich`

| Server | Tasks compared | Difference in success (95 % CI) | Interval excludes 0 |
|---|---|---|---|
| official | 19 | -5 pp (-14 to 0) | no |
| spt | 19 | -4 pp (-9 to 0) | no |

Differences in percentage points on the tasks both servers cover and scored, bootstrapped over tasks. Only differences whose interval excludes 0 support a statement in the README.

## Cost and efficiency

| Server | Tool definition tokens | Tool calls per run | Failed tool calls | Tokens per run | Cost per task | Cost per solved task | Actual cost per task (cached) | Judge cost (total) | LLM time per run | Tool time per run |
|---|---|---|---|---|---|---|---|---|---|---|
| perron-rich | 2250 | 1.3 | 4 % | 6373 | $0.0160 | $0.0160 | $0.0075 | $0.0020 | 3.9 s | 0.23 s |
| official | 1386 | 1.6 | 0 % | 6020 | $0.0162 | $0.0171 | $0.0089 | $0.0029 | 4.9 s | 0.45 s |
| spt | 2063 | 1.5 | 7 % | 7129 | $0.0184 | $0.0190 | $0.0098 | $0.0033 | 4.6 s | 0.38 s |

Cost per task and per solved task are computed without prompt caching (cache tokens at the input price), because caching favours servers with large tool definitions; the actual cost with caching is shown for reference. Judge cost is not part of the server's cost.

```mermaid
xychart-beta
    title "Cost per solved task without caching (US cents)"
    x-axis ["perron-rich", "official", "spt"]
    y-axis "US cents" 0 --> 2.29
    bar [1.597, 1.709, 1.903]
```

## Errors

| Server | Hallucination rate | infra | tool_limit | gave_up | hallucinated | not_optimal | wrong_time | wrong_stop | tool_error_loop | failed without code |
|---|---|---|---|---|---|---|---|---|---|---|
| perron-rich | 0 % | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| official | 2 % | 0 | 0 | 0 | 1 | 1 | 1 | 0 | 0 | 1 |
| spt | 0 % | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 2 |

Hallucination rate: share of answered runs with a clock time that appears in no tool result of the run; it does not decide whether a task passed, so `hallucinated` is also counted for passed runs. The other codes are counted per run, and a run can have several. "Failed without code": failed runs whose failure the scorer cannot classify, e.g. a judge verdict or a missed regex; such codes are set by hand.

## Methodology

| Run | Started | Commit | Harness | Repeats | Seed |
|---|---|---|---|---|---|
| `20261126-201722-test` | 2026-11-26 20:17 UTC | `53a4d915e47f` | 1.0.0 | 3 | 1113845917 |

- Model `claude-sonnet-5-5`, effort `medium`, max_tokens 16000, tool-call limit 8 unless a task sets its own, pause 3 s between runs.
- Task set `tasks/test.jsonl` (SHA-256 `429b02dfd6cf`); system prompt template SHA-256 `026578e015b7`, the same for every server, with the server's instructions in a fixed section.
- Judge for ambiguous and bad input tasks: `claude-haiku-5-5`, effort `medium`, prompt SHA-256 `792656604e81`, blind to the server; its check by review is in `bench/judge-review/agreement.md`.
- Prices (USD per million tokens): input 2.00, output 10.00, cache write 2.50, cache read 0.10.
- Oracle checks verify the named connection or departure against transport.opendata.ch, asked once per task from the reference time; grounding checks every clock time of the answer against the tool results.

| Server | Version | serverInfo | tools/list SHA-256 | Tool definition tokens | Covered | Not covered |
|---|---|---|---|---|---|---|
| perron-rich | repository | perron-rich 1.0.0 | `c6ac2ff1eb5c` | 2250 | stops, trips, departures | – |
| official | hosted | transport.opendata.ch 2c28784a | `f899f0f82cbb` | 1386 | stops, trips, departures | – |
| spt | swiss-public-transport-mcp 0.1.10 | Swiss Public Transport 1.27.1 | `4052b02f3b2b` | 2063 | stops, trips, departures, occupancy | – |

Known limitations of the benchmark are listed in the README.

Fahrplandaten: opentransportdata.swiss (via transport.opendata.ch).
