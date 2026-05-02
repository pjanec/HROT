# BTree + HSM Unification — Task Detail

All tasks use the prefix **BHU** (BTree-HSM Unification).
Design references point to sections in `DESIGN.md` in this directory.

---

## Phase 1 — Unified Hot Reload Coordinator

---

### BHU-001 — Add Fhsm references to `Hrot.AI.Behaviors.csproj`

**Design ref**: Phase 1 § 1.1

**Scope**: `Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj`

**What to do**:

Add three `ProjectReference` entries inside the existing `<ItemGroup>` that holds BTree
references:

```xml
<ProjectReference Include="..\..\..\FDP\ExtDeps\FastHSM\src\Fhsm.Kernel\Fhsm.Kernel.csproj" />
<ProjectReference Include="..\..\..\FDP\ExtDeps\FastHSM\src\Fhsm.Compiler\Fhsm.Compiler.csproj" />
<ProjectReference Include="..\..\..\FDP\ExtDeps\FastHSM\src\Fhsm.SourceGen\Fhsm.SourceGen.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

After adding these, add a single stub `[HsmAction]` static method in
`Hrot.AI.Behaviors/Brains/CgfHsmNodes.cs` (new file) to confirm the source generator
runs and emits `Hrot.AI.Behaviors.Generated.HsmActionRegistrar.g.cs`. The stub method
should have a valid `[HsmAction]` signature (`static void MethodName(void*, void*, HsmCommandWriter*)`)
but may have an empty body.

**Constraints**:
- Do not rename or relocate existing source files.
- The new `CgfHsmNodes.cs` file must be in the `Hrot.AI.Behaviors` namespace.

**Success conditions**:
1. `dotnet build Hrot.AI.Behaviors.csproj` succeeds with zero errors.
2. The build output directory contains `Hrot.AI.Behaviors.dll`.
3. A generated file `HsmActionRegistrar.g.cs` is produced in the analyzer output
   (visible via `obj/` folder or via IDE generated-files view).
4. Existing BTree tests for `Hrot.AI.Behaviors` continue to pass.

---

### BHU-002 — Add `HsmActionDispatcher.ClearAll()` to `Fhsm.Kernel` (via SourceGen)

**Design ref**: Phase 1 § 1.2

**Scope**: `FDP/ExtDeps/FastHSM/src/Fhsm.SourceGen/HsmActionGenerator.cs`

**What to do**:

In `HsmActionGenerator.GenerateKernelDispatcher()`, append `ClearAll()` after the
existing `RegisterAction`/`RegisterGuard` methods:

```csharp
sb.AppendLine("        public static void ClearAll()");
sb.AppendLine("        {");
sb.AppendLine("            ActionTable.Clear();");
sb.AppendLine("            GuardTable.Clear();");
sb.AppendLine("        }");
```

Because the generated file is re-emitted on every build of `Fhsm.Kernel`, this change
takes effect automatically.

**Constraints**:
- Do not change the generated method signatures for `ExecuteAction`, `EvaluateGuard`,
  `RegisterAction`, or `RegisterGuard`.
- `ClearAll()` must be `public static void`.

**Success conditions**:
1. After rebuilding `Fhsm.Kernel`, the generated `HsmActionDispatcher.g.cs` contains a
   `ClearAll()` method with `ActionTable.Clear(); GuardTable.Clear();`.
2. `dotnet build Fhsm.Kernel.csproj` succeeds with zero errors.
3. Calling `HsmActionDispatcher.ClearAll()` from a test removes all previously
   registered entries: a subsequent `EvaluateGuard` returns `true` (the default
   "no guard = always pass" fallback) even for a previously-registered guard ID.

---

### BHU-003 — Build `AiHotReloadCoordinator`

**Design ref**: Phase 1 § 1.3

**Scope**: New file `Hrot/Subsystems/Hrot.Editor/AiHotReloadCoordinator.cs`

**What to do**:

Create a new class `AiHotReloadCoordinator` in namespace `Hrot.Editor` (or a sub-namespace
`Hrot.Editor.HotReload`). It must implement the following contract:

**Constructor**:
```csharp
public AiHotReloadCoordinator(
    string watchDirectory,
    string dllFilter,
    EntityRepository world,
    BehaviorRegistry liveRegistry)
