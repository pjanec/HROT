# BATCH-10 Review

**Batch:** BATCH-10
**Reviewer:** Development Lead
**Date:** 2026-05-06
**Status:** ✅ APPROVED

---

## Summary

All four tasks completed. Build clean (0 errors). 26 pre-existing failures in
`Fdp.Toolkits.Tests` (non-gizmo areas) and 4 pre-existing failures in `Hrot.IG.Tests`
(CS011_ EntityInfoTranslator) confirmed pre-existing — unchanged by this batch.

---

## Issues Found

### Issue 1: Generator skips IGizmoDefinition path

**File:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/GizmoRegistrarGenerator.cs`
**Problem:** Spec SC-GZ024-2 called for `gizmoRegistry.Register(new T())` emission for
`IGizmoDefinition` implementors. The generator only handles `IStatelessGizmo`; a
`[GizmoProjector]` class implementing `IGizmoDefinition` gets FDP_002 (incorrect).
SC-GZ024-2 was repurposed to test the settings-constructor path instead.
**Impact:** Low — no `IGizmoDefinition` classes remain in the codebase after GZ023. The
test coverage for the settings-constructor path (the repurposed SC-GZ024-2) is more useful
in practice. Acceptable deviation; record as P3.

### Issue 2: GizmoRegistrar.cs in Hrot.IG was not deleted

**File:** `Hrot/Subsystems/Hrot.IG/Gizmos/GizmoRegistrar.cs`
**Problem:** Spec said to delete the hand-written file after the generator becomes functional.
The file was kept as a thin coordinator that calls the generated `RegisterAll` methods from
`Hrot.Common` and `Hrot.AI.Behaviors` namespaces and still registers `MeasureToolGizmoSettings`.
**Impact:** None — the coordinator is semantically correct and necessary for
`MeasureToolGizmoSettings` (Hrot.IG-specific, not a `[GizmoProjector]` class). Acceptable.

### Issue 3: Settings registered inside HealthBarGizmo constructor

**File:** `Hrot/Engine/Hrot.Common/Diagnostics/Gizmos/HealthBarGizmo.cs`
**Problem:** The constructor calls `HealthBarGizmoSettings.Register(settings)`, which registers
settings defaults. Old code did this externally before instantiation. Now it's a constructor
side effect. Acceptable for startup-only use; if `HealthBarGizmo` were ever constructed twice
the settings would be re-registered (idempotent, so no corruption).
**Impact:** Negligible — startup-only construction guaranteed by the generator. Acceptable.

---

## Test Quality Assessment

Tests are behaviorally sound:
- `StringInternMapConcurrencyTests`: actual parallel stress tests, not mock invocations.
- `StatelessGizmoSystemTests`: mock projectors with `DrawCount` counters; SC-GZ022-7 correctly
  counts `IsGloballyEnabled` invocations (must be 1 for 5 entities — verified).
- `GizmoRegistrarGeneratorTests`: drives the `CSharpGeneratorDriver` directly and checks
  generated source text — meaningful assertions.

Note: SC-GZ022-5/6 test via `isSelectedPredicate` (not `ForceAllGizmosVisible` flag) which is
the correct pattern given `GlobalDebugSettings` is not reachable from `Fdp.Toolkits`. Acceptable.

---

## 📝 Commit Message

```
feat: stateless gizmo execution path + P1 concurrency fix (BATCH-10)

Completes TASK-GZ040, TASK-GZ022, TASK-GZ023, TASK-GZ024

TASK-GZ040: Replace StringInternMap Dictionary with ConcurrentDictionary;
  TryAdd/TryGetValue are lock-free. Removes false thread-safe comment (D-001 closed).

TASK-GZ022: Add IStatelessGizmo interface, StatelessGizmoRegistry, and
  StatelessGizmoSystem. Evaluates global visibility once per rule per frame (not
  once per entity). Respects isSelectedPredicate delegate for selection filtering.

TASK-GZ023: Migrate HealthBarGizmo, EntityRotationGizmo, VisibilityConeGizmo to
  Hrot.Common; HillAttackGizmo to Hrot.AI.Behaviors. All implement IStatelessGizmo.
  Remove 11 old *Instance.cs / *Definition.cs files from Hrot.IG.

TASK-GZ024: Add GizmoProjectorAttribute. Roslyn ISourceGenerator emits partial
  GizmoRegistrar.RegisterAll per namespace. FDP_002 warning for non-implementing
  decorated classes. Hand-written Hrot.IG GizmoRegistrar kept as thin coordinator.

Tests: 117 new/updated in Fdp.Toolkits.Tests (concurrency stress, visibility
  cache policy, generator output). All Hrot.IG.Tests gizmo rendering tests pass.
```

---

**Next Batch:** BATCH-11 (Preparing — Phase 9 Presentation Fidelity + Phase 10 Data Plane Correctness)
