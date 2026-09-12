# Test Health Diagnostics — Hrot.StrideMock.Tests & Fdp.Presentation.Tests

**Date:** 2026-07-12  
**Branch:** main  
**Run:** `dotnet test <csproj> --nologo -v n` (no BLUEPRINT_REGENERATE_SNAPSHOTS)

---

## Summary

| Project | Total Failed | A (Stale) | B (Fixture) | C (Real Bug) |
|---------|-------------|-----------|-------------|--------------|
| Hrot.StrideMock.Tests | 10 | 0 | 0 | 10 |
| Fdp.Presentation.Tests | 28 | 0 | 0 | 28 |
| **Total** | **38** | **0** | **0** | **38** |

---

## Hrot.StrideMock.Tests — 10 failures

### Cluster SM-A: NedReplicationModule rejects NodeRole.None (9 tests)

| Test / Cluster | A/B/C | Root Cause (file:line) | Fix | SAFE-AUTO-FIX or NEEDS-DECISION |
|---|---|---|---|---|
| `NedReplication_RegisteredByBaseClass_GhostCreationSystemPresent` | C | `NedReplicationModule.cs:187-190` — constructor throws `ArgumentException` when `role == NodeRole.None`. Tests call `BootstrapNode(..., NodeRole.None, ...)` but `.WithReplication(role)` in `TestBootstrapper.BuildContext` feeds that `None` role straight into the constructor. | Tests must pass a real role flag (e.g. `NodeRole.Brain`) OR the test `HeadlessConfig()` bootstrapper must bypass replication when role is `None`. | NEEDS-DECISION (is the validator intentional? should tests use `NodeRole.Brain` or should headless mode skip replication?) |
| `NedReplication_NonNull_AfterBootstrapWithNedFactory` | C | Same as above | Same | NEEDS-DECISION |
| `TimeTranslators_RegisteredByBaseClass_SlaveSyncController_ReceivesEvent` | C | Same as above | Same | NEEDS-DECISION |
| `TimeControl_NonNull_AfterBootstrapWithFactory` | C | Same as above | Same | NEEDS-DECISION |
| `RegisterDomainComponents_RunsBeforeBuildSerializer_ComponentPresentInWorld` | C | Same as above | Same | NEEDS-DECISION |
| `BootstrapNode_WithMinimalSubclass_Headless_DoesNotThrow` | C | Same as above — test at line 230 passes `NodeRole.None`; `TestBootstrapper.BuildContext` calls `.WithReplication(NodeRole.None)` | Same | NEEDS-DECISION |
| `PopulateSystems_SystemInSimGroup_PassedToBuildOrchestration` | C | Same as above | Same | NEEDS-DECISION |
| `BuildOrchestration_ReceivesLifecycleGroup_FromNedReplication` | C | Same as above | Same | NEEDS-DECISION |
| `KernelInitialize_CalledExactlyOnce_AfterAllTranslators` | C | Same as above | Same | NEEDS-DECISION |

**Root cause detail:**  
`NedReplicationModule..ctor` at `Hrot\Network\Hrot.Network.NED\Replication\NedReplicationModule.cs:187-190` enforces a role guard added after the tests were written:
```csharp
if (!_roleHasMuscle && !_roleHasIG && !_roleHasBrain)
    throw new ArgumentException(
        $"NedReplicationModule requires a role with MuscleGround, ImageGenerator, or Brain. Got: {role}",
        nameof(role));
```
All 9 tests reach `BuildContext` → `.WithReplication(NodeRole.None)` → `HrotNodeBuilderWithReplication.Build()` at `HrotNodeBuilderReplicationExtensions.cs:80` → `NedReplicationModule..ctor` → throws.

Two valid fixes exist but both need a decision:
1. **Option A:** Change test helpers to use `NodeRole.Brain` instead of `NodeRole.None`. Simple but changes test intent for "headless" tests.
2. **Option B:** Make `WithReplication()` a no-op when `role == NodeRole.None` (headless path). Bigger production change, needs architect sign-off.

---

### Cluster SM-B: AbstractAndVirtualHooks reflection test (1 test)

