# Perron Bench report

Runs `20261126-193532-test`, 2026-11-26. Suite `test` (20 tasks), model `claude-haiku-5-5`, effort `medium`, max_tokens 16000, 3 repeat(s) per task and server.

## Success

| Server | Coverage | Success, covered tasks (95 % CI) | Success, all tasks (95 % CI) | pass^3 | Scored runs | Pending | Unscored | Infra errors |
|---|---|---|---|---|---|---|---|---|
| perron-rich | 19/20 | 95 % (86–100) | 90 % (77–100) | 89 % | 57 | 0 | 0 | 0 |
| perron-minimal | 19/20 | 100 % (100–100) | 95 % (85–100) | 100 % | 57 | 0 | 0 | 0 |
| perron-json | 19/20 | 100 % (100–100) | 95 % (85–100) | 100 % | 57 | 0 | 0 | 0 |
| perron-many | 19/20 | 98 % (95–100) | 93 % (83–100) | 95 % | 57 | 0 | 0 | 0 |
| official | 19/20 | 93 % (81–100) | 88 % (73–100) | 89 % | 57 | 0 | 0 | 0 |
| spt | 20/20 | 93 % (85–100) | 93 % (85–100) | 85 % | 60 | 0 | 0 | 0 |

Success is the share of passed scored runs per task, averaged over tasks; the interval is a 95 % percentile bootstrap over tasks (2000 resamples, fixed seed). "All tasks" counts tasks a server does not cover as 0. pass^k: share of covered tasks whose k scored runs all passed. Pending (judge missing) and unscored (no oracle data) runs are not counted.

```mermaid
xychart-beta
    title "Success on covered tasks (%)"
    x-axis ["perron-rich", "perron-minimal", "perron-json", "perron-many", "official", "spt"]
    y-axis "Success (%)" 0 --> 100
    bar [95, 100, 100, 98, 93, 93]
```

## Paired comparison with `perron-rich`

| Server | Tasks compared | Difference in success (95 % CI) | Interval excludes 0 |
|---|---|---|---|
| perron-minimal | 19 | +5 pp (0 to +14) | no |
| perron-json | 19 | +5 pp (0 to +14) | no |
| perron-many | 19 | +4 pp (0 to +9) | no |
| official | 19 | -2 pp (-14 to +7) | no |
| spt | 19 | -2 pp (-7 to +4) | no |

Differences in percentage points on the tasks both servers cover and scored, bootstrapped over tasks. Only differences whose interval excludes 0 support a statement in the README.

## Cost and efficiency

| Server | Tool definition tokens | Tool calls per run | Failed tool calls | Tokens per run | Cost per task | Cost per solved task | Actual cost per task (cached) | Judge cost (total) | LLM time per run | Tool time per run |
|---|---|---|---|---|---|---|---|---|---|---|
| perron-rich | 2250 | 1.5 | 7 % | 7085 | $0.0009 | $0.0010 | $0.0005 | $0.0022 | 3.1 s | 0.23 s |
| perron-minimal | 1211 | 1.3 | 8 % | 4454 | $0.0007 | $0.0007 | $0.0004 | $0.0018 | 2.9 s | 0.19 s |
| perron-json | 2250 | 1.5 | 6 % | 7681 | $0.0010 | $0.0010 | $0.0006 | $0.0023 | 3.2 s | 0.35 s |
| perron-many | 4248 | 1.5 | 6 % | 11748 | $0.0014 | $0.0014 | $0.0007 | $0.0023 | 3.4 s | 0.16 s |
| official | 1386 | 1.8 | 1 % | 8565 | $0.0012 | $0.0013 | $0.0007 | $0.0022 | 4.6 s | 0.38 s |
| spt | 2063 | 1.8 | 2 % | 9007 | $0.0012 | $0.0013 | $0.0007 | $0.0032 | 3.9 s | 0.48 s |

