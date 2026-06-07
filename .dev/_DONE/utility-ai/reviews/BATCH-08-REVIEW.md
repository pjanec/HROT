# BATCH-08 REVIEW

**Reviewer:** Dev Lead
**Date:** 2025-07-22
**Report reviewed:** `.dev/utility-ai/reports/BATCH-08-REPORT.md`
**Decision:** APPROVED

---

## Verification Results

| Item | Result |
|------|--------|
| Full solution build | BUILD SUCCEEDED (0 errors) |
| Utility test suite (114 tests) | 114/114 PASS |
| SC-P2-04-1 (ScanAndRegister invokes registrar) | PASS |
| SC-P2-04-2 (second call is no-op) | PASS |
| SC-P2-01-1 (3 inputs emit 2 files, 3 entries each, 0 errors) | PASS |
| SC-P2-01-2 (hash parity: AmmoFraction=0x2C39) | PASS |
| SC-P2-01-3 (hash collision -> UT0103) | PASS |
| SC-P2-01-4 (UT0110, UT0111, UT0112) | PASS |

---

## Code Quality Assessment

### Generator (UtilityInputGenerator.cs) — GOOD

The incremental generator pipeline is correctly structured. Validation order (UT0101 -> UT0110 -> UT0111 -> UT0112 in `GetUtilityInputInfo`, then UT0102/UT0103 in `Execute`) mirrors the design doc §5. The `UtilityInputInfo` data class returns early on the first error per method, which is the correct priority ordering. The `Fnv1a16` function matches the known vectors exactly (verified by SC-P2-01-2 test).

### Diagnostics (SharedUtilityDiagnostics.cs) — GOOD

All six descriptors correct: IDs, titles, categories (`Fdp.UtilityAI`), severity (`Error`), default enabled. Structure matches `SharedBhuDiagnostics.cs` pattern. Ready for reuse by the upcoming `UtilityAuthoringAnalyzer` (BATCH-10).

### AutoDiscovery (UtilityRegistrarAttribute.cs) — GOOD

Double-checked locking is correct. `_initialized` is `volatile`. `ResetForTesting()` is `internal`. `ScanInternal()` wraps `asm.GetTypes()` in try-catch (required for dynamic/reflection-only assemblies). The `RegisterAll` method lookup uses `Type.EmptyTypes` as parameter filter — correct since the generated registrar has no parameters.

### In class (UtilityDecisionBuilderInfra.cs) — GOOD

`partial` keyword added, 13 methods removed, 4 special overloads kept (EqsTopScore, EqsResultCount, DistanceToContext, Constant) plus Fnv1a32 helper. Removal is clean.

### DistanceToContext design decision — ACCEPTED

`[UtilityInput("DistanceToContext")]` removed from `StandardInputs.DistanceToContext(in UtilityInputCtx)` to avoid CS0121 ambiguity with the manual `In.DistanceToContext(InputContext ctx, float maxRange)` overload. The reader is still registered by `StandardInputs.RegisterAll()`. This is the correct call: the parameterized API is the intended public surface.

---

## Test Quality Assessment

### UtilityAutoDiscoveryTests — GOOD

- SC-P2-04-1: test places a real `[UtilityRegistrar]` class in the test assembly, calls `ScanAndRegister()`, and asserts the flag was set. This is an end-to-end behavioral test, not a fake.
- SC-P2-04-2: counter-based; verifies exactly one invocation even after two `ScanAndRegister()` calls. Correct and tight.
- Negative: verifies classes without the attribute are not invoked. Correct.
- `ResetForTesting()` used before each test to isolate state. Correct pattern.

### UtilityInputGeneratorTests — GOOD

- SC-P2-01-1: uses real `CSharpGeneratorDriver`, checks file count, Register call count via string split, and zero compilation errors on the output. Substantive.
- SC-P2-01-2: extracts the actual hex literal from generated source via regex and compares to `0x2C39`. Also pins HealthFraction and HaveLiveTarget. Strong hash parity coverage.
- SC-P2-01-3: dynamically finds a real collision at test time (birthday paradox search). Correct, though see P3 note below.
- SC-P2-01-4a/b/c: each exercises a distinct validation path and asserts a specific diagnostic ID. Well-targeted.

---

## Issues Found

### P3 (Low Priority): Hash collision test search overhead

`HashCollision_EmitsUT0103` searches up to 200,000 candidate strings at runtime to find a collision. Report states ~567ms. This is borderline; on a slow CI machine it could exceed 1s. Suggest precomputing a known collision pair (e.g., iterate once, note the two names, hard-code them) and adding a comment with the result. The Fnv1a16Ref helper already exists in the test file and can verify the pair at test time.

Record in DEBT-TRACKER as D-10, P3.

### P2 (Medium): DistanceToContext excluded from generated registrar

The generated `UtilityInputRegistrar.RegisterAll()` does not register `DistanceToContext` because its `[UtilityInput]` attribute was removed. In the current codebase this is fine because `StandardInputs.RegisterAll()` is still the startup call and covers all 17 readers. However, when the codebase transitions to `UtilityAutoDiscovery.ScanAndRegister()` as the sole startup mechanism (a future Phase 3/4 step), `DistanceToContext` will be silently missing from registration. The existing test suite would catch this because `StandardInputReaderTests` exercises all 17 readers.

BATCH-09 scope is not affected. The transition task should add an explicit registration call or a dedicated `[UtilityRegistrar]` wrapper for the special-case inputs. Record in DEBT-TRACKER as D-11, P2.

---

## Dev-Lead Fixes

None required. Approved as-is.

---

## Commit Approved

Use the commit message from the report:

```
feat(utility-ai): Phase 2 Step 1 - UtilityInputGenerator + UtilityAutoDiscovery

- Renamed UtilityInputRegistrar -> UtilityInputReaderStore (frees name for generator)
- Added [UtilityRegistrar] attribute + UtilityAutoDiscovery.ScanAndRegister()
- Implemented UtilityInputGenerator (IIncrementalGenerator): emits
  UtilityInputRegistrar.g.cs + UtilityInputAccessors.g.cs with FNV-1a-16 hashes
- Added SharedUtilityDiagnostics (UT0101-UT0112)
- Made In class partial; removed 13 manual accessor methods (now generated)
- Added UtilityAutoDiscoveryTests (3) and UtilityInputGeneratorTests (6)

114/114 utility tests pass. Resolves TASK-UAI-P2-01 and TASK-UAI-P2-04.
```