```

**Background thread (`LoadAndReload(string dllPath)` — private)**:
1. Retry-load the DLL into a new collectible `AssemblyLoadContext` (same retry loop as
   `FbtAssemblyHotReloader`; up to 5 retries, 50 ms sleep).
2. Reflect `AiBehaviorFactory.BuildRegistrationAction(ActionRegistry)` from the new
   assembly. If the method is not found, enqueue `OnReloadFailed` and unload new ALC.
3. Build a local `ActionRegistry`, call `FbtActionRegistrar.RegisterAll(actionRegistry)`
   via reflection on the type found in the new assembly.
4. Invoke `BuildRegistrationAction(actionRegistry)` to get `Action<BehaviorRegistry>
   applyAction`.
5. Create a temporary staging `BehaviorRegistry`. Call `applyAction(stagingRegistry)` to
   populate it with BTree blobs AND HSM blobs (once BHU-005 adds HSM blob registration).
6. Enqueue a main-thread struct containing `(stagingRegistry, newAlc, oldAlc)`.

**Main-thread drain (`DrainPendingCallbacks()` — public)**:
1. Dequeue pending reload work item.
2. Call `HsmActionDispatcher.ClearAll()`.
3. Reflect `Hrot.AI.Behaviors.Generated.HsmActionRegistrar.RegisterAll()` on
   `newAlc`'s assembly and invoke it.
4. Apply staging registry to `liveRegistry` (BTree behaviors).
5. For each HSM behavior in staging registry: iterate the matching `EntityQuery` by
   chunk and call `HotReloadManager.TryReload()` per-chunk component span.
   `TryReload`'s current signature accepts a single `Span<TInstance>`, which is
   architecturally impossible to obtain for the whole world in the FDP ECS (see Q6 in
   DESIGN.md). Before implementing this step, coordinate with the `Fhsm.Kernel`
   maintainer to refactor `TryReload` to accept chunk-based iteration (e.g.,
   `EntityRepository` + `EntityQuery`, or `IEnumerable<Span<TInstance>>`).
   `TryReload` must also force any instance in `InstancePhase.RTC` or
   `InstancePhase.Activity` back to `InstancePhase.Idle` before applying HardReset.
6. Set `oldAlc` to null (let GC collect). Store `PreviousAlcRef = new WeakReference(oldAlc)` before nulling.

**Events**: `event Action<string>? OnReloadCompleted` and
`event Action<string, Exception>? OnReloadFailed` (same semantics as `FbtAssemblyHotReloader`).

**`TriggerInitialLoad()`**: same logic as `FbtAssemblyHotReloader.TriggerInitialLoad()`.

**`IDisposable`**: dispose `FileSystemWatcher`, `Timer`, current ALC.

**Constraints**:
- All `HsmActionDispatcher.ClearAll()` and `HsmActionRegistrar.RegisterAll()` calls must
  happen inside `DrainPendingCallbacks()` on the main thread, NOT in `LoadAndReload()`.
- The old ALC must not be released until step 6 of the main-thread drain.
- `FbtAssemblyHotReloader` must not be used or instantiated inside this class.
- This class may use `System.Reflection` for the `BuildRegistrationAction` lookup.

**Success conditions**:
1. `dotnet build Hrot.Editor.csproj` succeeds.
2. Unit test: simulate two reload cycles. After each cycle, `PreviousAlcRef.TryGetTarget`
   returns false after a `GC.Collect()` + `GC.WaitForPendingFinalizers()` pair, confirming
   the old ALC was unloaded.
3. Unit test: verify `HsmActionDispatcher.ClearAll()` is called before
   `HsmActionRegistrar.RegisterAll()` in the drain sequence (instrument via a test
   subclass or mock).
4. Integration test: hot-reload `Hrot.AI.Behaviors.dll` twice; confirm the second reload
   supersedes the first with no crash.

---

### BHU-004 — Wire `AiHotReloadCoordinator` into `EditorSubsystem`

**Design ref**: Phase 1 § 1.4, 1.5

**Scope**:
- `Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`
- `Hrot/Subsystems/Hrot.AI.Behaviors/AiBehaviorFactory.cs`

**What to do**:

**In `AiBehaviorFactory`**:

Extend `BuildRegistrationAction(ActionRegistry actionRegistry)` to also build HSM
`HsmDefinitionBlob` objects and register them. The method returns
`Action<BehaviorRegistry>`; the returned lambda must now also call
`registry.RegisterHsmBehavior(name, blob)` (or the equivalent existing API) for each
HSM behavior. The `Idle_HSM` stub is the first candidate: give it a real minimal blob
(a single `Idle` state with no transitions) using `HsmBuilder` + `HsmCompiler`.

**In `EditorSubsystem`**:

1. Replace the `FbtAssemblyHotReloader _aiHotReloader` field with
   `AiHotReloadCoordinator _aiCoordinator`.
2. In the initialization block (around line 367), replace the `FbtAssemblyHotReloader`
   constructor call with the `AiHotReloadCoordinator` constructor, passing `_world` and
   `_behaviorRegistry`.
3. Replace all references to `_aiHotReloader.DrainPendingCallbacks()` with
   `_aiCoordinator.DrainPendingCallbacks()`.
4. Replace the `OnReloadCompleted`/`OnReloadFailed` event subscriptions similarly.
5. Remove the now-unused `_pendingBehaviorApply` field and related staging logic from
   `EditorSubsystem`, since this is now handled inside `AiHotReloadCoordinator`.

**Constraints**:
- Do not alter the reflection approach for `BuildRegistrationAction` — it must remain
  reflection-based so it works with the hot-loaded assembly.
- Do not break the `ClusterRunner` code path in this task (that is addressed separately
  or deferred).

**Success conditions**:
1. `dotnet build Hrot.Editor.csproj` succeeds with zero errors and zero new warnings.
2. The editor starts and `TriggerInitialLoad()` fires; BTree behaviors load correctly.
3. After modifying a BTree behavior in `Hrot.AI.Behaviors` and recompiling that project
   in isolation, the editor detects the change and applies the new behavior without crash.
4. All existing `Hrot.Editor.Tests` pass.

---

## Phase 2 — HSM Terminal State Routing

---

### BHU-005 — Implement `IsFinal` in `Fhsm.Compiler`

**Design ref**: Phase 2 § 2.1, 2.2

**Scope**:
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/Graph/StateNode.cs`
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmBuilder.cs`
- `FDP/ExtDeps/FastHSM/src/Fhsm.Compiler/HsmFlattener.cs`

**What to do**:

1. Add `public bool IsFinal { get; set; }` to `StateNode` after the existing boolean
   properties (`IsInitial`, `IsHistory`, `IsDeepHistory`, `IsParallel`).

2. Add `Final()` method to `StateBuilder` in `HsmBuilder.cs`:
   ```csharp
   public StateBuilder Final()
   {
       _state.IsFinal = true;
       return this;
   }
   ```

3. In `HsmFlattener.BuildStateFlags()`, add:
   ```csharp
   if (node.IsFinal) flags |= StateFlags.IsFinal;
   ```
   immediately after the `if (node.IsParallel)` line.

**Constraints**:
- A state that has `IsFinal = true` should still allow `OnEntry` actions (the cleanup
  action may run). The compiler must not reject final states with an `OnEntryAction`.
- Do not add validation that prevents child states under a final state — the compiler
  already handles degenerate graphs gracefully.

**Success conditions**:
1. `dotnet build Fhsm.Compiler.csproj` succeeds.
2. Unit test: build a state machine with `.State("Done").Final()`. Call
   `HsmCompiler.Compile(graph)` and check that the resulting `HsmDefinitionBlob`'s
   state table has `StateFlags.IsFinal` set for the "Done" state.
3. Unit test: verify that a state machine without any `.Final()` state compiles without
   `StateFlags.IsFinal` set on any state (regression guard).

---

### BHU-006 — Implement `StateFlags.IsFinal` → `InstanceFlags.Terminated` in `HsmKernelCore`

**Design ref**: Phase 2 § 2.3

**Scope**: `FDP/ExtDeps/FastHSM/src/Fhsm.Kernel/HsmKernelCore.cs`

**What to do**:

Locate the state-entry path where `OnEntryActionId` is executed (around lines 304 and 703
in the current file). After each invocation of `ExecuteAction(state.OnEntryActionId, ...)`
— and also immediately after entering a final state even if it has no OnEntry action —
add the terminal check:

```csharp
if ((state.Flags & StateFlags.IsFinal) != 0)
{
    ref InstanceHeader hdr = ref Unsafe.As<TInstance, InstanceHeader>(ref instance);
    hdr.Flags |= InstanceFlags.Terminated;
}
```

The `Unsafe.As<TInstance, InstanceHeader>` cast is valid because `InstanceHeader` is
defined as the leading 16 bytes of every instance tier by explicit layout or sequential
layout, and this invariant is tested elsewhere in `Fhsm.Kernel.Tests`.

**Constraints**:
- The `Terminated` flag must be set AFTER `OnEntry` executes, not before. The entry
  action may still need to fire (e.g., to emit a command via `HsmCommandWriter`).
- Once `Terminated` is set, the kernel must not attempt further transition evaluation
  on that instance. Add a guard at the top of the event-dispatch path:
  ```csharp
  if ((hdr.Flags & InstanceFlags.Terminated) != 0)
      return; // Instance is done
  ```

**Success conditions**:
1. `dotnet build Fhsm.Kernel.csproj` succeeds.
2. Unit test: drive a state machine to a final state. After the update call,
   `(InstanceHeader.Flags & InstanceFlags.Terminated) != 0` is true.
3. Unit test: call `HsmKernel.Update()` on a Terminated instance. Confirm the kernel
   returns without executing any transition or action.
4. All existing `Fhsm.Kernel.Tests` pass.

---

### BHU-007 — `HsmTickSystem<T>`: publish `BehaviorFinishedEvent`

**Design ref**: Phase 2 § 2.4

**Scope**: `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs`

**What to do**:

1. Add three private fields to the system:
   - `Dictionary<int, uint> _publishedTerminalForInstanceId` — dedup map
   - `HashSet<int> _seenThisFrame` — for stale-key pruning
   - `List<int> _staleKeys` — scratch list for pruning

2. At the top of the entity loop, call `_seenThisFrame.Clear()`, and inside the loop
   add `_seenThisFrame.Add(entity.Index)` before the terminal check.

3. In the per-entity tick loop, after `HsmKernel.Update(...)`, add:

```csharp
ref var hdr = ref Unsafe.As<T, InstanceHeader>(ref component);
if ((hdr.Flags & InstanceFlags.Terminated) != 0)
{
    uint instanceId = behavior.InstanceId; // matches BTreeTickSystem's dedup contract
    int  entityIdx  = entity.Index;
    if (!_publishedTerminalForInstanceId.TryGetValue(entityIdx, out uint prev)
        || prev != instanceId)
    {
        _publishedTerminalForInstanceId[entityIdx] = instanceId;
        _eventBus.Publish(new BehaviorFinishedEvent { Entity = entity });
    }
}
```

4. After the entity loop, prune stale keys (mirrors `BTreeTickSystem`):

```csharp
_staleKeys.Clear();
foreach (var key in _publishedTerminalForInstanceId.Keys)
    if (!_seenThisFrame.Contains(key)) _staleKeys.Add(key);
