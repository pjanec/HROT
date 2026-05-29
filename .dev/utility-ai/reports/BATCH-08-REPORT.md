# BATCH-08 REPORT

**Developer:** AI Assistant (GitHub Copilot)
**Date:** 2025-07-22
**Batch Instructions:** `.dev/utility-ai/batches/BATCH-08-INSTRUCTIONS.md`
**Target Success Conditions:** SC-P2-01-1 through SC-P2-01-4, SC-P2-04-1 through SC-P2-04-2

---

## Executive Summary

### Status: COMPLETE

**Completed:**
- Task 1-A: Renamed `UtilityInputRegistrar` -> `UtilityInputReaderStore` in all production and test files
- Task 1-B: Created `[UtilityRegistrar]` attribute + `UtilityAutoDiscovery.ScanAndRegister()` startup handshake
- Task 1-C: `UtilityAutoDiscoveryTests.cs` — 3 tests (SC-P2-04-1, SC-P2-04-2, negative) all pass
- Task 2-A: `SharedUtilityDiagnostics.cs` — 6 error descriptors (UT0101-UT0112) in correct namespace
- Task 2-B: `UtilityInputGenerator.cs` — complete `IIncrementalGenerator` with validation pipeline and dual-file emission
- Task 2-C: `UtilityDecisionBuilderInfra.cs` — `In` class made `partial`, 13 manual accessor methods removed
- Task 2-D: `UtilityInputGeneratorTests.cs` — 6 tests covering all SC-P2-01-1 through SC-P2-01-4 criteria
- Task 2-E: Full build verification — 0 errors, all utility tests pass

**Test Results:**
- UtilityAutoDiscoveryTests: 3/3 pass
- UtilityInputGeneratorTests: 6/6 pass
- Full utility test suite: 114/114 pass (0 failures)
- Analyzer project build: 0 warnings, 0 errors
- Production project build: 0 warnings, 0 errors

**Build Status:** 0 errors

---

## Detailed Task Breakdown

### Task 1-A: Rename `UtilityInputRegistrar` -> `UtilityInputReaderStore`

**Files modified:**
- `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityScorer.cs` — class declaration + all internal usages
- `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/StandardInputs.cs` — 17 `Register(...)` call sites
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityScorerTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StarterPackIntegrationTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StandardInputReaderTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/Integration/UtilityTransitionArbiterTests.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/Integration/UtilitySelectorNodeTests.cs`

The generator will emit a class named `UtilityInputRegistrar` (the startup registrar); freeing this name by renaming the existing lookup table was a prerequisite.

### Task 1-B: `[UtilityRegistrar]` attribute + `UtilityAutoDiscovery`

**New file:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityRegistrarAttribute.cs`

- `UtilityRegistrarAttribute`: class-level attribute marking generated registrar classes
- `UtilityAutoDiscovery.ScanAndRegister()`: double-checked locking around a one-time assembly scan; finds all types decorated with `[UtilityRegistrar]` and invokes their `RegisterAll()` via reflection
- `ResetForTesting()`: internal method to reset the initialized flag between unit tests

### Task 1-C: `UtilityAutoDiscoveryTests.cs`

**New file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityAutoDiscoveryTests.cs`

| Test | Success Condition |
|------|-------------------|
| `ScanAndRegister_InvokesRegistrarInCurrentAssembly` | SC-P2-04-1: `[UtilityRegistrar]` class in test assembly has `RegisterAll()` invoked |
| `ScanAndRegister_SecondCallDoesNotReinvoke` | SC-P2-04-2: Second `ScanAndRegister()` call is a no-op (counter stays at 1) |
| `ScanAndRegister_IgnoresClassesWithoutAttribute` | Negative: class without `[UtilityRegistrar]` is not invoked |

All 3 pass.

### Task 2-A: `SharedUtilityDiagnostics.cs`

**New file:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/SharedUtilityDiagnostics.cs`

| Descriptor | ID | Trigger |
|------------|----|----|
| `UT0101_MissingName` | UT0101 | `[UtilityInput]` with empty/missing Name |
| `UT0102_DuplicateName` | UT0102 | Two methods share the same Name string |
| `UT0103_HashCollision` | UT0103 | Two distinct names produce the same Fnv1a16 hash |
| `UT0110_NotStatic` | UT0110 | Method is not static |
| `UT0111_NotFloat` | UT0111 | Method does not return `float` |
| `UT0112_WrongSignature` | UT0112 | Method does not take exactly `(in UtilityInputCtx)` |

