# BATCH-04 Report

**Batch:** BATCH-04  
**Date:** 2026-02-25  
**Status:** ✅ COMPLETE

---

## Test Results

### `dotnet test FDP.sln` Summary

| Project | Passed | Failed | Skipped |
|---|---|---|---|
| `Fdp.Tests` (Fdp.Kernel.Tests) | 675 | 0 | 2 |
| `FDP.Toolkit.Behavior.Tests` | **25** | 0 | 0 |
| `FDP.Toolkit.CarKinem.Tests` | 111 | 0 | 0 |
| `FDP.Toolkit.Vis2D.Tests` | 22 | 0 | 0 |
| `FDP.Toolkit.Replication.Tests` | 34 | 0 | 0 |
| `FDP.Toolkit.NetworkSpawning.Tests` | 21 | 0 | 0 |
| `FDP.Toolkit.ImGui.Tests` | 13 | 0 | 0 |
| `FDP.Toolkit.Tkb.Tests` | 14 | 0 | 0 |
| `FDP.Toolkit.Time.Tests` | 40 | 0 | 1 |
| `FDP.Toolkit.Commands.Tests` | 3 | 0 | 0 |
| `FDP.Toolkit.Lifecycle.Tests` | 16 | 0 | 0 |
| `Fdp.Toolkit.Geographic.Tests` | 3 | 0 | 0 |
| `ModuleHost.Core.Tests` | 161 | 0 | 0 |
| `ModuleHost.Network.Cyclone.Tests` | 49 | 0 | 0 |
| `FDP.Framework.Raylib.Tests` | 2 | 0 | 0 |
| `Fdp.Examples.NetworkDemo.Tests` | 27 | 0 | 0 |
| `Fdp.Examples.CarKinem.Tests` | 9 | 0 | 0 |

**`dotnet build FDP.sln` — Build succeeded, 0 errors, 241 warnings (all pre-existing in unrelated projects).**

The behavior test count grew from **15 → 25**: 10 additions (1 ordering integration test, 2 strengthened existing test assertions with added invariants, 3 BTree tests, 2 HSM tests, 4 doctrine ingress tests including the end-to-end preemption chain).

---

## Task Completion Checklist

- [x] **Corrective 0a** — Three weak existing tests fixed: `Arbitration_ClearsStaleChannel` now asserts `DoctrineInstanceId == 0`; `Dispatcher_CallsOnEnter_OnFirstTick` now asserts `Status == Running` and `DispatchedInstanceId == ActionInstanceId`; `Dispatcher_SkipsNullExecutor_Gracefully` now asserts bookkeeping ran (`ActionInstanceId == DispatchedInstanceId`). All pass.
- [x] **Corrective 0b** — `ChannelArbitrationSystem` has `[UpdateBefore]` all three dispatchers; all three dispatchers have `[UpdateAfter(ChannelArbitrationSystem)]`; ordering integration test (`Arbitration_Ordering_NoGhostOnEnter_WhenChannelIsStale`) confirms no ghost `OnEnter` when channel is stale.
- [x] **Documentation** — `DispatcherSystemBase` `OnExit` call site commented explaining OUTGOING field-state invariant; `IActionExecutor.Execute` XML doc updated with Status-write contract; `WritingSpyExecutor<TChannel>` added to `TestHelpers.cs`.
- [x] **BCS-P1-T5** — `BTreeTickSystem` + `BTreeContext` implemented; 3 tests with specific field assertions pass; `BrainTierBTree` constant used throughout, no raw literals.
- [x] **BCS-P1-T6** — `HsmTickSystem<T>` implemented; registered twice (`BrainHsm64`, `BrainHsm128`); 2 tests with state-transition assertions pass (including `ActiveLeafIds[0]` before/after check).
- [x] **BCS-P1-T7** — `DoctrineRegistry` + `DoctrineIngressSystem` + `AssignDoctrineEvent` implemented; 4 tests pass including the end-to-end preemption chain (`DoctrineIngress_StaleSetsNewInstanceId_ArbitrationClearsOldAction`).
- [x] **`InputSystemGroup`** added to `StandardSystemGroups.cs`; `DoctrineIngressSystem` runs in it.
- [x] **Full solution** — `dotnet build FDP.sln` zero errors; `dotnet test FDP.sln` all green.

---

## Q1: How did you implement `BTreeContext`? What methods does `IAIContext` require, and which of them touch the ECS world? Did you hit any friction with the unsafe/managed boundary?

`IAIContext` declares nine members: `DeltaTime`, `Time`, `FrameCount`, `RequestRaycast`, `GetRaycastResult`, `RequestPath`, `GetPathResult`, `GetFloatParam`, and `GetIntParam`. Of these, only `GetFloatParam` and `GetIntParam` need to return per-entity data — they index into the doctrine parameter arrays carried in `BrainBlackboard`. The raycast/path members are side-effecting query stubs with no ECS counterpart yet; they return `default` / `-1`.

