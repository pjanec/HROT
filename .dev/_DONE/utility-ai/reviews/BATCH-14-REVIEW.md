# BATCH-14 Review

**Batch:** BATCH-14
**Reviewer:** Dev Lead
**Verdict:** APPROVED

---

## Summary

BATCH-14 wired the Utility editor into the shared AI editor infrastructure and created the full
editor model type hierarchy. Four shared-infra extension points added, nine new model/window
files created, and the `Hrot.Utility.Editor` project connected to `Hrot.Editor.AiShared`. Build
clean, 81 tests pass (12 new + 69 pre-existing `CurveWidget` tests), 0 regressions.

---

## Shared-Infra Extensions (P5-06)

**Correct.** All four extension points from design §11 implemented:

- `AssetKind.Utility` added as last value — correct, no existing ordinals disturbed.
- `SubElementKind.UtilityInput` added as last value — correct.
- `UtilityConsiderationSelection(int OptionIndex, int ConsiderationIndex)` added to
  `SubSelectionRecords.cs` — follows the existing positional-record pattern.
- `InspectorWindow.DrawClientArea` dispatch arm added for `UtilityConsiderationSelection` —
  renders `Option {idx}, Consideration {idx}` placeholder. The comment "Curve inspector panel
  wired in a later phase (P5-02)" is accurate and correct.
- `UtilityTraceLaneProvider` implemented: `Kind = AssetKind.Utility`, two lanes
  (`utility_scoring` / `utility_values`), correct `TraceLevel` values.

---

## Asset Model Types (P5-01 — model part)

**Correct.** All types in `Hrot.Utility.Editor/Model/`:

- `ResponseCurveModel`: mutable; `ToRuntime()` maps `M/K/B` to `Slope/Exponent/XShift` — correct
  (field names in the runtime `ResponseCurve` are `Slope/Exponent/XShift`, not `m/k/b`). `C`
  (YShift) is present in the model but is not forwarded to `ToRuntime` since `ResponseCurve` has
  no `YShift` field (arch §5 — 16-byte layout). Correct omission.
- `InputParamsModel`: all three union members as separate named fields (`BlueprintId`, `MaxRange`,
  `MountIndex`) — clean mutable equivalent of the `InputParams` explicit-layout union.
- `ConsiderationModel`: `VisualId` auto-generated with `Guid.NewGuid().ToString("N")` — correct;
  deterministic emit relies on stable `VisualId` not on creation order.
- `OptionModel`: same `VisualId` pattern. `Mode` defaults to `WeightedProduct` — matches arch default.
- `FixtureRef`, `UtilityLayoutData`: straightforward; `UtilityLayoutData.Collapsed` is `HashSet<string>`
  (correct for O(1) lookup by `VisualId`).
- `UtilityDecisionAsset`: implements `IEditableAsset` fully. `IsDirty` setter correctly fires
  `Changed` only on false→true transition (not on true→true or true→false). `IsEditorOwned` is a
  plain settable property — this is correct since loading sets it based on the file marker content.
  `Name => DisplayName` satisfies the interface contract.

---

## UtilityDecisionWindow (P5-01 — window part)

**Correct for this batch scope.** The host is a proper `ManagedWindow` skeleton:

- Subscribes to `EditorSelectionStore.OnSelectionChanged` (correct event name per the store).
- `OnSelectionChanged` pattern-matches `ActiveAsset is UtilityDecisionAsset` — correct; does not
  null-clear on other asset types (preserving the last open decision if a non-utility asset is
  selected, which is the correct behavior).
- `OpenAsset` sets `IsOpen = true` — sufficient for this batch; `RequestFocus()` is `internal`
  to `Fdp.Presentation` and could not be called directly. Acceptable deviation.
- `DrawClientArea` renders a correct placeholder. No ImGui state leaks.
- Constructor signature `base("utility_decision_editor", "Utility Decision Editor", "Authoring",
  WindowScope.PerspectiveBound)` — matches `BlackboardAuthoringWindow` precedent.

---

## Test Quality

12 new tests across one file (`UtilityDecisionAssetTests.cs`).

**`Changed` event contract (4 tests):** Covers false→true fires once, true→true does not re-fire,
and true→false does not fire. All three transitions explicitly tested — complete.

**`IEditableAsset` contract (2 tests):** `Kind` and `Name` delegation verified.

**`IsEditorOwned` (1 test):** Both `true` and `false` paths checked in one test. Acceptable.

**`ResponseCurveModel.ToRuntime` (1 test):** Uses `Quadratic` kind with non-default values for
all four params; verifies `Slope`, `Exponent`, `XShift`. Correct; `C`/`YShift` intentionally
not asserted (no matching runtime field).

**`UtilityTraceLaneProvider` (3 tests):** `Kind`, lane count, and lane IDs verified. Sufficient.

**`UtilityDecisionWindow` (2 tests):** Null-before-open and open-sets-asset-and-IsOpen. Both
correct.

**Gap noted (acceptable):** No test verifies `OnSelectionChanged` wires the window when
`EditorSelectionStore.ActiveAsset` changes. This is an integration concern that cannot be easily
unit-tested without firing the store's event internally; acceptable omission for now.

---

## Issues

None blocking.

---

## Final Test Count

| Project | Tests | Result |
|---------|-------|--------|
| Hrot.Utility.Editor.Tests | 81 (12 new) | Passed |
| Hrot.Editor.AiShared.Tests | 537 | Passed (no regressions) |
| **Total** | **618** | **Passed** |