foreach (var key in _staleKeys) _publishedTerminalForInstanceId.Remove(key);
```

5. Immediately after `_eventBus.Publish(...)`, clear the `Terminated` flag and reset
   `Phase` to `InstancePhase.Idle`:

```csharp
hdr.Flags &= ~InstanceFlags.Terminated;
hdr.Phase  = InstancePhase.Idle;
```

This prevents the Terminal State Latch bug: without this clear, when the mission
director assigns a new behavior (bumping `behavior.InstanceId`), the dedup cache
misses, and the sticky `Terminated` flag from the previous run would cause the very
first tick of the new behavior to instantly fire another `BehaviorFinishedEvent`.

6. The `_eventBus` reference must be acquired the same way `BTreeTickSystem` acquires
   it (constructor injection or system registration parameter — match the existing
   pattern exactly).

**Constraints**:
- The deduplication uses `behavior.InstanceId` (consistent with `BTreeTickSystem`). If
  an entity's behavior is reassigned, the new `InstanceId` triggers a fresh event.
- After publishing, the `Terminated` flag and `Phase` MUST be cleared (step 5 above).
  The dedup cache entry is still written so a second publish on the same `InstanceId`
  is still suppressed even after the flag is cleared.
- The stale-key pruning loop must run every frame to prevent unbounded dict growth when
  entities are destroyed and never queried again.
- The `Unsafe.As<T, InstanceHeader>` cast is only valid for `T = BrainHsm64` and
  `T = BrainHsm128`. The test must cover both instantiations.
- Do not modify the event type `BehaviorFinishedEvent`; use the existing type.

**Success conditions**:
1. `dotnet build Fdp.Toolkits.csproj` succeeds.
2. Unit test (`HsmTickSystem<BrainHsm64>`): create an entity with `BrainHsm64`, advance
   it to a final state. Assert exactly one `BehaviorFinishedEvent` is published.
3. Unit test: call `HsmKernel.Update()` again on the same entity (Terminated). Assert no
   second event is published (dedup works).
4. Unit test: advance entity to final state; then reassign behavior (new `InstanceId`);
   tick once. Assert `InstanceFlags.Terminated` is cleared and `Phase == InstancePhase.Idle`.
   Advance new behavior to final state. Assert a second `BehaviorFinishedEvent` IS
   published and NOT published again on the subsequent tick.
5. Unit test: destroy the entity and run one more tick. Assert
   `_publishedTerminalForInstanceId` no longer contains that entity index (stale key
   pruned).
6. All existing `Fdp.Toolkits.Tests` pass (or `Fdp.ModuleHost.Benchmarks` and related
   build targets).

---

## Phase 3 — Cognitive Interrupt Decoupling

---

### BHU-008 — Create `CognitiveInterruptSystem`

**Design ref**: Phase 3 § 3.1, 3.2

**Scope**:
- New file: `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/CognitiveInterruptSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BrainBlackboard.cs` (comment update)

**What to do**:

1. Add a block comment to `BrainBlackboard.cs` documenting the reserved interrupt register
   layout (byte 126 = MobilityLost, byte 127 = reserved). Do not change the struct itself.

2. Create `CognitiveInterruptSystem` using **edge-triggered detection** via a
   `PreviousCapabilities` component (verify the exact component name at implementation
   time — it is referenced in `HsmDamageBridgeSystem`'s existing logic):

```csharp
// In CognitiveInterruptSystem.cs
internal sealed class CognitiveInterruptSystem : ISystem
{
    public void Update(EntityRepository world, float deltaTime)
    {
        foreach (var entity in world.Query<BrainBlackboard, ActorCapabilityState, PreviousCapabilities>())
        {
            ref var bb   = ref entity.Get<BrainBlackboard>();
            ref var curr = ref entity.Get<ActorCapabilityState>();
            ref var prev = ref entity.Get<PreviousCapabilities>();

            bool wasAbleToMove = (prev.Capabilities & ActorCapabilities.CanMove) != 0;
            bool canMoveNow    = (curr.Capabilities & ActorCapabilities.CanMove) != 0;

            if (wasAbleToMove && !canMoveNow)
                bb.Memory[InterruptRegister_MobilityLost] = 1;

            prev.Capabilities = curr.Capabilities;
        }
    }
}
```

The byte index `126` must be a named constant:
```csharp
internal const int InterruptRegister_MobilityLost = 126;
```

**Constraints**:
- Edge-triggered detection (capability transition only) prevents re-firing the interrupt
  on every frame while the unit remains incapacitated. `CognitiveCleanupSystem` (BHU-015)
  clears the byte at end-of-frame; do NOT clear it here.
- BTree entities are included in the query. The write to byte 126 is harmless and
  intentional — BTree Observer nodes will read it as a single-frame pulse.

**Success conditions**:
1. `dotnet build Fdp.Toolkits.csproj` succeeds.
2. Unit test: entity transitions from `CanMove` to `!CanMove`. After one `Update()`,
   `bb.Memory[126] == 1`.
3. Unit test: entity remains `!CanMove` on the second frame (`PreviousCapabilities` now
   matches current). Assert `bb.Memory[126]` is NOT set again (edge, not level).
4. Unit test: entity with `CanMove` throughout. Assert `bb.Memory[126]` remains 0.

---

### BHU-009 — `HsmTickSystem<T>`: consume interrupt registers

**Design ref**: Phase 3 § 3.3

**Scope**: `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/HsmTickSystem.cs`

**What to do**:

At the top of the per-entity tick loop, before `HsmKernel.Update(...)`, add:

```csharp
ref var bb = ref entity.Get<BrainBlackboard>();
if (bb.Memory[CognitiveInterruptSystem.InterruptRegister_MobilityLost] == 1)
    HsmEventQueue.TryEnqueue(ref component, HsmEvents.EventId_MobilityLost);
// Byte 126 is zeroed by CognitiveCleanupSystem (BHU-015) at end of frame.
// Do NOT clear it here -- BTree Observer nodes need to read it in the same frame.
```

`HsmEvents.EventId_MobilityLost` is the same constant that `HsmDamageBridgeSystem`
currently uses (verify exact name at implementation time).

**Constraints**:
- Do NOT clear byte 126 in `HsmTickSystem<T>`. Clearing is exclusively
  `CognitiveCleanupSystem`'s responsibility (BHU-015). Clearing here would prevent
  BTree Observer nodes on the same frame from reading the interrupt.
- This change must not be applied to `BrainHsm64`/`BrainHsm128` instances that do not
  have a `BrainBlackboard` sibling. Verify the ECS query already includes `BrainBlackboard`
  or add it.

**Success conditions**:
1. `dotnet build Fdp.Toolkits.csproj` succeeds.
2. Unit test: set `bb.Memory[126] = 1`, run `HsmTickSystem<BrainHsm64>.Update()`.
   Assert the HSM received `EventId_MobilityLost`. Assert `bb.Memory[126] == 1` STILL
   after the tick (not consumed by HsmTickSystem).
3. Unit test: run `HsmTickSystem<BrainHsm64>.Update()` with `bb.Memory[126] == 0`.
   Assert no spurious event injection.

---

### BHU-010 — Update `CognitiveRuntimeModule` registration order

**Design ref**: Phase 3 § 3.4

**Scope**: `FDP/Toolkits/Fdp.Toolkits/Behavior/Modules/CognitiveRuntimeModule.cs`

**What to do**:

Replace the `HsmDamageBridgeSystem` registration line with `CognitiveInterruptSystem`:

```csharp
// Remove:
pack.Register(new HsmDamageBridgeSystem(...));

