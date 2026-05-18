# BATCH-04: Ordering Fix + BTreeTickSystem + HsmTickSystem + BehaviorRegistry

**Batch Number:** BATCH-04  
**Tasks:** CORRECTIVE (UpdateBefore ordering + OnExit doc), BCS-P1-T5, BCS-P1-T6, BCS-P1-T7  
**Phase:** Phase 1 — FDP.Toolkit.Behavior Core Infrastructure (completing)  
**Estimated Effort:** 9–11 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-03 ✅

---

## 📋 Onboarding & Workflow

### Developer Instructions

Three parts:

1. **Corrective (2 h):** Add ordering attributes + doc comments + fix three weak existing tests.
2. **Brain VM adapters (4–5 h):** `BTreeTickSystem` (FastBTree) and `HsmTickSystem<T>` (FastHSM).
3. **Behavior lifecycle (3–4 h):** `BehaviorRegistry` + `BehaviorIngressSystem`.

This batch **completes Phase 1**. After it, all behavior infrastructure is in place and Phase 2 (Perception) can begin.

### Required Reading (IN ORDER)

1. **BATCH-03 Review:** `.dev-workstream/reviews/BATCH-03-REVIEW.md` — understand Issue 1 (ordering) and Issue 2 (executor status contract) before touching any system
2. **CODE-STANDARDS.md:** `.dev-workstream/guides/CODE-STANDARDS.md` — magic numbers, SimMath, GetComponentRW, zero-alloc rules
3. **Task details BCS-P1-T5, T6, T7:** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — lines 396–484
4. **FastBTree IAIContext:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/IAIContext.cs` — what BTree nodes receive; you must implement this interface as `BTreeContext`
5. **FastHSM HsmKernel:** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmKernel.cs` — `UpdateBatch` signature
6. **Design §3.2–3.3:** `FDP/Docs/projects/behavior-control/DESIGN.md` — system ordering table (lines 172–204), behavior parameter flow (lines 205–240)

### Source Code Locations

