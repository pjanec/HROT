# BATCH-05: Phase 1 Completion -- TH-010 + CT0 P2 Fix

**Batch Number:** BATCH-05
**Tasks:** Corrective Task 0 (P2 fix from BATCH-04), TASK-TH-010
**Phase:** Phase 1 -- Test Harness (Part 3 of 3 -- Final)
**Estimated Effort:** 14-18 hours
**Priority:** HIGH
**Dependencies:** BATCH-04 committed (BlueprintTestFixture, AlcUnloadTests, CT0 P2 in place)

---

## Onboarding & Workflow

### Your Role

You are the **Developer**. Your role description is in `.github\skills\developer\SKILL.md`.
Read it before starting.

### Required Reading (IN ORDER)

1. **TASK-DETAIL.md:** `.dev/blueprints-1/TASK-DETAIL.md`
   Read the following sections in full:
   - **TASK-TH-010** -- BehaviorRegistry Wiring + InvokeBTree/Hsm Helpers + MockDispatcherSystem
2. **Test Harness Inline Patches:** `.dev/blueprints-1/Blueprint_Subsystem_Test_Harness_Detailed_Design_InlinePatches.md`
   Read from "Patch 3" onward (Patch 3 and Q-12.1 through Q-12.4 resolutions). These
   drive most of TASK-TH-010.
3. **BATCH-04 Review:** `.dev/blueprints-1/reviews/BATCH-04-REVIEW.md`
   Read the P2 defect description carefully before touching `AlcUnloadTests.cs`.
4. **DEBT-TRACKER:** `.dev/blueprints-1/DEBT-TRACKER.md`
   Note DEBT-005 (the AlcUnloadTests P2 defect), DEBT-009, and DEBT-010 (GC/ALC insights).

### Build & Test Commands

```powershell
# From repo root:
dotnet build IOS-IG-SimHost.sln
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Hrot.Blueprints.Tests.csproj
```

Ensure ALL tests continue to pass (87 pass, 5 skip -- or more pass after TH-010 new tests are
added).

### Report Submission

Submit report to: `.dev/blueprints-1/reports/BATCH-05-REPORT.md`
Questions: `.dev/blueprints-1/questions/BATCH-05-QUESTIONS.md`

---

## Test-Driven Task Progression (Mandatory Workflow)

For every Success Condition (SC) in each task:

1. **Write the test first** (it must fail or be skipped before you write production code).
2. **Write the minimum production code** to make the test pass.
3. **Verify** `dotnet test` shows the test now passes (not just "no new failures").
4. **Move to the next SC.**

Do not implement production code without a failing test driving it. Do not write a test that
passes trivially (e.g., `Assert.True(true)` or asserting a property is non-null when it is
always constructed). Tests must verify behavior/values.

---

## Corrective Task 0 -- Fix AlcUnloadTests SC3 (P2 from BATCH-04)

