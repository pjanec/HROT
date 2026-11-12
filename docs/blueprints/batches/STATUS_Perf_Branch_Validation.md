<!--STATUS
state: LIVE — in progress (exp/perf)
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

_(written at V-8)_

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