| Area | Path |
|---|---|
| Ordering fix | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/ChannelArbitrationSystem.cs` |
| Ordering fix | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/LocomotionDispatcherSystem.cs` (and Weapon, Interaction) |
| OnExit comment | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/DispatcherSystemBase.cs` |
| IActionExecutor doc | `FDP/Toolkits/FDP.Toolkit.Behavior/Executors/IActionExecutor.cs` |
| New: BTreeTickSystem | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BTreeTickSystem.cs` |
| New: BTreeContext | `FDP/Toolkits/FDP.Toolkit.Behavior/BTreeContext.cs` |
| New: HsmTickSystem | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/HsmTickSystem.cs` |
| New: BehaviorRegistry | `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorRegistry.cs` |
| New: BehaviorIngressSystem | `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BehaviorIngressSystem.cs` |
| New: AssignBehaviorEvent | `FDP/Toolkits/FDP.Toolkit.Behavior/Events/AssignBehaviorEvent.cs` |
| Behavior test project | `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/` |
| TestWorldFactory | `FDP/Toolkits/FDP.Toolkit.Behavior.Tests/TestWorldFactory.cs` |
| BehaviorConstants | `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorConstants.cs` |

### Build & Test Commands

```powershell
cd D:\Work\IOS-IG-SimHost-FDP\FDP
dotnet build FDP.sln
dotnet test FDP.sln
dotnet test Toolkits/FDP.Toolkit.Behavior.Tests/
```

### Report Submission

`.dev-workstream/reports/BATCH-04-REPORT.md`  
Questions: `.dev-workstream/questions/BATCH-04-QUESTIONS.md`

---

## Context

**Related tasks:**
- Corrective: ordering — see [BATCH-03-REVIEW.md](../reviews/BATCH-03-REVIEW.md) Issue 1
- Corrective: executor contract doc — see [BATCH-03-REVIEW.md](../reviews/BATCH-03-REVIEW.md) Issue 2 + 3
- [BCS-P1-T5](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p1-t5--btreeticksystem-fastbtree-adapter)
- [BCS-P1-T6](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p1-t6--hsmticksystemt-fasthsm-adapter)
- [BCS-P1-T7](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p1-t7--behaviorregistry--behavioringresssystem)

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

1. **Corrective 0a:** Fix three weak existing tests → all existing tests still pass ✅
2. **Corrective 0b:** Add ordering attributes + doc comments → all existing tests still pass ✅
3. **BCS-P1-T5:** `BTreeTickSystem` + `BTreeContext` → 3 tests pass ✅
4. **BCS-P1-T6:** `HsmTickSystem<T>` → 2 tests pass ✅
5. **BCS-P1-T7:** `BehaviorRegistry` + `BehaviorIngressSystem` + `AssignBehaviorEvent` → 4 tests pass (3 unit + 1 integration chain) ✅

---

## 🎯 Batch Objectives

- `ChannelArbitrationSystem` always runs before all dispatchers, enforced by attributes (not registration order).
- `IActionExecutor<T>` XML comments clearly document the `Status`-write contract and the field state during `OnExit`.
- `BTreeTickSystem` steps each entity's BTree brain once per frame when `BrainTier == BrainTierValues.BTree`.
- `HsmTickSystem<T>` is registered twice — once for `BrainHsm64`, once for `BrainHsm128` — and steps HSM instances.
- `BehaviorIngressSystem` consumes `AssignBehaviorEvent`, updates `BehaviorState`, resets brain state, writes initial blackboard params.
- All brain systems respect `[UpdateAfter(typeof(ChannelArbitrationSystem))]`.

---

## ✅ Tasks

### Task 0 (Corrective): Fix Weak Existing Tests + Ordering + Contract Documentation

**Part A — Fix three weak existing tests:**

**Fix 1 — `Arbitration_ClearsStaleChannel`** (`ChannelArbitrationTests.cs`):  
Add assertion that `BehaviorInstanceId` was also reset:
```csharp
Assert.Equal(0u, channel.BehaviorInstanceId); // channel = default resets entire struct
```
Without this, a selective-clear regression would go undetected.

**Fix 2 — `Dispatcher_CallsOnEnter_OnFirstTick`** (`LocomotionDispatcherTests.cs`):  
The current `SpyExecutor` does not write to `channel.Status`, so the test never verifies the write-back contract. Add a `WritingSpyExecutor<TChannel>` to `TestHelpers.cs` that sets `channel.Status = NodeStatus.Running` in `Execute`. Then:
- Assert `channel.Status == NodeStatus.Running` after tick 1 (the executor wrote it).
- Assert `DispatchedInstanceId == ActionInstanceId` after tick 1 (prevents repeat `OnEnter`).

**Fix 3 — `Dispatcher_SkipsNullExecutor_Gracefully`** (`LocomotionDispatcherTests.cs`):  
Add assertion that despite no registered executor, lifecycle bookkeeping still ran:
```csharp
var channel = world.GetComponent<LocomotionChannel>(e);
Assert.Equal(channel.ActionInstanceId, channel.DispatchedInstanceId); // updated even without executor
```

**Part B — Ordering attributes:**  
Same as previously specified — see task text below.

**Part C — Documentation:**  
Same as previously specified — see task text below.

```csharp
// ChannelArbitrationSystem — runs first in the group:
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(LocomotionDispatcherSystem))]
[UpdateBefore(typeof(WeaponDispatcherSystem))]
[UpdateBefore(typeof(InteractionDispatcherSystem))]