**File to fix:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/AlcUnloadTests.cs`

### What is wrong

The test `Fixture_AfterMultipleLoads_OldAlcsReclaimedNewestStillLive` currently only asserts
that all three loaded ALCs are live before Dispose. It does NOT test that old ALCs are reclaimed
after being unloaded. SC3 of TASK-TH-005 requires that old ALCs are verifiably dead after unload
and GC reclaim.

### What to implement

Replace the test with one that:
1. Loads 3 assemblies via `fixture.LoadTestAssemblyFromBytes(GetTestAsmBytes())`.
2. Obtains `WeakReference<AssemblyLoadContext>` refs for the first two ALCs using
   `fixture.GetAlcWeakReferences()[0]` and `[1]`.
3. Manually retrieves the actual ALC from each `WeakReference<T>` and calls `.Unload()` on them.
   IMPORTANT: Do this inside a `[MethodImpl(MethodImplOptions.NoInlining)]` helper to avoid
   Debug JIT pinning (per DEBT-009 / DEBT-010 guidance). After Unload, explicitly null out the
   ALC local variable before the helper returns.
4. After the helper returns, calls `fixture.ForceGcReclaim()`.
5. Asserts `fixture.GetAlcWeakReferences()[0].TryGetTarget(out _) == false` (first ALC reclaimed).
6. Asserts `fixture.GetAlcWeakReferences()[1].TryGetTarget(out _) == false` (second ALC reclaimed).
7. Asserts `fixture.GetAlcWeakReferences()[2].TryGetTarget(out _) == true` (third ALC still live).

The test name may remain `Fixture_AfterMultipleLoads_OldAlcsReclaimedNewestStillLive`.

**Note on why the third ALC stays live:** The fixture's `_activeAlcs` list still holds a strong
reference to the third ALC. Only the first two are unloaded manually (simulating what
`SimulateReload` will do in Phase 3). The third remains in `_activeAlcs` until `fixture.Dispose()`.

**Note:** Access to `_activeAlcs` is not directly exposed by the fixture API. Instead, obtain
the first two ALCs via `fixture.GetAlcWeakReferences()[0].TryGetTarget(out var alc1)` etc.,
then call `alc1!.Unload()`. The fixture's `_activeAlcs` field keeps the third one alive.

### Success condition

After the fix, `dotnet test --filter "Fixture_AfterMultipleLoads"` passes with the test
properly verifying that old ALCs are reclaimed and the newest is still live.

---

## TASK-TH-010 -- BehaviorRegistry Wiring + InvokeBTree/Hsm Helpers + MockDispatcherSystem

**Reference:** TASK-DETAIL.md section TASK-TH-010 (read it in full before implementing).

### Context

TASK-TH-010 extends `BlueprintTestFixture` with two engine-level dispatcher handles
(`BehaviorRegistry`, `HsmActionDispatcher`), adds the `Invoke*` helpers for driving compiled
Blueprint code from tests (stubs in Phase 1, real in Phase 3), and adds a
`MockDispatcherSystem<TChannel>` base class plus three concrete mock dispatchers.

### Files to create

```
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/MockSystems/MockDispatcherSystem.cs
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/MockSystems/MockLocomotionDispatcher.cs
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/MockSystems/MockWeaponDispatcher.cs
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/MockSystems/MockInteractionDispatcher.cs
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/MockDispatcherSystemTests.cs
```

### Files to modify

```
Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/BlueprintTestFixture.cs
```

### Key existing engine types (read before implementing)

```
FDP/Toolkits/Fdp.Toolkits/Behavior/BehaviorRegistry.cs         -- real registry
FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmActionDispatcher.cs     -- real dispatcher
FDP/Toolkits/Fdp.Toolkits/Behavior/Components/ChannelComponents.cs -- LocomotionChannel etc.
```

Read these before writing code to understand the actual APIs. Use the real types -- do NOT
create stub replacements.

### Step 1 -- Extend BlueprintTestFixture

Add two properties to `BlueprintTestFixture`:

```csharp
public BehaviorRegistry BehaviorRegistry { get; }
public HsmActionDispatcher HsmDispatcher { get; }
```

Both initialized in the constructor:
```csharp
BehaviorRegistry = new BehaviorRegistry();
HsmDispatcher = HsmActionDispatcher.Instance;  // or however the real type is obtained
```

Check how `HsmActionDispatcher` is instantiated by looking at `HsmActionDispatcher.cs`
and existing callers in the codebase. If it is a singleton (`.Instance` or `.Default`),
use that. If it is constructed directly, construct it.

**Dispose change:** Call `HsmDispatcher.ClearAll()` BEFORE unloading ALCs:
```csharp
public void Dispose()
{
    HsmDispatcher.ClearAll();    // clear stale function pointers FIRST
    UnloadAndClearAlcs();        // then unload ALCs
    // ... rest unchanged ...
}
```

**InvokeRegistrarMethod change:** Update `DiscoverAndInvokeRegistrars` (currently the
`ResolveRegistrarParam` helper) to also resolve `BehaviorRegistry` and `HsmActionDispatcher`
parameters:
```csharp
private object? ResolveRegistrarParam(Type t, BlueprintRegistryStaging staging)
{
    if (t == typeof(BlueprintRegistryStaging)) return staging;
    if (t == typeof(BlueprintRegistry))        return Registry;
    if (t == typeof(BehaviorRegistry))         return BehaviorRegistry;
    if (t == typeof(HsmActionDispatcher))      return HsmDispatcher;
    throw new InvalidOperationException($"Unknown registrar parameter type: {t.FullName}");
}
```

### Step 2 -- Invoke helpers (stubs)

Add to `BlueprintTestFixture`:

```csharp
// Phase 1: stubs -- throw NotImplementedException until Phase 3 compiler is in place.

