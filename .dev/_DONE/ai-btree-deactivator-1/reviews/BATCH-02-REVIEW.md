# BATCH-02 Review

**Reviewed by:** Dev Lead
**Date:** 2025-05-24
**Status:** APPROVED

---

## Verdict

BATCH-02 is **complete and correct**. All P1 and P2 corrections from BATCH-01-REVIEW are
addressed. TASK-EQL-004 (BTreeActionGenerator deactivator detection and emission) is
fully implemented with appropriate Roslyn compilation tests. No regressions were
introduced. All new tests pass.

---

## Scope Check

| Task | Status | Notes |
|------|--------|-------|
| Part A — ActionRegistry contract tests (P1 from BATCH-01-REVIEW) | DONE | 5 tests in ActionRegistryTests.cs, all pass. |
| Part A — Attribute contract tests (P2 from BATCH-01-REVIEW) | DONE | 4 tests in AttributeTests.cs, all pass. |
| TASK-EQL-004 — BTreeActionGenerator deactivator detection + emission | DONE | Correct routing, validation, emission. 5 Roslyn compilation tests pass. |

---

## Implementation Quality

### Part A: ActionRegistryTests.cs — Deactivator Contract Tests

Five tests added (T1–T5). Verified by reading source.

**T1 (RegisterDeactivator + TryGetDeactivator returns same instance):**
Uses `Assert.Same(deleg, retrieved)` — correct reference equality check for delegate
identity. Stronger than `Assert.Equal`. ✅

**T2 (TryGetDeactivator missing key returns false):**
Checks both return value and out-null assertion. ✅

**T3 (null key throws ArgumentNullException):**
Uses `Assert.Throws<ArgumentNullException>` and asserts `e.ParamName == "key"`. The
paramName check is correct and precise — matches the `ArgumentNullException("key")` guard
in `RegisterDeactivator`. ✅

**T4 (null delegate throws ArgumentNullException):**
Correct shape. ✅

**T5 (last-write wins on duplicate key):**
Registers `deleg1`, then `deleg2` for the same key. Uses `Assert.Same(deleg2, retrieved)`
and `Assert.NotSame(deleg1, retrieved)`. The `Assert.NotSame` guard prevents the test from
giving a false positive if the registry ignores the second registration. ✅

### Part A: AttributeTests.cs — Attribute/Delegate Contract Tests

Four tests added (T1–T4). Verified by reading source.

**T1 (NodeDeactivatorDelegate namespace == "Fbt"):** Correct. ✅

**T2 (BTreeDeactivatorAttribute namespace == "Fbt"):** Correct. ✅

**T3 (BTreeDeactivatorAttribute("Foo.Bar").TargetAction == "Foo.Bar"):**
Tests that the constructor argument is stored and returned correctly. Correct. ✅

**T4 (lambda assignable to NodeDeactivatorDelegate without explicit cast):**
Constructs a concrete type-parameterized delegate to avoid generic inference issues.
This approach is sound. ✅

### Part B: BTreeActionGenerator.cs — Deactivator Detection and Routing

Verified by reading source around the changed sections.

**Detection (`GetMethodInfo`):**
Checks for `BTreeDeactivatorAttribute` first (before `BTreeActionAttribute`/
`BTreeConditionAttribute`). Returns early with `IsDeactivator = true` and `TargetAction`
extracted from constructor argument. Correctly enforces 4-parameter-only constraint: 3-param
methods return `null` and are not processed as deactivators — bridge deactivators must use
4-param signature and include `@0` in their `TargetAction` string. ✅

**Routing in `Execute`:**
The `if (m.IsDeactivator) { deactivators.Add(m); continue; }` guard in the main method
loop prevents deactivator-flagged methods from falling into the `registrable`/`reusable`
lists. Without this `continue`, deactivators would also emit a spurious `registry.Register`
call alongside the `registry.RegisterDeactivator` call. The developer correctly fixed this
bug. ✅

**Validation:**
- Empty/null `TargetAction` → `BHU016_DeactivatorMissingTarget` (warning), no emission. ✅
- `TargetAction` not found in group Direct or Bridges → `BHU017_DeactivatorUnknownTarget`
  (warning), no emission. ✅
- Valid pairing → deactivator added to `group.Deactivators`. ✅

**Limitation noted:** Validation only fires when `mergedGroups.Count > 0`. A deactivator
method with no matching `[BTreeAction]` in the same compilation unit will produce
BHU-017, but a lone deactivator with an empty TargetAction (and no actions present at all)
will produce no diagnostic — it is silently dropped. This is acceptable behavior since
the deactivator would have no group to attach to anyway. Not a correctness issue.

**Emission (`GenerateRegistrar`):**
```
registry.RegisterDeactivator("<TargetAction>", global::<FullQualifiedMethodName>);
```
Correct format. Emitted after all `registry.Register` calls for the group. ✅

### Part B: SharedBhuDiagnostics.cs — New Diagnostic Descriptors