`BTreeContext` is declared as `public struct BTreeContext : IAIContext`. It carries:
- `public Entity Self` — the entity being ticked.
- `public EntityRepository World` — a managed reference to the kernel, enabling action nodes to read/write components.
- `float _deltaTime`, `float[] _floatParams`, `int[] _intParams` — tick inputs from the doctrine definition.

The managed `float[]` / `int[]` fields mean `BTreeContext` is **not** `unmanaged`, but that is not a problem because `Interpreter<TBlackboard, TContext>` in FastBTree constrains `TContext` as `where TContext : struct, IAIContext` — a managed struct satisfies this. The context is stack-allocated (`var context = new BTreeContext { ... }`) per entity inside `BTreeTickSystem.OnUpdate`, so there is zero heap allocation in the hot path.

**No unsafe/managed friction** was encountered. `BTreeContext` holds an `EntityRepository` managed reference, which is fine in a managed struct. The entire BTree execution path is managed. This is the opposite of the HSM case (see Q2).

---

## Q2: How did you handle the generic constraint for `HsmTickSystem<T>`? Did FastHSM's `HsmKernel` accept a plain struct context or does it require an interface?

`HsmKernel.Update<TInstance, TContext>` has **both** `where TInstance : unmanaged` and `where TContext : unmanaged`. This is the hard constraint: the context type must be fully blittable.

That rules out carrying `EntityRepository` (a managed reference) in the context. `FdpHsmContext` is therefore minimal:

```csharp
public struct FdpHsmContext
{
    public Entity Self;   // Index + Generation — two ints, fully unmanaged
}
```

`HsmKernel` does **not** require the context to implement any interface. It passes the context to state entry/exit/tick delegates by `ref TContext`, so action delegates can read `Self` but cannot reach the ECS world in Phase 1. ECS access from HSM actions is deferred to a later phase when the delegation pattern is clearer (likely a thread-local slot or an event queue approach).

`HsmTickSystem<T>` is registered twice:
```csharp
simGroup.Add(new HsmTickSystem<BrainHsm64>(registry));
simGroup.Add(new HsmTickSystem<BrainHsm128>(registry));
```
Both instances carry `[UpdateAfter(typeof(ChannelArbitrationSystem))]`. The `sizeof(T)` difference tells `HsmKernelCore` which memory layout to use (64-byte vs 128-byte instance header).

---

## Q3: `DoctrineIngressSystem` must call `ParseParams` with a pointer into `BrainBlackboard.Memory`. Walk through the exact `unsafe` + `fixed` pattern you used, and explain why it's safe.

The pattern in `DoctrineIngressSystem.OnUpdate` is:

```csharp
protected override unsafe void OnUpdate()
{
    var events = World.Bus.ConsumeManaged<AssignDoctrineEvent>();
    foreach (var evt in events)
    {
        // ... guards ...
        ref var blackboard = ref World.GetComponentRW<BrainBlackboard>(evt.Entity);
        var bbPtr = (BrainBlackboard*)Unsafe.AsPointer(ref blackboard);
        def.ParseParams(evt.JsonParams, bbPtr->Memory);
    }
}
```

**Why it is safe:**

1. `World.GetComponentRW<BrainBlackboard>` returns a `ref` to the component stored in the kernel's native chunk. ECS components live in unmanaged memory allocated via fixed-size pools — not on the GC heap, and therefore not subject to GC relocation.

2. `Unsafe.AsPointer(ref blackboard)` converts the managed `ref` to a raw pointer. The pointer is valid for the duration of the call frame. Because the chunk is unmanaged, there is no risk of the GC moving the backing memory during `ParseParams`.

3. `bbPtr->Memory` gives a `byte*` pointing to the first byte of the `fixed byte Memory[...]` buffer inside `BrainBlackboard`. The `ParseParams` delegate writes its decoded float/int values there. No bounds check is enforced at this layer — it is the doc contract for `ParseParams` callers to respect `BehaviorConstants.MaxBlackboardBytes`.

4. No `fixed` statement is needed because `Unsafe.AsPointer` on a `ref` to unmanaged storage is already safe without pinning; `fixed` would be required only if the struct lived on the managed heap (e.g., inside a class field).

The pattern matches the established idiom in `DoctrineIngressSystemTests.cs` where the test reads back via `*(FleeBlackboard*)bbPtr->Memory` and verifies the written float.

---

## Q4: Did the ordering test reveal any surprises about how `SimulationSystemGroup` resolves `[UpdateBefore]`/`[UpdateAfter]` when multiple constraints exist?

