# IG-BATCH-06 Developer Report

**Batch:** IG-BATCH-06 — Advanced Rendering & Subsystems  
**Phase:** IG4  
**Tasks:** IG.4.1, IG.4.2, IG.4.3, IG.4.4, IG.4.5  
**Status:** ✅ COMPLETE  
**Test Result:** 170 / 170 passed

---

## Files Produced

### New production files

| File | Purpose |
|------|---------|
| `Hrot.IG/Components/HistoryTrailConstants.cs` | Named constants for trail buffer size, sample interval, colour, line width |
| `Hrot.IG/Components/HistoryTrail.cs` | Unmanaged circular-buffer component (`unsafe struct`, `fixed float[64]`) |
| `Hrot.IG/Components/VisualEffectStateConstants.cs` | Duration, scale, and RGBA constants for explosions and tracers |
| `Hrot.IG/Components/VisualEffectState.cs` | Unmanaged ECS component for ephemeral visual effects; `EffectType` enum; `TracerTarget` companion component |
| `Hrot.IG/Components/ContextMenuState.cs` | Managed class component holding the open/close flag, screen position, and action list |
| `Hrot.IG/Components/EditablePolyline.cs` | Managed class component holding a `List<Vector2>` of editable vertices |
| `Hrot.IG/IgEvents.cs` | `FireInteractionEvent` (unmanaged, `[EventId(3001)]`); `ContextActionsUpdate` / `ContextActionTriggered` (managed class events) |
| `Hrot.IG/Systems/HistoryRecordingSystem.cs` | Simulation-phase system that samples entity XY positions into `HistoryTrail` at configurable intervals |
| `Hrot.IG/Systems/EventToEffectSystem.cs` | Simulation-phase system that spawns explosion + tracer entities from `FireInteractionEvent` |
| `Hrot.IG/Systems/VisualEffectCleanupSystem.cs` | PostSimulation system (co-located in EventToEffectSystem.cs) that ages and destroys expired effect entities |
| `Hrot.IG/Systems/ContextMenuSystem.cs` | Simulation-phase system managing `ContextMenuState` open/close lifecycle and `ContextActionsUpdate` event ingestion |
| `Hrot.IG/Tools/EditToolConstants.cs` | Pick radius and handle-size constants for `EditTool` |
| `Hrot.IG/Tools/EditTool.cs` | `IMapTool` for vertex drag-editing of `EditablePolyline`; raises `OnPolylineCommitted` on right-click |
| `Hrot.IG/Modules/HistoryTrailModule.cs` | Thin `IModule` wrapper registering `HistoryRecordingSystem` |
| `Hrot.IG/Modules/EventEffectModule.cs` | Thin `IModule` wrapper registering `EventToEffectSystem` + `VisualEffectCleanupSystem` |

### Modified production files

| File | Change |
|------|--------|
| `Hrot.IG/IgApplication.cs` | Added `RegisterComponent<HistoryTrail/VisualEffectState/TracerTarget>()`, `RegisterManagedComponent<ContextMenuState/EditablePolyline>()`, `RegisterEvent<FireInteractionEvent>()`, `RegisterModule(new HistoryTrailModule())`, `RegisterModule(new EventEffectModule())` |

### New test files

| File | Tests |
|------|-------|
| `Hrot.IG.Tests/HistoryRecordingSystemTests.cs` | 9 tests — buffer overflow, ordering, ShowTrail flag, sub-frame timing, multi-tick accumulation |
| `Hrot.IG.Tests/EventToEffectSystemTests.cs` | 10 tests — no-event guard, explosion / tracer spawn counts, positions, TracerTarget values, cleanup tick, boundary expiry |
| `Hrot.IG.Tests/ContextMenuSystemTests.cs` | 8 tests — open/close flag, screen position, `ActiveMenuEntity`, multi-entity isolation, `ContextActionsUpdate` ingestion |
| `Hrot.IG.Tests/EditToolTests.cs` | 12 tests — ghost-point loading, vertex pick hit/miss, drag moves ghost, drag returns false without selection, right-click commit, committed-list copy independence |
| `Hrot.IG.Tests/AdvancedFeaturesIntegrationTests.cs` | 3 tests — full end-to-end scenario, multi-event spawn, per-entity trail flag isolation |

