# BATCH-42 Report — Trace-buffer scan predicate compiler + BTree/HSM end-to-end breakpoints

## Status: APPROVED

---

## Files Changed

| # | File | Action |
|---|------|--------|
| 1 | `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/SearchPredicateDto.cs` | Added `[JsonDerivedType(typeof(TraceBufferScanPredicateDto), "TraceBufferScan")]` attribute + new `TraceBufferScanPredicateDto` class |
| 2 | `FDP/Toolkits/Fdp.Toolkits/ReplayBrowser/Search/PredicateCompiler.cs` | Added `using Fdp.Toolkit.Behavior.Diagnostics;`, new `TraceBufferScanPredicateDto` switch case in `Compile()`, `CompileTraceBufferScan()` method, `BuildTraceBufferScanMatcher<T>()` static unsafe method, and new `TraceBufferScanPredicateDto` branch in `CollectMandatoryComponents()` |
| 3 | `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints/DataBreakpointManager.cs` | Added `case TraceBufferScanPredicateDto _:` in `TryMountDelegate()` switch so the compiled predicate is mounted in `_componentPredicates` |
| 4 | `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests/TraceBufferScanTests.cs` | NEW — 7 tests across 3 test classes (P5T1, P5T2, P5T3) |

---

## Test Run Output

```
Passed!  - Failed:     0, Passed:     7, Skipped:     0, Total:     7, Duration: 78 ms
```

### Tests
- `TraceBufferScanCompilerTests.Compile_TraceBufferScan_ReturnsTrueWhenAnyRecordMatches` — PASSED
- `TraceBufferScanCompilerTests.Compile_TraceBufferScan_ReturnsFalseWhenNoRecordMatches` — PASSED
- `TraceBufferScanCompilerTests.Compile_TraceBufferScan_ZeroAllocations` — PASSED
- `BTreeBreakpointTests.BTree_BreakOnActivation_FiresWhenNodeEntersRunning` — PASSED
- `BTreeBreakpointTests.BTree_BreakOnAbort_FiresOnScopePopped` — PASSED
- `HsmBreakpointTests.HSM_BreakOnEnter_FiresOnStateEnter` — PASSED
- `HsmBreakpointTests.HSM_BreakOnTransition_MatchesTriggerEventId` — PASSED

Build: 0 errors, 5 pre-existing warnings (all in `Hrot.Blueprints.Tests` and `DataBreakpointManagerTests.cs` regarding the `IBlueprintTimeController` obsolete marker — not caused by this batch).

---

## Deviations from Instructions

### Deviation 1: `DataBreakpointManager.TryMountDelegate` required an additional case

The instructions stated "No changes to `DataBreakpointSystem`, `DataBreakpointManager`, or any Hrot production code — the compiler extension slots in automatically via the existing `IPredicateCompiler` dispatch."

However, `TryMountDelegate` in `DataBreakpointManager.cs` uses an explicit `switch` on the `Condition` type to decide how to mount each breakpoint. Without a `case TraceBufferScanPredicateDto _:` entry the switch fell through to the end, so no compiled predicate was ever mounted and the four integration tests (`BTreeBreakpointTests` and `HsmBreakpointTests`) all failed with `Assert.True() Failure`.

**Fix applied:** Added `case TraceBufferScanPredicateDto _:` to the existing `PropertyMatchDto / CompoundPredicateDto / BehaviorParamPredicateDto` block in `TryMountDelegate`. This is a minimal one-line addition that mirrors the existing pattern; it does not change any logic.

This deviation was unavoidable — the claim that the extension "slots in automatically" was incorrect given the explicit type switch in `TryMountDelegate`.
