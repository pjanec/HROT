# BATCH-15 Review

**Batch:** BATCH-15
**Reviewer:** Dev Lead
**Verdict:** APPROVED

---

## Summary

BATCH-15 implemented `UtilityFluentEmitter`, `UtilityAssetHasher`, extended `InputParamsModel` with
`TemplateName` for EQS round-trip fidelity, and added 19 tests. Build clean, 100/100 tests pass.

---

## InputParamsModel Extension

**Correct.** `TemplateName = string.Empty` added as the last field — no existing code broken.
This is the minimal change needed for `In.EqsTopScore("CoverQuery")` round-trips.

---

## UtilityFluentEmitter

**Correct.** Key design points verified:

- `IFluentCSharpEmitter<UtilityDecisionAsset>` implemented directly (not via the abstract base,
  which is appropriate since the base only adds file-write helpers not needed in a pure emitter).
- Output format matches the starter-pack runtime decisions exactly: file-scoped namespace, `partial`
  class, `:` attribute arg syntax, correct indentation, `));` chain terminator.
- Options and considerations sorted by `VisualId` with `StringComparer.Ordinal` — correct and
  deterministic. The ordering is independent of insertion order.
- `Curve.*` preset matching covers all 8 non-piecewise kinds. The switch pattern correctly matches
  `Bell (kind=Bell, M=1, K=8, B=1.0)`, `Step (kind=Step, M=1, K=1, B=0.5)`,
  `Threshold (kind=Threshold, M=1, K=1, B=0.5)`. Non-matching curves emit `new ResponseCurve(...)`.
- `HysteresisBonus = 0f` → NOT emitted in attribute. Non-zero → emitted with `R` format float.
- `[UtilityLayout]` placeholder emitted when `HasLayoutData` returns true. The layout body defers
  to BATCH-16 with a comment — correct for this batch scope.
- Float literals use `CultureInfo.InvariantCulture` with `R` format — correct for round-trip
  precision (design §8.4).
- `BuildHeader` from `FluentCSharpEmitterBase` used for header — correct.
- `CollectUsings()` always adds `"Fdp.Toolkit.Utility"` and sorts — sufficient for current scope.

---

## UtilityAssetHasher

**Correct.** `HashCode` struct used correctly (`.ToHashCode()` not `.GetHashCode()`).
`SortedOptions` and `SortedConsiderations` use `StringComparer.Ordinal` — consistent with emitter.
Layout changes are intentionally NOT included in either hash — only structure and params are hashed.
This matches the design (§8.5): layout-only changes classify as Cosmetic.

The `Classify` delegate call to `HotReloadClassifier.Classify` is correct — no logic duplication.

---

## Test Quality

19 tests total.

**Determinism (3 tests):** `Emit_SameModel_ByteIdentical_SecondEmit` directly tests the SC-P5-1
byte-stable property. The two sort tests are good behavioral tests, not just existence checks —
they verify position ordering by index in the output string.

**Header/Attribute (7 tests):** Full coverage of all attribute args. `hysteresisBonus` tested for
both zero-omit and non-zero-emit cases — correct.

**Build method (4 tests):** `CandidateOption` vs `Option` dispatch verified. Preset curve emission
and custom curve `new ResponseCurve(...)` emission verified. Weight R-format precision verified.

**Hot-reload classification (4 tests):** Cosmetic (layout only), Soft (weight change), Hard (add
option), Hard (input name change) — all four required tiers correctly exercised.

---

## Issues

None blocking.

---

## Final Test Count

| Project | Tests | Result |
|---------|-------|--------|
| Hrot.Utility.Editor.Tests | 100 (19 new) | Passed |
| **Total new** | **19** | **Passed** |