// Each dispatcher — runs after arbitration:
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ChannelArbitrationSystem))]
```

**Ordering integration test** — add to `ChannelArbitrationTests.cs`:  
Register all four systems in the world. Give the entity a stale channel (`BehaviorInstanceId = 1`, `channel.ActiveAction = 1`) and a behavior at `InstanceId = 2`. Register a `SpyExecutor` on the dispatcher. Call `world.RunAll()` (or equivalent that runs all registered systems). Assert:
```csharp
assert(spy.OnEnterCallCount == 0); // arbitration cleared it BEFORE dispatcher ran
assert(world.GetComponent<LocomotionChannel>(e).ActiveAction == 0); // confirmed cleared
```
This test fails if the ordering attributes are absent or wrong.

**Part B — Documentation:**

In `DispatcherSystemBase.cs`, at the `OnExit` call site, add a comment:
```csharp
// Note: at the time OnExit is called, channel.ActiveAction and channel.ActionInstanceId
// still hold the OUTGOING action's values. DispatchedInstanceId is updated after this call.
// This allows OnExit to identify what it is cleaning up.
_executors[oldAction]?.OnExit(entity, ref channel, World);
```

In `IActionExecutor.cs`, update the `Execute` XML doc:
```xml
/// <summary>
/// Drive the active action for one simulation frame.
/// To signal completion or failure, write directly into <paramref name="channel"/>:
///   channel.Status = NodeStatus.Success;  // or NodeStatus.Failure
/// This direct write is intentional — zero allocation, no boxing.
/// </summary>
```

---

### Task 1: BTreeTickSystem + BTreeContext (BCS-P1-T5)

**Files:**
- `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BTreeTickSystem.cs`
- `FDP/Toolkits/FDP.Toolkit.Behavior/BTreeContext.cs`

**Task Definition:** [TASK-DETAIL.md §BCS-P1-T5](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p1-t5--btreeticksystem-fastbtree-adapter) — lines 396–423

Key notes not repeated in the task doc:

**BTree tier constant:** `BehaviorState.BrainTier` must be compared against a named constant. Add to `BehaviorConstants`:
```csharp
public const byte BrainTierBTree = 2;
public const byte BrainTierHsm   = 1;
```

**`BTreeContext` implements `IAIContext`** from `Fbt.Kernel`. Study that interface before designing the context — it defines what BTree action nodes can call. At minimum it needs the current `Entity` and a way to access `EntityRepository` (for reading/writing components from inside a node). Do not capture `EntityRepository*` as an unsafe pointer unless FastBTree's threading model makes it necessary — check the IAIContext contract first.

**BehaviorRegistry dependency:** `BTreeTickSystem` receives a `BehaviorRegistry` reference via constructor injection (same pattern as `CarKinematicsSystem` receives `RoadNetworkBlob`). The registry maps `ActiveBehaviorId → BTreeBlobAsset`. If the ID is not registered, skip that entity silently (log once in debug builds if possible, don't throw).

**`[UpdateAfter(typeof(ChannelArbitrationSystem))]` is mandatory.** Missing it is the same class of bug as the one fixed in Task 0.

**Tests required** (new file `BTreeTickSystemTests.cs`) — specify exact assertions:

```csharp
[Fact] void BTreeTick_DoesNotThrow_WhenBlobNotRegistered()
// Entity has BehaviorState.ActiveBehaviorId = 999 (not in registry).
// sys.Run() must not throw. Entity's BrainBTreeState must be unchanged.
// Assert: sys.Run() completes without exception AND
//         btState.State.RunningNodeIndex == 0 (untouched)

[Fact] void BTreeTick_DoesNotTick_WhenBrainTierIsNotBTree()
// Entity has BehaviorState.BrainTier = BehaviorConstants.BrainTierHsm (not BTree tier).
// Register a spy BTree blob that tracks if it was called.
// Assert: spy blob was NOT invoked (tick count == 0)

[Fact] void BTreeTick_WritesActionToChannel_ForRegisteredTree()
// Register a minimal one-node BTree that unconditionally sets:
//   LocomotionChannel.ActiveAction = 1
//   LocomotionChannel.ActionInstanceId++ (or any non-zero value)
// Give entity BrainTier = BehaviorConstants.BrainTierBTree, a registered behavior.
// sys.Run() one tick.
// Assert:
//   channel.ActiveAction == 1           // BTree wrote the action
//   channel.ActionInstanceId != 0       // instance was stamped
// This is the core behavioural contract of BTreeTickSystem.
```

Do NOT write the third test as just "confirm it writes LocomotionChannel" — it must assert the specific field values.

---

### Task 2: HsmTickSystem\<T\> (BCS-P1-T6)

**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/HsmTickSystem.cs`

