# BATCH-19 Report — P6-03 (editor/console bridge) + P6-04 (snapshot/restore)

**Batch ID:** BATCH-19
**Tasks:** TASK-UAI-P6-03, TASK-UAI-P6-04
**Status:** COMPLETE

---

## Files Modified

| File | Change |
|---|---|
| `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/Tunable.cs` | Added `Default` field after `Write` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/CurveTunable.cs` | Added `DefaultCurve` field after `Write` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/TuningRegistry.cs` | Capture defaults in `Register`/`RegisterCurve`; added `RevertGroup`/`RevertAll` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/UtilityDecisionOverlaySource.cs` | Added `_onDecisionSelected` field, updated constructor, added `SelectDecision` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Tuning/Gizmos/TuningConsoleGizmo.cs` | Added `_focusedGroup` field, `IsEditing` property, `FocusedGroup` property, `OpenForGroup` method |
| `Hrot/Diagnostics/Hrot.Diagnostics.Overlays.Tests/OverlaySourceTests.cs` | Added 2 tests: `SelectDecision_NullCallback_DoesNotThrow`, `SelectDecision_InvokesCallback_WithGroupPrefix` |
| `Hrot/Diagnostics/Hrot.Diagnostics.Tuning.Tests/TuningConsoleGizmoTests.cs` | Added 3 tests: `OpenForGroup_SetsIsEditingTrue`, `OpenForGroup_SetsFocusedGroup`, `OpenForGroup_OverridesPreviousFocusedGroup` |

## Files Created

| File | Description |
|---|---|
| `Hrot/Diagnostics/Hrot.Diagnostics.Tuning.Tests/SnapshotRestoreTests.cs` | 5 tests for P6-04 snapshot/restore (SC-P6-4) |

---

## Build Results

| Project | Result |
|---|---|
| `Hrot.Diagnostics.Overlays` | **0 errors, 0 warnings** |
| `Hrot.Diagnostics.Tuning` | **0 errors, 0 warnings** |

---

## Test Results

| Project | Passed | Failed | Total |
|---|---|---|---|
| `Hrot.Diagnostics.Overlays.Tests` | 18 | 0 | 18 |
| `Hrot.Diagnostics.Tuning.Tests` | 32 | 0 | 32 |
| `Hrot.Utility.Editor.Tests` | 141 | 0 | 141 |

**New tests added: 10** (2 overlay + 3 gizmo + 5 snapshot/restore)

---

## Implementation Notes

### P6-03 — Editor/console bridge

- `UtilityDecisionOverlaySource` constructor gained an optional `Action<string>? onDecisionSelected`
  parameter (default `null`). Existing callers compile unchanged.
- `SelectDecision(string decisionName)` fires the callback with `"utility." + decisionName`.
  Method is `internal`; accessible to the test project via the existing `InternalsVisibleTo`.
- `TuningConsoleGizmo` gained `_focusedGroup` field, `IsEditing` and `FocusedGroup` read-only
  properties, and `OpenForGroup(string groupPrefix)` which sets `_isEditing = true` and stores
  the prefix. No existing methods were changed.

### P6-04 — Snapshot/restore

- `Tunable.Default` and `CurveTunable.DefaultCurve` are captured in `Register`/`RegisterCurve`
  by calling `tunable.Read()` immediately after key assignment.
- `RevertGroup` and `RevertAll` batch all enqueues under a single lock acquisition, preserving
  thread-safety and frame-top discipline — values apply at next `BeginFrame`.

### Deviations

None. Implementation matches instructions exactly.
