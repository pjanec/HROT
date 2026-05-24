# BATCH-43 Report — UBP-P6T1 + UBP-P6T2

**Date:** 2026-05-25  
**Tasks:** UBP-P6T1 (BlueprintVariablePredicateDto + JSON registration), UBP-P6T2 (Slot-table-aware IL emission)

---

## Files Modified

### 1. `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs`
- Added `[JsonDerivedType(typeof(BlueprintVariablePredicateDto), "BlueprintVariable")]` to the `[JsonPolymorphic]` attribute list after the `TraceBufferScan` entry.
- Added `BlueprintVariablePredicateDto` sealed class with `TargetBlueprintAssetId` (Guid), `VariableName` (string), `Operator` (SearchOperator), and `Predicate` (SearchPredicateDto) properties, positioned after `TraceBufferScanPredicateDto` and before the Result types region.

### 2. `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/PredicateCompiler.cs`
- Added three `using` directives: `Fdp.Toolkit.Blueprints`, `Fdp.Toolkit.Blueprints.Components`, `Fdp.Toolkit.Blueprints.Partitioning`.
- Added private field `_blueprintRegistry` of type `BlueprintRegistry?`.
- Extended constructor with optional third parameter `BlueprintRegistry? blueprintRegistry = null`; assigns `_blueprintRegistry = blueprintRegistry`. Existing callers with zero, one, or two args are unaffected.
- Added `case BlueprintVariablePredicateDto blueprintVar:` switch case in `Compile()`, before the specialized loop predicates comment.
- Added `CompileBlueprintVariablePredicate` instance method: resolves blueprint ID, looks up definition and field descriptor, dispatches to `BuildBlueprintVariableMatcher<TField>` via reflection.
- Added `BuildBlueprintVariableMatcher<TField>` static unsafe method: bakes tier component type IDs (1024/4096/16384) at compile time, builds an expression-compiled field matcher, returns a closure that probes all three tiers on every evaluation and calls `BlueprintBlackboardPartitions.TryGetSlotOffset` to locate the slot before reading the field.

### 3. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs`
- Added `case BlueprintVariablePredicateDto _:` to the component-predicate switch block in `TryMountDelegate`, after the existing `TraceBufferScanPredicateDto` case.

### 4. `FDP/Toolkits/Fdp.Toolkits.Tests/ReplayBrowser/Search/SearchPredicateDtoSerializationTests.cs`
- Added `BlueprintVariablePredicate_SerializesRoundTrip` test method at end of class.

### 5. `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/BlueprintVariableTests.cs` (NEW)
- Created new test file with `[Collection("ComponentRegistry")]` on `BlueprintVariableCompilerTests`.
- Contains three test methods (see below).

---

## Tests Added

| Test | File | Result |
|------|------|--------|
| `BlueprintVariablePredicate_SerializesRoundTrip` | `SearchPredicateDtoSerializationTests.cs` | PASS |
| `Compile_BlueprintVariable_NoSlotPresent_ReturnsFalse` | `BlueprintVariableTests.cs` | PASS |
| `Compile_BlueprintVariable_SlotPresent_EvaluatesField` | `BlueprintVariableTests.cs` | PASS |
| `Compile_BlueprintVariable_TierUpgrade_StillWorks` | `BlueprintVariableTests.cs` | PASS |

---

## Deviations from Instructions

None. All instructions followed exactly:
- `CollectMandatoryComponents` left unchanged (no single mandatory component for BlueprintVariable).
- `BlueprintBlackboard16384` not registered in test setup.
- Real `BlueprintBlackboardPartitions` functions used (no mocking).
- BB1024 reference re-fetched after `repo.AddComponent(entity, new BlueprintBlackboard4096())` in tier upgrade test.
- `[Collection("ComponentRegistry")]` attribute applied to new test class.

---

## Final dotnet test output

### Hrot.Diagnostics.Breakpoints.Tests

```
Test run for ...Hrot.Diagnostics.Breakpoints.Tests.dll (.NETCoreApp,Version=v8.0)
VSTest version 18.0.2 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:    57, Skipped:     0, Total:    57, Duration: 240 ms - Hrot.Diagnostics.Breakpoints.Tests.dll (net8.0)
```

### Fdp.Toolkits.Tests (BlueprintVariablePredicate filter)

```
Test run for ...Fdp.Toolkits.Tests.dll (.NETCoreApp,Version=v8.0)
VSTest version 18.0.2 (x64)

Starting test execution, please wait...
A total of 1 test files matched the specified pattern.

Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 56 ms - Fdp.Toolkits.Tests.dll (net8.0)
```