All are `DiagnosticSeverity.Error`, category `"Fdp.UtilityAI"`, enabled by default.

### Task 2-B: `UtilityInputGenerator.cs`

**New file:** `FDP/Toolkits/Fdp.Toolkits.Analyzers/UtilityInputGenerator.cs`

**Pipeline:**
```
SyntaxProvider.CreateSyntaxProvider
  predicate: MethodDeclarationSyntax with AttributeLists
  transform: GetUtilityInputInfo -> UtilityInputInfo?
  .Where(m => m != null)
  .Collect()
  .Combine(CompilationProvider)
  RegisterSourceOutput -> Execute
    -> AddSource("UtilityInputRegistrar.g.cs")
    -> AddSource("UtilityInputAccessors.g.cs")
```

**Validation order in `GetUtilityInputInfo`:**
1. Confirm `[UtilityInputAttribute]` attribute present
2. Extract Name — UT0101 if empty
3. Check `IsStatic` — UT0110 if not
4. Check return type `float` — UT0111 if not
5. Check single `in UtilityInputCtx` parameter — UT0112 if not

**In `Execute`:**
- UT0102 duplicate name check (first occurrence wins)
- UT0103 hash collision check (first occurrence wins)
- Emits `UtilityInputRegistrar.g.cs` with `[UtilityRegistrar]` class
- Emits `UtilityInputAccessors.g.cs` with `partial class In` methods

**Hash formula:** 32-bit FNV-1a truncated to low 16 bits — matches `StandardInputIds` constants exactly.

### Task 2-C: `In` class partial + manual method removal

**File modified:** `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityDecisionBuilderInfra.cs`

- `public static class In` -> `public static partial class In`
- Removed 13 manual accessor methods: `AmmoFraction`, `WeaponHasAmmo`, `WeaponReadiness`, `HealthFraction`, `ContactHealthFraction`, `ContactThreatLevel`, `HasLineOfSight`, `HaveLiveTarget`, `EnemyStrengthRatio`, `IsAssignedTarget`, `AllyAdvancingNearby`, `WeaponRangeBandFit`, `WeaponEffectivenessVsTarget`
- Kept 4 special overloads: `DistanceToContext(ctx, maxRange)`, `EqsTopScore(templateName, ctx)`, `EqsResultCount(templateName, ctx)`, `Constant(value, ctx)`, and `Fnv1a32(name)` helper

**Ambiguity fix:** Removed `[UtilityInput("DistanceToContext")]` from the reader method in `StandardInputs.cs`. The manual `In.DistanceToContext(ctx, maxRange)` overload has distinct semantics (range cap parameter) that the simple generated accessor cannot replicate. The reader is still registered by `StandardInputs.RegisterAll()`.

### Task 2-D: `UtilityInputGeneratorTests.cs`

