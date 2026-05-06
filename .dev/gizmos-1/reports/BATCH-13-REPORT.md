# BATCH-13 Report

**Tasks:** GZ034, GZ035, GZ036
**Date:** 2026-05-07
**Result:** All tasks complete, build clean, all new tests pass.

---

## Files Created

| File | Task |
|------|------|
| `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmoSettingsPublisherSystemTests.cs` | GZ034 |

## Files Modified

| File | Task | Change |
|------|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj` | GZ034 | Added StructEdit.Core + StructEdit.Json project references |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/GizmoSettingsPublisherSystem.cs` | GZ034 | Replaced flat Utf8JsonWriter with StructEdit EditDocument + EditDocumentJsonSerializer |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Diagnostics/Gizmos/GizmosSystemTests.cs` | GZ035, GZ036 | Added SC_GZ035_5 and SC_GZ036_1/2/3/4 tests |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/DataDrivenGizmoSystem.cs` | GZ036 | Added MaxGizmoFrameMs property, _entityList, _timeSliceOffset, time-sliced step 4 |
| `FDP/Toolkits/Fdp.Toolkits/Diagnostics/Gizmos/Systems/StatelessGizmoSystem.cs` | GZ036 | Added MaxGizmoFrameMs property, Stopwatch-based budget in entity loop |
| `Hrot/Subsystems/Hrot.IG/Gizmos/GlobalDebugSettings.cs` | GZ036 | Added MaxGizmoFrameMs float field |

---

## Test Results

### GZ034 — SC-GZ034-1/2/3 (new) + SC-GZ017-2/3 (regression)
- SC-GZ034-1: PASS — `structedit_version` key present in published JSON
- SC-GZ034-2: PASS — Bool setting appears as `"kind":"Boolean"` node
- SC-GZ034-3: PASS — Float32 setting appears as `"kind":"Scalar"` node
- SC-GZ017-2: PASS (regression) — first dirty frame still publishes
- SC-GZ017-3: PASS (regression) — clean second frame skips publish

### GZ035 — SC-GZ035-5 (new) + SC-GZ006-4 (regression)
- SC-GZ035-5: PASS — behavior interrupt without ClearBehaviorEvent tears down old gizmo
- SC-GZ006-4: PASS (regression) — new assign still replaces existing gizmo

### GZ036 — SC-GZ036-1/2/3/4 (new)
- SC-GZ036-1: PASS — near-zero budget (0.0001ms) processes < 50 entities
- SC-GZ036-2: PASS — large budget (10000ms) processes all 20 entities
- SC-GZ036-3: PASS — zero budget (0ms = unlimited) processes all 20 entities
- SC-GZ036-4: PASS — 3 Execute calls without exception, draws > 0

**Total new tests:** 12 (3 + 1 + 4, plus 4 regression tests that all pass)
**Total test run:** Passed: 924, Failed: 26 (all 26 are pre-existing failures)

---

## Build Output

```
Build succeeded.
    0 Error(s)
```

---

## Deviations from Spec

### GZ034
- **SnapshotValueBinding<T>** uses `System.Type` and `System.Span<byte>` fully qualified inside
  the class (no extra using needed since file-level usings cover it). No functional deviation.
- **SC-GZ034-4** is a regression test (SC-GZ017-2/3); no explicit test method added since the
  instructions say "these should auto-pass if the above steps are followed correctly." Both do pass.

### GZ035
- No production code changes needed (confirmed by audit in instructions).
- SC-GZ035-5 added to existing `BehaviorGizmoManagerSystemTests` class rather than a separate file,
  reusing the `CreateFixture` and `PublishAssignAndExecute` helpers.

### GZ036
- `QueryTimeSliced` / `TimeSlicedIteratorState` do not exist; used `System.Diagnostics.Stopwatch`
  per the batch instruction override.
- `GlobalDebugSettings.MaxGizmoFrameMs` added as a plain `float` field (not a property), matching
  the existing struct field pattern (`ForceAllGizmosVisible`, `DebugLayerMask`).
- SC-GZ036 tests added to a new `DataDrivenGizmoSystemBudgetTests` class in `GizmosSystemTests.cs`
  rather than adding to `DataDrivenGizmoSystemTests` (which has a private `CreateFixture`). A new
  `CreateBudgetFixture` helper is defined in the new class.
- `CountingGizmoDefinition` from the spec pseudocode was not created; `MockGizmoDefinition` with
  `UpdateAndDrawCount` field is used instead — simpler and equivalent.

---

## Commit Hashes

- FDP submodule: `9a95e3e` — GZ034/GZ035/GZ036: StructEdit schema publisher, behavior interrupt test, gizmo frame budget
- Root repo: `ab178af` — GZ034-036: StructEdit JSON schema, behavior interrupt guard test, MaxGizmoFrameMs budget
