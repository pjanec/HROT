# Test Fix Reports

## FDP-G01: DebugGizmoLayer Tests (SC-GZ013, SC-GZ025, SC-GZ026)

**Batch**: BATCH-11  
**Date**: 2025  
**Tests fixed**: 12/12 (DebugGizmoLayerHitTests, DebugGizmoLayerActivationTests, DebugGizmoLayerGizmoTests)

### Root Causes

1. **NullReferenceException in Draw()** (`DebugGizmoLayer.cs:~102`, `DebugPrimitiveRenderer2D.cs`)  
   Tests inject no `IResourceProvider` so `ctx.Resources` was null; code called `ctx.Resources.Get<MapCamera>()` unconditionally.

2. **AccessViolationException from Raylib in headless mode**  
   `DebugPrimitiveRenderer2D._inner.Render()` calls into native Raylib draw calls that require an initialized window. With no window (unit test environment), this crashed.

3. **`HandleInput` was a stub (`=> false`)**  
   No hit detection was implemented, so click tests always got no events and `TestHook_IsInteractionActive` never returned `true`.

### Files Changed

| File | Change |
|------|--------|
| `FDP/Engine/Fdp.Presentation/Vis2D/Layers/DebugGizmoLayer.cs` | Added `_lastCtx`/`_isInteractionActive` fields; stored ctx in `Draw()`; implemented geometry-aware `HandleInput`, `HitTest`, `PointToSegmentDistance`; fixed `TestHook_IsInteractionActive`. |
| `FDP/Engine/Fdp.Presentation/Vis2D/Gizmos/DebugPrimitiveRenderer2D.cs` | Guarded `ctx.Resources?.Get<MapCamera>()` and `_inner.Render()` with null-check. |

### Fix Approach

**Null safety**: Changed `ctx.Resources.Get<MapCamera>()` to `ctx.Resources?.Get<MapCamera>()` and wrapped the Raylib render call in `if (ctx.Resources != null)`.

**Hit detection**: Implemented `HandleInput` to iterate the primitive buffer, calling `HitTest` for each primitive with a valid pick token. `HitTest` dispatches on `DebugPrimitiveShape`:
- `Sphere`: point-in-circle test (`distance(center, testPos) <= sphereRadius + hitRadius`)
- `Line`/`Arrow`: point-to-segment distance test

Hit radius is `HitRadiusWorld = 5f` world units, scaled by `1/zoom` for `SizeMode.ScreenPixels` primitives. On a hit, `GizmoInteractionStartedEvent` is published and `_isInteractionActive` is set to `true`. The context (`_lastCtx`) is captured at the end of each `Draw()` call to provide the current zoom value.

### Verification

```
dotnet test FDP\FDP.sln --no-build --filter "FullyQualifiedName~DebugGizmoLayerHitTests|FullyQualifiedName~DebugGizmoLayerActivationTests|FullyQualifiedName~DebugGizmoLayerGizmoTests"
```

Result: **Failed: 0, Passed: 12, Skipped: 0**

---

## FDP-G03: ModuleHost SharedSnapshotProvider

**Tests fixed:**
- `ConvoyAutoGroupingTests` (3 tests) — legacy `ModuleTier.Slow` modules sharing one `SharedSnapshotProvider`
- `ProviderAssignmentTests.ProviderAssignment_AsyncSoD_MultipleModules_Convoy` — explicit `SlowBackground` modules expecting convoy grouping
- `ConvoyIntegrationTests` — 5 async SoD modules expected to share the same `SharedSnapshotProvider` instance
- `HonestSodGdbTests.BatchInstall_SodModules_ActivatedAtomically` — dynamic batch install of 3 SoD modules expecting convoy
- `HonestSodGdbTests.UnionMask_Expansion_NewSodModule_ExpandsSharedProvider` — sequential SoD installs expecting shared provider promotion
- `ResilienceIntegrationTests.Resilience_MultipleModulesFailing_SystemDegrades` — circuit breaker opens for 3 bad async SoD modules

**Root cause:** In `ModuleHostKernel.AssignProviderForDynamicInstall`, the `case DataStrategy.SoD:` block filtered potential convoy group members with two contradictory conditions:

```csharp
&& e.Module.Policy.Mode == policy.Mode            // require Mode matches (e.g. Asynchronous)
&& e.Module.Policy.Mode != RunMode.Asynchronous   // exclude Asynchronous  <-- THE BUG
```

`SlowBackground(hz)` always produces `Mode = RunMode.Asynchronous`. The second condition directly negated the first, so `groupMembers` was always empty for async SoD modules. Each module was issued its own exclusive `OnDemandProvider` instead of sharing a `SharedSnapshotProvider` with its convoy mates.

**Fix:** Removed the redundant/contradictory line `&& e.Module.Policy.Mode != RunMode.Asynchronous` from the `case DataStrategy.SoD:` LINQ filter in `AssignProviderForDynamicInstall`.

**File changed:** `FDP/Engine/Fdp.ModuleHost/ModuleHostKernel.cs` (~line 1701)

**Verification:**

```
dotnet test FDP\FDP.sln --no-build --filter "FullyQualifiedName~ConvoyAutoGroupingTests|FullyQualifiedName~ProviderAssignmentTests|FullyQualifiedName~HonestSodGdbTests|FullyQualifiedName~ConvoyIntegrationTests|FullyQualifiedName~ResilienceIntegrationTests"
```

Result: **Failed: 0, Passed: 26, Skipped: 0**
