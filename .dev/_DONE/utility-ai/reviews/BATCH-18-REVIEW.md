# BATCH-18 Review

**Batch:** BATCH-18
**Reviewer:** Dev Lead
**Verdict:** APPROVED

---

## Summary

BATCH-18 implemented P6-01 (`UtilityCurveFieldEditor` + `UtilityCurveFieldDrawer`) and P6-02
(piecewise translate-on-apply). 10 new tests. 141/141 pass in `Hrot.Utility.Editor.Tests`, 24/24
pass in `Hrot.Diagnostics.Tuning.Tests`. 0 build errors.

---

## P6-01: UtilityCurveFieldEditor

**Correct.** Follows `GuidFieldEditor` exactly. `TargetType` returns `typeof(UtilityCurve)`.
`CreateNode` produces an `EditNodeKind.Custom` node with the correct type, binding, and null
children. Nothing else — no logic, no StructEdit modification.

---

## P6-01: UtilityCurveFieldDrawer

**Correct.** Follows `QuaternionEulerFieldDrawer`. `DrawInput` unboxes `object` to `UtilityCurve`
(defaulting to `default` if the cast fails — safe because StructEdit guarantees TargetType matches),
delegates to `CurveWidget.Draw`, writes back on change. Returning `true` marks the StructEdit
session dirty, which is exactly the commit trigger the tuning console's apply path keys on (guide §3
Step 3).

---

## P6-02: CurveTunable + TuningRegistry extension

**Correct.** `CurveTunable` mirrors `Tunable` without min/max fields (appropriate because curve
sub-field ranges vary). The `_curveTunables` and `_curveApplyQueue` additions to `TuningRegistry`
preserve the existing float infrastructure untouched. Both queues share `_queueLock` (correct —
both are enqueued from any thread and drained from the simulation thread).

`BeginFrame()` drains float queue first, then curve queue. Ordering within a single batch is
acceptable (a single commit is either float-valued or curve-valued properties, not both for the
same key).

---

## P6-02: UtilityTuningBinder extension

**Correct.** The 5th registration per consideration (`prefix.curve`) uses
`UtilityCurve.FromResponseCurve(Curve)` for Read and `uc.ToResponseCurve()` for Write inside
`Action<UtilityCurve>`. `ToResponseCurve()` registers piecewise points in `PiecewiseCurveCatalog`
and returns the `ResponseCurve` with the content-hashed `CurveId` — this is the exact
translate-on-apply the guide §6 specifies.

---

## P6-02: TuningConsoleGizmo.OnStructUpdate extension

**Correct.** The sub-agent fixed a subtle bug: the original float-try path used
`prop.Value.TryGetSingle(...)` which throws `InvalidOperationException` on Object-valued
properties in .NET 8 `System.Text.Json`. The fix adds a `ValueKind == Number` guard before the
`TryGetSingle` call. This is a real defensive improvement.

`DeserializeUtilityCurve` correctly handles both integer Kind (e.g., `8`) and string Kind
(e.g., `"PiecewiseLinear"`). Points are deserialized with invariant-culture float parsing (via
`TryGetSingle` which is locale-independent). Clamping emits a warning via `Console.Error` and
copies only `MaxPiecewisePoints` entries.

---

## Test Quality

**UtilityCurveFieldEditorTests (4 tests):**
Verify `TargetType`, `EditNodeKind.Custom`, `FieldType`, and `UtilityCurveFieldDrawer.TargetType`.
Use a minimal `StubBinding` inline class. Not testing ImGui rendering (correct — that requires an
active frame).

**PiecewiseTranslateTests (6 tests):**

Test 1 (`RegisterCurve_ThenBeginFrame_WriteIsInvoked`): Verifies the basic `CurveTunable`
registration + drain path works.

Test 2 (`OnStructUpdate_ObjectProperty_CallsApplyCurve`) — **SC-P6-2 core test**: Registers a
decision via `UtilityTuningBinder`, calls `OnStructUpdate` with a piecewise curve JSON object,
drains via `BeginFrame()`, asserts `Kind == PiecewiseLinear`, then evaluates the piecewise catalog
at x=0.5 and asserts the result equals 0.5 (linear identity points [0,0]→[1,1]). This is the
round-trip proof.

Test 3 (`OnStructUpdate_PointsClamped_EmitsWarning`): Verifies the SC-P6-2 requirement that
overflow is NOT silent. Redirects `Console.Error` to a `StringWriter`, passes 69 points
(MaxPiecewisePoints + 5), asserts "clamped" appears in the output, and calls `GetPoints` via
`InternalsVisibleTo` to confirm the registered points are within the 64-point limit.

Test 4 (`OnStructUpdate_NonCurveObject_IsIgnoredGracefully`): Verifies resilience to
non-curve object properties.

Test 5 (`OnStructUpdate_FloatAndCurveInSameBatch_BothApplied`): Verifies that a mixed batch
(float + curve in the same JSON object) applies both correctly.

Test 6 (`DeserializeUtilityCurve_KindAsInteger_IsHandled`): Verifies integer Kind parsing.

`IDisposable.Dispose()` calls `PiecewiseCurveCatalog.ClearAll()` — correct test isolation.
Float values in `BuildCurveJson` use `CultureInfo.InvariantCulture` — correct fix for locale-
sensitive string formatting.

---

## Issues

The `.TryGetSingle` guard bug found by the sub-agent is a genuine improvement:
- Before: `prop.Value.TryGetSingle(out float v)` threw on Object-valued properties
- After: `prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetSingle(...)` is safe

This is correct and should be kept.

---

## Final Test Count

| Project | Tests | Result |
|---------|-------|--------|
| Hrot.Utility.Editor.Tests | 141 (+4) | Passed |
| Hrot.Diagnostics.Tuning.Tests | 24 (+6) | Passed |
| **Total new** | **10** | **Passed** |