| Test / Cluster | A/B/C | Root Cause (file:line) | Fix | SAFE-AUTO-FIX or NEEDS-DECISION |
|---|---|---|---|---|
| `AbstractAndVirtualHooks_ExactlyAsSpecified_Reflection` | C | `SharedApplicationBootstrapperTests.cs:317-349` — test expects exactly 6 abstract methods; actual has 7. `BuildContext` (at `SharedApplicationBootstrapper.cs:175`) was added as `protected abstract` after the test was written. Expected list omits `"BuildContext"`. Failure message: `Expected: ["BuildOrchestration","BuildSerializer","PopulateSystems","RegisterDomainComponents","RegisterNetworkTranslators",…]` vs `Actual: ["BuildContext","BuildOrchestration",…]`. | Add `"BuildContext"` to `expectedAbstract` array in the test (line 317-325). | SAFE-AUTO-FIX (one-line addition to test; no design decision required — `BuildContext` is clearly intentional as abstract) |

---

## Fdp.Presentation.Tests — 28 failures

All 28 failures share a single root cause: `RenderContext.Resources` is `null` in test contexts.

### Cluster FP-A: DebugPrimitiveRenderer2D NullReferenceException (14 tests)

Tests in `DebugPrimitiveRenderer2DTests`, `DebugPrimitiveRenderer2DSizeModeTests`, and `DebugPrimitiveRenderer2DEntityLocalTests` / `DebugPrimitiveRenderer2DEntityLocalAllShapesTests` all fail at:
```
DebugPrimitiveRenderer2D.cs:28 — ctx.Resources.Get<MapCamera>()
```

| Test / Cluster | A/B/C | Root Cause (file:line) | Fix | SAFE-AUTO-FIX or NEEDS-DECISION |
|---|---|---|---|---|
| `SC_GZ011_1_TargetView_None_Skipped` | C | `DebugPrimitiveRenderer2D.cs:28` — `ctx.Resources` is null; `RenderTestHelpers.MakeCtx()` at `DebugPrimitiveRenderer2DTests.cs:33-36` creates `RenderContext` without setting `Resources`. | Change `ctx.Resources.Get<MapCamera>()` to `ctx.Resources?.Get<MapCamera>()` in production code (1-char fix). This was already done in branch `test-fixing` (commit `eebd7d9e`) but NOT merged to `main`. | SAFE-AUTO-FIX |
| `SC_GZ011_2_Layer5_MaskBitSet_Dispatched` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ011_3_Layer5_MaskBitClear_Skipped` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ011_6_SameLayer_ZIndex_SortedAscending` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ011_7_MinZoomLod_CullsAtLowZoom` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ011_8_MaxZoomLod_CullsAtHighZoom` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ011_9_ZeroLodLimits_NeverCulled` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ012_1_EntityLocal_Line_TranslatesPosition` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ012_2_EntityLocal_DeadEntity_Skipped` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ027_1_EntityLocal_Sphere_TranslatesCenter` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ027_2_EntityLocal_Arrow_RotatesWithEntity` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ027_3_EntityLocal_Text_TranslatesAnchor` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ027_4_EntityLocal_DeadEntity_Skipped` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ027_5_EntityLocal_Line_Regression` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ028_1_Sphere_ScreenPixels_ScalesRadiusWithZoom` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ028_2_Sphere_WorldMeters_RadiusUnchangedAtHighZoom` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ028_3_Arrow_ScreenPixels_ScalesHeadSize` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ028_4_Box2D_ScreenPixels_ScalesExtents` | C | Same | Same | SAFE-AUTO-FIX |

### Cluster FP-B: DebugGizmoLayer NullReferenceException (10 tests)

Tests in `DebugGizmoLayerHitTests`, `DebugGizmoLayerActivationTests`, and `DebugGizmoLayerGizmoTests` all fail at:
```
DebugGizmoLayer.cs:102 — ctx.Resources.Get<MapCamera>()
```