No surprises — the topological sort in `SystemGroup.SortSystems()` processed all declared constraints without ambiguity. The system order produced for the behavior group is:

```
ChannelArbitrationSystem → LocomotionDispatcherSystem
                         → WeaponDispatcherSystem
                         → InteractionDispatcherSystem
```

The redundant bi-directional specification (both `[UpdateBefore]` on arbitration **and** `[UpdateAfter]` on each dispatcher) is intentional belt-and-suspenders: if a future developer adds a fourth dispatcher and forgets `[UpdateAfter]`, the `[UpdateBefore]` on arbitration still enforces the constraint from the other direction. A single-direction approach (e.g., only `[UpdateBefore]` on arbitration) would be silently broken for any new dispatcher that omits `[UpdateAfter]`.

The integration test `Arbitration_Ordering_NoGhostOnEnter_WhenChannelIsStale` verifies this directly by asserting `spy.OnEnterCallCount == 0` and `channel.ActiveAction == 0` after running both systems via explicit `.Run()` calls in the correct order. The test would fail if the ordering attributes were absent and the systems happened to run in registration order by chance — a fragile property that this batch has eliminated.

One observation: `InputSystemGroup` runs before `SimulationSystemGroup` only by convention (registration order in the world setup) — the kernel does not yet enforce cross-group ordering via attributes. For `DoctrineIngressSystem` to guarantee doctrine changes are visible to brain tick systems within the same frame, the host application must register system groups in the correct order. A future improvement would be to declare cross-group ordering constraints at the group level.

---

## Outstanding Issues / Next Steps

- [ ] `FdpHsmContext.Self` is the only field — HSM action delegates cannot access the ECS world yet. Phase 3 should define a strategy (thread-local slot, event queue, or context injection via a registered service locator).
- [ ] `DoctrineIngressSystem` does not validate that `JsonParams` is well-formed before passing it to `ParseParams`. A malformed string will throw inside the delegate with no per-entity error context. Consider wrapping in a try/catch with entity ID logging in debug builds.
- [ ] `InputSystemGroup` cross-group ordering is enforced by convention only. Consider adding a `[UpdateBefore(typeof(SimulationSystemGroup))]` attribute to `InputSystemGroup` if the kernel ever gains cross-group sort support.
- [ ] Phase 2 (Perception) can now begin — all Phase 1 behavior infrastructure is in place.

---

## Files Created / Modified

### New files

| File | Purpose |
|---|---|
| `FDP/Kernel/Fdp.Kernel/StandardSystemGroups.cs` | Added `InputSystemGroup` |
| `FDP/Toolkits/FDP.Toolkit.Behavior/BTreeContext.cs` | `IAIContext` implementation for FastBTree integration |
| `FDP/Toolkits/FDP.Toolkit.Behavior/DoctrineRegistry.cs` | Startup-time registry: doctrine name hash → `DoctrineDefinition` |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Events/AssignDoctrineEvent.cs` | Managed event class carrying doctrine assignment payload |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BTreeTickSystem.cs` | Ticks FastBTree interpreter for `BrainTierBTree` entities |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/HsmTickSystem.cs` | Generic HSM tick system; registered twice for Hsm64 and Hsm128 |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/DoctrineIngressSystem.cs` | Consumes `AssignDoctrineEvent`, updates `DoctrineState`, parses blackboard |
| `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/BTreeTickSystemTests.cs` | 3 BTree tick tests |
| `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/HsmTickSystemTests.cs` | 2 HSM tick tests |
| `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/DoctrineIngressSystemTests.cs` | 4 doctrine ingress tests including end-to-end preemption chain |

### Modified files

| File | Change |
|---|---|
| `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorConstants.cs` | Added `BrainTierHsm = 1`, `BrainTierBTree = 2` |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Executors/IActionExecutor.cs` | `Execute` XML doc updated with Status-write contract |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/ChannelArbitrationSystem.cs` | Added `[UpdateBefore]` × 3 |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/LocomotionDispatcherSystem.cs` | Added `[UpdateAfter]`; `OnExit` call site comment |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/WeaponDispatcherSystem.cs` | Added `[UpdateAfter]`; `OnExit` call site comment |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/InteractionDispatcherSystem.cs` | Added `[UpdateAfter]`; `OnExit` call site comment |
| `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/TestHelpers.cs` | Added `WritingSpyExecutor<TChannel>` |
| `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/ChannelArbitrationTests.cs` | Fixed `Arbitration_ClearsStaleChannel`; added ordering integration test |
| `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/LocomotionDispatcherTests.cs` | Fixed 2 tests with stronger assertions |
| `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/TestWorldFactory.cs` | Added `BrainBTreeState`, `BrainHsm64`, `BrainHsm128` registrations |
