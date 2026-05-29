# BATCH-10 REVIEW

**Reviewer:** Dev Lead
**Date:** 2025-07-22
**Report reviewed:** `.dev/utility-ai/reports/BATCH-10-REPORT.md`
**Decision:** APPROVED

---

## Verification Results

| Item | Result |
|------|--------|
| Full solution build | BUILD SUCCEEDED (0 errors) |
| Utility test suite (132 tests) | 132/132 PASS |
| SC-P2-03-2a (clean Build → no UT0130) | PASS |
| SC-P2-03-2b (static field → UT0130) | PASS |
| SC-P2-03-2c (DateTime.Now → UT0130) | PASS |
| SC-P2-03-1/UT0131 (weight out of range) | PASS |
| SC-P2-03-3 (unknown input → UT0120) | PASS |
| SC-P2-03-3 (cross-assembly known input → no UT0120) | PASS |
| SC-P2-03-1/UT0143 (PostureSelect zero options) | PASS |

---

## Code Quality Assessment

### Analyzer structure — GOOD

Correct `[DiagnosticAnalyzer(LanguageNames.CSharp)]` implementation. `RegisterCompilationStartAction` is the right hook for building the per-compilation input catalog and then registering per-symbol and per-syntax-node actions from within it. `ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None)` correctly excludes generated code. `EnableConcurrentExecution()` correctly declared.

### UT0130 (purity) — GOOD, mirrors EqsTemplatePurityAnalyzer

Stage 1 (static mutable field read) is a verbatim copy of `EqsTemplatePurityAnalyzer.EQS_002`. Stage 2 (disallowed type names) is a simple syntactic name check covering `DateTime`, `EntityRepository`, `ISimulationView`, `Random`. Both stages report at method location (not expression location), matching the existing analyzer convention.

### UT0131 (weight range) — GOOD

Only fires on `SemanticModel.GetConstantValue` resolved constants, which avoids false positives on variable weights. Uses `Convert.ToSingle` inside a try-catch for safety against unexpected type widening.

### UT0120 (unknown input) — GOOD

Cross-assembly catalog built via `compilation.Assembly.GlobalNamespace` + explicit referenced assembly iteration. `TryExtractInAccessorName` uses semantic resolution first (the `[UtilityInput]` Name), falling back to the method identifier text. This correctly handles both the common case (well-decorated `In.*` accessor) and the malformed case (bare method on `In` without attribute). SC-P2-03-3 cross-assembly test correctly builds two separate compilations and links them via `MetadataReference`.

### UT0143 (PostureSelect zero options) — GOOD

Syntactic count correctly handles both `Option(...)` and `CandidateOption(...)` patterns. Uses `IsPostureSelectDecision` which reads arg[2] from the `[UtilityDecision]` attribute and compares to enum value 1.

---

## Test Quality Assessment

9 tests cover all implemented diagnostics with both positive and negative cases. The cross-assembly test (test 9) is particularly important: it builds a real upstream compilation, creates a `MetadataReference` from it, and verifies the analyzer finds the upstream input. This is exactly the SC-P2-03-3 requirement.

The `CommonStubs` in the test uses `System.AttributeUsage(System.AttributeTargets.X)` (fully qualified) instead of `using System;` + shortname, which correctly avoids the CS1529 ordering issue discovered during development.

---

## Issues Found

None requiring fixes. Deferred diagnostics (UT0121, UT0122, UT0144, UT0145) are reasonable given their complexity and lower priority. They are tracked in DEBT-TRACKER as open items.

---

## Deferred Items

| ID | Diagnostic | Priority | Target |
|----|------------|---------|--------|
| D-15 | UT0121 (wrong context) — requires per-input AllowedContexts metadata | P3 | Phase 3 cleanup |
| D-16 | UT0122 (missing required param) — requires param-schema per input | P3 | Phase 3 cleanup |
| D-17 | UT0144 (no sum-mode fallback warning) | P3 | Phase 3 cleanup |
| D-18 | UT0145 (duplicate OptionId warning) | P3 | Phase 3 cleanup |

---

## Dev-Lead Fixes

None.

---

## Commit Approved

```
feat(utility-ai): Phase 2 Step 3 - UtilityAuthoringAnalyzer

- Added SharedUtilityDiagnostics UT0120, UT0130, UT0131, UT0143
- Implemented UtilityAuthoringAnalyzer (DiagnosticAnalyzer):
  - UT0130: purity violation in Build (static field reads + disallowed types)
  - UT0131: weight literal outside [0, 1]
  - UT0120: unknown input name (cross-assembly catalog via GlobalNamespace)
  - UT0143: PostureSelect decision with zero options
- Added UtilityAuthoringAnalyzerTests (9) covering SC-P2-03-1/2/3

132/132 utility tests pass. Resolves TASK-UAI-P2-03.
```
