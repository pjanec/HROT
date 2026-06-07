# BATCH-09 REVIEW

**Reviewer:** Dev Lead
**Date:** 2025-07-22
**Report reviewed:** `.dev/utility-ai/reports/BATCH-09-REPORT.md`
**Decision:** APPROVED WITH DEV-LEAD FIXES

---

## Verification Results

| Item | Result |
|------|--------|
| Full solution build | BUILD SUCCEEDED (0 errors) |
| Utility test suite (123 tests after fix) | 123/123 PASS |
| SC-P2-02-1 (catalog + ids emitted, 0 errors) | PASS |
| SC-P2-02-2 (blueprintId == FNV-1a-32(assetId)) | PASS |
| SC-P2-02-3 (simple Build → full manifest) | PASS |
| SC-P2-02-4 (foreach Build → partial manifest) | FIXED (see dev-lead fix D-12) |
| UT0140 diagnostic | PASS |
| UT0141 diagnostic | PASS |
| UT0150 diagnostic | PASS |

---

## Code Quality Assessment

### Generator (UtilityDecisionGenerator.cs) — GOOD

Incremental pipeline structure is consistent with `UtilityInputGenerator`. Recognition on `ClassDeclarationSyntax`, attribute extraction covers all 5 constructor arguments including named `Category` and `HysteresisBonus`. Validation order (UT0140 interface check → UT0141 method check per class; UT0150 duplicate asset-id check cross-class in Execute) is correct. The `AnalyzeBuildBody` syntactic walker correctly returns `(false, 0, 0)` on any `foreach`/`for`/`while`/`if`/`local declaration` presence. Float literals use `CultureInfo.InvariantCulture` to prevent locale-dependent decimal separators.

### Namespace conflict resolution — ACCEPTABLE

Generated catalog placed in `<decisionNs>.Generated` (e.g. `Fdp.Toolkit.Utility.Decisions.Generated`) to avoid CS0101 clash with the existing reflective `UtilityDecisionCatalog` in `Fdp.Toolkit.Utility`. The `[UtilityRegistrar]` attribute is still on the generated class so `ScanAndRegisterDecisions` finds it correctly. This is a pragmatic workaround; the existing reflective catalog should be removed in a future cleanup batch (add to DEBT-TRACKER).

### StarterPack decision classes made partial — CORRECT

`CombatPostureDecision`, `ThreatRankingDecision`, `WeaponSelectionDecision`, `LeaderAssignmentDecision` — existing `static readonly int Id` fields replaced by `const int Id` generated via partial class. The `unchecked((int)0x...)` form handles unsigned overflow correctly.

### ScanAndRegisterDecisions — GOOD

Double-checked lock pattern matching `ScanAndRegister`. Uses `MakeByRefType()` + `parameters[0].IsOut` check to match the `out UtilityRegistry` signature. Uses a separate `_decisionsInitialized` flag and `_cachedDecisionRegistry` field so the two scans are independent. `ResetDecisionsForTesting()` provided.

### MergeFrom on UtilityRegistry — NOTE

`internal void MergeFrom(UtilityRegistry source)` was added but `ScanAndRegisterDecisions` appears to pass a single shared registry to each found registrar, which means the registrars populate the same registry directly (each registrar calls `RegisterAll(out registry)` and the results are aggregated). The `MergeFrom` method may be unused in the current implementation — verify and remove if unused to keep the API clean. Added as D-12.

---

## Test Quality Assessment

### UtilityDecisionGeneratorTests — GOOD AFTER FIX

Developer submitted 6 tests; SC-P2-02-4 (foreach → partial manifest) was missing. Dev-lead added `ForeachBuild_EmitsPartialManifest` as the 7th test. Coverage now complete:

| Test | SC |
|------|-----|
| `DecisionClass_EmitsCatalogAndIds` | SC-P2-02-1 |
| `BlueprintId_MatchesFnv1a32OfAssetId` | SC-P2-02-2 |
| `ManifestEntry_CountsOptionsAndConsiders` | SC-P2-02-3 (full) |
| `ForeachBuild_EmitsPartialManifest` (DEV-LEAD FIX) | SC-P2-02-4 (partial) |
| `MissingInterface_EmitsUT0140` | UT0140 |
| `MissingBuildMethod_EmitsUT0141` | UT0141 |
| `DuplicateAssetId_EmitsUT0150` | UT0150 |

The `ManifestEntry_CountsOptionsAndConsiders` test uses `Assert.Contains("true,", ...)` which is a broad substring check. This is acceptable given the generated structure but could produce a false positive if `true` appears elsewhere in the manifest section. Not blocking.

### UtilityAutoDiscoveryTests additions — GOOD

`ScanAndRegisterDecisions_InvokesDecisionRegistrar` and `ScanAndRegisterDecisions_SecondCallDoesNotReinvoke` both use the same counter/flag pattern as the input scan tests. `ResetDecisionsForTesting()` correctly isolates state.

---

## Issues Found

### D-12 (DONE — dev-lead fix): SC-P2-02-4 test missing

`ForeachBuild_EmitsPartialManifest` test was not submitted. Added by dev lead directly. 123/123 tests pass.

### D-13 (P3): `MergeFrom` may be unused

`UtilityRegistry.MergeFrom(UtilityRegistry)` was added but may not be called in the current `ScanAndRegisterDecisions` implementation. Confirm and remove if unused (dead code).

### D-14 (P3): Reflective `UtilityDecisionCatalog` coexists with generated one

The existing reflective `UtilityDecisionCatalog.RegisterAll(out UtilityRegistry)` in `UtilityDecisionCatalog.cs` is now superseded by the generated catalog. Both perform the same scan/register. After Phase 2 is complete and the generated catalog is used in production, the reflective version should be removed to eliminate duplication. Target: Phase 3 cleanup batch.

---

## Dev-Lead Fixes

| Fix | File | Change |
|-----|------|--------|
| D-12 | `UtilityDecisionGeneratorTests.cs` | Added `ForeachBuild_EmitsPartialManifest` test (SC-P2-02-4) |

---

## Commit Approved

```
feat(utility-ai): Phase 2 Step 2 - UtilityDecisionGenerator

- Added UtilityDecisionManifestEntry struct to UtilityDecisionCatalog.cs
- Added ScanAndRegisterDecisions(out UtilityRegistry) + ResetDecisionsForTesting()
  to UtilityAutoDiscovery
- Implemented UtilityDecisionGenerator (IIncrementalGenerator): emits
  UtilityDecisionCatalog.g.cs + UtilityDecisionIds.g.cs with FNV-1a-32 blueprintIds
  and best-effort manifest (full vs partial based on Build body analysis)
- Added SharedUtilityDiagnostics UT0140, UT0141, UT0150
- Made StarterPack decision classes partial; replaced readonly Id fields with
  const int Id generated via partial class
- Added UtilityDecisionGeneratorTests (7) and UtilityAutoDiscoveryTests additions (2)
- Dev-lead fix: added ForeachBuild_EmitsPartialManifest test (SC-P2-02-4)

123/123 utility tests pass. Resolves TASK-UAI-P2-02.
```