public NodeStatus InvokeBTreeAction(BlueprintAsset asset, Entity entity, int paramIndex = 0)
    => throw new NotImplementedException("Requires compiled blueprint assembly (Phase 3).");

public unsafe bool InvokeHsmAction(BlueprintAsset asset, Entity entity)
    => throw new NotImplementedException("Requires compiled blueprint assembly (Phase 3).");

public unsafe bool InvokeHsmGuard(BlueprintAsset asset, Entity entity, ushort eventId = 0)
    => throw new NotImplementedException("Requires compiled blueprint assembly (Phase 3).");
```

These stubs satisfy the interface contract. Phase 3 will replace them with real thunk resolution
using `_repo.UnmanagedHandle` per Patch 3 (Test Harness Inline Patches doc).

### Step 3 -- MockDispatcherSystem<TChannel>

Create `MockDispatcherSystem.cs` in `MockSystems/`:

```csharp
namespace Hrot.Blueprints.Tests.MockSystems;

public abstract class MockDispatcherSystem<TChannel> : IEcsModuleSystem, IProfiledSystem
    where TChannel : unmanaged
{
    public string ProfileName => $"Mock{typeof(TChannel).Name}Dispatcher";

    protected EntityRepository? Repo { get; private set; }
    private IEntityQuery? _query;

    public void Execute(ISimulationView view)
    {
        Repo = (EntityRepository)view;
        _query ??= Repo.Query().With<TChannel>().Build();

        foreach (var entity in _query)
        {
            ref var channel = ref Repo.GetComponentRW<TChannel>(entity);
            HandleChannel(ref channel, entity, view);
        }
    }

    protected abstract void HandleChannel(ref TChannel channel, Entity entity, ISimulationView view);
}
```

Check `IProfiledSystem`'s exact API by reading an existing implementor
(e.g., `MovingEntitySystem` or any other system file in the codebase).

### Step 4 -- Concrete mock dispatchers

Create the three concrete dispatchers in separate files in `MockSystems/`:

- `MockLocomotionDispatcher : MockDispatcherSystem<LocomotionChannel>`
- `MockWeaponDispatcher : MockDispatcherSystem<WeaponChannel>` (check `ChannelComponents.cs` for exact type name)
- `MockInteractionDispatcher : MockDispatcherSystem<InteractionChannel>` (check `ChannelComponents.cs` for exact type name)

Each concrete dispatcher has:
```csharp
public Func<TChannel, NodeStatus> NextStatus { get; set; } = _ => NodeStatus.Success;
public int InvokeCount { get; private set; }
public int LastObservedActionInstanceId { get; private set; }

