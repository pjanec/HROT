# BATCH-19 Review

**Batch:** BATCH-19
**Reviewer:** Dev Lead
**Verdict:** APPROVED

---

## Summary

BATCH-19 completed Phase 6 with P6-03 (editor/console bridge, SC-P6-3) and P6-04
(snapshot/restore, SC-P6-4). 10 new tests. 32/32 pass in Tuning.Tests, 18/18 pass in
Overlays.Tests, 141/141 pass in Utility.Editor.Tests. 0 build errors.

---

## P6-03: Editor/console bridge

**UtilityDecisionOverlaySource — correct.** Optional `Action<string>? onDecisionSelected` added
as a defaulted constructor parameter — all existing test callsites continue to compile unchanged.
`SelectDecision(string decisionName)` is `internal` (accessible via `InternalsVisibleTo`), fires
`_onDecisionSelected?.Invoke("utility." + decisionName)`. The `"utility."` prefix is the canonical
group prefix that matches what `UtilityTuningBinder` registers.

**TuningConsoleGizmo — correct.** `_focusedGroup` field, `IsEditing` and `FocusedGroup` properties,
`OpenForGroup(string)` method added. Existing `ToggleEditor`, `OnMenuAction`, `UpdateAndDraw`, and
`OnStructUpdate` are untouched. `OpenForGroup` sets `_isEditing = true` and `_focusedGroup` — both
required by SC-P6-3 ("opens the tuning console focused on that decision's group").

---

## P6-04: Snapshot/restore

**Tunable.cs / CurveTunable.cs — correct.** `Default` (float) and `DefaultCurve` (UtilityCurve)
fields added. No other changes.

**TuningRegistry.Register — correct.** `tunable.Default = tunable.Read()` captures the value at
registration time. This is the authored default.

**TuningRegistry.RegisterCurve — correct.** `tunable.DefaultCurve = tunable.Read()` captures the
curve at registration.

**TuningRegistry.RevertGroup — correct.** Iterates `_tunables` and `_curveTunables`, enqueues
defaults for keys whose group prefix matches. Enqueues are done inside a single `lock (_queueLock)`
block — atomic from other threads' perspective. Frame-top discipline preserved: values are
applied only on the next `BeginFrame()` call.

**TuningRegistry.RevertAll — correct.** Same pattern, enqueues ALL registered tunables.

---

## Test Quality

**P6-03 overlay tests (2 tests):**
`SelectDecision_NullCallback_DoesNotThrow` — null safety.
`SelectDecision_InvokesCallback_WithGroupPrefix` — SC-P6-3: asserts callback receives
`"utility.CombatPosture"` for decision name `"CombatPosture"`.

**P6-03 gizmo tests (3 tests):**
`OpenForGroup_SetsIsEditingTrue`, `OpenForGroup_SetsFocusedGroup`,
`OpenForGroup_OverridesPreviousFocusedGroup` — cover all state transitions.

**P6-04 snapshot tests (5 tests):**
`DefaultCapturedAtRegistration` — reads back `Tunable.Default` via `TryGet`.
`RevertGroup_RestoresDefaultValue` — the SC-P6-4 core test: register 1.0, apply 5.0, revert,
assert 1.0.
`RevertGroup_DoesNotAffectOtherGroup` — confirms group isolation.
`RevertAll_RestoresAllTunables` — two groups, both restored.
`DefaultCaptured_CurveTunable` — `CurveTunable.DefaultCurve.Kind` checked after `RegisterCurve`.

---

## Issues

None.

---

## Final Test Count

| Project | Tests | Result |
|---------|-------|--------|
| Hrot.Utility.Editor.Tests | 141 | Passed |
| Hrot.Diagnostics.Tuning.Tests | 32 (+8) | Passed |
| Hrot.Diagnostics.Overlays.Tests | 18 (+2) | Passed |
| **Total new** | **10** | **Passed** |
