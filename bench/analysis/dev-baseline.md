# Ablations and tool improvements on the development set (task 4.2)

Model Claude Haiku 5.5, 25 dev tasks, 3 repeats. Two runs:

- **Before:** baseline [`20261126-105446-full`](../results/20261126-105446-full/report.md) (task 4.1, all servers).
- **After:** [`20261126-175920-full`](../results/20261126-175920-full/report.md), only the four Perron variants, after the changes below. The foreign servers were not run again, because nothing changed for them.

The two runs have different reference times and oracle data, so the before/after numbers are not a paired comparison. The per-task view below shows where the changes come from.

**This is the development set.** Perron was improved here after reading its failures; the foreign servers were not. The fair comparison is the final run on the held-out test set (task 4.3).

## Ablations (baseline, paired with `perron-rich`)

| Variant | What differs | Success, covered tasks | Difference to rich (95 % CI) | Tool definition tokens | Cost per solved task |
|---|---|---|---|---|---|
| perron-rich | – | 91 % | – | 2124 | $0.0009 |
| perron-minimal | one-sentence descriptions | 88 % | −3 pp (−12 to +4) | 1158 | $0.0007 |
| perron-json | results as JSON | 93 % | +1 pp (−3 to +7) | 2124 | $0.0009 |
| perron-many | 7 tools instead of 3 | 94 % | +3 pp (0 to +7) | 3870 | $0.0013 |

No interval excludes 0. These runs support no claim that one variant solves more tasks. What they do show:

- **Number of tools:** `perron-many` costs about 45 % more per solved task (63 % more tokens per run), because of its larger tool definitions. Success is the same within the interval.
- **Descriptions:** `perron-minimal` is the cheapest (−24 % per solved task). It had more failed tool calls (7 % against 4 %) and the most failures without a code, all from the judge (6 runs).
- **Format:** `perron-json` is indistinguishable from `perron-rich` in success and cost.

## Failures in the baseline and what changed

| Task | Failures before (rich, minimal, json, many) | Cause | Change |
|---|---|---|---|
| dev-009, at least 10 min per transfer | 3, 2, 3, 3 (and on every foreign server) | **Tool gap.** No server offered a minimum transfer time. The agents saw the best connection only with a 5-minute transfer and fell back to a later direct train. | New parameter `min_transfer_minutes` (0–60) in `plan_trip` and the connection tools of `many`. |
| dev-018, "Oberdorf" | 0, 2, 0, 0 | **Tool bug.** Candidates were only stops without a comma, so Oberdorf BL ("Oberdorf BL, Station") was missing. With a single place-level stop Perron would have picked Oberdorf SO silently (AK-2.4). | A place with only stops within it is its own candidate unless a place-level stop covers it. |
| dev-017, "Buchs" | 1, 3, 2, 1 | **Description.** Haiku passed "Buchs SG" itself before the tool could list the candidates. | `rich` descriptions: "Pass place names as the user wrote them; do not add a canton or region yourself." `minimal` stays as it is (it is the ablation). |
| dev-014, Interlaken Ost to Zermatt | 1, 0, 0, 0 | **Scorer.** The answer was right (arrival 15:50) but gave 13:21, the first vehicle after an 11-minute walk, instead of 13:10. It also named an alternative in full, and the check counted only the alternative. | Scorer version 3: the oracle stores the first vehicle's departure and the last vehicle's arrival when a connection starts or ends with a walk, and both count as naming the connection. Stored traces lack these fields, so a rescore of the baseline changes nothing. |
| dev-021, date outside the timetable | 1, 1, 0, 0 | **Judge.** It mistakes today's times, given for comparison, for times of the requested date (known from task 3.5). | None; the judge prompt stays fixed. |

A first idea for dev-014 was to let a partly named entry count even when another entry is named in full. It was dropped: on the baseline it also passed dev-009/perron-json r1, whose answer recommends a later train and rejects the best arrival.

## After the changes

| Variant | Success, covered tasks, before → after | pass^3 | Tool definition tokens | Tokens per run | Cost per solved task |
|---|---|---|---|---|---|
| perron-rich | 91 % → 100 % | 83 % → 100 % | 2124 → 2250 | 6155 → 6433 | $0.0009 → $0.0008 |
| perron-minimal | 88 % → 93 % | 83 % → 87 % | 1158 → 1211 | 4089 → 3995 | $0.0007 → $0.0006 |
| perron-json | 93 % → 97 % | 91 % → 91 % | 2124 → 2250 | 6538 → 6663 | $0.0009 → $0.0009 |
| perron-many | 94 % → 97 % | 91 % → 91 % | 3870 → 4248 | 10061 → 11035 | $0.0013 → $0.0014 |

Passed runs per task where something changed (out of 3):

| Task | perron-rich | perron-minimal | perron-json | perron-many |
|---|---|---|---|---|
| dev-009 | 0 → 3 | 1 → 2 | 0 → 3 | 0 → 3 |
| dev-018 | 3 → 3 | 1 → 3 | 3 → 3 | 3 → 3 |
| dev-017 | 2 → 3 | 0 → 0 | 1 → 2 | 2 → 2 |
| dev-014 | 2 → 3 | 3 → 3 | 3 → 3 | 3 → 3 |
| dev-021 | 2 → 3 | 2 → 3 | 3 → 2 | 3 → 2 |
| dev-004 | 3 → 3 | 3 → 2 | 3 → 3 | 3 → 3 |

- **dev-009:** every agent passed `min_transfer_minutes: 10` in all 12 runs. The one failure (minimal r1) named a valid connection that was not the earliest.
- **dev-018:** `perron-minimal` now lists Oberdorf BL and passes in every run.
- **dev-017:** `perron-rich` passes 3/3. With `minimal` descriptions Haiku still adds "SG" itself (0/3), as with `json` and `many` in some runs, so the new sentence helps but does not settle it.
- **dev-021:** judge noise in both directions, as before.
- **dev-004/minimal r3: a known limit of the oracle check.** The answer names the best arrival (08:58) with the user's time (08:00) instead of the departure (08:02), and the alternative in full; the check counts only the alternative. This is the same pattern as dev-014, without a walk. It is left as it is, to avoid tuning the scorer on single cases.

In the after run the paired differences to `perron-rich` are −7 pp (minimal), −3 pp (json) and −3 pp (many), and none of their intervals excludes 0.

## Cost

Before (baseline, all six servers): $0.21 with caching. After (four variants): $0.13 with caching, $0.24 estimated by the dry run. Smoke run before the full run: [`20261126-175816-smoke`](../results/20261126-175816-smoke), 6/6 passed, $0.0044.
