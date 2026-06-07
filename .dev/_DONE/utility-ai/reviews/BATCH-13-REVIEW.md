# BATCH-13 Review

**Batch:** BATCH-13
**Reviewer:** Dev Lead
**Verdict:** APPROVED

---

## Summary

BATCH-13 implemented the TuningRegistry, TuningConsoleGizmo, UtilityTuningBinder, and all supporting
types in a new `Hrot.Diagnostics.Tuning` project. SC-P4-03-1 (bounds clamping) and SC-P4-03-2
(frame-top apply) are correctly covered by tests. Build is clean, 18/18 tests pass.

---

## TuningRegistry

**Correct.** Key design points verified:

- `Apply` locks the `_applyQueue` before enqueuing — thread-safe as required (called from any
  thread, e.g. `OnStructUpdate` which may arrive from a network callback).
- `BeginFrame` drains the queue in a single lock acquisition (`ToArray` + `Clear`), then applies
  outside the lock — correct pattern that minimizes lock contention on the game thread.
- `Math.Clamp(value, tunable.Min, tunable.Max)` handles both out-of-range directions.
- The `_warn` callback is invoked when clamping happens; if not provided, the registry is silent.
- `GetGroupPrefix` correctly extracts the first two dotted segments (e.g.
  `utility.CombatPosture.0.0.weight` → `utility.CombatPosture`).

SC-P4-03-1 tested: `Apply_AboveMax_ClampsToMax_AndWarns`, `Apply_BelowMin_ClampsToMin_AndWarns`,
`Apply_InRange_NoClamp_NoWarn`. All test the warn callback contract.

SC-P4-03-2 tested: `Apply_IsQueuedNotImmediate` asserts that the live value does not change between
`Apply` and `BeginFrame`. This correctly models the "no mid-tick mutation" invariant.

---

## TuningConsoleGizmo

**Correct.** Follows `LayerControlGizmo.Example` pattern exactly:

- Implements `IStatefulGizmo` (from GizmoMap.Contracts, ECS-free).
- `UpdateAndDraw` always emits the `MainMenuBinding`; only emits `StructInspector` when `_isEditing`.
- `OnStructUpdate` uses `System.Text.Json.JsonDocument` to iterate JSON properties and calls
  `_registry.Apply` per field. Invalid JSON is caught and logged to `Console.Error` — does not
  propagate. Empty/whitespace payload is a no-op.
- `OnMenuAction(OpenActionId)` toggles `_isEditing` — correct.
- All no-op stubs for unused `IGizmoInteractionHandler` methods are present.

**No IComponentEditService dependency** — correct for Slice 1. The StructInspector primitive is
emitted with a static schema hash computed at class init from the fully qualified type name. This
is a forward declaration; the StructEdit schema integration comes in Phase 6.

---

## UtilityTuningBinder

**Correct.** Key design points:

- Registers 4 tunables per consideration: `weight`, `slope`, `exponent`, `xShift` — matching the
  `ResponseCurve` field names (`Slope`, `Exponent`, `XShift`).
- Write delegates use array-element replacement since `UtilityConsideration` is `readonly struct`.
  Pattern: `option.Considerations[ci] = new UtilityConsideration(...)` with the changed field and
  all other fields copied from the current element. This is correct and safe.
- Closure captures: `ci` and `option` are captured per iteration. The loop index `ci` is captured
  correctly (inside the `RegisterConsideration` helper function, avoiding the classic for-loop
  closure capture bug).
- Provenance set to `$"decision:{decName}"` — good for debugging.
- Bounds: weight [0..10], slope [-2..2], exponent [0..20], xShift [-1..2] — reasonable defaults
  matching the ResponseCurve usage patterns.

---

## CycloneDdsDisableCodeGen

**Correct and necessary.** The `buildTransitive` CycloneDDS.NET target scans all `public` types for
IDL generation. Since the Tuning types are `public` (unlike the Overlays types which are `internal`),
this would fail without the flag. Adding `<CycloneDdsDisableCodeGen>true</CycloneDdsDisableCodeGen>`
is the correct workaround used elsewhere in the codebase.

---

## Test Quality

18 tests total across 3 files.

**TuningRegistryTests (8 tests):**
All tests use a tidy `MakeRegistry` helper that captures a local `float val`. Coverage is complete
for SC-P4-03-1 and SC-P4-03-2. The `Apply_UnknownKey_ReturnsFalse` test correctly validates the
return value contract. `BeginFrame_MultipleQueued_AppliesAll` verifies batched drain.

**TuningConsoleGizmoTests (6 tests):**
Tests cover: `DrawMainMenuBinding` always called; `EmitRaw` only when editing; `OnStructUpdate`
with valid JSON enqueues and applies after `BeginFrame`; empty/invalid JSON does not throw;
`OnMenuAction(OpenActionId)` toggles editing state.
The test stub `TuningDrawBuilder` correctly separates `MainMenuCount` and `EmitRawCount`.

**UtilityTuningBinderTests (4 tests):**
Covers: correct registration count (4 per consideration, 16 for 2 options x 2 considerations);
read delegates return current values; write delegates mutate the array element; writes are
independent across different fields.

**Gap noted (acceptable):** No test verifies the `TryGet` method returns the correct `Tunable`
after registration. Minor gap; `TryGet` is a thin dictionary lookup and is exercised implicitly.

---

## Deferred Items (correctly not implemented)

SC-P4-03-3 (replay honesty via FlightRecorder), SC-P4-03-4 (DDS Brain routing), and SC-P4-03-5
(Muscle routing) are correctly deferred. `TuningChangeEvent` struct is defined but not wired —
correct forward declaration for when the FlightRecorder integration arrives.

---

## Issues

None blocking.

---

## Final Test Count

| Project | Tests | Result |
|---------|-------|--------|
| Hrot.Diagnostics.Tuning.Tests | 18 | Passed |
| **Total** | **18** | **Passed** |
