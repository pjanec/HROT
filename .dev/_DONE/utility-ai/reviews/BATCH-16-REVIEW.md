# BATCH-16 Review

**Batch:** BATCH-16
**Reviewer:** Dev Lead
**Verdict:** APPROVED

---

## Summary

BATCH-16 added `InputCatalogEntry`, `InputCatalogBrowser` (reflection-based discovery), 
`UtilityComparisonSanitizer`, and `UtilityTuningDiffEngine` (+`TuningParamDiff`, `TuningDiffResult`).
23 new tests. 123/123 pass. 0 build errors.

---

## InputCatalogBrowser

**Correct.** Reflects over assemblies for types named `"In"` with public static methods returning
`"InputRef"`. Handles multiple overloads by preferring the richest (most non-context params) — good
defensive choice. `GetCustomAttributesData()` used instead of `GetCustomAttributes()` to avoid
dependency on the attribute type being loadable. Results sorted by `Name` (Ordinal). All three
`InputParamKind` variants (`String` for `EqsTopScore`, `Float` for `Constant`, `None` for
everything else) correctly inferred from the first non-`InputContext` parameter type.

Duplication handled: same `Name` across assemblies → first-assembly wins (by `Dictionary` semantics —
`TryGetValue` check ensures first wins). Correct.

---

## UtilityComparisonSanitizer

**Correct.** Pipeline exactly as specified:
1. Normalize line endings → `\n`
2. Strip `[UtilityLayout]` block via brace counting — handles indented case (uses `TrimStart`)
3. Sanitize the HROT_EDITOR_GENERATED line to strip the trailing prose suffix
4. Preserve all other content verbatim

Metadata extraction from `// AssetId:` and `public sealed partial class <Name>` lines is correct.
File-not-found → warning (no throw). Determinism test passes: same input → same output.

One noted detail: the brace counter starts counting from `[UtilityLayout]` line, which has no `{`.
The layout method body `{...}` appears two lines later, so the counter correctly starts at 0 and 
tracks the method body. The Layout attribute line and signature line (with no braces) are included
in the removal range because the loop includes all lines from `startIndex` through `endIndex`. Correct.

---

## UtilityTuningDiffEngine

**Correct.** Three-tier algorithm:
1. `structA != structB` → `IsStructureEqual=false` (fast lane not applicable)
2. `paramA == paramB` → `IsIdentical=true`
3. Walk by VisualId → emit diffs per param label

`CurveKind` diffs use `(float)(int)kind` — acceptable since the label `"CurveKind"` distinguishes
this diff entry from numeric param diffs. VisualId-sorted traversal matches the emitter's order.

The `Compute` method never throws — matching options by VisualId with null safety.

---

## Test Quality

**InputCatalogBrowserTests (8 tests):** Full coverage of the reflection path. Correct use of
`AppDomain.CurrentDomain.GetAssemblies()` to find `Fdp.Toolkits` assembly without requiring a
direct package reference in the test project. Parametrize on a real assembly and verify actual
method names — good behavioral tests, not just existence checks.

**UtilityComparisonSanitizerTests (9 tests):** All 9 required tests implemented. The determinism
test writes the same file twice and checks byte-identity. Layout stripping verified by `Assert.DoesNotContain`.

**UtilityTuningDiffEngineTests (6 tests):** Tests all four branches of the diff algorithm.
`Compute_WeightChange_IsStructureEqualTrue_OneWeightDiff` correctly builds two assets with different
weights and checks that exactly one `TuningParamDiff` with label `"Weight"` is returned.

---

## Issues

None blocking.

---

## Final Test Count

| Project | Tests | Result |
|---------|-------|--------|
| Hrot.Utility.Editor.Tests | 123 (23 new) | Passed |
| **Total new** | **23** | **Passed** |
