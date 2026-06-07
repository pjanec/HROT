# BATCH-05-REPORT

**Batch:** BATCH-05 -- Phase 1 Completion: TH-010 + CT0 P2 Fix
**Developer:** AI Developer Agent
**Status:** COMPLETE
**Build:** PASS (0 errors)
**Tests:** 90 pass, 5 skip, 0 fail (was 87 pass before this batch)

---

## Tasks Completed

| Task | Description | Status |
|------|-------------|--------|
| CT0 (P2 fix) | Fix `Fixture_AfterMultipleLoads_OldAlcsReclaimedNewestStillLive` to actually verify ALC reclaim | DONE |
| TASK-TH-010 SC1 | `BehaviorRegistry` property on `BlueprintTestFixture`, non-null after construction | DONE |
| TASK-TH-010 SC2 | `HsmActionDispatcher.ClearAll()` called in `Dispose()` before ALC unload | DONE |
| TASK-TH-010 SC3 | `MockLocomotionDispatcher` counts invocations on entities with `ActiveAction != 0` | DONE |
| TASK-TH-010 SC4 | `NextStatus` lambda controls channel `Status` written back | DONE |
| TASK-TH-010 SC5 | `MockDispatcherSystemTests` -- 3 tests pass | DONE |
| TASK-TH-010 SC6 | `dotnet build` succeeds with zero errors | DONE |

---

## 1. Corrective Task 0 -- AlcUnloadTests SC3 Fix

**File modified:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/AlcUnloadTests.cs`

**Root cause of original failure:** The old test body only asserted that all three ALCs are live before Dispose. It never called `Unload()` on any ALC, never called `ForceGcReclaim()`, and never asserted that old ALCs are dead. It was the inverse of what SC3 requires.

**Additional fixture method added:** `internal void UnloadAndReleaseAlc(AssemblyLoadContext alc)` on `BlueprintTestFixture`. This removes an ALC from `_activeAlcs` and calls `.Unload()`, mirroring what `SimulateReload` does for old-generation ALCs. This was strictly necessary because `_activeAlcs` holds a strong managed reference to the ALC, preventing GC collection even after `Unload()` is called on it.

**Key design insight discovered during implementation (DEBT-011):** Even with the ALC removed from `_activeAlcs`, the test was still failing because the `Assembly` temporaries returned by `fixture.LoadTestAssemblyFromBytes(bytes)` (discarded in the test method) were kept alive by the Debug JIT as implicit stack locals for the entire test-method scope. An `Assembly` object holds a reference to its owning ALC, so the ALC could not be collected. Fix: moved `LoadTestAssemblyFromBytes` calls into a separate `[MethodImpl(MethodImplOptions.NoInlining)]` helper (`LoadThreeGenerations`). After that helper returns, the Assembly temporaries are off-stack.

**New test structure:**
1. `LoadThreeGenerations(fixture)` -- `[NoInlining]` helper isolates Assembly temporaries
2. `UnloadFirstTwoAlcs(fixture)` -- `[NoInlining]` helper removes alc1/alc2 from `_activeAlcs` and unloads them, nulls locals
3. `fixture.ForceGcReclaim()` -- drives GC
4. Assert alc0 dead, alc1 dead, alc2 alive

**Test outcome:** PASS. The test now correctly verifies that old ALCs are GC-reclaimed after unload and that the newest (still in `_activeAlcs`) remains live.

---

## 2. TASK-TH-010

### 2a. BlueprintTestFixture Extensions

**File modified:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs`

**Changes:**

| Change | Description |
|--------|-------------|
| `using Fdp.Toolkit.Behavior;` | Import for `BehaviorRegistry` type |
| `using Fhsm.Kernel;` | Import for `HsmActionDispatcher` static class |
| `public BehaviorRegistry BehaviorRegistry { get; }` | New property, initialized with `new BehaviorRegistry()` |
| `HsmActionDispatcher.ClearAll()` in `Dispose()` | Called before `UnloadAndClearAlcs()` per Q-12.1 resolution |
| `ResolveRegistrarParam` update | Added `typeof(BehaviorRegistry)` case |
| `InvokeBTreeAction` stub | Returns `NodeStatus`, throws `NotImplementedException` |
| `InvokeHsmAction` stub | `unsafe`, throws `NotImplementedException` |
| `InvokeHsmGuard` stub | `unsafe`, throws `NotImplementedException` |
| Aux sys loop in `TickFrame` | Changed `sys.Execute(View, deltaTime)` to `sys.Execute(_repo, deltaTime)` |
| `internal void UnloadAndReleaseAlc(...)` | Added for CT0 P2 fix (see Section 1) |

**Deviation -- `HsmDispatcher` property not added:** `HsmActionDispatcher` is a `static class` (not an instance type). A property of type `static class` is illegal in C#. The design documents anticipated it would be a singleton instance type. The `ClearAll()` call in `Dispose()` is implemented directly as `HsmActionDispatcher.ClearAll()` (static call). The `ResolveRegistrarParam` case for `HsmActionDispatcher` is also skipped (no object to pass). SC1 test verifies `BehaviorRegistry != null` only; the HsmDispatcher part is not testable via a property assertion.

