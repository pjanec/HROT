# BATCH-21 Report

**Batch:** BATCH-21
**Tasks:** GZ057, GZ058
**Status:** COMPLETED
**Build:** `Build succeeded. 0 Error(s)`
**Tests:** All new + regression tests pass (see below)

---

## Files Changed

| File | Task | Change |
|------|------|--------|
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts/IDebugDrawBuilder.cs` | GZ057 | Added default interface methods `DrawSpatialAnchor` and `DrawSemanticShape` |
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts/DebugPrimitiveBuffer.cs` | GZ057 | Implemented `DrawSpatialAnchor` and `DrawSemanticShape` overrides |
| `Hrot/Engine/Hrot.Presentation/Hrot.Presentation.csproj` | GZ058 | Added `GizmoRegistrarGenerator` Analyzer reference |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/IgEntityPresentationGizmo.cs` | GZ057 | NEW — SpatialAnchor + SemanticShape for IG entities, gated by `CullingState.IsVisible` |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/RouteGizmo.cs` | GZ058 | NEW — line segments for route waypoints, filtered by `TkbType == TacGraphic_Route` |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/MapOverlayGizmo.cs` | GZ058 | NEW — border polyline for map overlay entities, handles `IsClosed` |
| `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Gizmos/MissionPresentationGizmo.cs` | GZ058 | NEW — line from selected entity to mission task targets; manual registration (no `[GizmoProjector]`) |
| `Hrot/Subsystems/Hrot.IG/Hrot.IG.csproj` | GZ058 | Added `GizmoRegistrarGenerator` Analyzer reference |
| `Hrot/Subsystems/Hrot.IG/Gizmos/EffectPresentationGizmo.cs` | GZ058 | NEW — Sphere for Explosion, Line for Tracer effects |
| `Hrot/Subsystems/Hrot.IG/Gizmos/GizmoRegistrar.cs` | GZ058 | Changed to `partial class`; added calls to `Hrot.ScenarioEditor.Gizmos.GizmoRegistrar.RegisterAll` and self `RegisterAll` |
| `Hrot/Subsystems/Hrot.IG/IgApplication.cs` | GZ057-058 | Added manual `MissionPresentationGizmo` registration; re-enabled `StatelessGizmoSystem` |
| `Hrot/Subsystems/Hrot.SimHost/Hrot.SimHost.csproj` | GZ057 | Added `GizmoRegistrarGenerator` Analyzer reference |
| `Hrot/Subsystems/Hrot.SimHost/Gizmos/SimHostEntityPresentationGizmo.cs` | GZ057 | NEW — SpatialAnchor + SemanticShape for SimHost entities; optional `VehicleParams` for dims |
| `Hrot/Subsystems/Hrot.SimHost/Gizmos/GizmoRegistrar.cs` | GZ057 | NEW — partial wrapper class calling `RegisterAll` |
| `Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs` | GZ057 | Added `Hrot.SimHost.Gizmos.GizmoRegistrar.RegisterAll(...)` call |
| `Hrot/Subsystems/Hrot.CGF/Gizmos/CgfEntityPresentationGizmo.cs` | GZ057 | NEW — SpatialAnchor + SemanticShape for CGF entities; prefers `NetworkTransform` |
| `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | GZ057 | Added gizmo infrastructure (registry, buffer, system) and `CgfEntityPresentationGizmo` registration |

### Test Files

| File | Task | Tests Added |
|------|------|-------------|
| `FDP/Diagnostics/Fdp.Diagnostics.Contracts.Tests/ContractsStandaloneTests.cs` | GZ057 | `DrawSpatialAnchor_EmitsCorrectShape`, `DrawSemanticShape_EmitsCorrectShape` |
| `Hrot/Subsystems/Hrot.SimHost.Tests/Gizmos/SimHostEntityPresentationGizmoTests.cs` | GZ057 | SC_GZ057_1 through SC_GZ057_4 (new file) |
| `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/PresentationGizmoTests.cs` | GZ057-058 | SC_GZ057_5, SC_GZ057_6, SC_GZ058_1 through SC_GZ058_4 (new file) |

### Test Files Fixed (Pre-existing / Regression)

| File | Reason |
|------|--------|
| `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/HealthBarGizmoTests.cs` | Added missing component registrations (NetworkIdentity, CullingState, VisualEffectState, TkbIdentity, MapOverlayStyle) to `SC_GZ021_HB_5` which calls the full `GizmoRegistrar.Register()` |
| `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/HillAttackGizmoTests.cs` | Same fix for `SC_GZ021_HA_6` |
| `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/EntityRotationGizmoTests.cs` | Same fix for `SC_GZ021_ROT_4` |
| `Hrot/Subsystems/Hrot.IG.Tests/Gizmos/GizmosRemoteVisualizationTests.cs` | Fixed `SC_GZ015_2_MarshalSizeOf_Is_4_Bytes` — expected 4 bytes but struct is 8 due to `MaxGizmoFrameMs` added in BATCH-13 |

---

## Test Results

