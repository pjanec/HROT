# BATCH-12 Report — GZ031, GZ032, GZ033

**Status:** COMPLETE
**Commits:** FDP submodule `a9ff73e` + root `7f99b6a`
**Date:** 2026-05-07

---

## Summary

BATCH-12 closes the three production wiring gaps that left the gizmo framework correct but
inert: selection filtering, SimHost visual layer, and DDS egress publisher.

---

## GZ031 — Fix Selection Filtering in IgApplication

**File changed:** `Hrot/Subsystems/Hrot.IG/IgApplication.cs`

Replaced `isSelectedPredicate: null` with a `static` lambda in both
`DataDrivenGizmoSystem` and `StatelessGizmoSystem` registrations:

```csharp
isSelectedPredicate: static (view, entity) =>
    view.HasComponent<SelectionState>(entity) &&
    view.GetComponentRO<SelectionState>(entity).IsSelected
```

The `static` modifier prevents per-frame closure allocations on the hot path.

**Verification:** 223/223 `Hrot.ClusterRunner.Tests` pass (includes all
DataDrivenGizmoPredicateTests and StatelessGizmoSystemTests). 466 Hrot.IG.Tests pass,
4 pre-existing CS011 EntityInfoTranslator failures unchanged.

---

## GZ032 — Wire DebugGizmoLayer into SimHostVisualization

**Files changed:** `Hrot/Subsystems/Hrot.SimHost/SimHostVisualization.cs`,
`Hrot/Subsystems/Hrot.SimHost/SimHostApp.cs`

### SimHostVisualization.cs
- Added `private DebugPrimitiveBuffer? _gizmoBuffer;` field.
- Added `public DebugPrimitiveBuffer? GizmoBuffer => _gizmoBuffer;` property.
- Added optional `DebugPrimitiveBuffer? gizmoBuffer = null` parameter to `Initialize()`.
- After `SimHostTrajectoryLayer`, added:
  ```csharp
  _gizmoBuffer = gizmoBuffer ?? new DebugPrimitiveBuffer();
  _map.AddLayer(new DebugGizmoLayer(31, _gizmoBuffer, repo.Bus, _map, repo));
  ```

### SimHostApp.cs
- Added fields: `_gizmoBuffer`, `_gizmoRegistry`, `_statelessGizmoRegistry`.
- Before `_kernel.Initialize()`, creates them and registers:
  ```csharp
  _kernel.RegisterGlobalSystem(new DataDrivenGizmoSystem(..., isSelectedPredicate: ...));
  _kernel.RegisterGlobalSystem(new StatelessGizmoSystem(..., isSelectedPredicate: ...));
  ```
- Passes `gizmoBuffer: _gizmoBuffer` to `_vis.Initialize()` so both the kernel systems and the
  visual layer share the same buffer instance.

**Design decision:** Buffer must be created before `_kernel.Initialize()` (kernel rejects
`RegisterGlobalSystem` after init), but `SimHostVisualization` is created after init. The
optional parameter on `Initialize()` threads the pre-created buffer into the layer.

---

## GZ033 — Wire DebugPrimitivesBatch DDS Egress

**Files created:**
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Network/IDdsWriter.cs`
- `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DebugPrimitivesBatchPublisherSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/DebugPrimitivesBatchPublisherTests.cs`

**IDdsWriter<T>** is a thin local interface in `Fdp.Toolkit.Diagnostics.Gizmos.Network` that
decouples production code from CycloneDDS so tests can inject a capturing stub without a live
DDS participant. This is consistent with the `IGizmoUiStatePublisher` pattern already used.

**DebugPrimitivesBatchPublisherSystem** reads `GetFrame()` (returns
`ReadOnlySpan<DebugPrimitive>`), copies to a `DebugPrimitive[]`, and writes a
`DebugPrimitivesBatch`. No-ops when writer is null or frame is empty. Decorated with
`[UpdateInPhase(SystemPhase.PostSimulation)]`.

### Tests: 6/6 passing

| Test | Result |
|------|--------|
| SC-GZ033-1 (n=1) NonEmpty -> 1 Write, Length==1 | PASS |
| SC-GZ033-1 (n=5) NonEmpty -> 1 Write, Length==5 | PASS |
| SC-GZ033-1 (n=10) NonEmpty -> 1 Write, Length==10 | PASS |
| SC-GZ033-2 Empty buffer -> 0 Write calls | PASS |
| SC-GZ033-3 Null writer -> no exception | PASS |
| SC-GZ033-4 FrameNumber increments per Execute | PASS |

---

## Build Result

`Build succeeded. 0 Error(s)`

---

## Regression Summary

| Test Assembly | Before | After | Delta |
|---|---|---|---|
| Hrot.ClusterRunner.Tests | 223 pass | 223 pass | 0 |
| Hrot.IG.Tests | 4 fail (pre-existing) | 4 fail | 0 |
| Fdp.Toolkits.Tests | 26 fail (pre-existing) | 26 fail | 0 |
| Fdp.Presentation.Tests | 3 fail (pre-existing) | 3 fail | 0 |

No new failures introduced.

---

## Notes

- GZ034 (GizmoSettingsPublisherSystem StructEdit schema) is next in BATCH-13.
- GZ035 (behavior lifecycle leak on AI abort) is in BATCH-13.
- GZ036 (CPU performance budget) is in BATCH-13.