**Deviation -- aux systems receive `_repo` instead of `View`:** `MockDispatcherSystem<TChannel>.Execute` casts `ISimulationView` to `EntityRepository` for `GetComponentRW<T>` write access. `View` is `MockSimulationView` which is NOT `EntityRepository`, so the cast would fail. `_repo` is an `EntityRepository` that also implements `ISimulationView`, making the cast valid. The `CountingSystem` test is unaffected (it does not use the view parameter). This change matches the constraint in TASK-TH-010: "MockDispatcherSystem casts ISimulationView to EntityRepository for writable ref access."

### 2b. MockDispatcherSystem + Concrete Dispatchers

**Files created:**

| File | Description |
|------|-------------|
| `MockSystems/MockDispatcherSystem.cs` | Abstract base, `Execute(ISimulationView, float)` casts to `EntityRepository`, lazily builds `EntityQuery`, iterates entities, calls `HandleChannel` |
| `MockSystems/MockLocomotionDispatcher.cs` | Concrete for `LocomotionChannel` |
| `MockSystems/MockWeaponDispatcher.cs` | Concrete for `WeaponChannel` |
| `MockSystems/MockInteractionDispatcher.cs` | Concrete for `InteractionChannel` |

**Namespace:** `Hrot.Blueprints.Tests.MockSystems`

**Channel types found:** All three channel types exist in `Fdp.Toolkit.Behavior.Components`:
- `LocomotionChannel` -- `ActiveAction (ushort)`, `ActionInstanceId (uint)`, `Status (Fbt.NodeStatus)`, `[ComponentId(GlobalComponentIds.LocomotionChannel)]`
- `WeaponChannel` -- same field structure, `[ComponentId(GlobalComponentIds.WeaponChannel)]`
- `InteractionChannel` -- same field structure, `[ComponentId(GlobalComponentIds.InteractionChannel)]`

No placeholder structs needed. `MockSystems/Placeholders.cs` was NOT created.

**Design deviation -- `IEntityQuery` does not exist:** The design referenced `IEntityQuery` as a field type. The actual engine uses the concrete `EntityQuery` class with no interface. Changed field type to `EntityQuery?`.

**Design deviation -- `Execute` signature:** The design doc shows `Execute(ISimulationView view)` with no `deltaTime`. The real `IEcsModuleSystem` interface is `Execute(ISimulationView view, float deltaTime)`. The concrete implementation uses the correct interface signature.

### 2c. MockDispatcherSystemTests

