<!--STATUS
state: LIVE
updated: 2026-11-10
current-answer: the table below
related-designs:
  - HANDOFF_Coordinator_To_Backend_Brain_Cost_1000.md — the frame (dispatched at 4385ab0d8)
  - ../../DESIGN_Evaluation_Cost_And_Cadence.md — the owning design (behaviors')
-->

# STATUS — backend: the brain's cost at 1 000 men (CE-4395)

Started at `cec9e162d` (merged `coordinator@cec9e162d`). Behaviors told: NOTE_Backend_To_Behaviors_Brain_Cost.md.

| step | state |
|---|---|
| B-0 baseline: scenario regression | ⚠ the full run (107 scenarios) is > 6 h on this VM — replaced by a TARGETED base-vs-head run of every scenario that failed at `95925a8ba` (9: condition failures + the `SimInstant` ∞-to-JSON crash). ⏳ running. The other 15 run at `95925a8ba` either passed or failed only on a golden that differs at t = 0 s (stale golden — before any brain tick) |
| B-1 attribute the cost (1 000 / 3 000 men, fighting) | ✅ Cost & Cadence §11g — the table; the superlinear terms named with file:line |
| B-2 fix the biggest | ✅ `95925a8ba` the parts index takes frame adds (3 000: 171 → 130 ms a tick) + `Release` reads the index · ✅ `30a45c708` the combat decisions at 0.2 s (R-3085; 1 000: **20.5 → 10.5 ms** a tick, 3 000: 130 → 102 ms) · ⏳ the Windows measurement (the budget is per the user's machine) |
| B-3 no garbage | ⏳ the brain still allocates (160 KB a frame at 1 000; 1.75 MB at 3 000) — next |

**Gates** (every suite built as its TEST project, then `--no-build`): `Fdp.Core.Tests` 1 297 / 2 red · `Fdp.Toolkits.Tests` (Utility, Behavior, Squad,
Perception, Replication) 1 383 / 1 red · `Hrot.SimHost.Tests` (all) 2 662 / 2 red after the rail moves. Every red is pre-existing: the
source-scanning rails name files outside the diff (`OneSharedJsonOptions`, `NoSystemSchedulesOnTheWallClock`, `S0IdTypes`,
`StrictNetworkSeparation`, `ShippedTreesAreCSharp` ×6), and `CgfLogicPackTests.CE454` fails at `deea88565` too (a clean worktree). New rails:
`PartIndexTests` CE4395 ×2, `EqsModuleTests` Release order, `UtilityScorerTests` CE4395 ×2 — each red-proved by a compiling mutation.
