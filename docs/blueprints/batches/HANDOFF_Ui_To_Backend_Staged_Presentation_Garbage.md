# Handoff: ui → backend — the staged-presentation copy allocates ≈ 35–40 MB/s on a headless node (CE-11047)

| | |
|---|---|
| **From** | `ui`, 2026-11-23 |
| **For** | `backend` (owner of the staged presentation, [DESIGN_Threaded_Simulation_Presentation.md](../../DESIGN_Threaded_Simulation_Presentation.md) §15, CE-3446) |
| **User, verbatim** | *"headless performance is important (for running with Unreal as presentation layer later)"* (via `unreal-ig`); then *"Yes."* to `ui`'s measured re-scope |
| **Ids** | the tracker row is `ui`'s CE-11047 (re-targeted to you); allocate your own for the build |

## Measured (`ui`, headless editor, `showcases/stechovice-battle` live, 4 cores, clean `b6a0c7f4c` Release, EventPipe `GCAllocationTick` with stacks, 20 s)

- **45–55 MB/s** allocated on the main thread while the battle runs (≈ 4 MB per tick at ~13 ticks/s); gen2 collections climbing.
- **74 %** under `EntityRepository.SyncPresentationFrom` / `SyncPresentationCore` (`StagedPresentation.Step` → `StagingWorlds.Publish` 37 %, `StagedPresentation.Pull` → `StagingWorlds.Pull` 37 %):
  - **52 %** in `ManagedComponentTable<T>.CopyRowFrom` — by type: arrays of the managed SINGLETON types, each ~7.4 %: `IGeographicTransform[]`, `IEqsTemplateRegistry[]`, `ICoverProvider[]`, `INavmeshProvider[]`, `TerrainWorld[]`, `WorldEpoch[]`, `SoundEmissionRing[]`, `ReplayState[]` — a new array per copy;
  - `PaddedVersion[]` 10 %;
  - **~13 %** in `ComponentTable<T>..ctor` / `NativeChunkTable<T>..ctor` (`SpatialGridData` 11.6 %, `RaycastBatchData` 1.5 %, `EqsResultPool` 1.1 %) — component TABLES constructed per sync.
- (The other 21 % is `GizmoFramePanel.Describe` — `ui` fixes that, CE-11045.)

## Ask

Make a steady copy allocate nothing (CLAUDE.md HOT PATH, R-2084): reuse the destination table's chunk arrays for managed rows (singleton
tables especially — their values rarely change), and never construct a component table during a sync (create once, or skip types the
presentation does not read). Rail: `GC.GetAllocatedBytesForCurrentThread` across N steady `Publish` + `Pull` cycles on a world with
managed singletons `== 0`; the copied values still equal the source. Re-measure on headless Stechovice (allocation MB/s, gen2 per minute).

## ⭐ Root cause, traced by `ui` (`2026-11-23`, read at `b6a0c7f4c`) — the user asked "are you sure? it goes against any possible rules for zero allocation"

The singleton OBJECTS are not copied (a managed singleton slot holds a reference). The defect is two steps of ONE sync fighting over the same slots:

1. `SyncPresentationCore` step 2, `SyncFromCore` → `SyncSingletonById` (`EntityRepository.Sync.cs:216-225`, `:295-306`): BORROWS ten singletons —
   `SpatialGridData`, `EqsResultPool`, `IEqsTemplateRegistry`, `ICoverProvider`, `INavmeshProvider`, `RaycastBatchData`, `EqsSolverGlobalState`,
   `TerrainWorld`, `IGeographicTransform`, `SoundEmissionRing` — the slot points at the source's table and `_borrowedSingletons` is set.
2. Step 4, `CopySingletonsFrom` (`EntityRepository.TwoWorlds.cs:37-54`): a borrowed slot ⇒ *"first sync only (a type change or a borrowed slot)"* ⇒
   `NewTableLike` — a NEW table (and its chunk array / `NativeChunkTable`), the borrow bit cleared.
3. Next tick, step 2 sees the slot no longer referencing the source's table ⇒ borrows again ⇒ step 4 builds a new table again.

⇒ ten singleton tables rebuilt EVERY tick. It matches the allocation profile type for type (`IGeographicTransform[]`, `INavmeshProvider[]`,
`TerrainWorld[]` … ≈ 7.4 % each; `NativeChunkTable<SpatialGridData>..ctor` 11.6 %, `RaycastBatchData` / `EqsResultPool` ctors). ⭐ **The design decides the direction** (the user: *"Does presentation need the singletons copied? … Why wouldn't it read them?"*): §13.1 / D-4 of
`DESIGN_Threaded_Simulation_Presentation.md` give these the treatment **SHARE** (`TerrainWorld`, `IGeographicTransform`, `INavmeshProvider`,
`ICoverProvider`, `IEqsTemplateRegistry`, `SpatialGridData` — *"immutable services shared"*); only per-tick values (`GlobalTime`, the traces)
are COPIED. Step 2's borrow IS that share; ⇒ **step 4 (`CopySingletonsFrom`) must not replace a borrowed slot** — skip it. Check the two not in
§13.1's share rows (`EqsResultPool`, `RaycastBatchData`, `EqsSolverGlobalState`, `SoundEmissionRing`: sim-written every tick) against §13.1 /
§13.2 and say in the as-built which treatment each has. Rail: two consecutive presentation syncs of an unchanged world ⇒ the
same singleton table instances and 0 B.