---

## Developer Insights

### Q1 — Memory layout and history-interval limitations

The fixed-size circular buffer (`fixed float _x[64]; fixed float _y[64]`) was the only viable approach here. Managed heap collections (`List<Vector3>`) cannot live inside an unmanaged ECS struct without boxing — which would put the trail on the GC heap and break the zero-allocation hot-path requirement (§CODE-STANDARDS §4).

The 64-point limit (`HistoryTrailConstants.MaxTrailPoints`) is a compile-time constant baked into the struct layout. This means the struct is always `64 × 4 × 2 + 16 = 528 bytes` regardless of how many points are actually stored — larger than a cache line but acceptable for rendering data read only once per frame. Increasing the limit is a source-change, not a runtime parameter, which is intentional: it prevents operators accidentally flooding memory.

The sub-frame timing issue was subtle. Zeroing `ElapsedSinceSample` on every sample would drift at non-round frame rates (e.g. 60 fps, 0.5 s interval → actual samples at 0.516 s, 1.033 s, …). The fix — `ElapsedSinceSample -= SampleInterval` instead of `= 0` — preserves the fractional remainder so drift cancels over time.

---

### Q2 — Effect lifecycle and decay strategy

The two-system split (`EventToEffectSystem` + `VisualEffectCleanupSystem`) was deliberate. A single system that both spawns and destroys in the same Execute call would destroy effects spawned in the current frame before the renderer ever sees them, because command-buffer playback applies all changes together at the end of the tick.

By running the spawn system in `SystemPhase.Simulation` and the cleanup system in `SystemPhase.PostSimulation`, the command-buffer ordering guarantees:

1. `EventToEffectSystem.Execute` → enqueues `CreateEntity + AddComponent` commands.  
2. Playback applies those commands → new entities exist in the repo.  
3. `VisualEffectCleanupSystem.Execute` sees only entities that were alive at the start of _its_ tick, never the just-spawned ones.  
4. Playback destroys the expired entities.

No custom lifecycle hooks or separate "pending spawn" queues were needed. The `IsExpired` property (`ElapsedTime >= Duration`) is a pure computed property on the struct, keeping the cleanup system stateless and trivially testable.

A simpler alternative — a `DestroyAfterSeconds` counter in a single system — was considered but rejected because it would require the cleanup logic to re-implement the age computation rather than delegating it to the component itself. The current design keeps the component as the single source of truth for expiry.

---

### Q3 — Isolating EditTool multi-point logic from Raylib inputs

The key isolation boundary is the `_ghostPoints` list and the `OnPolylineCommitted` event. The tool never writes to ECS during its interaction loop — it only mutates an in-memory `List<Vector2>`. The ECS write happens outside the tool, in whatever handler is subscribed to `OnPolylineCommitted`. This means:

- **Tests** can verify all vertex-selection, drag, and commit logic without a Raylib window by asserting against `GhostPoints` and the event arguments.  
- **Application code** can apply the committed list to ECS in one place without coupling it to the drag internals.

The vertex pick uses Euclidean distance (`Vector2.Distance`) against `EditToolConstants.VertexPickRadiusWorldUnits = 15f`. All constants are named (§CODE-STANDARDS §1). The inner loop iterates the ghost list once, tracking `minDist` — O(n) with no allocation.

The `Draw` method intentionally makes Raylib calls (`DrawLineEx`, `DrawCircle`) only in the rendering path, never in `HandleClick` or `HandleDrag`. This architectural separation is what allows the headless tests to cover 100 % of the stateful logic without a graphics context.

---

## Test Run Summary

```
Test Run Successful.
Total tests: 170
     Passed: 170
 Total time: 1.52 seconds
```

All 170 tests passed on first successful build. One compile error was fixed during development: `ISimulationView` was missing from the `using` directives of `EditToolTests.cs` (added `using ModuleHost.Core.Abstractions`), and an xUnit analyser warning (xUnit2013) was resolved by replacing `Assert.Equal(0, count)` with `Assert.Empty`.
