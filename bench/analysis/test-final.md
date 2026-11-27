# Final comparison on the held-out test set (task 4.3)

20 test tasks, written before the Perron tools by an isolated subagent and frozen in task 1.3. Claude Code never read them before this task. Three repeats per task and server, the same system prompt, tool-call limit and reference time for every server in a task round. Two runs:

- **Haiku 5.5, all servers:** [`20261126-193532-test`](../results/20261126-193532-test/report.md), the four Perron variants, `official` and `spt` (`stm` skipped without key).
- **Sonnet 5.5, control:** [`20261126-201722-test`](../results/20261126-201722-test/report.md), the servers of the main comparison (`perron-rich`, `official`, `spt`). The judge stays Haiku 5.5.

Perron was improved on the development set after reading its failures (task 4.2); the foreign servers were not. These test runs are the fair comparison: nothing was changed on the tools, descriptions, scorer or judge prompt after them. The one correction is a data error in a check, below.

## A data error in test-002

test-002 asks which station has the ID 8500218. Every server answered "Olten" in every run, and every run failed: the regex of the check was written as `"(?i)\bOlten\b"` in the JSON file, where `\b` is a backspace, not a word boundary, so it could never match. Sandro corrected the pattern to `\\b` (new SHA-256 in the decision log of plan.md), the same correction was applied to the copy of the check stored in the 27 test-002 traces, and both runs were rescored (`bench rescore`, no model calls). The agents never see the checks, so the correction changes the scoring, not what the agents did. All 27 runs pass with the corrected pattern. The format test of the task sets now rejects regex patterns with control characters. Before the correction every server lost about 5 pp; the paired differences were the same.

## Main comparison (Haiku 5.5)

| Server | Success, covered tasks (95 % CI) | pass^3 | Difference to perron-rich (95 % CI) | Tool definition tokens | Tokens per run | Cost per solved task |
|---|---|---|---|---|---|---|
| perron-rich | 95 % (86–100) | 89 % | – | 2250 | 7085 | $0.0010 |
| official | 93 % (81–100) | 89 % | −2 pp (−14 to +7) | 1386 | 8565 | $0.0013 |
| spt | 93 % (85–100) | 85 % | −2 pp (−7 to +4) | 2063 | 9007 | $0.0013 |

19 of 20 tasks are covered by every server; the occupancy task (test-020) only by `spt`, which passes it 3/3. No interval excludes 0: **on the test set, Perron does not solve measurably more tasks than the official or the community server.** All three solve about 93–95 % of the covered tasks.

What the data do show: Perron needs fewer tool calls per run (1.5 against 1.8) and fewer tokens per run (7085 against 8565 and 9007), and costs about a quarter less per solved task ($0.0010 against $0.0013), although its tool definitions are larger than those of `official`. The cost difference has no interval in the report; it is the same direction as on the development set.

**Target of spec.md §7 (≥ 0.85 on covered tasks, Haiku 5.5):** reached. `perron-rich` 95 %, lower bound of the interval 86 %.

## Sonnet 5.5 control

| Server | Success, covered tasks (95 % CI) | pass^3 | Difference to perron-rich (95 % CI) | Tokens per run | Cost per solved task |
|---|---|---|---|---|---|
| perron-rich | 100 % (100–100) | 100 % | – | 6373 | $0.0160 |
| official | 95 % (86–100) | 89 % | −5 pp (−14 to 0) | 6020 | $0.0171 |
| spt | 97 % (92–100) | 90 % | −4 pp (−9 to 0) | 7129 | $0.0190 |

Sonnet solves a few more tasks on every server than Haiku (perron-rich 95 → 100 %, official 93 → 95 %, spt 93 → 97 %; separate runs with different reference times, not a paired comparison). The intervals of the differences reach 0 but do not exclude it, so there is no supported statement that Perron is better with Sonnet either. Sonnet costs about 16 times as much per solved task as Haiku on the same server. With the stronger model the gap between the servers' token use narrows (official uses fewer tokens per run than Perron with Sonnet).

## Ablations on the test set (Haiku 5.5, paired with perron-rich)

