<!--STATUS
state: LIVE — dispatched, not started
updated: 2026-11-12
current-answer: the whole file
related-designs:
  - ../../DESIGN_Threaded_Simulation_Presentation.md — OWNS this: §14 the target shape (img/threading-architecture.svg), §14.1–14.2 the anti-race strategy and the gizmo split (img/threading-anti-race.svg), ⭐ §14.4 (on exp/threaded-poc-3) POC 3's verdict and the guard's list
  - ../../DESIGN_Parallel_Simulation.md — §12–§17 (on exp/perf): the parallel simulation this POC is combined with
  - STATUS_Threaded_POC_3.md (on exp/threaded-poc-3) — what is built and the measured cost to finish
  - HANDOFF_Coordinator_To_Backend_Perf_Branch_Validation.md — another backend session is validating exp/perf in parallel
-->

# HANDOFF — coordinator → backend: an OVERNIGHT EXPERIMENT — POC 4 of the threaded editor: finish the list, prove the hard modes, and run it WITH the parallel simulation

**For:** the idle backend session. **From:** coordinator. **Dispatched at `<stamp>`**, scope frozen there. ⭐ **Still an experiment.**
🔒 User, `2026-11-12`: *"how can we prove what is still unproven? one backend VM lane is still idle."* · *"yes, write the threaded POC 4 handoff"*.
⛔⛔ **Autonomous — never stop for a question** (R-4156). Decide, record with a claim table, continue; a failed premise stops THAT item (R-106).
Fold the as-built into §14 (as-built rule).

## Where to work

⭐ A NEW branch **`exp/threaded-poc-4`** from **`exp/threaded-poc-3`**, with **`exp/perf`** merged in (the parallel simulation POC 1→4 + the managed-read
flip — which is the SAME fix as POC 2's "reads are reads": keep `exp/perf`'s audited one, `ThreadedReads.PureReads`) and **`coordinator`**.
Status file `docs/blueprints/batches/STATUS_Threaded_POC_4.md` ON THAT BRANCH.
⛔⛔ **Never push to `exp/perf`** (another session validates it), never to `backend` or any lane. ⛔ No lane imports this branch. Switch
`HROT_THREADED_KERNEL=3` as POC 3 (default OFF). The VM proves correctness; extend the Windows command (every window HIDDEN).

## The items — in this order, as far as the night goes

| # | item | proof — done when |
|---|---|---|
| **F-1** | the merge: `exp/perf` into the POC 3 shape — one read path, one worker pool | the full solution builds; POC 3's rails (`WorldReferenceWalker`, `TwoWorldsTests`, `PublishedGizmoFrameTests`, `PureReadsTests`) and `exp/perf`'s (serial-vs-parallel, `DeterminismRails`, the pool) green |
| **F-2** | ⭐ **finish POC 3's list until the guard is SILENT** (`WorldGuard` reads + writes over the whole scripted session): ① `StationMode` one per world (`SharedApplicationBootstrapper.cs:239-240`) · ② `UnitDebugSettingsMemory` / `BrainObservation` on the presentation side, the setting riding to S as UI state · ③ ⭐ **the gizmo split as ONE move** — a main-side gizmo buffer; the pickers, tools, pick-box producers and the selection systems (`SelectionRequest`, `SelectionNotification`, `ToolActivationDrain`, `CenterOnEntity`) on the main thread; the selection rides to S each tick (§14.1 row 6); the 47 overlays stay on S · ④ typed **commands** for the HTTP `edit_component` / patch routes, the clock routes, the follow camera's `SimTransform`, the HTTP spawn; drag = a ghost in A, the drop one command · ⑤ `BlueprintTierSpec.Memory`'s read through the RW accessor | ⭐ **the guard reports 0** over the session, twice; picking, dragging, rotating, placing and deleting work while PLAYING (a rail each); the remaining list (if any) is the cost to finish |
| **F-3** | ⭐ **stepping and Run-to threaded** (Time Architecture §17: stepped mode waits for background jobs; the clock lives with the simulation — commands for play / pause / step / Run-to) | a stepped run of N steps gives the SAME world hashes threaded and unthreaded; Run-to lands on the same frame both ways; `DeterminismRails` with the switch ON |
| **F-4** | **hot reload threaded** — POC 3 run 3: the reload works but the node fell out of sync afterwards | root cause named; a reload during play, then 2 min in sync (the node's clock and the cluster's agree; 0 faults) |
| **F-5** | ⭐ **the COMBINATION** — the threaded editor (`=3`) WITH the parallel simulation (`HROT_PARALLEL_SIM=1`, Throughput, the brain pre-score) on the 1 000-man rig | the scripted session green, 0 faults; ⭐ the core budget: threads per process, the pool's `cores − 2` vs the main thread + the sim thread + the replica / egress work — oversubscribed or not, measured; the drawing frame, the sim tick and the sync, playing and paused; a recommendation for the budget rule when both are on |
| **F-6** | ⭐ **the verdict** — what is proven now, what is still not, the real cost to finish for a production version (S-1…S-6), go / change / stop; the Windows command (OFF · `=3` · `=3` + parallel sim) | top of the status file; folded into §14 |

## Rules

- ⛔ Default OFF; 0 B per steady frame with the switches OFF (R-2084); the editor's feature rails and a scenario-regression subset green with them OFF.
- **Measure before claiming**; "the guard is silent" is the proof, not a reading of the code. A pre-existing red names its base sha.
- Run anything slow in the background; never poll. ⛔ No screenshots or recordings in git.

**Done when:** F-6 is written — however far F-1…F-5 got.

---
_coordinator lane, 2026-11-12_