**Task Definition:** [TASK-DETAIL.md §BCS-P1-T6](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p1-t6--hsmticksystemt-fasthsm-adapter) — lines 427–451

Key notes:

**`FdpHsmContext`:** The HSM action context must carry enough information for HSM action delegates to interact with the ECS world. Define:
```csharp
public struct FdpHsmContext
{
    public Entity Self;
    public EntityRepository World; // or ref — check HsmKernel's context type constraint
}
```
Check `HsmKernel.cs` to see whether the context type is constrained (e.g., must be a struct, must implement an interface). Use the minimal surface the kernel requires.

**Generic constraint issue (from Q2 pattern):** Similar to the dispatcher base class friction — `HsmKernel.UpdateBatch` is typed. You may need `where TBrainComponent : struct, IHsmComponent` and an `IHsmComponent` interface. The task doc covers this. If the FastHSM API already provides a clean path, use it; don't invent an extra interface layer if unnecessary.

**Registration:** `HsmTickSystem` is registered **twice** in the world setup:
```csharp
systemGroup.Add(new HsmTickSystem<BrainHsm64>());
systemGroup.Add(new HsmTickSystem<BrainHsm128>());
```
Both must have `[UpdateAfter(typeof(ChannelArbitrationSystem))]`.

**Tests required** (new file `HsmTickSystemTests.cs`):

```csharp
[Fact] void HsmTick_TransitionsState_OnRegisteredEvent()
// Build minimal 2-state HSM: StateA --EventX--> StateB
// Give entity BrainHsm128 initialised to StateA
// Push EventX into the HsmInstance128
// sys.Run()
// Assert: HsmInstance128.CurrentStateId == StateB.Id
// This verifies the HSM kernel was actually called with the right instance.

[Fact] void HsmTick64_And_HsmTick128_AreIndependent()
// Entity A has BrainHsm64 only. Entity B has BrainHsm128 only.
// Hsm128 system runs → Assert: entity A's BrainHsm64 is UNCHANGED
// Hsm64 system runs  → Assert: entity B's BrainHsm128 is UNCHANGED
// Neither system should process components it does not own.
```

---

### Task 3: BehaviorRegistry + BehaviorIngressSystem (BCS-P1-T7)

**Files:**
- `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorRegistry.cs`
- `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/BehaviorIngressSystem.cs`
- `FDP/Toolkits/FDP.Toolkit.Behavior/Events/AssignBehaviorEvent.cs`