| Variant | What differs | Success, covered tasks | Difference to rich (95 % CI) | Tool definition tokens | Tokens per run | Cost per solved task |
|---|---|---|---|---|---|---|
| perron-rich | – | 95 % | – | 2250 | 7085 | $0.0010 |
| perron-minimal | one-sentence descriptions | 100 % | +5 pp (0 to +14) | 1211 | 4454 | $0.0007 |
| perron-json | results as JSON | 100 % | +5 pp (0 to +14) | 2250 | 7681 | $0.0010 |
| perron-many | 7 tools instead of 3 | 98 % | +4 pp (0 to +9) | 4248 | 11748 | $0.0014 |

No interval excludes 0, as on the development set. The variants differ in cost, not measurably in success:

- **Number of tools:** `perron-many` uses 66 % more tokens per run and costs 46 % more per solved task ($0.00143 against $0.00098), for the same success.
- **Descriptions:** `perron-minimal` is the cheapest variant (−34 % per solved task, $0.00065) and lost nothing on the test set. On dev it had the most failures (task 4.2: dev-017, Haiku adds the canton itself); none of the test tasks hit that weakness.
- **Format:** `perron-json` and `perron-rich` cost the same and solve the same tasks.

`perron-rich` is the variant with the most failures here (3 runs, two of them judge verdicts on test-014, below); with 19 tasks and 3 repeats a single task decides such differences.

## Tool definition tokens and cost

Within the Perron variants, cost per solved task follows the size of the tool definitions: 1211 tokens $0.0007, 2250 tokens $0.0010, 4248 tokens $0.0014 (Haiku). Across servers the number of calls matters as much: `official` has the smallest definitions (1386 tokens) but more calls and longer results, and costs more per solved task than `perron-rich`.

## Failures

Failed runs after the correction of test-002, both runs:

| Task | What it asks | Failures (Haiku) | Failures (Sonnet) | Cause |
|---|---|---|---|---|
| test-006 | latest departure Baden → Frauenfeld for 09:30 | perron-rich 1 | – | **Agent.** The answer recommends 07:38 although the tool result lists 08:08, the latest that arrives in time. |
| test-008 | earliest arrival with at least 15 min per transfer | spt 1 | official 2 | **Tool gap.** Neither `official` nor `spt` has a minimum transfer time; the agents search via stations and name a connection with a short transfer or a late detour. Perron used `min_transfer_minutes` in all 15 runs and passed them. The parameter was added in task 4.2 after the same failure on dev-009. |
| test-011 | first arrival at Brig after 17:00 | official 3 | – | **Tool output.** The arrivals of `official` came without times; Haiku guessed or used a connection from one direction and named a later train. Sonnet worked around it in all 3 runs. |
| test-014 | "Rüti" (ZH or GL), ambiguous | perron-rich 2, perron-many 1, official 1, spt 2 | spt 1 | **Agent, judged.** The answers assume Rüti ZH and say so, but name no other Rüti; the rubric asks for the alternatives too. The verdicts follow the rubric. |
| test-015 | departures from "Wangen", ambiguous | spt 1 | official 1, spt 1 | **Judge.** The answers list both Wangen with correct times from the tools; the judge, blind to tool results by design, calls the times invented. The known weakness of the blind judge (task 3.5). |

## Hallucination rate

The report counts a run as `hallucinated` when its answer contains a clock time that appears in no tool result. On the test set all 10 flags of the Haiku run are false positives of the grounding check:

- **Durations in French:** test-018 answers write durations as "2h21" and "2h13"; the check reads them as the clock times 02:21 and 02:13 (6 runs with Haiku: perron-rich 3, perron-minimal 1, perron-many 1, spt 1).
- **Times the agent asked for:** test-017 answers repeat "06:00", the time the agent itself put into a tool call that failed (date outside the timetable); the check only looks at tool results (perron-rich 1, perron-minimal 1).
- **The only true case, with Sonnet:** `official` (test-006 r3) names "the next connection at 09:42", which is in no tool result; the run passes, because the recommended connection is right.

The hallucination rates in the reports (Haiku: perron-rich 7 %, perron-minimal 4 %) therefore say nothing about the servers. The check stays unchanged, because the test set may not be used to tune the scorer; the limitation is listed in the README.

## Cost

Haiku, six servers: $0.21 with caching ($0.30 estimated by the dry run without caching). Sonnet, three servers: $1.53 with caching ($2.89 estimated). The judge (Haiku) cost $0.02 in both runs together.