// Add:
pack.Register(new CognitiveInterruptSystem());
```

Verify the system order after the change:
1. `ChannelArbitrationSystem`
2. `CognitiveInterruptSystem`  (was HsmDamageBridgeSystem)
3. `BTreeTickSystem`
4. `HsmTickSystem<BrainHsm128>`
5. `HsmTickSystem<BrainHsm64>`
6. `CognitiveCleanupSystem`   (new — zeros interrupt registers last, see BHU-015)

Delete `HsmDamageBridgeSystem.cs` from the project. Update any unit tests that reference
`HsmDamageBridgeSystem`.

**Constraints**:
- Do not reorder `BTreeTickSystem` and `HsmTickSystem<T>` relative to each other.
- The `ChannelArbitrationSystem` must still run first.
- If `HsmDamageBridgeSystem` is referenced by a test, update the test to use
  `CognitiveInterruptSystem` instead.

**Success conditions**:
1. `dotnet build Fdp.Toolkits.csproj` succeeds.
2. No reference to `HsmDamageBridgeSystem` remains anywhere in the solution (search via
   `grep -r HsmDamageBridgeSystem`).
3. All existing `Fdp.Toolkits.Tests` (and any module-integration tests) pass.
4. Unit test: run `CognitiveRuntimeModule` end-to-end with a single HSM entity.
   Verify that a mobility-lost event results in the correct HSM state transition.

---

### BHU-015 — Create `CognitiveCleanupSystem`

**Design ref**: Phase 3 § 3.5

**Scope**:
- New file: `FDP/Toolkits/Fdp.Toolkits/Behavior/Systems/CognitiveCleanupSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Modules/CognitiveRuntimeModule.cs` (registration)

**What to do**:

1. Create `CognitiveCleanupSystem` in the same namespace as `CognitiveInterruptSystem`:

```csharp
internal sealed class CognitiveCleanupSystem : ISystem
{
    public void Update(EntityRepository world, float deltaTime)
    {
        foreach (var entity in world.Query<BrainBlackboard>())
        {
            ref var bb = ref entity.Get<BrainBlackboard>();
            bb.Memory[CognitiveInterruptSystem.InterruptRegister_MobilityLost] = 0;
            bb.Memory[127] = 0;
        }
    }
}
```

2. In `CognitiveRuntimeModule`, register it as the **last** system, after both
   `HsmTickSystem<BrainHsm128>` and `HsmTickSystem<BrainHsm64>`:

```csharp
pack.Register(new CognitiveCleanupSystem());
```

**Constraints**:
- Must run after ALL tick systems. Position it last in `CognitiveRuntimeModule`.
- Does not check brain tier. Clears for all entities with `BrainBlackboard`.
- This is the ONLY system allowed to clear the interrupt register bytes. Neither
  `HsmTickSystem<T>` nor `CognitiveInterruptSystem` should clear them.

**Success conditions**:
1. `dotnet build Fdp.Toolkits.csproj` succeeds.
2. Unit test: set `bb.Memory[126] = 1`. Run `CognitiveCleanupSystem.Update()`. Assert
   `bb.Memory[126] == 0`.
3. Integration test: run one full `CognitiveRuntimeModule` frame with an HSM entity
   where byte 126 was set. Assert the HSM received the event (from `HsmTickSystem<T>`)
   AND byte 126 is 0 after the frame.
4. Integration test: run one full `CognitiveRuntimeModule` frame with a BTree entity
   where byte 126 was set. Assert byte 126 is 0 after the frame.
5. All existing `Fdp.Toolkits.Tests` pass.

---

### BHU-016 — `BehaviorIngressSystem`: reset HSM state on behavior assignment

**Design ref**: DESIGN.md — BehaviorIngressSystem gap section

**Scope**: Locate `BehaviorIngressSystem` at task start (likely `Hrot.CGF` or
`Fdp.Toolkits`). Confirm the exact file path and namespace before editing.

**What to do**:

In the per-entity path where a new behavior is assigned, extend the reset logic to
handle HSM components.

When `behavior.BrainTier == BrainTierHsm64`:
1. Get `ref BrainHsm64 hsm = ref entity.Get<BrainHsm64>()`.
2. Call the `Fhsm.Kernel` reset helper that `HotReloadManager.HardReset` uses
   (`ClearInstance64State` or equivalent) to scrub active-leaf IDs, event queues, and
   history slots. Do NOT duplicate the logic.
3. Set `InstanceHeader.Flags &= ~InstanceFlags.Terminated`.
4. Set `InstanceHeader.Phase = InstancePhase.Idle`.
5. Set `InstanceHeader.MachineId` to the new behavior's machine ID.

Apply the same steps for `BrainTierHsm128` using `BrainHsm128`.

The existing BTree reset (`BrainBTreeState.State = default`) must remain unchanged.

**Constraints**:
- Verify the exact reset API in `Fhsm.Kernel` before implementing. Do NOT duplicate
  reset logic; call the existing helpers.
- The reset must occur before the first tick of the new behavior (same frame, before
  `HsmTickSystem<T>` runs).
- This task depends on BHU-005/BHU-006 since `InstanceFlags.Terminated` is only
  meaningful once the IsFinal chain is wired.

**Success conditions**:
1. `dotnet build` succeeds for the project containing `BehaviorIngressSystem`.
2. Unit test: assign Behavior A (HSM, reaches final state → `Terminated` set). Then
   assign Behavior B (HSM). Assert `InstanceFlags.Terminated` is cleared and
   `Phase == InstancePhase.Idle` before the first tick of Behavior B.
3. Unit test: assign two consecutive HSM behaviors without either reaching a final state.
   Assert execution state is clean (all active-leaf IDs zeroed) on the second assignment.
4. Existing behavior-assignment tests pass without regression.

---

## Phase 4 — Shared AI Node Attributes

---

### BHU-011 — Add `SharedAiConditionAttribute`, `SharedAiActionAttribute`, `WritesChannelAttribute` to `Fbt.Kernel`

**Design ref**: Phase 4 § 4.1; Phase 5 § 5.1

**Scope**: New file `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/SharedAiAttributes.cs`

**What to do**:

Create one new file containing all three attribute classes in namespace `Fbt.Kernel`:

```csharp
using System;

