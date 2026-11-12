<!--STATUS
state: LIVE — verdict written (CE-4454)
updated: 2026-11-12
current-answer: VERDICT (top), then V-1 … V-7
stale-below: nothing yet
related-designs:
  - ../../DESIGN_Parallel_Simulation.md — §12–§17: what exp/perf carries (POC 1→4 + the managed reads)
  - HANDOFF_Coordinator_To_Backend_Perf_Branch_Validation.md — this validation (dispatched at 07e178275)
  - HANDOFF_Coordinator_To_Ui_Windows_Perf_Measure.md — the Windows measurement of the same branch, in parallel
  - STATUS_Managed_Reads_Audit.md — the managed-reads part, validated there (CE-4445)
-->

# STATUS — validating `exp/perf` from every side

**Branch:** `exp/perf` at `06a3bc69e` (the merge) · **base:** `786e99599` (its merge base with `coordinator`) · ⛔ never merged into a lane here.

## VERDICT

⭐ **MERGE-READY — nothing must be fixed first** *(CE-4454, `2026-11-12`; the branch's changed features proven through their own rails, T-1)*.
⛔ It still merges only when the user says so: the branch stays experimental until then.

| item | state | finding |
|---|---|---|
| V-1 build + the changed projects' suites | ✅ | 0 build errors; every red is red at the base `786e99599`. Two census rails had new branch fields → **fixed** (`a1b074bdf` + `DESIGN_System_State_Inventory.md`) |
| V-2 defaults unchanged | ✅ | switches off ⇒ the base's world, entity for entity (only a per-process random `EntityGuid` differs, as it does base vs base) |
| V-3 determinism | ✅ | `DeterminismRails` 5/5 serial, at 1/2/4/8 threads, and Throughput |
| V-4 scenario regression | ⛔ stopped by ruling | user: test features, not scenarios — see V-4 |
| V-5 replication / recording | ✅ | no new red; managed-read detector **0 hits** over 74 726 frames |
| V-6 editor + frame rails (xvfb) | ✅ | no branch red; 2 order-dependent tests (green alone) |
| P-1 the 10 reds of the extra suites | ✅ | `Fdp.Examples.Scenarios.Tests` 9 (AutoDrive ×4, UrbanCombatNew ×5) and `Fdp.Presentation.Tests` 1 (`CE4351_AFixedBufferOfStructs…`): **all 10 red at the base**, run by name ⇒ pre-existing |
| P-2 / V-7 garbage per frame | ✅ | the 0 B rails in the V-1 suites green; the 1 000-man rig (V-7 below): **no regression** |
| ⚠ not run | stated | **31 test projects of projects the branch does not touch** — by ruling (CE-4454: the changed features' own rails prove a branch, not every suite) |

⚠ What would change this verdict: a red in one of the 31 unrun suites that is red only on `exp/perf`, or the Windows measurement
(`HANDOFF_Coordinator_To_Ui_Windows_Perf_Measure.md`) finding a slowdown with the switches off.

## V-1 — the build and the suites

The whole solution: `dotnet restore HROT.sln` + `dotnet build HROT.sln --no-restore` — **0 errors**. Then each suite `--no-build`.
Every red was re-run at the base (a worktree at `786e99599`, the red suites built there, the failing tests run by name).

| suite | exp/perf | the reds |
|---|---|---|
| `Fdp.Core.Tests` | 1 332 passed / 3 failed / 22 skipped | all 3 red at the base (wall-clock census, two JSON-options census) |
| `Fdp.ModuleHost.Tests` | 258 / 0 / 0 | — |
| `Fdp.Toolkits.Tests` | 4 960 / 3 / 1 | all 3 red at the base (`TakeCheckpointIntent`, two S0 id/type census) |
| `Fdp.Network.Cyclone.Tests` | 112 / 0 / 0 | — |
| `Hrot.Network.NED.Tests` | 197 / 0 / 0 | — |
| `Hrot.SimHost.Tests` | 2 698 / 10 / 6 | 9 red at the base (`CE454`, six `ShippedTreesAreCSharp`, the host-reference and DDS-placement census); `CE3345` (load chain) timed out at its 10 s budget in the full run — 3 / 3 green alone, green alone at the base ⇒ load, not the branch |
| `Hrot.Editor.Tests` | 913 / 2 / 43 | both red at the base |
| `Hrot.ClusterRunner.Tests` | 412 / 6 / 0 | all 6 red at the base — ⚠ but three CENSUS rails carried NEW entries from the branch (fixed, below) |

**Fixed on exp/perf** — the branch's parallel steps added state fields the census rails did not know (a diff of each failure's contents,
base vs perf):

| rail | the branch added | fix |
|---|---|---|
| `EverySystemHoldingEntities_ForgetsThemAtTheWorldBoundary` | `CalloutSystem._list/_about`, `StressSystem._list`, `RaycastSolverSystem._turretOf` | exempted as scratch (refilled from 0 each run / nulled in a `finally`) — `a1b074bdf` |
| `EveryFieldThatCanCarryStateBetweenFrames_IsClassified` | 18 fields (batch lists, the stamp-on-change buffer, the raycast job's copies, `StressSystem._prevNow`, the EQS-config gate's state, three experiment switches) | classified in `DESIGN_System_State_Inventory.md`: 15 **A**, 3 **K** — each read in code |

After both: the three census rails' failures equal the base's, entry for entry.

## V-2 — defaults unchanged (every opt-in switch OFF)

The same scenario, the same 600 fixed steps (10.0 s sim), one fresh headless editor per (tree, scenario), the base runner built in a
worktree at `786e99599`; every networked entity's full component dump compared (`GET /entities/{networkId}`).

| scenario | base vs exp/perf | base vs base (a second run) |
|---|---|---|
| `hill-attack-close` | 32 / 33 identical — the one difference: entity 1008's `EntityGuid` | **the same** 32 / 33, the same entity, only its `EntityGuid` |
| `cc-platoon-bound` | 58 / 59 identical — only entity 1014's `EntityGuid` | (not re-run) |
| `bc-house-defended` | ⛔ not compared — `GET /entities` answers 500 on BOTH trees (pre-existing) | |

⇒ ⭐ **exp/perf with its switches off produces the base's world**, exactly where the base reproduces itself (a spawned entity's GUID is
random per process). ⚠ Coverage: the entities carrying a network id (10 of 33, 16 of 59); parts sharing id 0 are not dumped by that route.
⚠ A cross-branch RECORDING replay was not compared; in-process, a recording replays to the same state hash with stamping and non-stamping
reads (`ManagedReadsReplayEquivalenceTests`, CE-4445).
📌 Tool rot found: `scripts/determinism-probe.py` enters preview after a live load (the current editor then holds an empty world and the first
step 504s), reads `/entities/{id}/state` (now 404), and Python's urllib sends `localhost` through the sandbox proxy. A scratch copy that
steps the paused live run and reads `/entities/{id}` was used; the repo script is unchanged.