| Test / Cluster | A/B/C | Root Cause (file:line) | Fix | SAFE-AUTO-FIX or NEEDS-DECISION |
|---|---|---|---|---|
| `SC_GZ013_1_Draw_WithInjectedRenderer_NoException` | C | `DebugGizmoLayer.cs:102` — `ctx.Resources` null; local `MakeCtx()` in `DebugGizmoLayerGizmoTests.cs:14-21` omits `Resources`. | Change `ctx.Resources.Get<MapCamera>()` to `ctx.Resources?.Get<MapCamera>()` in `DebugGizmoLayer.cs:102`. Also already fixed in `test-fixing` branch commit `eebd7d9e`. | SAFE-AUTO-FIX |
| `SC_GZ013_2_HandleInput_HitPrimitive_ReturnsTrueAndPublishesEvent` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ025_1_HitPickable_PushesProxyTool` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ025_2_OnEnter_PublishesStartedEventOnce` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ025_3_MissedClick_NoToolPushed` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ025_5_NullCanvas_FallbackPublishesEvent` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ026_1_LineMidpoint_IsHit` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ026_2_BeyondEndpoint_IsMiss` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ026_3_SphereCenter_IsHit` | C | Same | Same | SAFE-AUTO-FIX |
| `SC_GZ026_4_ScreenPixels_ZoomScalesHitRadius` | C | Same | Same | SAFE-AUTO-FIX |

---

## Shared Root Causes

### RC-1: `NedReplicationModule` rejects `NodeRole.None` (9 StrideMock tests)

A guard clause was added to `NedReplicationModule..ctor` that throws when `role` has no recognized bit flags. All StrideMock bootstrapper tests pass `NodeRole.None` for "headless" scenarios, which now triggers this guard.

**Files involved:**
- Production: `Hrot\Network\Hrot.Network.NED\Replication\NedReplicationModule.cs:187-190`
- Test bootstrapper: `Hrot\Subsystems\Hrot.StrideMock\Hrot.StrideMock.Tests\SharedApplicationBootstrapperTests.cs:230,248,…`
- Fix entrypoint: `HrotNodeBuilderReplicationExtensions.cs:80`

**Classification:** NEEDS-DECISION (two viable approaches; pick requires architect input on whether "headless" should be a recognized role concept)

### RC-2: `RenderContext.Resources` not initialized in test `MakeCtx()` helpers (28 Presentation tests)

`RenderContext` (a struct at `Vis2D\Abstractions\CoreInterfaces.cs:10`) has a reference-type field `IResourceProvider Resources`. When default-constructed in tests, this is `null`. Production code at `DebugPrimitiveRenderer2D.cs:28` and `DebugGizmoLayer.cs:102` dereferences it without a null-check.

The fix (adding `?.` null-conditional) was already authored in commit `eebd7d9e` on branch `test-fixing` but was **not merged to `main`**. The fix is:
- `DebugPrimitiveRenderer2D.cs:28`: `ctx.Resources.Get<MapCamera>()` → `ctx.Resources?.Get<MapCamera>()`
- `DebugPrimitiveRenderer2D.cs:35-36`: `_inner.Render(...)` → guard with `if (ctx.Resources != null)`
- `DebugGizmoLayer.cs:102`: same `?.` fix

**Classification:** SAFE-AUTO-FIX — cherry-pick or re-apply the two-line change from `test-fixing` branch commit `eebd7d9e`.

### RC-3: Abstract hook list stale in reflection test (1 StrideMock test)

`SharedApplicationBootstrapper` gained a 7th abstract method `BuildContext` (at `SharedApplicationBootstrapper.cs:175`) after the reflection test was written with a hard-coded 6-item expected list.

**Files involved:**
- Test: `SharedApplicationBootstrapperTests.cs:317-325` (expectedAbstract array)
- Production: `SharedApplicationBootstrapper.cs:175`

**Classification:** SAFE-AUTO-FIX — add `"BuildContext"` to `expectedAbstract` in the test.

---

## Fix Action Table

| # | Root Cause | Files to Change | SAFE-AUTO-FIX or NEEDS-DECISION |
|---|---|---|---|
| RC-1 | `NedReplicationModule` rejects `NodeRole.None` | Tests: `SharedApplicationBootstrapperTests.cs` (pass `NodeRole.Brain`), OR production: `NedReplicationModule.cs` + `HrotNodeBuilderReplicationExtensions.cs` (skip replication for None role) | NEEDS-DECISION |
| RC-2 | `ctx.Resources` NPE in renderer/layer | `DebugPrimitiveRenderer2D.cs:28,35-36`, `DebugGizmoLayer.cs:102` (cherry-pick from `test-fixing`/`eebd7d9e`) | SAFE-AUTO-FIX |
| RC-3 | Reflection test missing `BuildContext` | `SharedApplicationBootstrapperTests.cs:318-325` (add `"BuildContext"` to array) | SAFE-AUTO-FIX |
