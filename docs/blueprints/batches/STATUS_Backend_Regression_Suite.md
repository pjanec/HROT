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
| 2 | feature scenarios | 🟡 nearly done | [`STATUS_Backend_Feature_Scenarios.md`](STATUS_Backend_Feature_Scenarios.md): **8 PASS with goldens** (drive-by, drive-by-grenade, parked-bomb 9/9, horse-rider, guard-dog, flock-flush, herd-road, village-fight); swarm-raid ⚠ 10/11 (CE-3454, behaviors); drone-dash ⏳. Found and fixed CE-3453, CE-3455, CE-3456, CE-3459; filed CE-3452, CE-3454, CE-3457, CE-3458 |
| 3 | RT-5 cluster tier | ✅ built | `--verify <s> --mode all` → `ClusterVerifyRunner` + `ClusterVerifyWorld` (design §11.5). `farm-under-fire` 8/8 and `ua-drone-shelter` 11/11 **PASS** in the cluster tier. Found building it: every state cue (near/task/side/health/embark/gesture) read FALSE in a world without a repository — `ConditionBook.StateHolds` (R-3094) |
| 4 | RT-6 replication sanity | ✅ built | `ReplicationSanity.Judge` on `/diagnostics/architecture` in every cluster run; first runs: no problem, one note (an unsubscribed `PathResponseBatch`) |
| 5 | RT-8 GitHub Actions | ✅ built (`af95eaed6`) | `.github/workflows/scenario-regression.yml`: build once → editor-tier + cluster-tier matrix, nightly cron + manual dispatch, junit summary; ⏳ **the first manual run** (runner cores / memory / DDS) needs the workflow on the default branch — measured once it merges |
| 6 | RT-7 Python checks → checks.json | 🟡 scoped | 📐 measured: the script's `SCENARIOS` table has **107** scenarios (not 81) — **87** already carry a committed `checks.json`, **17** get one in item 7, **3** have none (`pc-assault`, `morale-barrage-green`, `morale-barrage-veteran`). ⇒ the work is an AUDIT per scenario (every `run_*` condition is an `expect` row, add the missing ones), then delete the condition from the script. Queued to the worker after item 7 |
| 7 | the scenarios without checks | 🟡 worker | 23 `checks.json` written so far (in the tree, uncommitted until reviewed); worker paused by the rate limit, resumed `2026-11-16 23:00` |
| 8 | CAPABILITIES marks | 🟡 part | motorcycles + animals marked by their feature scenarios (`d9a8af458`); the rest after items 6–7 |

## Lane decisions (R-3xxx block — each says what would change it)
| # | decision | why · what would change it |
|---|---|---|
| R-3086 | the visible state = position, health, `EntityInfo.ForceId`, `IsEmbarkedTag`, `NavigationStatus.Phase`, `WeaponPosture`, `CivilianState.Mode`, `Demeanour`, `Custody`, `AnimalState`, `FlockState` + every path a scenario's checks read (`VisibleState.Paths`, one list) | what a viewer of the unit sees; a component a user sees that is not listed is one line in that list |
| R-3087 | a MISSING golden is a warning too (not only a different one) | a scenario that asks for a golden but has none is not broken; its first green run captures it |
| R-3094 | a world with no repository judges state cues on the dumped components, through AnchorBook's own value comparisons | the cluster tier needs it; one judge, two readings (CE-4117 N6) |
| R-3096 | `SwarmRaid` fulfils `Dash` (no contact reaction may halt it or send it to cover) | a dedicated `Raid` order kind (behaviors lane) |
| R-3088…R-3093, R-3095 | the feature scenarios' decisions | [`STATUS_Backend_Feature_Scenarios.md`](STATUS_Backend_Feature_Scenarios.md) |

## Gates
| gate | result |
|---|---|
| `Hrot.Editor.Tests` `ScenarioChecksTests` + `DirectorRunnerTests` | 45 / 45 (new `RT3_TheGolden_ComparesVisibleState_AndADifferenceIsAWarning`; red-proofs: CompareVisible over everything ⇒ red; the golden back in `Passed` ⇒ red). ⚠ the first build broke the replay harness's 6 rails (H1a/H1b/H2/H3/H5/R1 compared the new `visible` map) — fixed: the full comparison leaves it out |
| scenarios `farm-under-fire`, `ua-drone-shelter` | PASS before (warning) and after the re-capture (golden SAME) |