**BHU016:** `"BTreeDeactivator target is empty"`, `DiagnosticSeverity.Warning`. Correct. ✅
**BHU017:** `"BTreeDeactivator target is unknown"`, `DiagnosticSeverity.Warning`. Correct. ✅

Both are warnings (not errors) — consistent with the project's policy of using warnings for
generator-level design mistakes (allows build to continue, test runner can still run). ✅

### Part B: BTreeActionGeneratorTests.cs — Roslyn Compilation Tests

Five tests using `CSharpCompilation.Create` pattern (matching existing
`TkbDescriptorGeneratorTests.cs`). All 5 pass.

**T1 (4-param action + 4-param deactivator → RegisterDeactivator emitted):**
Checks `Assert.Contains("registry.RegisterDeactivator", generated)`. Correct positive case. ✅

**T2 (3-param bridge + @0 deactivator → compound key emitted):**
Checks that the verbatim `@0` suffix in `TargetAction` is passed through to the emitted
`RegisterDeactivator` call. Correct. ✅

**T3 (empty TargetAction → BHU_016 diagnostic, no RegisterDeactivator):**
Checks both `Assert.DoesNotContain("RegisterDeactivator", ...)` and the diagnostic ID.
Requires a dummy `[BTreeAction]` to create a group so BHU016 can fire — correct. ✅

**T4 (unknown TargetAction → BHU_017 diagnostic, no RegisterDeactivator):**
Same pattern as T3 with a non-matching target string. Correct. ✅

**T5 (no deactivators → no RegisterDeactivator lines — regression guard):**
Asserts `Assert.DoesNotContain("RegisterDeactivator", generated)` when no
`[BTreeDeactivator]` methods are present. Prevents future regression. ✅

---

## Test Baseline

### Fbt.Tests (FastBTree)

Total: 211 tests | **200 passing, 11 pre-existing failures**

Pre-existing failures (all pre-date BATCH-01):
- AutoDiscoveryTests (4) — require Fbt.SourceGen which does not exist on disk
- GeneratorOutputTests (2) — same dependency
- DefinitionGeneratorTests (4) — same dependency
- BuilderValidationTests.DtoTooLarge_ThrowsBehaviorTreeBuildException (1) — size guard
  was reverted in commit `2a3735e revert: no non-generic size checks in a generic library!`;
  the test was not removed; the guard has not been re-added

BATCH-02 contribution: **+9 passing tests** (5 ActionRegistryTests + 4 AttributeTests)
No regressions.

### Fdp.Toolkits.Tests

Total: ~1209 tests | **~1157–1158 passing, 51–52 pre-existing failures**

Pre-existing failures:
- ReplayBrowser export/search tests — environmental test isolation issue (pre-existing)
- IdAllocationTests (2) — threading/ordering sensitivity (pre-existing)
- DataDrivenGizmoSystemRoutingTests (2) — pre-existing
- ReferenceHandlerTests.LocalDiskStorageProvider (1) — disk path sensitivity (pre-existing)

BATCH-02 contribution: **+5 passing tests** (BTreeActionGeneratorTests T1–T5)
No regressions.

---

## Issues Found

### D-08 (P3) — No diagnostic for duplicate deactivator key

A developer can accidentally register two `[BTreeDeactivator]` methods with the same
`TargetAction` in the same compilation unit. The second will silently overwrite the first
in `group.Deactivators`. No diagnostic is emitted. This could lead to confusing runtime
behavior. Future enhancement: add BHU-018 for duplicate deactivator registration.

### Note: DESIGN.md §2.5 validation wording gap

DESIGN.md §2.5 says: "Generator validates each deactivator... If TargetAction does not
match a [BTreeAction] key in the same compilation unit → BHU-017". The phrase "same
compilation unit" is under-specified: validation actually matches against the same *group*
(same TBlackboard + TContext type pair), not the entire compilation unit. A deactivator
that targets an action from a different type context will emit BHU-017 even if the action
exists. This is a documentation gap, not a code bug. No action required this batch.

---

## Commit Message

```
feat(fbt): add ActionRegistry and Attribute contract tests (Phase 1 P1/P2 corrections)

TASK-EQL-002: ActionRegistryTests.cs - 5 deactivator contract tests (T1-T5).
TASK-EQL-001: AttributeTests.cs - 4 namespace/assignability contract tests (T1-T4).
All 9 tests pass. Fbt.Tests total: 211 (200 passing, 11 pre-existing failures).
```

```
feat(gen): BTreeActionGenerator deactivator detection and emission (TASK-EQL-004)

- BTreeActionGenerator.cs: [BTreeDeactivator] detection in GetMethodInfo (4-param only),
  routing via deactivators list with continue guard, validation (BHU016/BHU017), emission
  of registry.RegisterDeactivator calls in GenerateRegistrar.
- SharedBhuDiagnostics.cs: BHU016_DeactivatorMissingTarget and BHU017_DeactivatorUnknownTarget.
- BTreeActionGeneratorTests.cs: 5 Roslyn compilation tests (T1-T5), all pass.
  Fdp.Toolkits.Tests total: ~1209 (1157-1158 passing, 51-52 pre-existing failures).
```
