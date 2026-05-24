# BATCH-07 Report — Gizmo Renderer Wiring & Entity Health Bar

## Status

**Build:** SUCCESS (0 errors, pre-existing warnings only)
**Tests:** 15 / 15 PASSED (filter: `FullyQualifiedName~Gizmo`)

---

## Tasks Implemented

### TASK-GZ020 — Local Gizmo Renderer Wiring in IgApplication

**Files modified:**
- `Hrot/Subsystems/Hrot.IG/Hrot.IG.csproj` — Added `Fdp.Toolkits` project reference
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs` — Fields, initialization, system registration, properties

**Changes:**
1. Added `using Fdp.Toolkit.Diagnostics.Gizmos;` and `using Fdp.Toolkit.Diagnostics.Gizmos.Systems;`
2. Added private fields `_gizmoBuffer` (`DebugPrimitiveBuffer?`) and `_gizmoRegistry` (`GizmoRegistry?`)
3. In `InitializeEcs()`, after `ZoneObstacleRenderLayer`:
   - Creates `DebugPrimitiveBuffer(capacity: 4096)`
   - Creates `GizmoRegistry`
   - Creates `DebugGizmoLayer(31, _gizmoBuffer, _world.Bus)` and adds to canvas
4. Before `_kernel.Initialize()`: registers `DataDrivenGizmoSystem(_gizmoRegistry, _gizmoBuffer, isSelectedPredicate: null)` as a global system
5. Added public property `GizmoRegistry? GizmoRegistry => _gizmoRegistry`
6. Added internal property `DebugPrimitiveBuffer? GizmoBuffer => _gizmoBuffer`

**Tests (GizmoRendererWiringTests.cs):**
- SC-GZ020-1: `GizmoRegistry` is not null after init ✅
- SC-GZ020-2: `GizmoBuffer` is not null after init ✅
- SC-GZ020-3: `Register(HealthBarGizmoDefinition)` does not throw ✅

### TASK-GZ021 (partial) — Entity Health Bar Gizmo

**Files created:**
- `Hrot/Subsystems/Hrot.IG/Gizmos/HealthBarGizmoSettings.cs` — Static keys and defaults
- `Hrot/Subsystems/Hrot.IG/Gizmos/HealthBarGizmoDefinition.cs` — `IGizmoDefinition` implementation
- `Hrot/Subsystems/Hrot.IG/Gizmos/HealthBarGizmoInstance.cs` — `IStatefulGizmo` implementation
- `Hrot/Subsystems/Hrot.IG/Gizmos/GizmoRegistrar.cs` — Static registrar

**Tests (HealthBarGizmoTests.cs):**
- SC-GZ021-HB-1: `RequiredComponents` contains `typeof(IgHealthState)` ✅
- SC-GZ021-HB-2: `VisibilityPolicy == AlwaysVisiblePolicy.Instance` ✅
- SC-GZ021-HB-3: `UpdateAndDraw` with full-health entity calls `DrawEntityBadge` ✅
- SC-GZ021-HB-4: `OnInitialize` and `OnTeardown` do not throw ✅
- SC-GZ021-HB-5: `GizmoRegistrar.Register` registers both settings keys ✅

---

## Deviations from Instructions

1. **`IStatefulGizmo` signature mismatch in instructions:** Instructions showed `UpdateAndDraw(ISimulationView, Entity, IDebugDrawBuilder, bool isSelected)` and `OnTeardown(ISimulationView, Entity)`. The actual interface has `UpdateAndDraw(ISimulationView, Entity, float deltaTime, IDebugDrawBuilder)` and `OnTeardown()` (no parameters). Implementation uses the correct actual signatures.

2. **`IGizmoDefinition.RequiredComponents` type:** Instructions showed `int[]` (component IDs). The actual interface uses `Type[]` (CLR types). `GizmoRegistry.Register` internally resolves types via `ComponentTypeRegistry.GetId`. Implementation uses `typeof(IgHealthState)`.

3. **`GizmoSettingValue` property names:** Instructions showed `.AsFloat`. Actual struct uses `.FloatValue`. Fixed in `HealthBarGizmoInstance`.

4. **`IsRegistered` is internal:** `GizmoSettingsRegistry.IsRegistered(uint)` is `internal` and not visible from `Hrot.IG.Tests` (Fdp.Toolkits does not include it in InternalsVisibleTo). SC-GZ021-HB-5 uses `EnumerateAll()` (public) to verify key registration instead.

5. **DataDrivenGizmoSystem takes `IDebugDrawBuilder`:** The instructions referenced `DebugPrimitiveBuffer` directly as the drawBuilder argument. The constructor actually takes `IDebugDrawBuilder`. Since `DebugPrimitiveBuffer : IDebugDrawBuilder`, `_gizmoBuffer` is passed directly.

6. **Cache sizing:** `DataDrivenGizmoSystem` sizes its visibility cache to `registry.Rules.Count` at construction. Rules added via `GizmoRegistrar.Register` after init fall outside the cache — they are implicitly always visible (safe fallback per `gi.RuleIndex < cacheSize` check). This is a known design limitation deferred to Phase 6 (GZ015 GlobalDebugSettings integration).

---

## Issues Encountered

None. All source files compiled cleanly. All 15 gizmo tests passed on first run.

---

## Weak Points Spotted

- The two-phase registry (create system with empty registry, add rules later) means late-registered gizmo definitions bypass the global-visibility cache. This could be addressed by adding a `RefreshCache()` method to `DataDrivenGizmoSystem` or by deferring system construction until after all registrations complete.
- `GizmoRegistrar` is a static class — callers must supply both `GizmoRegistry` and `GizmoSettingsRegistry` explicitly. An alternative design would expose a `GizmoSettingsRegistry` property on `IgApplication` to avoid callers needing to create a separate instance.
