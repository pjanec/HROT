# BATCH-03 Report: Engine Integration — Deactivators for Channel and EQS Cleanup

**Batch:** BATCH-03
**Tasks:** TASK-EQL-005, TASK-EQL-006, TASK-EQL-007, TASK-EQL-008
**Status:** COMPLETE

---

## Files Created / Modified

### Implementation files (modified)

| File | Change |
|---|---|
| `FDP/Examples/Fdp.Examples.UrbanCombat/Brains/InsurgentNodes.cs` | Added `Deactivate_AimAndFire` static method (EQL-005) |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackTankNodes.cs` | Added `Deactivate_CreepToAndBeyondSlot` and `Deactivate_AimAndFireSpecific` methods (EQL-006, EQL-007) |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HillAttackCommanderNodes.cs` | Added `Deactivate_RequestAreaQuery` method (EQL-008) |

### Test files (created)

| File | Coverage |
|---|---|
| `FDP/Examples/Fdp.Examples.UrbanCombat.Tests/InsurgentNodesDeactivatorTests.cs` | EQL-005 — 4 tests |
| `Hrot/Subsystems/Hrot.IG.Tests/Brains/HillAttackTankNodesDeactivatorTests.cs` | EQL-006 — 3 tests, EQL-007 — 4 tests (two test classes in one file) |
| `Hrot/Subsystems/Hrot.IG.Tests/Brains/HillAttackCommanderNodesDeactivatorTests.cs` | EQL-008 — 3 tests |

---

## Test Results

### EQL-005 — InsurgentNodesDeactivatorTests

```
dotnet test FDP\Examples\Fdp.Examples.UrbanCombat.Tests\Fdp.Examples.UrbanCombat.Tests.csproj --filter "InsurgentNodesDeactivator"
Passed!  - Failed: 0, Passed: 4, Skipped: 0, Total: 4
```

### EQL-006 / EQL-007 / EQL-008 — Hrot deactivator tests

```
dotnet test Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj --filter "Deactivator"
Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10
```

### Full baseline (no regressions)

```
dotnet test FDP\Examples\Fdp.Examples.UrbanCombat.Tests\Fdp.Examples.UrbanCombat.Tests.csproj
Passed!  - Failed: 0, Passed: 29, Skipped: 0, Total: 29

dotnet test Hrot\Subsystems\Hrot.IG.Tests\Hrot.IG.Tests.csproj
Failed: 69, Passed: 330  (same 69 pre-existing failures as before this batch; all Deactivator tests passed)
```

The 69 pre-existing failures in Hrot.IG.Tests are unrelated infrastructure failures (IgApplication bootstrap, GizmoRegistrar, etc.) that existed before BATCH-03.

---

## Generated `RegisterDeactivator` Lines

From `Hrot\Subsystems\Hrot.AI.Behaviors\obj\GeneratedFiles\Fdp.Toolkits.Analyzers\Fdp.Toolkit.Behavior.Analyzers.BTreeActionGenerator\FbtActionRegistrar.g.cs`:

```csharp
registry.RegisterDeactivator("Hrot.AI.Behaviors.Brains.HillAttackCommanderNodes.Action_RequestAreaQuery@0", global::Hrot.AI.Behaviors.Brains.HillAttackCommanderNodes.Deactivate_RequestAreaQuery);
registry.RegisterDeactivator("Hrot.AI.Behaviors.Brains.HillAttackTankNodes.Action_CreepToAndBeyondSlot@0", global::Hrot.AI.Behaviors.Brains.HillAttackTankNodes.Deactivate_CreepToAndBeyondSlot);
registry.RegisterDeactivator("Hrot.AI.Behaviors.Brains.HillAttackTankNodes.Action_AimAndFireSpecific@0", global::Hrot.AI.Behaviors.Brains.HillAttackTankNodes.Deactivate_AimAndFireSpecific);
```

All three Hrot deactivators (EQL-006, EQL-007, EQL-008) were emitted correctly with `@0` compound keys.

---

## Deviation: EQL-005 Generator Not Emitting RegisterDeactivator

**Observation:** The Roslyn generator does NOT emit a `RegisterDeactivator` line for `InsurgentNodes.Deactivate_AimAndFire` in the `Fdp.Examples.UrbanCombat` assembly.

**Root cause:** The generator's `Execute` method has an early-return guard:
```csharp
if (registrable.Count == 0 && reusable.Count == 0 && sharedAiMethods.Count == 0) return;
```
`InsurgentNodes` methods (`Action_AimAndFire`, `Condition_HasTarget`, `Action_HoldPosition`) are 4-param methods that are **not** annotated with `[BTreeAction]` or `[BTreeCondition]`. They are manually registered in `HeadlessDemoApp.cs`. Because no registrable/reusable/sharedAi methods exist in the compilation, the generator returns early and no `FbtActionRegistrar.g.cs` is produced for the `Fdp.Examples.UrbanCombat` assembly.

**Impact:** The `Deactivate_AimAndFire` method exists, compiles, and functions correctly when called directly. The unit tests (T1–T4) validate the method behavior. For runtime use, the deactivator would need to be manually registered in `HeadlessDemoApp.cs` (e.g., `ambushReg.RegisterDeactivator(...)`) — this is out of scope per TASK-EQL-005 ("Not in scope: Changes to AiBehaviorFactory").

**Build verification:** `dotnet build FDP\Examples\Fdp.Examples.UrbanCombat\Fdp.Examples.UrbanCombat.csproj` succeeds with 0 errors and 0 warnings. The build for `Fdp.Examples.UrbanCombat.Tests` also succeeds with 0 errors and 0 warnings.

---

## Blockers

None. All four tasks were implemented successfully.

---

## Summary

- All 4 deactivator methods implemented per spec
- All 14 tests pass (4 for EQL-005, 3 for EQL-006, 4 for EQL-007, 3 for EQL-008)
- Build passes for all target projects with 0 errors and 0 warnings
- EQL-006, EQL-007, EQL-008: generator correctly emits `RegisterDeactivator` calls with `@0` compound keys
- EQL-005: deactivator method implemented correctly; generator limitation prevents auto-registration (deviation noted above; not in scope per spec)
