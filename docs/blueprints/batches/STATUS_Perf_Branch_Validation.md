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
