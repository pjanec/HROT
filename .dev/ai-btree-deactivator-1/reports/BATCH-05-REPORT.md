# BATCH-05 Report — Interpreter Cleanup + Editor Integration (EQL-012)

**Status:** COMPLETE  
**Task:** EQL-012  
**Date completed:** 2026-05-24

---

## Summary

All five deliverables for EQL-012 were implemented successfully. The
`_deactivatorDelegates` pre-built array has been removed from `Interpreter` and
replaced with a stored `_registry` reference. The Roslyn generator, factory, and
visualizer were updated accordingly. Four new tests were written and all pass.

---

## Files Changed

| File | Action | Description |
|------|--------|-------------|
| `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/Runtime/Interpreter.cs` | Modified | Removed `_deactivatorDelegates` field; added `_registry`; updated V1 fallback and `SweepExitedNode`; deleted `BindDeactivators` method |
| `FDP/ExtDeps/FastBTree/src/Fbt.Compiler/BTreeBuilder.cs` | Modified | Added `Compile(string, Func<string,bool>?)` overload (see Deviations) |
| `FDP/Toolkits/Fdp.Toolkits.Analyzers/BTreeDefinitionGenerator.cs` | Modified | Updated `GenerateCatalog` — all generated `Get*` methods now accept `isResourceOwning = null` and forward to `.Compile(treeName, isResourceOwning)` |
| `Hrot/Subsystems/Hrot.AI.Behaviors/AiBehaviorFactory.cs` | Modified | Added `isResourceOwning` delegate after `FbtActionRegistrar.RegisterAll`; passed to all 7 `FbtTreeCatalog.Get*()` calls |
| `Hrot/Engine/Hrot.Presentation/Renderers/BTreeVisualizerRenderer.cs` | Modified | Added `ColorPurple` field; added `[R]` indicator + tooltip in `DrawNode` |
| `FDP/ExtDeps/FastBTree/tests/Fbt.Tests/Unit/InterpreterCleanupTests.cs` | New | 4 tests: T1–T4 for EQL-012 success conditions |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/BTreeTickSystemTests.cs` | Modified | Fixed pre-existing BATCH-04 regression: `PayloadIndex = 0` → `RawPayloadIndex = 0` (see Deviations) |

---

## Interpreter Changes Detail

### `_deactivatorDelegates` → `_registry`

```csharp
// BEFORE
private readonly NodeDeactivatorDelegate<TBlackboard, TContext>?[] _deactivatorDelegates;

// AFTER
private readonly ActionRegistry<TBlackboard, TContext> _registry;
```

Constructor now stores `_registry = registry;` instead of calling `BindDeactivators`.

### V1 fallback loop

```csharp
// BEFORE
if (_deactivatorDelegates.Length > pi && _deactivatorDelegates[pi] != null)
    node.SetResourceOwning();

// AFTER
if (_registry.TryGetDeactivator(_blob.MethodNames[pi], out _))
    node.SetResourceOwning();
```

### `SweepExitedNode`

```csharp
// BEFORE
if ((uint)pi < (uint)_deactivatorDelegates.Length)
{
    var deactivator = _deactivatorDelegates[pi];
    deactivator?.Invoke(ref blackboard, ref state, ref context, pi);
}

// AFTER
if ((uint)pi < (uint)_blob.MethodNames.Length)
{
    if (_registry.TryGetDeactivator(_blob.MethodNames[pi], out var deactivator))
        deactivator.Invoke(ref blackboard, ref state, ref context, pi);
}
```

`BindDeactivators` method (17 lines) was deleted entirely.

---

## Test Results

```
dotnet test FDP\ExtDeps\FastBTree\tests\Fbt.Tests\Fbt.Tests.csproj --no-build

Failed:     9  (pre-existing generator-related failures — unchanged)
Passed:   207  (203 baseline + 4 new InterpreterCleanupTests)
Skipped:    0
Total:    216
```

### New tests (all pass)

| Test | Success condition |
|------|-------------------|
| `Deactivator_FiresOnBranchSwitch_AfterRegistryCleanup` | T1 — regression: deactivator fires via registry path after array removal |
| `Interpreter_HasNo_DeactivatorDelegatesField` | T2 — reflection verifies no `NodeDeactivatorDelegate?[]` field |
| `Constructor_ZeroResourceOwningNodes_NoGcPressure` | T3 — `GC.CollectionCount(0)` unchanged for 500-node tree |
| `Deactivator_CorrectDelegateInvoked_NotOtherAction` | T4 — only actionA's deactivator fires; actionB unaffected |

### Pre-existing failures (9 — unchanged)

All 9 failures are generator-related tests that were failing before BATCH-05:
`BuilderValidationTests.DtoTooLarge_ThrowsBehaviorTreeBuildException`,
`SharedAiGeneratorTests` (3 tests), `GeneratorOutputTests` (1 test),
`AutoDiscoveryTests` (3 tests).

---

## Deviations from Instructions

### 1. Added `BTreeBuilder.Compile(string, Func<string,bool>?)` overload

**Reason:** The generated catalog code `builder.Compile("treeName", isResourceOwning)` requires
a 2-argument overload on `BTreeBuilder`. This overload was specified in EQL-010 (BATCH-04) but
was not present in the codebase. Without it, both the test file and the generated catalog code
fail to compile with CS1501.

The overload delegates to `TreeCompiler.FlattenToBlob` using the provided delegate (or falls
back to the internal registry delegate when `null` is passed), preserving backward compatibility
with the single-argument form.

**Impact:** One additional method on `BTreeBuilder`. No behavioral change to existing callers.

### 2. Fixed pre-existing `PayloadIndex` regression in `BTreeTickSystemTests.cs`

**Reason:** BATCH-04 made `NodeDefinition.PayloadIndex` a read-only computed property backed
by `RawPayloadIndex`. The test file `BTreeTickSystemTests.cs` still used `PayloadIndex = 0`
in an object initializer, which caused a CS0200 compile error that blocked `FDP.sln` from
building. This was listed as a known debt item (D-10 area) but not fixed in BATCH-04.

The fix is mechanical: `PayloadIndex = 0` → `RawPayloadIndex = 0`.

---

## Manual Verification Items (skipped per instructions)

- **T6 (factory wiring):** Load `UrbanCombat` scenario; verify `Insurgent` blob node
  `Action_AimAndFire` has `IsResourceOwning == true`. Requires running simulation.
- **T7 (hot-reload end-to-end):** Trigger hot-reload during scenario; assert deactivator
  fires via `_registry` path after ALC swap. Requires running simulation.
- **T8 (editor indicator):** Pin BTree Visualizer to Insurgent; verify `[R]` appears in
  purple on `Action_AimAndFire` and tooltip reads
  `"Resource Owning Node: Manages standing ECS resources via OnDeactivate."`.
  Visual-only; code change verified against spec (tooltip text exact match confirmed).

---

## Build Verification

```
dotnet build FDP\ExtDeps\FastBTree\FastBTree.sln --no-restore
  => Build succeeded, 1 Warning, 0 Errors

dotnet build FDP\FDP.sln --no-restore
  => Build succeeded, 1 Warning, 0 Errors
```

The 1 warning in each is the pre-existing `BTree002` / `CS8892` warning unrelated to BATCH-05.