namespace Fbt.Kernel
{
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SharedAiConditionAttribute : Attribute
    {
        public int BlackboardOffset { get; }
        public SharedAiConditionAttribute(int blackboardOffset)
        {
            BlackboardOffset = blackboardOffset;
        }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class SharedAiActionAttribute : Attribute
    {
        public int BlackboardOffset { get; }
        public SharedAiActionAttribute(int blackboardOffset)
        {
            BlackboardOffset = blackboardOffset;
        }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class WritesChannelAttribute : Attribute
    {
        public ChannelKind Channel { get; }
        public WritesChannelAttribute(ChannelKind channel)
        {
            Channel = channel;
        }
    }

    public enum ChannelKind { Locomotion, Weapon, Interaction }
}
```

**Constraints**:
- These attributes are data-only; they must have no runtime behavior.
- `ChannelKind` must be a public enum in `Fbt.Kernel` (NOT in `Fdp.Toolkits`), since
  `Fbt.SourceGen` and `Fhsm.SourceGen` must recognize it by fully qualified name.

**Success conditions**:
1. `dotnet build Fbt.Kernel.csproj` succeeds.
2. Unit test: annotate a test method with
   `[SharedAiCondition(typeof(TestDto), nameof(TestDto.Weapon))]` and verify the
   attribute is readable via reflection:
   `attr.DtoType == typeof(TestDto) && attr.FieldName == "Weapon"`.
3. Unit test: apply two `[SharedAiCondition]` attributes with different DTO types on the
   same method. Verify `GetCustomAttributes<SharedAiConditionAttribute>()` returns two
   entries (`AllowMultiple = true` constraint check).
4. Existing `Fbt.Kernel.Tests` pass.

---

### BHU-012 — Extend `Fbt.SourceGen` for `[SharedAiCondition]` and `[SharedAiAction]`

**Design ref**: Phase 4 § 4.2

**Scope**: `FDP/ExtDeps/FastBTree/src/Fbt.SourceGen/BTreeActionGenerator.cs`

**What to do**:

Extend `BTreeActionGenerator` to scan for `SharedAiConditionAttribute` and
`SharedAiActionAttribute` (by fully qualified name `"Fbt.Kernel.SharedAiConditionAttribute"`
and `"Fbt.Kernel.SharedAiActionAttribute"`) in addition to the existing `BTreeConditionAttribute`
and `BTreeActionAttribute`.

For each discovered `[SharedAiCondition(typeof(T), "Field")]` method, the generator
resolves `offset = offsetOf(T, "Field")` by analyzing the struct layout of `T` via
Roslyn's semantic model, then emits a condition registration with compound key
`"{MethodName}@{offset}"`:

```csharp
// In FbtActionRegistrar.g.cs (existing generated file)
// Offset resolved at generation time from T.Field layout
actionRegistry.RegisterCondition(
    "{MethodName}@{offset}",
    static (ref BrainBlackboard bb, BTreeContext ctx) =>
    {
        ref {FieldType} dto = ref Unsafe.As<byte, {FieldType}>(
            ref Unsafe.AddByteOffset(ref bb.Memory[0], (nint){offset}));
        return {FullMethodName}(ref dto, ctx.Self, ctx.Repo);
    });
```

Where `{FieldType}` is the type of the specified field on the parent DTO (must match the
`ref TValue` parameter type of the shared method — emit a generator diagnostic if they
differ). Because `AllowMultiple = true`, a single method may have multiple attributes;
emit one adapter per attribute instance.

For `[SharedAiAction(typeof(T), "Field")]`, emit an action registration returning
`NodeStatus` from the shared method call.

**Constraints**:
- The `{MethodName}@{offset}` compound key must be the EXACT string used in behavior tree
  JSON/DSL to reference this node. Document the convention in a source comment in the
  generated file header.
- Existing `[BTreeCondition]` / `[BTreeAction]` generation must remain unchanged.
- If a method has BOTH `[BTreeCondition]` and `[SharedAiCondition]`, emit both registrations
  (they have different keys: one is just `{MethodName}`, the other is `{MethodName}@{offset}`).

**Success conditions**:
1. `dotnet build Fbt.SourceGen.csproj` succeeds.
2. Integration test (happy path): add a `[SharedAiCondition(typeof(TestDto), nameof(TestDto.Weapon))]`-
   annotated method where `TestDto.Weapon` is at offset 16. Verify the generated
   `FbtActionRegistrar.g.cs` contains a `RegisterCondition` call with key `"MethodName@16"`.
3. Unit test (runtime call-through): invoke the generated condition via
   `actionRegistry.TryGetCondition("MethodName@16")`. Verify it calls through correctly
   with a mock `BrainBlackboard`.
4. Integration test (multi-attribute): apply two `[SharedAiCondition]` attributes with
   different DTOs on the same method. Verify two separate adapter registrations are
   emitted with distinct compound keys.
5. **Negative / diagnostic tests** via `CSharpSourceGeneratorVerifier` (or equivalent
   Roslyn testing framework such as `Microsoft.CodeAnalysis.CSharp.Testing`):
   a. Mismatched `ref TValue` parameter: the shared method declares `ref WeaponParams`
      but the attribute specifies a field of type `AmmoParams`. Assert the generator
      emits the expected `DiagnosticDescriptor` error (e.g. `BHU_001`) and produces no
      adapter registration for that method.
   b. Non-static method: annotate an instance method with `[SharedAiCondition]`. Assert
      the generator emits a `DiagnosticDescriptor` warning (e.g. `BHU_002`) and skips
      generation for that method.
   c. Unknown field name: the attribute references `nameof(TestDto.NonExistentField)`.
      Assert the generator emits a `DiagnosticDescriptor` error (e.g. `BHU_003`) and
      produces no adapter.
   In each negative case assert that compilation does NOT produce broken pointer casts or
   syntactically invalid source, and that no exception escapes the generator.
6. **Offset calculation edge cases** — all verified via the compound key in the generated
   source:
   a. Sequential struct: `struct Sequential { int A; int B; }` where `B` is at offset 4.
      Assert generated key `"MethodName@4"`.
   b. Explicit-layout struct: `[StructLayout(LayoutKind.Explicit)]` with
      `[FieldOffset(12)] int C`. Assert generated key `"MethodName@12"`.
   c. Nested sequential struct: `struct Outer { int X; Inner Y; }` where `Inner` starts
      at the offset of its parent field `Y`. Assert the correct cumulative offset.
   d. Assert that annotating a C# **property** (not a field) with the attribute
      triggers the `BHU_003`-class diagnostic (properties have no memory offset).
7. **Snapshot test** using `Verify.SourceGenerators` (or equivalent): feed a minimal
   valid annotated method through the generator pipeline and assert the exact emitted
   C# source matches an approved snapshot. The snapshot must cover: the `RegisterCondition`
   call, the `Unsafe.As` / `Unsafe.AddByteOffset` projection, the `static` lambda,
   all required `using` directives, and the compound-key string literal.
8. Existing `Fbt.SourceGen` tests pass.

---

### BHU-013 — Extend `Fhsm.SourceGen` for `[SharedAiCondition]` and `[SharedAiAction]`

**Design ref**: Phase 4 § 4.3, 4.4

**Scope**: `FDP/ExtDeps/FastHSM/src/Fhsm.SourceGen/HsmActionGenerator.cs`

**What to do**:

Extend `HsmActionGenerator` to scan for `SharedAiConditionAttribute` and
`SharedAiActionAttribute` (fully qualified names as in BHU-012). No `ProjectReference` to
`Fbt.Kernel` is needed — scan by name string only.

For each `[SharedAiCondition(typeof(T), "Field")]` method, resolve `offset = offsetOf(T, "Field")`
via Roslyn's semantic model (same struct-layout analysis as BHU-012), then emit:
1. A private static unsafe guard thunk named `Guard_{MethodName}_At{offset}`.
2. Inside the thunk: recover `EntityRepository` via
   `GCHandle.FromIntPtr(bridge->WorldHandle).Target!` (the field is named `WorldHandle`,
   NOT `RepoHandle` — verify against the actual `HsmKernelBridge` struct definition).
   Project `BrainBlackboard.Memory` to the field type at the resolved offset using
   `Unsafe.As` + `Unsafe.AddByteOffset`, then call the shared condition method.
3. In `HsmActionRegistrar.RegisterAll()`, register the thunk with hash computed over
   `"{MethodName}@{offset}"` (SAME hash function as BTree uses for the identical key).

For each `[SharedAiAction(typeof(T), "Field")]` method, emit an action thunk
(`Action_MethodName_AtN`) that discards the `NodeStatus` return value.

**ECS mutation constraint** — emit the following as a comment inside every generated
action thunk body:

```
// CONSTRAINT: Do NOT add or remove ECS components from this thunk.
// Shared action thunks write directly to EntityRepository, bypassing FastHSM's
// deferred HsmCommandWriter. Structural ECS mutations during chunk iteration
// corrupt the chunk arrays. Only read/write fields of existing components.
```

**Constraints**:
- The `bridge->WorldHandle` field name must be verified against the actual
  `HsmKernelBridge` struct definition before emitting thunks.
- The hash for `"MethodName@{N}"` must produce the SAME `ushort` in both
  `Fbt.SourceGen` and `Fhsm.SourceGen` (FNV-1a `ComputeHash` — verify identical
  implementations or extract to a shared helper).
- Because `AllowMultiple = true`, emit one guard/action thunk per attribute instance.

**Success conditions**:
1. `dotnet build Fhsm.SourceGen.csproj` succeeds.
2. Integration test (happy path): add a `[SharedAiCondition(typeof(TestDto), nameof(TestDto.Weapon))]`-
   annotated method where `Weapon` is at offset 16. Verify the generated
   `HsmActionRegistrar.g.cs` contains `Guard_MethodName_At16` and its `RegisterGuard` call.
3. Structural assertion: verify the generated thunk body uses `bridge->WorldHandle`,
   not `bridge->RepoHandle`.
4. Unit test (runtime call-through): invoke the thunk via
   `HsmActionDispatcher.EvaluateGuard(hash, ...)` with a simulated bridge. Verify it
   calls through to the shared condition method.
5. **Negative / diagnostic tests** via `CSharpSourceGeneratorVerifier` (same test
   framework as BHU-012). The EXACT same `DiagnosticDescriptor` identifiers emitted by
   `Fbt.SourceGen` must also be recognized and re-emitted by `Fhsm.SourceGen` when it
   encounters the same malformed inputs:
   a. Mismatched `ref TValue` parameter type → `BHU_001`-class error; no thunk emitted.
   b. Non-static method → `BHU_002`-class warning; thunk skipped.
   c. Unknown field name → `BHU_003`-class error; no thunk emitted.
   Verify in each case that the generated file is syntactically valid (no broken
   `delegate*` casts, no missing semicolons) and that no generator exception propagates.
6. **Offset calculation edge cases** (same matrix as BHU-012, applied to HSM thunks):
   a. Sequential struct field at offset 4 → thunk named `Guard_MethodName_At4`.
   b. Explicit-layout struct field at offset 12 → thunk named `Guard_MethodName_At12`.
   c. Nested sequential struct: correct cumulative offset baked into thunk name and
      `Unsafe.AddByteOffset` call.
   d. Property reference → `BHU_003`-class diagnostic, no thunk.
7. **Hash cross-check**: for the same compound key string (e.g. `"MethodName@16"`),
   assert that `ComputeHash` in `Fbt.SourceGen` and `ComputeHash` in `Fhsm.SourceGen`
   produce the SAME `ushort` value. This must be a dedicated unit test that directly
   calls both `ComputeHash` implementations (or the shared helper if extracted) with
   identical inputs and asserts equality.
8. **Snapshot test** using `Verify.SourceGenerators`: feed a minimal valid annotated
   method through the `HsmActionGenerator` pipeline. The approved snapshot must cover:
   the guard thunk signature (`static unsafe bool Guard_MethodName_At16(...)`), the
   `GCHandle.FromIntPtr(bridge->WorldHandle)` call, the `Unsafe.As` projection, the
   ECS mutation constraint comment, and the `RegisterGuard` invocation.
9. Existing `Fhsm.SourceGen` tests pass.

---

## Phase 5 — Actuator Channel Safety

---

### BHU-014 — Channel safety SourceGen thunks

**Design ref**: Phase 5 § 5.2, 5.3

**Scope**:
- `FDP/ExtDeps/FastBTree/src/Fbt.SourceGen/BTreeActionGenerator.cs`
- `FDP/ExtDeps/FastHSM/src/Fhsm.SourceGen/HsmActionGenerator.cs`

**What to do**:

**BTree (BTreeActionGenerator)**:

For each `[BTreeAction]` or `[SharedAiAction]` method that also has
`[WritesChannel(ChannelKind.Locomotion)]`, wrap the generated delegate:

```csharp
actionRegistry.RegisterAction(
    "{key}",
    static (ref BrainBlackboard bb, BTreeContext ctx) =>
    {
        var status = {OriginalMethod}(ref bb, ctx);
        if (status == NodeStatus.Failure)
        {
            ref var loco = ref ctx.Entity.Get<LocomotionChannel>();
            loco.ActiveAction     = 0;
            loco.ActionInstanceId = unchecked((ushort)(loco.ActionInstanceId + 1));
        }
        return status;
    });
```

Apply analogously for `WeaponChannel` and `InteractionChannel`.

**HSM (HsmActionGenerator)**:

For each `[HsmAction]` or `[SharedAiAction]` method that also has
`[WritesChannel(ChannelKind.Locomotion)]`, emit an additional exit-cleanup thunk
`ExitCleanup_{MethodName}`:

```csharp
private static unsafe void ExitCleanup_{MethodName}(
    void* instancePtr, void* contextPtr, HsmCommandWriter* writer)
{
    var bridge = (HsmKernelBridge*)contextPtr;
    var repo   = (EntityRepository)GCHandle.FromIntPtr(bridge->RepoHandle).Target!;
    ref var loco = ref bridge->Entity.Get<LocomotionChannel>();
    loco.ActiveAction     = 0;
    loco.ActionInstanceId = unchecked((ushort)(loco.ActionInstanceId + 1));
}
```

Register it in `RegisterAll()` under the key `"ExitCleanup_{MethodName}"`.

Also emit a **channel-safety registry** into `HsmActionRegistrar.g.cs`:

```csharp
// Emitted by Fhsm.SourceGen -- one entry per [WritesChannel]-annotated action
public static readonly IReadOnlyDictionary<string, string> RequiredExitCleanups =
    new Dictionary<string, string>
    {
        ["MoveTo"] = "ExitCleanup_MoveTo",
        // ...
    };
```

Extend `HsmGraphValidator` (in `Fhsm.Compiler`) with a channel-safety validation pass.
In `HsmCompiler.Compile()`, after building the state graph, call this extended validator
passing `RequiredExitCleanups`. The validator throws a descriptive error if any state
uses a channel-writing action as `OnEntry` or `Activity` but lacks the corresponding
cleanup as `OnExit`. The error message must name the offending state and the missing
cleanup key.

State machine authors must still explicitly call `.OnExit("ExitCleanup_MoveTo")` in
the `HsmBuilder` chain; the validator enforces this and fails the build if they forget.

**Constraints**:
- The BTree wrapper must preserve the returned `NodeStatus` exactly — only the
  side-effect (channel clear) is added.
- The HSM cleanup thunk name must follow the exact convention `"ExitCleanup_{MethodName}"`
  so state authors can reference it predictably.
- `HsmGraphValidator` changes must not break any existing structural validation.

**Success conditions**:
1. Both `Fbt.SourceGen.csproj` and `Fhsm.SourceGen.csproj` build successfully.
2. Unit test (BTree, failure path): invoke a `[WritesChannel(Locomotion)]` BTree action
   that returns `NodeStatus.Failure`. Assert `LocomotionChannel.ActiveAction == 0` and
   `ActionInstanceId` was incremented.
3. Unit test (BTree, non-failure path): same action returning `NodeStatus.Running`.
   Assert channel is unchanged.
4. Unit test (HSM): call `ExitCleanup_MoveTo` thunk directly via `HsmActionDispatcher`.
   Assert `LocomotionChannel.ActiveAction == 0` and `ActionInstanceId` was incremented.
5. Unit test (validator — missing cleanup): build a state machine that uses `MoveTo` as
   `Activity` but omits `OnExit`. Assert `HsmCompiler.Compile()` throws an error naming
   the offending state and the missing `"ExitCleanup_MoveTo"` key.
6. Unit test (validator — correctly wired): same machine with
   `.OnExit("ExitCleanup_MoveTo")`. Assert `HsmCompiler.Compile()` succeeds.
7. **Registry deduplication tests**:
   a. Two separate `[HsmAction]` methods (e.g. `MoveTo` and `Cruise`) both annotated
      with `[WritesChannel(ChannelKind.Locomotion)]`. Assert `RequiredExitCleanups`
      contains exactly two entries: `["MoveTo"] = "ExitCleanup_MoveTo"` and
      `["Cruise"] = "ExitCleanup_Cruise"` — no duplicates.
   b. One `[SharedAiAction]` method with two `[WritesChannel]` attributes for
      different channels (e.g. Locomotion and Weapon). Assert the method appears ONCE
      in `RequiredExitCleanups` (keyed by action name) with its primary cleanup thunk;
      multi-channel cleanup logic is handled inside the single `ExitCleanup_MethodName`
      thunk body, not via duplicate dictionary entries.
   c. The same method registered under two different `[SharedAiAction]` DTO attributes
      (two distinct attribute instances, same method name). Assert the generated
      `RequiredExitCleanups` deduplicates entries by action-name key, not by attribute
      instance — each unique method name appears at most once.
   d. Assert the generated `RequiredExitCleanups` dictionary initializer is
      syntactically valid C# (parseable without error by Roslyn) and that all string
      literals follow the `"ExitCleanup_{MethodName}"` convention exactly.
8. **BTree channel wrapper snapshot test** using `Verify.SourceGenerators`: feed a
   `[BTreeAction, WritesChannel(Locomotion)]`-annotated method through
   `BTreeActionGenerator`. The approved snapshot must cover: the wrapping lambda
   structure, the `NodeStatus.Failure` branch, the `unchecked((ushort)(...+1))`
   increment, and the `ref var loco = ref ctx.Entity.Get<LocomotionChannel>()` call.
9. All generator tests pass.

---

## Task Dependency Map

```
BHU-001 (add Fhsm refs)
  |
  +-- BHU-003 (coordinator)
  |     |
  |     +-- BHU-004 (wire into Editor)  <-- also needs BHU-002
  |
BHU-002 (ClearAll)
  |
  +-- BHU-003

BHU-005 (IsFinal in compiler)
  |
  +-- BHU-006 (IsFinal in kernel)
        |
        +-- BHU-007 (HsmTickSystem terminal detection)
        |
        +-- BHU-016 (BehaviorIngressSystem HSM reset)

BHU-008 (CognitiveInterruptSystem)
  |
  +-- BHU-009 (HsmTickSystem interrupt ingestion)
  |     |
  |     +-- BHU-010 (CognitiveRuntimeModule wiring)  <-- also needs BHU-015
  |
  +-- BHU-015 (CognitiveCleanupSystem)
        |
        +-- BHU-010

BHU-011 (attributes)
  |
  +-- BHU-012 (Fbt.SourceGen extension)
  |
  +-- BHU-013 (Fhsm.SourceGen extension)
        |
        +-- BHU-014 (channel safety)  <-- also needs BHU-012
```

Phases 1 and 2 can be developed in parallel.
Phases 3 and 4 can be developed in parallel with each other and with Phases 1 and 2.
Phase 5 depends on Phase 4 (BHU-011 must exist before BHU-014).
BHU-015 and BHU-016 can be developed in parallel with Phases 1, 2, and 4.
BHU-017 depends on all preceding tasks.

---

## Integration Tests

---

### BHU-017 — End-to-end integration tests proving all unified features work together

**Design ref**: All phases.

**Scope**:
- New file: `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/Integration/BhuIntegrationTests.cs`
- New file: `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Integration/HsmTerminalStateIntegrationTests.cs`
- New file: `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Integration/HsmSourceGenIntegrationTests.cs`
- New file: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/HsmBehaviorIntegrationTests.cs`

All four files must compile and all tests must pass with zero errors against the
implementation produced by BHU-001 through BHU-016.

**Dependency**: BHU-017 depends on ALL prior tasks in this workstream (BHU-001 to
BHU-016). Do NOT implement any of these tests until those tasks are complete and build
succeeds with zero errors.

---

#### Group A — HSM Terminal State Routing (proves BHU-005 + BHU-006 + BHU-007 + BHU-016)

**File**: `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/Integration/BhuIntegrationTests.cs`

Use `TestWorldFactory.Create()` (already in `Fdp.Toolkits.Tests`) to build an
`EntityRepository` with all behavior components registered.

Helper: build a minimal 3-state HSM blob with `IsFinal` set on the third state:

```
State "Idle"  (initial) --EventX(id=10)--> State "Active"
State "Active"           --EventY(id=20)--> State "Done"
State "Done"  (final, IsFinal=true)
```

Construct the blob using `HsmBuilder` + `HsmCompiler.Compile()` so the test exercises
the real compiler path and proves `StateFlags.IsFinal` is correctly emitted.

**IT-BHU-A1 — HSM reaches final state, `BehaviorFinishedEvent` published**

```
Arrange:
  entity with BehaviorState(BrainTierHsm, InstanceId=1), BrainHsm128, BrainBlackboard
  HsmTickSystem<BrainHsm128> wrapping the blob above
  Manually inject EventX then EventY into the HSM event queue before ticking

Act:
  Execute HsmTickSystem<BrainHsm128> once

Assert:
  repo.Bus.Read<BehaviorFinishedEvent>() contains exactly one event with Entity == entity
  InstanceFlags.Terminated bit is CLEAR (published + latch cleared in same tick)
  InstanceHeader.Phase == InstancePhase.Idle (latch reset)
```

**IT-BHU-A2 — Second tick on same instance does NOT re-publish event (dedup)**

```
Arrange: same as IT-BHU-A1 after the entity has just finished

Act:
  Execute HsmTickSystem<BrainHsm128> a second time (no new behavior assigned)

Assert:
  No new BehaviorFinishedEvent published this tick (dedup suppressed it)
  _publishedTerminalForInstanceId[entity.Index] == 1 (InstanceId unchanged)
```

**IT-BHU-A3 — Behavior reassignment clears terminal latch, allows new event**

```
Arrange: entity finished behavior A (InstanceId=1, TerminatedFlag cleared by A1)

Act:
  Publish AssignBehaviorHashEvent for behavior B
  Execute BehaviorIngressSystem (bumps InstanceId to 2, resets BrainHsm128 via BHU-016)
  Drive new behavior to final state (inject EventX + EventY again)
  Execute HsmTickSystem<BrainHsm128>

Assert:
  BehaviorFinishedEvent published with InstanceId dedup key == 2
  BrainHsm128 ActiveLeafIds were all 0xFFFF before first tick of behavior B
    (proves BHU-016 reset ran)
```

**IT-BHU-A4 — BrainHsm64 also publishes event (covers both instance sizes)**

Identical to IT-BHU-A1 but using `BrainHsm64` and `HsmTickSystem<BrainHsm64>`.

Assert the event is published and the latch is cleared.

---

#### Group B — Cognitive Interrupt Decoupling (proves BHU-008 + BHU-009 + BHU-015)

**File**: `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/Integration/BhuIntegrationTests.cs`
(same file, continuation)

Helper: three systems run in order on the same `EntityRepository`:
`CognitiveInterruptSystem`, `HsmTickSystem<BrainHsm128>`, `CognitiveCleanupSystem`.

Build an HSM blob where `EventId_MobilityLost` (id=1) causes a transition from
"Patrol" → "Stopped".

**IT-BHU-B1 — Mobility-lost edge writes byte 126 and HSM receives the event**

```
Arrange:
  entity with BrainBlackboard, ActorCapabilityState(CanMove=true),
  PreviousCapabilities(CanMove=true), BrainHsm128 in "Patrol" state

Act Frame 1:
  Set ActorCapabilityState.Capabilities &= ~CanMove  (capability lost)
  Run CognitiveInterruptSystem

Assert mid-frame:
  bb.Memory[126] == 1

Act Frame 1 continued:
  Run HsmTickSystem<BrainHsm128>
  Run CognitiveCleanupSystem

Assert end-of-frame:
  bb.Memory[126] == 0  (cleanup zeroed it)
  HSM transitioned from "Patrol" to "Stopped"
    (confirm by reading ActiveLeafIds via Unsafe.As cast)
```

**IT-BHU-B2 — No re-trigger on second frame (edge, not level)**

```
Arrange: entity still has CanMove=false; PreviousCapabilities already updated to false

Act Frame 2:
  Run CognitiveInterruptSystem (PreviousCapabilities == current, no edge)
  Run HsmTickSystem<BrainHsm128>
  Run CognitiveCleanupSystem

Assert:
  bb.Memory[126] == 0 throughout (no byte set, no spurious event)
  HSM remains in "Stopped" state (no second transition)
```

**IT-BHU-B3 — BTree entity also gets byte 126 cleared (brain-tier-agnostic cleanup)**

```
Arrange:
  BTree entity with BrainBlackboard; force bb.Memory[126] = 1 directly

Act:
  Run CognitiveCleanupSystem

Assert:
  bb.Memory[126] == 0
```

---

#### Group C — Unified Action/Condition via SharedAiAttributes (proves BHU-011 + BHU-012 + BHU-013)

**File**: `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Integration/HsmSourceGenIntegrationTests.cs`

This group proves the cross-paradigm adapter pipeline is correctly wired at runtime.
It does NOT use a Roslyn test verifier (that belongs in unit tests for BHU-012/013).
Instead it exercises the GENERATED code after compilation.

Define in the test assembly a shared condition method:

```csharp
// DTO placed at bb.Memory[0]
[StructLayout(LayoutKind.Sequential)]
private struct TestDto { public int Value; }  // Value at offset 0

[SharedAiCondition(typeof(TestDto), nameof(TestDto.Value))]
private static bool IsValuePositive(ref int value, Entity self, EntityRepository repo)
    => value > 0;
```

After compilation the generators will have emitted:
- In `FbtActionRegistrar.g.cs`: `actionRegistry.RegisterCondition("IsValuePositive@0", ...)`.
- In `HsmActionRegistrar.g.cs`: `HsmActionDispatcher.RegisterGuard(hash("IsValuePositive@0"), ...)`.

**IT-BHU-C1 — BTree adapter calls through to the shared condition**

```
Arrange:
  ActionRegistry with generated FbtActionRegistrar.RegisterAll called
  BrainBlackboard with Memory[0..3] = BitConverter.GetBytes(42)  (positive value)

Act:
  Retrieve condition via actionRegistry.TryGetCondition("IsValuePositive@0")
  Invoke it with a mock BTreeContext

Assert:
  Returns true (42 > 0)
```

```
Arrange: same but Memory[0..3] = BitConverter.GetBytes(-1)

Assert: Returns false
```

**IT-BHU-C2 — HSM guard adapter calls through to the shared condition**

```
Arrange:
  Call HsmActionDispatcher.ClearAll() then generated HsmActionRegistrar.RegisterAll()
  Construct a fake HsmKernelBridge with a live EntityRepository via GCHandle
  entity has BrainBlackboard with Memory[0..3] = BitConverter.GetBytes(5)

Act:
  ushort hashKey = ComputeHash("IsValuePositive@0")
  bool result = HsmActionDispatcher.EvaluateGuard(hashKey, instancePtr, bridgePtr, eventId=0)

Assert:
  result == true  (5 > 0)
```

**IT-BHU-C3 — Both adapters agree on the compound key hash**

```
Assert:
  The ushort produced by Fbt.SourceGen's ComputeHash("IsValuePositive@0")
    equals the ushort produced by Fhsm.SourceGen's ComputeHash("IsValuePositive@0")
```

Call both `ComputeHash` implementations directly (or the shared helper if extracted).
This is the cross-generator hash-consistency check required by BHU-013 success condition 7.

---

#### Group D — ClearAll / hot-reload round-trip (proves BHU-002 + generated ClearAll)

**File**: `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Integration/HsmTerminalStateIntegrationTests.cs`

**IT-BHU-D1 — ClearAll removes previously registered guards**

```
Arrange:
  Register a guard under key id=1234 via HsmActionDispatcher.RegisterGuard(1234, ptr)

Act:
  HsmActionDispatcher.ClearAll()

Assert:
  HsmActionDispatcher.EvaluateGuard(1234, null, null, 0) == true
    (default "no guard = always pass" fallback — guard table is empty)
```

**IT-BHU-D2 — ClearAll then RegisterAll restores all guards**

```
Arrange:
  Call HsmActionDispatcher.ClearAll()
  Call generated HsmActionRegistrar.RegisterAll()  (re-populates tables)

Act:
  Evaluate a guard that was originally registered in Fhsm.Kernel's built-in methods

Assert:
  The guard is correctly dispatched (does not fall through to default)
```

**IT-BHU-D3 — ClearAll → RegisterAll → IsFinal chain produces correct Terminated flag**

End-to-end: reload sequence followed by advancing to final state.

```
Arrange:
  HsmActionDispatcher.ClearAll()
  HsmActionRegistrar.RegisterAll()
  Build blob with final state using HsmBuilder.Final()
  BrainHsm128 entity initialized to the blob

Act:
  Inject events to drive machine to final state
  HsmKernel.Update(blob, ref component, bridge, dt)

Assert:
  (InstanceHeader.Flags & InstanceFlags.Terminated) != 0
```

---

#### Group E — Full CognitiveRuntimeModule frame (system order + all features together)

**File**: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/HsmBehaviorIntegrationTests.cs`

This is the highest-level integration test. It runs the complete `CognitiveRuntimeModule`
system sequence (all 6 systems) against real entities and proves the whole feature set
integrates without regression.

**IT-BHU-E1 — CognitiveRuntimeModule system registration order after BHU-010**

```
Arrange:
  new CognitiveRuntimeModule(new BehaviorRegistry())

Assert (exact system types at exact indices):
  [0] ChannelArbitrationSystem
  [1] CognitiveInterruptSystem     (NOT HsmDamageBridgeSystem)
  [2] BTreeTickSystem
  [3] HsmTickSystem<BrainHsm128>
  [4] HsmTickSystem<BrainHsm64>
  [5] CognitiveCleanupSystem

  Total count == 6
  No HsmDamageBridgeSystem anywhere in the list
```

**IT-BHU-E2 — Full frame: HSM entity mobility-lost interrupt → behavior finishes → event published**

This test runs all 6 systems in correct order to prove the complete feature set integrates.

```
Arrange:
  EntityRepository with TestWorldFactory + BehaviorRegistry
  Build HSM blob: "Patrol" --MobilityLost(id=1)--> "Stopped" --EventDone(id=99)--> "Done"(final)
  Register blob as behavior "HsmPatrol" with BrainTierHsm
  entity: BehaviorState(HsmPatrol, InstanceId=1), BrainHsm128 initialized to blob,
          BrainBlackboard, ActorCapabilityState(CanMove=true),
          PreviousCapabilities(CanMove=true)

Act Frame 1: trigger mobility-lost edge
  Set ActorCapabilityState.Capabilities &= ~CanMove
  Run all 6 systems in order (CognitiveInterrupt, BTreeTick, HsmTick128, HsmTick64, Cleanup)

Assert end of Frame 1:
  HSM transitioned to "Stopped" (verify ActiveLeafIds)
  bb.Memory[126] == 0 (CognitiveCleanupSystem ran)

Act Frame 2: drive "Stopped" to final state
  Inject EventDone (id=99) into HSM event queue
  Run all 6 systems in order

Assert end of Frame 2:
  repo.Bus.Read<BehaviorFinishedEvent>() contains one event for this entity
  InstanceFlags.Terminated is CLEAR (latch was cleared after publish)
  InstanceHeader.Phase == InstancePhase.Idle
```

**IT-BHU-E3 — BTree entity in same world is unaffected by HSM interrupt path**

```
Arrange: same world as IT-BHU-E2, plus a second entity with BrainBTreeState + BTreeTickSystem
         BTree behavior returns NodeStatus.Running unconditionally

Act Frame 1 (same mobility-lost frame as E2):
  Run all 6 systems

Assert:
  BTree entity's BrainBTreeState.State.RunningNodeIndex is unchanged (not disrupted)
  BTree entity's bb.Memory[126] is 0 after frame (CognitiveCleanupSystem cleared it)
  No BehaviorFinishedEvent for the BTree entity
```

---

#### What to do (implementation instructions)

1. **Create `BhuIntegrationTests.cs`** in
   `FDP/Toolkits/Fdp.Toolkits.Tests/Behavior/Integration/` (create directory if absent).
   Add all Group A and Group B tests. Use `TestWorldFactory.Create()` for world setup.
   Use `HsmBuilder` + `HsmCompiler.Compile()` for blobs — do NOT hard-code raw
   `StateDef[]` arrays; the compiler exercises the IsFinal path.

2. **Create `HsmTerminalStateIntegrationTests.cs`** in
   `FDP/ExtDeps/FastHSM/tests/Fhsm.Tests/Integration/` (create directory if absent).
   Add all Group D tests.

3. **Create `HsmSourceGenIntegrationTests.cs`** in the same `Fhsm.Tests/Integration/`
   directory. Add Group C tests.
   - The test assembly must define the `TestDto` struct and the `IsValuePositive` method
     with `[SharedAiCondition]` so the source generators emit adapters into the test
     build. Verify in `obj/` that the generated files appear before writing assertions.
   - For IT-BHU-C3, call `ComputeHash` from `Fbt.SourceGen`'s `BTreeActionGenerator`
     and from `Fhsm.SourceGen`'s `HsmActionGenerator` via `InternalsVisibleTo` or by
     extracting to a shared test helper.

4. **Create `HsmBehaviorIntegrationTests.cs`** in
   `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/`.
   Add all Group E tests.
   The existing `[Collection("EditorOfflineTests")]` collection fixture is already defined
   in the project; add a new `[Collection("HsmBehaviorTests")]` if isolation is needed.

5. **Update `CognitiveRuntimeModuleTests.cs`** (existing file in
   `Fdp.Toolkits.Tests/Behavior/Modules/`) to assert the NEW system order (6 systems,
   `CognitiveInterruptSystem` at index 1, `CognitiveCleanupSystem` at index 5, no
   `HsmDamageBridgeSystem`). The old assertion in that file will fail after BHU-010 and
   must be updated in this task.

**Constraints**:
- All test methods must use the `Xunit` framework (`[Fact]`).
- No test may use `Thread.Sleep` or real-time waits. All timing must be expressed as
  fixed `deltaTime` values passed to system `Execute()` methods.
- Tests must call `world.Dispose()` (or use `using`) to avoid native memory leaks.
- Do NOT introduce a new test project. Use the existing `Fdp.Toolkits.Tests`,
  `Fhsm.Tests`, and `Hrot.ClusterRunner.Integration.Tests` projects.
- The BHU-E tests run inside `Hrot.ClusterRunner.Integration.Tests` which has access to
  `Hrot.AI.Behaviors` and full ECS bootstrap infrastructure.
- Do not add dependencies that are not already transitively available in each test project.

**Success conditions**:
1. All tests compile with zero errors against the post-BHU-001-to-BHU-016 codebase.
2. All tests pass: `dotnet test` on each of the three affected projects shows 0 failures.
3. IT-BHU-A1 to A4 confirm the full IsFinal → Terminated → BehaviorFinishedEvent chain.
4. IT-BHU-B1 to B3 confirm edge-triggered interrupt byte, HSM event injection, and
   single-frame pulse cleanup for both brain tiers.
5. IT-BHU-C1 to C3 confirm that `[SharedAiCondition]`-annotated methods are callable
   from both the BTree adapter (`actionRegistry`) and the HSM guard dispatcher
   (`HsmActionDispatcher.EvaluateGuard`), and that both adapters hash the compound key
   identically.
6. IT-BHU-D1 to D3 confirm that `HsmActionDispatcher.ClearAll()` genuinely empties the
   tables and that the full ClearAll → RegisterAll → IsFinal chain works end-to-end.
7. IT-BHU-E1 confirms the exact 6-system order with no `HsmDamageBridgeSystem`.
8. IT-BHU-E2 and E3 confirm the complete frame-level interaction between all phases
   without cross-contamination between HSM and BTree entities.
9. The updated `CognitiveRuntimeModuleTests` assertion passes (6 systems, correct types
   at correct indices).