## V-3 — determinism

`dotnet test Hrot/Runner/Hrot.SystemTests --no-build --filter DeterminismRails` (two fresh editor processes per rail):

| switches | result |
|---|---|
| none (serial) | ✅ 5 / 5 |
| `HROT_PARALLEL_SIM=1`, `HROT_SIM_THREADS` = 1 · 2 · 4 · 8 (Deterministic) | ✅ 5 / 5 at each |
| `HROT_PARALLEL_SIM=1 HROT_SIM_THREADS=4 HROT_SIM_MODE=throughput` | ✅ 5 / 5 |

The serial-vs-parallel rails at 1 / 2 / 4 / 8 threads (`ParallelEntitiesTests` P0, `ThreatEvaluationParallelRailTests`, `StressSystemTests` P1,
`CalloutsTests` P1) are green in V-1's suites.

## V-4 — the scenario regression: ⛔ STOPPED (user, `2026-11-12`)

> 🔒 **User, verbatim:** *"the check can not depend on how many scenarios we have"* · *"we need to test features, not scenarios"*

⇒ The validation runs on the **feature rails** (V-1, V-3, V-5, V-6), whose cost does not grow with the scenario catalogue. The 3 × ~3 h
regression (exp/perf serial, Throughput, base) was stopped after 66 of 107 scenarios of the first run.
⚠ That partial run had NO base run beside it, so it is evidence of nothing either way. For the record: 12 scenarios failed their checks.
29 more passed their checks and differed only from their goldens. The scenario regression's own history shows 15–21 check reds of 103 at
recent lane heads (`DESIGN_Scenario_Regression.md` §7).