protected override void HandleChannel(ref TChannel channel, Entity entity, ISimulationView view)
{
    // Only process when there is an active action (non-zero ActiveAction field)
    if (channel.ActiveAction != 0)
    {
        InvokeCount++;
        LastObservedActionInstanceId = (int)channel.ActionInstanceId;
        channel.Status = NextStatus(channel);
    }
}
```

**Important:** Check `LocomotionChannel`, `WeaponChannel`, and `InteractionChannel` in
`ChannelComponents.cs` for the exact field names (`ActiveAction`, `ActionInstanceId`, `Status`).
Adjust if the actual field names differ -- do not assume.

If `WeaponChannel` or `InteractionChannel` do not exist in the engine, place placeholder
struct definitions in a `MockSystems/Placeholders.cs` file with a `// TODO: replace with
real engine type` comment, and note the deviation in your report.

### Step 5 -- MockDispatcherSystemTests

Create `MockDispatcherSystemTests.cs` with **exactly 3 tests** (per SC5):

1. **Construction test (SC1 equivalent):** `new BlueprintTestFixture()` -- assert
   `fixture.BehaviorRegistry != null`, `fixture.HsmDispatcher != null`.

2. **Invocation test (SC3 equivalent):** Create `MockLocomotionDispatcher`, add to fixture,
   create entity, add `LocomotionChannel { ActiveAction = 1 }` component, call
   `fixture.TickFrame(0.016f)`, assert `dispatcher.InvokeCount == 1`.

3. **Status control test (SC4 equivalent):** Set `dispatcher.NextStatus = _ => NodeStatus.Running`,
   call `fixture.TickFrame(0.016f)`, assert channel's `Status == NodeStatus.Running`.

Tests 2 and 3 can use the same fixture and entity (or set up separately -- whichever is cleaner).

### Success Conditions (verbatim from TASK-DETAIL.md TASK-TH-010)

- SC1: `fixture.BehaviorRegistry != null`, `fixture.HsmDispatcher != null`.
- SC2: Dispose a fixture with one loaded ALC -- `HsmDispatcher.ClearAll()` called before unload.
  (This is implied by the Dispose change -- verify in `Dispose_WithNoAlcsLoaded_Succeeds` or add
  a specific test if the order cannot be observed without a spy.)
- SC3: Add `MockLocomotionDispatcher`, create entity with `LocomotionChannel { ActiveAction = 1 }`,
  call `TickFrame`. Assert `dispatcher.InvokeCount == 1`.
- SC4: `dispatcher.NextStatus = _ => NodeStatus.Running`. After TickFrame, channel's
  `Status == NodeStatus.Running`.
- SC5: `dotnet test --filter "FullyQualifiedName~MockDispatcherSystemTests"` all 3 tests pass.
- SC6: `dotnet build` succeeds with zero errors.

---

## Developer Insights (Questions to Answer in Report)

1. What is the exact API for `HsmActionDispatcher`? Is it a singleton, `new`'d, or obtained via
   static property? How does `ClearAll()` work?
2. Do `WeaponChannel` and `InteractionChannel` exist in `ChannelComponents.cs`? If not, what
   channel types exist?
3. What are the exact field names on `LocomotionChannel` for `ActiveAction`, `ActionInstanceId`,
   `Status`?
4. Does `IProfiledSystem` require any other methods beyond `ProfileName`?
5. Were there any build or test failures during development? How were they resolved?
6. What design decisions did you make that were not explicitly specified?

---

## Report Format

Submit `.dev/blueprints-1/reports/BATCH-05-REPORT.md` with:

```
# BATCH-05-REPORT

## Tasks Completed
[table]

## 1. Corrective Task 0 -- AlcUnloadTests SC3 Fix
[description of change, test outcome]

## 2. TASK-TH-010
### 2a. BlueprintTestFixture Extensions
[files modified, key changes]
### 2b. MockDispatcherSystem + Concrete Dispatchers
[files created, deviations if channel types not found]
### 2c. MockDispatcherSystemTests
[test results table]

## 3. Build Status
[dotnet build output summary]

## 4. Test Summary
[dotnet test output -- pass/skip/fail counts, full table breakdown]

## 5. Developer Insights
[answers to the 6 questions above]

## 6. Deviations from Instructions
[any deviation with reason]
```