**Task Definition:** [TASK-DETAIL.md §BCS-P1-T7](../../../FDP/Docs/projects/behavior-control/TASK-DETAIL.md#bcs-p1-t7--behaviorregistry--behavioringresssystem) — lines 455–484

Key notes not repeated in the task doc:

**`AssignBehaviorEvent`** is a managed class (it carries a `string JsonParams`). It is dispatched through the kernel event bus (`World.PublishEvent<AssignBehaviorEvent>(...)`) and consumed synchronously in `BehaviorIngressSystem.OnUpdate` via `World.ReadEvents<AssignBehaviorEvent>()`. Do not make it a struct.

**`BehaviorRegistry`** is a plain C# class, not a component. It holds:
- A `Dictionary<int, BehaviorDefinition>` keyed on `BehaviorName.GetHashCode()` (or an explicit `int BehaviorId`).
- `BehaviorDefinition` contains: `string Name`, `byte BrainTier`, `int BTreeBlobId` (for BTree brains), `Action<string, unsafe byte*> ParseParams` (a delegate that writes JSON parameters into the `BrainBlackboard.Memory` buffer).

**`BehaviorIngressSystem` must:**
1. Increment `BehaviorState.InstanceId` — this triggers `ChannelArbitrationSystem` to clear stale channels on the next frame.
2. Zero `BrainBTreeState.State` (reset BTree execution pointer).
3. Call `ParseParams(event.JsonParams, blackboard.Memory)` — use `unsafe` block with `fixed`.
4. Run in the `InputSystemGroup` (before `SimulationSystemGroup`) so behavior changes take effect within the same frame as they are signalled. Use `[UpdateInGroup(typeof(InputSystemGroup))]`.

**`InstanceId` counter:** `BehaviorState.InstanceId` is a `uint`. Use `unchecked { behavior.InstanceId++; }` so wrapping is deliberate and safe. Add a named constant or a comment if you feel wrapping needs documenting.

**Tests required** (new file `BehaviorIngressSystemTests.cs`):

```csharp
[Fact] void BehaviorIngress_ParsesFleeBlackboard_FromJson()
// Define in-test: struct FleeBlackboard { float SafeDistance; }
// Register "FleeToSafety" behavior with a ParseParams that does:
//   unsafe void Parse(string json, byte* mem) { *(float*)mem = float.Parse(json); }
// PublishEvent: AssignBehaviorEvent { Entity=e, BehaviorName="FleeToSafety", JsonParams="50.0" }
// sys.Run()
// Read BrainBlackboard.Memory as FleeBlackboard (unsafe reinterpret).
// Assert: FleeBlackboard.SafeDistance == 50.0f

[Fact] void BehaviorIngress_IncrementsInstanceId_MonotonicallyAcrossMultipleAssignments()
// Start: entity with BehaviorState.InstanceId = 0
// Assignment 1 → sys.Run() → capture instanceId1 = BehaviorState.InstanceId
// Assignment 2 → sys.Run() → capture instanceId2 = BehaviorState.InstanceId
// Assert: instanceId1 > 0            // was incremented from 0
// Assert: instanceId2 > instanceId1  // strictly increasing each assignment
// NOT just checking "it changed once" — must verify monotonic increase.

[Fact] void BehaviorIngress_ResetsBTreeState_OnNewBehavior()
// Give entity BrainBTreeState with RunningNodeIndex = 5 (mid-execution)
// Assign new behavior via BehaviorIngressSystem
// Assert: BrainBTreeState.State.RunningNodeIndex == 0 (reset to start)

[Fact] void BehaviorIngress_StaleSetsNewInstanceId_ArbitrationClearsOldAction()
// Integration test — runs arbitration + behavior ingress together:
// Setup: entity has LocomotionChannel with ActiveAction=1, BehaviorInstanceId=1
//        BehaviorState.InstanceId = 1 (matching, so arbitration leaves it alone)
// Step 1: PublishEvent(AssignBehaviorEvent) → BehaviorIngressSystem.Run() → InstanceId becomes 2
// Step 2: ChannelArbitrationSystem.Run()
// Assert: channel.ActiveAction == 0  // arbitration cleared the now-stale channel
// This is the full preemption chain. Without it we only tested the parts, not the contract.
```

---

## 🧪 Testing Requirements

- Minimum **11 new tests:** 1 ordering (corrective) + 3 weak-test fixes (corrective) + 3 BTree + 2 HSM + 4 Behavior Ingress (3 unit + 1 integration chain).
- `SpyExecutor` stays as-is (call counter only). Add `WritingSpyExecutor<TChannel>` to `TestHelpers.cs` — it sets `channel.Status = NodeStatus.Running` in `Execute` so tests can verify the write-back path.
- All pre-existing tests must remain green (they will, once the three weak ones are corrected).
- The behavior integration test (`BehaviorIngress_StaleSetsNewInstanceId_ArbitrationClearsOldAction`) must run arbitration and ingress as separate systems, sequenced by calling each `.Run()` explicitly — do not rely on `world.RunAll()` ordering for this test.

---

## ⚠️ Quality Standards

See `.dev-workstream/guides/CODE-STANDARDS.md` — all rules apply.

**❗ Brain tier values must be `BehaviorConstants.BrainTierBTree` and `BehaviorConstants.BrainTierHsm`**, not raw `1` or `2`.

**❗ `[UpdateAfter(typeof(ChannelArbitrationSystem))]` on every brain tick system** — missing this is the same class of bug fixed in Task 0.

**❗ `BehaviorIngressSystem` runs in `InputSystemGroup`, not `SimulationSystemGroup`** — a behavior change this frame must be visible to brain tick systems this same frame.

**❗ `AssignBehaviorEvent` is managed (class)** — do not make it a struct, do not try to register it as an ECS component.

**❗ No `new` allocation in `BTreeTickSystem.OnUpdate` or `HsmTickSystem.OnUpdate`** — context structs must be stack-allocated.

---

## 📊 Report Requirements

Submit `.dev-workstream/reports/BATCH-04-REPORT.md`:

- **Test results:** `dotnet test FDP.sln` summary.
- **Q1:** How did you implement `BTreeContext`? What methods does `IAIContext` require, and which of them touch the ECS world? Did you hit any friction with the unsafe/managed boundary?
- **Q2:** How did you handle the generic constraint for `HsmTickSystem<T>`? Did FastHSM's `HsmKernel` accept a plain struct context or does it require an interface?
- **Q3:** `BehaviorIngressSystem` must call `ParseParams` with a pointer into `BrainBlackboard.Memory`. Walk through the exact `unsafe` + `fixed` pattern you used, and explain why it's safe.
- **Q4:** Did the ordering test reveal any surprises about how `SimulationSystemGroup` resolves `[UpdateBefore]`/`[UpdateAfter]` when multiple constraints exist?

---

## 🎯 Success Criteria

- [ ] **Corrective 0a** — three weak existing tests fixed with additional assertions; all pass
- [ ] **Corrective 0b** — `ChannelArbitrationSystem` has `[UpdateBefore]` all dispatchers; dispatchers have `[UpdateAfter(ChannelArbitrationSystem)]`; ordering test confirms no ghost `OnEnter` on stale channel
- [ ] **Documentation** — `DispatcherSystemBase` `OnExit` call site commented; `IActionExecutor.Execute` XML doc updated; `WritingSpyExecutor<TChannel>` added to `TestHelpers.cs`
- [ ] **BCS-P1-T5** — `BTreeTickSystem` + `BTreeContext` exist; 3 tests with specific field assertions pass; no magic BrainTier literals
- [ ] **BCS-P1-T6** — `HsmTickSystem<T>` exists; registered twice; 2 tests with state-transition assertions pass
- [ ] **BCS-P1-T7** — `BehaviorRegistry` + `BehaviorIngressSystem` + `AssignBehaviorEvent` exist; 4 tests pass including the end-to-end preemption chain test
- [ ] **Full solution** — `dotnet build FDP.sln` zero errors; `dotnet test FDP.sln` all green
- [ ] **Report submitted**

---

## 📚 Reference Materials

- **BATCH-03 Review:** `.dev-workstream/reviews/BATCH-03-REVIEW.md`
- **CODE-STANDARDS.md:** `.dev-workstream/guides/CODE-STANDARDS.md`
- **Task Details (BCS-P1-T5, T6, T7):** `FDP/Docs/projects/behavior-control/TASK-DETAIL.md` — lines 396–484
- **Design §3.2–3.3:** `FDP/Docs/projects/behavior-control/DESIGN.md` — system ordering + behavior flow
- **FastBTree IAIContext:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/IAIContext.cs`
- **FastHSM HsmKernel:** `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmKernel.cs`
- **BehaviorConstants (to extend):** `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorConstants.cs`
- **Note on task doc numerics:** The task doc uses raw `2` for BrainTier and raw `4001–4003` for event IDs. In production code, these must be `BehaviorConstants.BrainTierBTree` and named event ID constants. The docs describe *what*; you own *how to name it*.