Cost per task and per solved task are computed without prompt caching (cache tokens at the input price), because caching favours servers with large tool definitions; the actual cost with caching is shown for reference. Judge cost is not part of the server's cost.

```mermaid
xychart-beta
    title "Cost per solved task without caching (US cents)"
    x-axis ["perron-rich", "perron-minimal", "perron-json", "perron-many", "official", "spt"]
    y-axis "US cents" 0 --> 0.18
    bar [0.098, 0.065, 0.099, 0.143, 0.128, 0.128]
```

## Errors

| Server | Hallucination rate | infra | tool_limit | gave_up | hallucinated | not_optimal | wrong_time | wrong_stop | tool_error_loop | failed without code |
|---|---|---|---|---|---|---|---|---|---|---|
| perron-rich | 7 % | 0 | 0 | 0 | 4 | 1 | 0 | 0 | 0 | 2 |
| perron-minimal | 4 % | 0 | 0 | 0 | 2 | 0 | 0 | 0 | 0 | 0 |
| perron-json | 0 % | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| perron-many | 2 % | 0 | 0 | 0 | 1 | 0 | 0 | 0 | 0 | 1 |
| official | 0 % | 0 | 0 | 0 | 0 | 2 | 1 | 0 | 0 | 1 |
| spt | 2 % | 0 | 0 | 0 | 1 | 0 | 1 | 0 | 0 | 3 |

Hallucination rate: share of answered runs with a clock time that appears in no tool result of the run; it does not decide whether a task passed, so `hallucinated` is also counted for passed runs. The other codes are counted per run, and a run can have several. "Failed without code": failed runs whose failure the scorer cannot classify, e.g. a judge verdict or a missed regex; such codes are set by hand.

## Methodology

| Run | Started | Commit | Harness | Repeats | Seed |
|---|---|---|---|---|---|
| `20261126-193532-test` | 2026-11-26 19:35 UTC | `53a4d915e47f` | 1.0.0 | 3 | 6960936 |

- Model `claude-haiku-5-5`, effort `medium`, max_tokens 16000, tool-call limit 8 unless a task sets its own, pause 3 s between runs.
- Task set `tasks/test.jsonl` (SHA-256 `429b02dfd6cf`); system prompt template SHA-256 `026578e015b7`, the same for every server, with the server's instructions in a fixed section.
- Judge for ambiguous and bad input tasks: `claude-haiku-5-5`, effort `medium`, prompt SHA-256 `792656604e81`, blind to the server; its check by review is in `bench/judge-review/agreement.md`.
- Prices (USD per million tokens): input 0.10, output 0.50, cache write 0.125, cache read 0.01.
- Oracle checks verify the named connection or departure against transport.opendata.ch, asked once per task from the reference time; grounding checks every clock time of the answer against the tool results.

| Server | Version | serverInfo | tools/list SHA-256 | Tool definition tokens | Covered | Not covered |
|---|---|---|---|---|---|---|
| perron-rich | repository | perron-rich 1.0.0 | `c6ac2ff1eb5c` | 2250 | stops, trips, departures | – |
| perron-minimal | repository | perron-minimal 1.0.0 | `efdc70514fe4` | 1211 | stops, trips, departures | – |
| perron-json | repository | perron-json 1.0.0 | `c6ac2ff1eb5c` | 2250 | stops, trips, departures | – |
| perron-many | repository | perron-many 1.0.0 | `6f6230b5ed69` | 4248 | stops, trips, departures | – |
| official | hosted | transport.opendata.ch 2c28784a | `f899f0f82cbb` | 1386 | stops, trips, departures | – |
| spt | swiss-public-transport-mcp 0.1.10 | Swiss Public Transport 1.27.1 | `4052b02f3b2b` | 2063 | stops, trips, departures, occupancy | – |

Skipped servers (not part of the comparison):

- stm: missing STM_TRANSPORT_API_KEY (optional server)

Known limitations of the benchmark are listed in the README.

Fahrplandaten: opentransportdata.swiss (via transport.opendata.ch).
