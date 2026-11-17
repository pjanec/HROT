<!--STATUS
state: LIVE — for exp/agg-behaviors (and agg-backend for DP3-B)
updated: 2026-11-17
current-answer: the whole note
related-designs:
  - ../../designs/aggregate-units/DESIGN_DP3_Planned_Posts.md — the design these decisions approve (on exp/agg-behaviors)
  - ../../designs/aggregate-units/DESIGN_Stochastic_Units_And_Resolution.md — §16a / SV-5 / R-8330
-->

# NOTE — coordinator → agg-behaviors: D-P3 planned posts APPROVED as written (R-4286)

🔒 User, `2026-11-17`: *"approved, write the notes"* — on the four sub-questions of `DESIGN_DP3_Planned_Posts.md` §6, with their leans.

| # | approved |
|---|---|
| DP3-A | reuse SquadDefend's OWN plan (extract `DefencePlan.Step` over an `IDefenceCrew`, constrained generic, no boxing); the stochastic crew is the unit + its slot table; squad echelon only |
| DP3-B | ASYNC planning, stepped by `UnitPostPlanningSystem`; the manager waits in `Planning` for `PlannedPosts.Ready` or `DeadlineSeconds` (10 s), forced splits as today — ⭐ the manager change is **agg-backend's** (`UnitResolution`); agg-behaviors sends it the note it needs |
| DP3-C | `PlannedPosts` = component **138** (your block), unmanaged ≤ 1024 B, recorded, `NoScenario`, no wire descriptor |
| DP3-D | ① now: `Materialise` places each man AT his planned post; ② with X-2: the re-resolved Defend carries them as D-2f `Posts` (`MaxPosts` 8 → 13 in that slice) |

⭐ The physical `SquadDefend` stays byte-for-byte the same apart from the extract-method and the carrier parameter — its own suites
(`SquadDefend*`, the defend scenarios) are the proof, run before and after. Mark the design `READY-TO-BUILD` and build ST-5 C against it.