| Project | Tests | Result |
|---------|-------|--------|
| `Fdp.Diagnostics.Contracts.Tests` | 17 | Passed |
| `Hrot.SimHost.Tests` (gizmo filter) | 4 | Passed |
| `Hrot.IG.Tests` (gizmo filter) | 49 | Passed |

---

## Issues Encountered & Resolutions

1. **`AddManagedComponent` is `internal` on `EntityRepository`**
   - In `PresentationGizmoTests.SC_GZ058_3`, initial code called `_repo.AddManagedComponent(entity, plan)` directly.
   - Fix: used the public ECB pattern: `var ecb = (EntityCommandBuffer)((ISimulationView)_repo).GetCommandBuffer(); ecb.AddManagedComponent(entity, plan); ecb.Playback(_repo);` — same pattern as `HillAttackIntegrationTests.cs`.

2. **`StatelessGizmoRegistry.Register` requires all component types registered globally**
   - The global `ComponentTypeRegistry` is populated by `repo.RegisterComponent<T>()` calls.
   - Existing gizmo registrar tests (`SC_GZ021_HB_5`, `SC_GZ021_HA_6`, `SC_GZ021_ROT_4`) did not register the 5 new component types used by BATCH-21 gizmos.
   - Fix: added registrations for `NetworkIdentity`, `CullingState`, `VisualEffectState`, `TkbIdentity`, `MapOverlayStyle` in each of those 3 test setups.

3. **`SC_GZ015_2_MarshalSizeOf_Is_4_Bytes` was a stale test**
   - `GlobalDebugSettings` had `MaxGizmoFrameMs float` added in BATCH-13, making it 8 bytes.
   - The test still asserted 4 bytes. Fixed the assertion and updated the comment.

4. **Source generator does NOT propagate transitively**
   - `GizmoRegistrarGenerator` must be added as a direct `OutputItemType="Analyzer"` reference in EACH project that contains `[GizmoProjector]` classes.
   - Added to `Hrot.Presentation.csproj`, `Hrot.IG.csproj`, `Hrot.SimHost.csproj`.

5. **`StatelessGizmoSystem` was intentionally removed in GZ038 from IG**
   - GZ057-058 re-enables it for the new gizmos to execute per frame.
   - Replaced the old "NOT registered" comment in `IgApplication.cs` with the actual registration call.

6. **`MissionPresentationGizmo` cannot use `[GizmoProjector]`**
   - Its constructor requires `IGeographicTransform` — source generator cannot instantiate it.
   - Registered manually in `IgApplication.cs` after `GizmoRegistrar.Register(...)`.

---

## Design Decisions Beyond the Spec

- **`IgEntityPresentationGizmo` early-returns on `CullingState.IsVisible == false`** — avoids emitting diagnostics for off-screen entities, consistent with how render layers work.
- **`EffectPresentationGizmo` alpha from `ColorA * Alpha`** — mirrors the existing render layer formula; alpha is a byte cast from the product.
- **`RouteGizmo` uses `Position.Z` for canvas Y** — `RouteWaypoint.Position.Z` is the North axis (canvas Y), matching the coordinate convention described in the design.
- **`MapOverlayGizmo` closes the polygon by connecting last point back to first when `IsClosed`** — consistent with the existing `MapOverlayRenderLayer` behavior.

---

## Weak Points / Tech Debt Spotted

- **`ComponentTypeRegistry` is a global mutable singleton** — tests that call `RegisterComponent` leave state in the global registry between test runs. This is existing tech debt, not introduced in this batch.
- **`GizmosRemoteVisualizationTests.SC_GZ015_2`** was already stale since BATCH-13. No review caught this — suggests that registrar-level test updates should be a checklist item when adding fields to any gizmo singleton component.
- **`CgfSubsystem.cs` inline gizmo wiring** (lines ~660-670) has null-dereference warnings (CS8602) from the compiler because `_gizmoBuffer` is nullable. These are pre-existing patterns, suppressed by `<NoWarn>CS8602</NoWarn>` elsewhere but not explicitly in the CGF project.

---

## Suggested Git Commit Message

```
GZ057-058: entity SpatialAnchor/SemanticShape gizmos + IG effect/route/overlay/mission gizmos

- DrawSpatialAnchor and DrawSemanticShape added to IDebugDrawBuilder/DebugPrimitiveBuffer
- SimHostEntityPresentationGizmo, CgfEntityPresentationGizmo: broadcast entity positions/dims
- IgEntityPresentationGizmo: culling-gated IG entity gizmo (in Hrot.Presentation)
- EffectPresentationGizmo: explosion sphere + tracer line (in Hrot.IG)
- RouteGizmo, MapOverlayGizmo, MissionPresentationGizmo: route/overlay/mission debug visuals
- GizmoRegistrar updated in IG, SimHost; StatelessGizmoSystem re-enabled in IG
- Analyzer references added to Hrot.Presentation, Hrot.IG, Hrot.SimHost
- Fixed SC_GZ015_2 stale size assertion; fixed 3 registrar tests for new component types
```
