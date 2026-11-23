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
