<!--STATUS
state: PAUSED (user, 2026-11-12 — the lane moved to HANDOFF_Coordinator_To_Backend_Entity_Access_Census.md); F-1…F-2 partly done, F-3…F-6 not started
updated: 2026-11-12
current-answer: "Where it stopped" (top) · then "Measured" · then "The plan to resume"
related-designs:
  - ../../DESIGN_Threaded_Simulation_Presentation.md — OWNS this: §14 the target shape · ⭐ §14.2.0 NO gizmo split (the user's decision, this session) · §14.4 POC 3
  - HANDOFF_Coordinator_To_Backend_Threaded_POC_4.md — the handoff (CE-4448, dispatched at 40c328f9c) — PAUSED
  - STATUS_Threaded_POC_3.md — what this builds on
  - HANDOFF_Coordinator_To_Backend_Entity_Access_Census.md — what the lane does instead
-->

# STATUS — threaded POC 4 (branch `exp/threaded-poc-4`) — ⏸ PAUSED

## Where it stopped

| item | state | sha |
|---|---|---|
| F-1 the merge | ✅ `exp/perf` + `coordinator` into the POC 3 shape: ONE read path (`exp/perf`'s audited `ThreadedReads.PureReads`, default ON); the A-1 tracer hooks AND the CE-4445 mutation detector both kept (`EntityRepository.TryGetComponent`, `UnsafeShim.GetRO`). The Release runner builds; two scripted sessions in mode 3 (1 000 men, demo-city) green, 0 worker faults. ⏳ NOT run: the full-solution build after F-2, and the rails (`WorldReferenceWalker`, `TwoWorldsTests`, `PublishedGizmoFrameTests`, `PureReadsTests`, `exp/perf`'s serial-vs-parallel + `DeterminismRails`) | `56be8a53b` `945144e63` |
| F-2 ① `StationMode` per world | ✅ `SharedApplicationBootstrapper.InstallStation(world, ctx)` — ONE helper, called for S by the bootstrapper and for A by the editor; A's kept out of the sync (`_twSingletonExcept`). The guard's StationMode hits are gone | `7d99cb66f` |
| F-2 ② debug settings on A | ✅ `UnitDebugSettings` owned by A (out of the sync), its requests ridden A → S at the sync (`UnitDebugSettings.TakeRequestsFrom` — S keeps its own `_managed` and breakpoint arming); `BrainObservation` / `UnitDebugSettingsMemory` ticks moved to the presentation step in mode 3 (`TickDebugSettingsUi`) | `7d99cb66f` |
| F-2 ③ the gizmos | ⭐ **decided, not built**: NO split — the whole gizmo group runs once per tick in the presentation step (design §14.2.0) | `28ddeae64` |
| F-2 ④–⑥, F-3…F-6 | ⏳ not started | — |

## Measured (this session)

| what | result | how |
|---|---|---|
| ⭐ the gizmo group per tick, playing (VM, mode 3) | overlays (`StatelessGizmoSystem`, already sliced 256 / rule / frame) **8.7–10.6 ms**; interaction (`GlobalGizmoManager` 0.2 + `DataDrivenGizmoSystem` 0.3 + `MapSelfCheckSystem` 0.8) **≈ 1.3 ms**; for scale BrainTick 52–117 ms, node frame mean 159 ms, drawing frame mean 34 ms | `/diagnostics/architecture` sampled every 15 s |
| ⭐ the types only the gizmos read | **9** (`DebugState`, `AcousticEmitter`, `HeardTraces`, `NavigationIntent`, `Utterance`, `ScenarioIgnoreTag`, `RoutePlan`, `ObstacleShape`, `DangerAreaCognitiveBuffer`) — all read by the inspector too; gizmos read 37 types, the presentation side 64 (290 with the inspector) | A-1 trace + `scripts/access-trace-gizmo-diff.py` |
| the sync (ALL types — the masked sync was never run in mode 3) | tables **5.49 ms** + rows / map **0.47 ms** per tick | `[POC2] syncs 564` |
| ⭐ the guard after ①② (short session: load, select, play 45 s, 3-D view) | **14 distinct hits**, all below | `HROT_WORLD_GUARD=1` |

**The guard's 14 hits — and which planned fix removes each:**

| hit | count | fix |
|---|---|---|
| HTTP `list_entities` writes `BlueprintBlackboard4096` / `256` into A — the read path `RootParamsAccess.TryGetRootBytes` → `OccurrenceStoreAccess.TryFindSlot` → `BlueprintTierSpec.Memory` (the RW resolver) | 125 + 60 | ⑥: a read-only `TryFindSlot` (via `MemoryReadOnly`) for `RootParamsAccess` |
| `SelectionRequestSystem` reads / writes A's `SelectionState`, `SelectionStore`, `NetworkIdentity` from the tick | 10 | ③: the whole gizmo group (selection systems included) moves to the presentation step |
| `SimTransform` written by `layer.Update: Map camera` | 3 | ④: the follow camera's write → UI state or a command |
| HTTP sets `GlobalTime`, `WorldEpoch` in A | 1 + 1 | ④ / F-3: clock commands |

## Found, not fixed

- ⚠ **The row copy follows the SELECTION, not the inspected entity** (`SyncPresentationWorldCore` takes `SelectionState.IsSelected`): a PINNED
  inspector on an unselected entity gets no row copy ⇒ stale values for types outside the mask (masked mode only). Agreed with the user: copy
  rows for the selected ∪ inspected / pinned entities, with a rail.
- The inspector reads **290** types — every type it can list, collapsed or not; the row copy (all components of one entity, µs) is the right
  shape, ⛔ not a per-expanded-component mask.
- ⛔ For F-5: the guard decides "the sim side" by the tracer's context, which flows by `AsyncLocal` — the ONE pool's dedicated worker threads
  (exp/perf) do not inherit it ⇒ a parallel system's writes would read as "S written off the sim thread". The pool must mark a frame-lane batch
  as working for the tick before F-5 can be judged.

## The plan to resume (the handoff's items, as amended by the user this session)

1. **F-1** the full-solution build + the rails above, scoped narrowly.
2. **F-2** ③ the WHOLE gizmo group into the presentation step (once per tick, main thread, A; the drawing thread draws the last published frame) ·
   rows for selected ∪ inspected / pinned · ④ commands for the HTTP edit / clock routes and the follow camera · ⑥ the read-only slot path —
   then the guard at 0 over the full session, twice; a rail each for pick / drag / rotate / place / delete while playing.
3. **F-3** play / pause / step / Run-to as commands; same world hashes threaded and not; `DeterminismRails` with the switch on.
4. **F-4** the OFF baseline for "edit after a hot reload" first, then the root cause (suspect: the reload builds on the node-frame thread ⇒ no heartbeat).
5. **F-5** after the pool marks frame-lane batches as tick work: `=3` + `HROT_PARALLEL_SIM=1` + Throughput + the pre-score — threads, oversubscription, frame / tick / sync.
6. **F-6** the verdict, §14 as built, the Windows command.