## V-5 — replication and recording

| check | result |
|---|---|
| replication suites (Egress §7's gate list lives in these) | `Hrot.Network.NED.Tests` 197 / 0 · `Fdp.Network.Cyclone.Tests` 112 / 0 · `Fdp.Toolkits.Tests` Replication/Replay namespaces: no red beyond the base's (V-1) |
| record → replay → seek | `Fdp.Core.Tests` flight-recorder rails and `Fdp.Toolkits.Tests` Replay — green in V-1; `Hrot.ReplayBrowser.Tests` 96 / 0 / 8 (V-6, under a display); `ManagedReadsReplayEquivalenceTests` (both read modes, the same state hash) |
| ⭐ the managed-read detector (`HROT_MANAGED_MUTATION_CHECK=1`) over a 12-scenario subset (`bc-house-clear, bc-house-defended, bc-room-clear, bt-doors, bt-grenade-posture, bt-mortar-roof, bt-window-duel, cc-platoon-bound, df-civilian-ordered, pc-ambush-bypass, pc-attack-dry, uc-a10-zu23-dry`) | ✅ **0 hits** — 12 editor processes, 74 726 frames, 175 781 managed reads checked |

⚠ 6 of those 12 scenarios failed their checks with the detector on. The detector's verdict is the 0 hits; the scenario checks were not
compared with the base (V-4 stopped).

## V-6 — the editor and the UI (under `xvfb-run -a`)

| suite | exp/perf | the reds |
|---|---|---|
| `Hrot.Presentation.Tests` | 2 117 / 5 / 11 | 4 red at the base (three 3-D / film frame rails, one window-per-process); `TheBuildingDamagePlan…OnADisplayNode` green alone (2 / 2, twice) ⇒ order-dependent in the full run |
| `Hrot.Editor.AiShared.Tests` | 2 351 / 4 / 0 | 3 red at the base; `AIE004…GetFontForSize_NoContext_ReturnsZero` green alone (19 / 19, twice) ⇒ order-dependent (a theme context left by an earlier test) |
| `Hrot.Editor.Tests` | 953 / 2 / 3 | both red at the base (with a display it runs 40 more tests than V-1's 913) |
| `Hrot.Blueprints.Tests` | 4 239 / 1 / 9 | red at the base (the edit-dialog width frame rail) |
| `Hrot.ReplayBrowser.Tests` | 96 / 0 / 8 | — |
| `Hrot.Diagnostics.Breakpoints.Tests` | 171 / 0 / 0 | — |

⇒ no red the branch caused. ⚠ The two order-dependent ones were run alone at the base, not in a base full-suite run.

## V-7 — allocation (CE-4454 P-2)

The 0 B rails (`ParallelEntitiesTests`, the CE-2394 / CE-4445 allocation rails) are green inside V-1's suites. The scale rig: `scale-rig.py
--terrain scale-1 --men 1000 --vehicles 50 --aircraft 10 --seconds 30`, `HROT_QUERY_TIMINGS=0`, Debug runner, every switch off, one run per tree,
the runner given through `HROT_RUNNER_DLL` (⚠ `--runner-dll` puts the dll path on python's command line and `cluster-node.sh`'s
`pkill -f ClusterRunner.dll` then kills the rig itself).

| node | garbage B/frame exp/perf | base | work ms/frame exp/perf | base | entities exp/perf / base |
|---|---|---|---|---|---|
| CGF | **2 940 974** | 2 996 810 (−1.9 %) | 556 | 536 | 14 296 / 13 800 |
| SimHost | **5 363 519** | 5 336 141 (+0.5 %) | 261 | 224 | 13 063 / 12 665 |

⇒ ⭐ **no garbage regression** — the largest producers are the same systems at the same size on both trees (CGF `BrainTickSystem` 1.56 vs 1.63 MB,
SimHost `EqsSolverSystem` 3.86 vs 3.60 MB, `EventHistoryCaptureSystem` on both). ⚠ The rig's budget (0 B per steady frame) is missed by BOTH
trees — pre-existing, not this branch's. ⚠ Work per frame is one Debug run per tree on a 4-core VM, with ~3–4 % more entities on exp/perf at the
sample: not evidence either way; the Windows Release measurement is the performance answer.