**File created:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/MockDispatcherSystemTests.cs`

| Test | Description | Result |
|------|-------------|--------|
| `Fixture_HasBehaviorRegistry` | `new BlueprintTestFixture()` -- `fixture.BehaviorRegistry != null` | PASS |
| `MockLocomotionDispatcher_WhenEntityHasActiveAction_IncreasesInvokeCount` | Entity with `ActiveAction=1`, TickFrame -- `dispatcher.InvokeCount == 1` | PASS |
| `MockLocomotionDispatcher_NextStatusLambda_WritesStatusToChannel` | `NextStatus = _ => NodeStatus.Running`, TickFrame -- channel `Status == NodeStatus.Running` | PASS |

**Note:** `LocomotionChannel` must be registered with the `EntityRepository` before use (`fixture.World.RegisterComponent<LocomotionChannel>()`). This is done inline in each test that needs it.

---

## 3. Build Status

`dotnet build IOS-IG-SimHost.sln` -- **succeeded with 0 errors, 0 warnings** (in tested projects).

---

## 4. Test Summary

`dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj`

| Metric | Count |
|--------|-------|
| Total | 95 |
| Passed | 90 |
| Skipped | 5 |
| Failed | 0 |

**3 new tests added (TASK-TH-010):**
- `MockDispatcherSystemTests.Fixture_HasBehaviorRegistry`
- `MockDispatcherSystemTests.MockLocomotionDispatcher_WhenEntityHasActiveAction_IncreasesInvokeCount`
- `MockDispatcherSystemTests.MockLocomotionDispatcher_NextStatusLambda_WritesStatusToChannel`

**Previously failing test now passing (CT0 P2):**
- `AlcUnloadTests.Fixture_AfterMultipleLoads_OldAlcsReclaimedNewestStillLive`

**Previously passing tests (87): all still pass.**

Skipped tests (5, unchanged):
- `TierUpgrade_HappensInBeforeSync_NotInSimulation` (Requires BlueprintMaintenanceSystem)
- `Debug_Breakpoint_FiresWhenNodeEntered` (Requires Phase 3 compiler)
- `Debug_TraceMode_RecordsAllNodeEntries` (Requires Phase 3 compiler)
- `AttachBlueprint_RegisteredAsset_SetsHasSlot` (Requires Phase 3 compiler)
- `CompileAndLoad_IncrementsAlcWeakReferences` (Requires Phase 3 compiler)

---

## 5. Developer Insights

**1. What is the exact API for `HsmActionDispatcher`? Singleton, `new`'d, or static property? How does `ClearAll()` work?**

`HsmActionDispatcher` is a **static class** (`public static unsafe class HsmActionDispatcher`). It has NO instance, no `.Instance` property, no constructor. All members are static. `ClearAll()` calls `ActionTable.Clear()` and `GuardTable.Clear()` -- two private static `Dictionary<ushort, IntPtr>` fields. The design documents incorrectly anticipated it would be an instance singleton with `.Instance`. Consequence: the `public HsmActionDispatcher HsmDispatcher { get; }` property specified in the design cannot be implemented (static classes are not valid property types). Workaround: call `HsmActionDispatcher.ClearAll()` directly in `Dispose()`.

**2. Do `WeaponChannel` and `InteractionChannel` exist in `ChannelComponents.cs`?**

Yes. All three channel types exist in `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/ChannelComponents.cs`:
- `LocomotionChannel` with `[ComponentId(GlobalComponentIds.LocomotionChannel)]`
- `WeaponChannel` with `[ComponentId(GlobalComponentIds.WeaponChannel)]`
- `InteractionChannel` with `[ComponentId(GlobalComponentIds.InteractionChannel)]`

No placeholder file was needed.

**3. What are the exact field names on `LocomotionChannel` for `ActiveAction`, `ActionInstanceId`, `Status`?**

The field names match the spec exactly:
- `public ushort ActiveAction;`
- `public uint ActionInstanceId;` (note: `uint`, not `int` -- `LastObservedActionInstanceId` in dispatcher uses `(int)channel.ActionInstanceId` cast)
- `public NodeStatus Status;` where `NodeStatus` is `Fbt.NodeStatus` (byte-sized enum: `Success=0`, `Failure=1`, `Running=2`)

All three channel types share identical field names and types.

**4. Does `IProfiledSystem` require any other methods beyond `ProfileName`?**

No. `IProfiledSystem` has exactly one member: `string ProfileName { get; }`. The interface is in `Fdp.ModuleHost.Abstractions`.

**5. Were there any build or test failures during development? How were they resolved?**

Three issues encountered:

- **CS0246 `IEntityQuery` not found:** The design referenced `IEntityQuery` as an interface, but the engine uses the concrete `EntityQuery` class with no interface. Fixed by changing the field type from `IEntityQuery?` to `EntityQuery?`.
- **ALC reclaim test failure (first attempt):** `Fixture_AfterMultipleLoads_OldAlcsReclaimedNewestStillLive` failed because removing the ALC from `_activeAlcs` was not enough -- the Debug JIT kept `Assembly` temporaries (from discarded return values of `LoadTestAssemblyFromBytes`) alive as implicit locals in the test-method frame. These Assembly objects hold a strong reference to their owning ALC. Fixed by moving the `LoadTestAssemblyFromBytes` calls into a `[NoInlining]` helper (`LoadThreeGenerations`).
- **ALC reclaim test failure (second attempt -- `_activeAlcs` strong ref):** Before adding `UnloadAndReleaseAlc`, the original approach called `alc.Unload()` directly but `_activeAlcs` in the fixture still held a strong managed reference to alc1/alc2, preventing GC collection. Fixed by adding `internal void UnloadAndReleaseAlc(AssemblyLoadContext alc)` to `BlueprintTestFixture`, which removes the ALC from `_activeAlcs` before calling `Unload()`.

**6. What design decisions did you make that were not explicitly specified?**

- **`UnloadAndReleaseAlc` added to fixture:** The design said to call `alc.Unload()` directly but did not account for `_activeAlcs` holding a strong reference. Added an `internal` method that removes from `_activeAlcs` and calls `Unload()`, mirroring what `SimulateReload` does for old-generation ALCs. This is a necessary implementation detail, not a spec change.
- **`LoadThreeGenerations` helper in AlcUnloadTests:** Not specified in the batch instructions, but necessary to prevent Debug-JIT pinning of Assembly temporaries (a deeper application of DEBT-009 beyond ALC locals -- it applies to Assembly references too).
- **Aux systems receive `_repo` instead of `View` in `TickFrame`:** Changed from `sys.Execute(View, deltaTime)` to `sys.Execute(_repo, deltaTime)`. This is required for `MockDispatcherSystem.Execute` to successfully cast `ISimulationView` to `EntityRepository`. The design constraint ("casts ISimulationView to EntityRepository") implicitly required this change. `CountingSystem` and all existing tests are unaffected.
- **`DEBT-011` candidate (new insight):** Assembly objects returned by `LoadTestAssemblyFromBytes` (even when discarded) can be kept alive by Debug-JIT as implicit stack locals for the entire calling method's scope, preventing ALC GC collection just like ALC locals. The fix is the same: isolate ALL ALC-related operations (including loading) in `[NoInlining]` helpers. This extends DEBT-009 from "ALC locals" to "Assembly locals and discarded return values."
