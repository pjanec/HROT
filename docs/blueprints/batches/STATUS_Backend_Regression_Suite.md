<!--STATUS
state: LIVE — in progress, unattended
updated: 2026-11-16
current-answer: the whole file (the item table first)
related-designs:
  - ../../DESIGN_Scenario_Regression.md — THE design (§11); as-built folded there (§11.5)
  - HANDOFF_Coordinator_To_Backend_Regression_Suite.md — the dispatch (frozen at 816edc3b9)
  - HANDOFF_Coordinator_To_Backend_Feature_Scenarios.md — item 2
  - STATUS_Backend_Feature_Scenarios.md — item 2's own status
-->

# STATUS — the two-tier regression suite, backend (R-4279) — unattended

Started at `0ef5b4084` (merged `coordinator@1a8f074a6`), marker `7e7cfc7a0`.

| # | item | state | result |
|---|---|---|---|
| 1 | RT-3 visible-state golden, a WARNING | ✅ built | each snapshot carries `visible` (`VisibleState.Paths` + every path the checks read); the golden verdict compares only times, rounds, bursts, positions, health and visible state (`ScenarioGolden.CompareVisible`); a difference or a missing golden is a WARNING (`VerifyResult.Warnings`; exit 0; `summary.json` `warned`; junit system-out); the full comparison (`Compare`) stays for the replay harness and leaves `visible` out. `farm-under-fire` and `ua-drone-shelter`: PASS with "golden different (a WARNING)" on their old goldens → re-captured once → PASS, golden SAME |
| 2 | feature scenarios | ⏳ | — |
| 3 | RT-5 cluster tier | ⏳ | — |
| 4 | RT-6 replication sanity | ⏳ | — |
| 5 | RT-8 GitHub Actions | ⏳ | — |
| 6 | RT-7 Python checks → checks.json | ⏳ | — |
| 7 | the 41 scenarios without checks | ⏳ | — |
| 8 | CAPABILITIES marks | ⏳ | — |

## Lane decisions (R-3xxx block — each says what would change it)
| # | decision | why · what would change it |
|---|---|---|
| R-3086 | the visible state = position, health, `EntityInfo.ForceId`, `IsEmbarkedTag`, `NavigationStatus.Phase`, `WeaponPosture`, `CivilianState.Mode`, `Demeanour`, `Custody`, `AnimalState`, `FlockState` + every path a scenario's checks read (`VisibleState.Paths`, one list) | what a viewer of the unit sees; a component a user sees that is not listed is one line in that list |
| R-3087 | a MISSING golden is a warning too (not only a different one) | a scenario that asks for a golden but has none is not broken; its first green run captures it |

## Gates
| gate | result |
|---|---|
| `Hrot.Editor.Tests` `ScenarioChecksTests` + `DirectorRunnerTests` | 45 / 45 (new `RT3_TheGolden_ComparesVisibleState_AndADifferenceIsAWarning`; red-proofs: CompareVisible over everything ⇒ red; the golden back in `Passed` ⇒ red). ⚠ the first build broke the replay harness's 6 rails (H1a/H1b/H2/H3/H5/R1 compared the new `visible` map) — fixed: the full comparison leaves it out |
| scenarios `farm-under-fire`, `ua-drone-shelter` | PASS before (warning) and after the re-capture (golden SAME) |