**New file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityInputGeneratorTests.cs`

Uses `CSharpGeneratorDriver.Create` pattern matching `BTreeActionGeneratorTests.cs`. Compilation created with `allowUnsafe: true` and a `CommonStubs` constant that stubs all required types (`UtilityInputAttribute`, `UtilityRegistrarAttribute`, `InputContext`, `UtilityInputCtx`, `InputRef`, `UtilityInputReaderStore`, `In` partial).

| Test | Success Condition |
|------|-------------------|
| `ThreeInputMethods_EmitRegistrarAndAccessors` | SC-P2-01-1: 2 generated files, 3 Register calls, 3 In methods, 0 compilation errors |
| `HashParity_AmmoFraction_MatchesStandardInputIds` | SC-P2-01-2: emitted hash == 0x2C39; pins AmmoFraction=0x2C39, HealthFraction=0x13D9, HaveLiveTarget=0xC20C |
| `HashCollision_EmitsUT0103` | SC-P2-01-3: dynamically found collision -> UT0103 diagnostic |
| `NonStaticMethod_EmitsUT0110` | SC-P2-01-4a: instance method -> UT0110 |
| `NonFloatReturn_EmitsUT0111` | SC-P2-01-4b: `int` return -> UT0111 |
| `WrongSignature_EmitsUT0112` | SC-P2-01-4c: `int` parameter -> UT0112 |

All 6 pass.

### Task 2-E: Build Verification

```
dotnet build FDP\Toolkits\Fdp.Toolkits\Fdp.Toolkits.csproj --no-incremental -v quiet
```
Result: **Build succeeded. 0 Warning(s) 0 Error(s)**

```
dotnet build FDP\Toolkits\Fdp.Toolkits.Analyzers\Fdp.Toolkits.Analyzers.csproj --no-incremental -v quiet
```
Result: **Build succeeded. 0 Warning(s) 0 Error(s)**

---

## Testing Results

| Suite | Passed | Failed | Total |
|-------|--------|--------|-------|
| `UtilityAutoDiscoveryTests` | 3 | 0 | 3 |
| `UtilityInputGeneratorTests` | 6 | 0 | 6 |
| Full utility suite (`~Utility`) | 114 | 0 | 114 |

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

The main issue was a CS0121 ambiguity after making `In` partial and running the generator: both the generated `In.DistanceToContext(InputContext ctx = default)` and the manually-kept `In.DistanceToContext(InputContext ctx = InputContext.Candidate, float maxRange = 0f)` matched a 0-argument call site. This was unique among the kept methods because `EqsTopScore`/`EqsResultCount`/`Constant` all have distinct first-parameter types (string or float) that prevent ambiguity with the generated `(InputContext)` signature.

Resolution: removed `[UtilityInput("DistanceToContext")]` from the reader method. The manual `In.DistanceToContext(ctx, maxRange)` is the correct public API for that input since it exposes the range-cap parameter. The reader itself is still registered by `StandardInputs.RegisterAll()`.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

The `UtilityAutoDiscovery.ScanInternal()` calls `AppDomain.CurrentDomain.GetAssemblies()` which only returns assemblies already loaded at startup. If a registrar-bearing assembly is loaded lazily later, it would be missed. A future improvement would be to also register an `AssemblyLoad` event handler. This is noted in DEBT-TRACKER as a low-priority item.

**Q3: What design decisions did you make beyond the instructions? How did you resolve them?**

For SC-P2-01-3 (hash collision test), the instructions said to find two real colliding names. Rather than hard-coding a pre-computed pair (which could become wrong if the hash function changes), the test includes a `Fnv1a16Ref` helper and finds a collision dynamically at test time. With a 16-bit hash space, the birthday paradox guarantees a collision within ~300 iterations on average. The test searched prefix `"Cand"` + integer and found a collision well within the 200,000 iteration ceiling, completing in ~567 ms.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

The generator uses the namespace of the first valid `[UtilityInput]` method to determine where to place the generated `UtilityInputRegistrar` class. When all inputs live in a single namespace (as in production with `Fdp.Toolkit.Utility.StandardInputs`), this works correctly. If inputs span multiple namespaces, the registrar lands in the first method's namespace — a limitation noted in the design doc but not stressed in the batch instructions.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

The incremental generator uses `.Collect()` which means the entire set of `[UtilityInput]` methods is reprocessed whenever any of them changes. For 16 inputs this is negligible, but with hundreds of inputs it could be optimized by splitting registration and accessor emission into two separate `RegisterSourceOutput` calls, each with a narrower upstream. Not a concern at current scale.

---

## Success Criteria Checklist

- [x] `UtilityInputReaderStore` compiles — rename complete, all references updated
- [x] `UtilityAutoDiscovery.ScanAndRegister()` works — 3 tests pass (SC-P2-04-1, SC-P2-04-2, negative)
- [x] `UtilityInputGenerator` emits correct registrar + accessors — 6 tests pass (SC-P2-01-1 through SC-P2-01-4)
- [x] Production `Fdp.Toolkits` project builds with zero errors using generated files
- [x] All 114 utility AI tests pass (no regressions)
- [x] Report submitted to `.dev/utility-ai/reports/BATCH-08-REPORT.md`

---

## Outstanding Issues / Next Steps

- The `DistanceToContext` reader is excluded from generator-driven registration (no `[UtilityInput]` attribute). It continues to be registered by `StandardInputs.RegisterAll()`. A follow-up task could address the design question of whether `DistanceToContext` should eventually have a dedicated generated accessor with the `maxRange` parameter baked in, or remain a manual keep.
- `UtilityAutoDiscovery` does not handle assemblies loaded after `ScanAndRegister()` is called. If lazy-loaded assemblies need to register inputs, an `AssemblyLoad` event hook would be required.
