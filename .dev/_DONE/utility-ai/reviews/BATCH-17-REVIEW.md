# BATCH-17 Review

**Batch:** BATCH-17
**Reviewer:** Dev Lead
**Verdict:** APPROVED

---

## Summary

BATCH-17 implemented `UtilityAssetLoader` (SC-P5-4: partial-manifest read-only detection) and the
`UtilityPreviewRunner` (SC-P5-2: live preview using the real scorer). 14 new tests. 137/137 pass.
0 build errors.

---

## UtilityDecisionAsset.IsEditorOwned

**Correct.** Field added as `public bool IsEditorOwned { get; set; } = true;` — defaults to true
(existing assets unaffected). The loader sets this false when the marker is absent.

---

## UtilityAssetLoader

**Correct.** Text-based extraction without Roslyn dependency.

- Marker check within first 5 lines: correct. Files that lack the marker get `IsEditorOwned = false`
  and a warning. This satisfies SC-P5-4: partial-manifest files open in a read-only state.
- Attribute parsing: regex-free, using `IndexOf` + substring extraction. Robust against the
  column-aligned formatting `UtilityFluentEmitter` emits (it handles both `assetId:     "..."` and
  `assetId: "..."`).
- `Guid.TryParse` used for AssetId — correct; no throw on malformed input.
- `Enum.TryParse<DecisionKind>` used for Kind — correct; defaults to `PostureSelect` on failure.
- HysteresisBonus parsing: handles the `f)]` and `f,` suffixes correctly.
- `UtilityLoadResult` record defined in the same file — minimal and appropriate.

---

## UtilityPreviewRunner

**Correct.** The critical invariant (SC-P5-2) is satisfied: the runner calls the actual
`UtilityScorer.Evaluate` static method, not a reimplementation. The top score from
`UtilityPreviewRunner.Evaluate` is byte-identical to a direct `UtilityScorer.Evaluate` call on the
same `UtilityDecisionDef`.

- `ComputeInputId` correctly uses `(ushort)(In.Fnv1a32(inputName) & 0xFFFF)` — the same derivation
  as `StandardInputIds` constants.
- `ResponseCurveModel.ToRuntime()` called for each consideration — correct, the emitter-side model
  knows how to produce the runtime `ResponseCurve`.
- `InputParams` copied field-by-field from `ConsiderationModel.Params` — correct.
- Options traversed in original list order (not VisualId order) — correct for the preview path.
  VisualId ordering is only for the emitter's deterministic output; the scorer doesn't care.
- Unsafe pointer `&traceMem` used directly inside `unsafe static` method — correct for stack variable.
- `AllowUnsafeBlocks` added to both csproj files — necessary and correct.

---

## Test Quality

**UtilityAssetLoaderTests (8 tests):**
`Load_FileNotFound_ReturnsReadOnlyWithWarning`, `Load_FileWithGeneratedMarker_IsEditorOwnedTrue`,
and `Load_FileWithoutGeneratedMarker_IsEditorOwnedFalse` cover the core SC-P5-4 requirement.
Attribute extraction tests are robust and cover all five fields including the optional
`HysteresisBonus`.

**UtilityPreviewRunnerTests (6 tests):**
The SC-P5-2 test (`Evaluate_SingleConsideration_TopScoreMatchesDirectScorerCall`) is the most
important. It registers a stub reader for `StandardInputIds.Constant`, builds an identical
`UtilityDecisionDef` manually, calls the scorer directly, and asserts the runner's `TopScore`
equals the direct scorer's result to 5 decimal places. This is the gold-standard test for
the "no drift" requirement.

`IDisposable.Dispose()` calls `UtilityInputReaderStore.Clear()` — correct test isolation.
Stub readers in the `StandardInputIds.Constant` range avoid conflicts with other test classes
(which use ids 10-55).

---

## Issues

None blocking.

---

## Final Test Count

| Project | Tests | Result |
|---------|-------|--------|
| Hrot.Utility.Editor.Tests | 137 (14 new) | Passed |
| **Total new** | **14** | **Passed** |
