# EQS sensor lifecycle: design options for architect review

## Context

The EQS v1.3 design uses `EqsSensor` as an ECS component on the Brain-side entity, replicated downward to Muscle via standard component replication. Sensor lifetime equals component lifetime: when the component is removed on Brain, the Muscle solver drops the query on its next tick.

The remaining question is **who removes the component when it is no longer wanted**, particularly when the Brain's BTree shifts execution to a branch that no longer needs the sensor (intra-behavior branch switch, e.g. via `ObserverSelector`). FastBTree action nodes are stateless static delegates and do not receive `OnExit` / `OnAbort` callbacks, so the action that created the sensor cannot directly observe its own deactivation.

The architect proposed a **tick-lease** pattern: the sensor carries a `LastPolledTick` field, the BTree action and `WaitForSensor` decorator stamp it on every tick they execute, and a Brain-side `EqsSensorCleanupSystem` running after `BTreeTickSystem` removes any sensor whose lease is stale.

This document compares the tick-lease pattern with the conventions used by shipping AAA engines, and proposes options that better balance elegance against the engine's stateless-BTree performance commitment.

---

## How AAA engines handle this

Three idiomatic patterns appear in shipping engines:

1. **Stateful task nodes with lifecycle callbacks** (Unreal's `UBTTaskNode`).
   Task nodes have `ExecuteTask`, `TickTask`, and `AbortTask`. When the BTree's execution pointer leaves a task — branch switch, observer fires, parent aborts — `AbortTask` is called and the task cleans up its own resources (EQS queries, target reservations, etc.). The BTree framework maintains an active-tasks list and walks it during deactivation. **No user code is needed for cleanup; the framework guarantees it.** This is the cleanest solution but requires per-instance node state and virtual dispatch.

2. **Subtree-scoped services** (Unreal's `UBTService`).
   A service is attached to a composite node (sequence, selector). The framework starts the service when the composite is activated and stops it when execution leaves the composite. Standing EQS queries are typically owned by services rather than tasks, so their lifetime matches the cognitive scope of the parent subtree, not the individual leaf node. Cleanup is automatic because composites already track their own activation state.

3. **Planner-managed desires** (GOAP, utility AI, Halo's behavior DAG).
   There is no "tree pointer"; behaviors register desires that a planner satisfies. EQS-like queries are ongoing facts the planner consults. Cleanup happens when the desire leaves the active plan, which the planner already tracks. Out of scope for this engine but worth knowing exists.

## Why the architect's tick-lease is a reasonable workaround

It does not require BTree framework changes. It is idiomatic ECS — "watch a component, sweep it when stale" matches the existing `CognitiveCleanupSystem` pattern for transient interrupt bytes. It cleans up at one-frame granularity, which is acceptable for sensor lifetime. The 4-byte `LastPolledTick` field is cheap.

## Where the tick-lease falls short of AAA elegance

Three concrete issues.

**Discipline burden.** Every BTree action and decorator that uses a sensor must remember to stamp `LastPolledTick` on every tick it returns `Running`. Forgetting it once causes silent removal on the next tick — a hard-to-debug failure mode where the sensor appears to "vanish" with no obvious cause. The compiler cannot help.

**Conflates "in use" with "executed this frame."** A BTree node behind a throttle decorator that ticks every N frames would lose its sensor. The lease must be made tolerant of an N-tick gap, but choosing N is another configuration knob, and a single conservative value either wastes solver budget (large N) or risks false-positive removal (small N).

**Cannot easily express subtree-scoped sensors.** A common AAA pattern is "the whole 'find cover and shoot' subtree wants a cover sensor running" — Unreal expresses this via a service attached to the parent composite. The tick-lease forces sensors to be owned by individual leaf actions; subtree-scoped ownership requires every leaf in the subtree to touch the sensor, which is brittle.

**Subtle correctness issue:** if `WaitForSensor` is `Running` because no result has yet arrived, and the BTree is LOD-throttled or skips a tick for any reason, the sensor is removed, the Muscle solver discards its in-flight evaluation, and the next tick has to start over. A pathological interaction can prevent the sensor from ever completing if BTree ticking is intermittent.

---

## Options for the engine

Listed in order from "least invasive to BTree" to "most elegant."

### Option 1 — Tick-lease as-is

The architect's proposal. Implement `EqsSensorCleanupSystem` and require all sensor-using actions/decorators to stamp the lease.

**Pros:** zero BTree changes; pure ECS.
**Cons:** all four issues above.

### Option 2 — Tick-lease with a discoverability helper

Same mechanism, but provide a `SensorScope` helper that makes the lease impossible to forget:

```csharp
public static NodeStatus Action_FindCover(in BTreeContext ctx, ref FindCoverParams p)
{
    using var scope = ctx.SensorScope<EqsSensor>();   // stamps lease, ensures removal on dispose
    // ... action body ...
    if (scope.IsReady(out var result)) {
        // use result
        return NodeStatus.Success;
    }
    return NodeStatus.Running;
}
```

The scope is a `ref struct` allocated on the stack. Entry stamps the lease; `Dispose` releases the held reference (no allocation, no GC pressure). The structure preserves the stateless-static-delegate signature of the action — the scope lives only on the call stack and disappears on return. A Roslyn analyzer can enforce that any code touching `EqsSensor` does so through a scope.

This addresses the discipline burden without changing the BTree framework. It does not address subtree scoping or the LOD/throttle interaction.

**Pros:** still zero BTree changes; impossible to forget the lease; analyzable; analyzers can warn on misuse.
**Cons:** still leaf-scoped; still tick-granularity sensitive.

### Option 3 — Composite-anchored sensor leases (subtree scoping without BTree state)

The most elegant option that I think might still respect the stateless-static-delegate constraint, if the architect agrees the mechanism is sound.

Observation: composite nodes (Sequence, Selector, ObserverSelector) already track which child is currently active via the execution pointer. The path from the root to the current leaf — the "active path" — is implicitly known each tick because the BTree walks the tree to reach the current leaf.

A sensor declared as "subtree-scoped" at composite node N is alive iff N is on the active path. The cleanup system, instead of comparing `LastPolledTick`, checks each tick whether the owning composite is still on the active path:

```csharp
public struct EqsSensor
{
    // ... existing fields ...
    public NodeIndex OwningCompositeNode;   // index in the BTree blob; 0 = leaf-scoped
    public byte OwningCompositeDepth;       // depth at which the owner sits
}
```

The cleanup system queries `BTreeState` for the entity and walks from the root following the per-node "currently active child" pointer. If `OwningCompositeNode` is reached on this walk, the sensor is kept. If not, it is removed.

This is no more expensive than the tick-lease (a constant-time walk of depth ≤ tree depth, typically 4-8 nodes), and it expresses subtree-scoped lifetime without adding lifecycle callbacks to the framework. The BTree itself stays stateless — only the sensor knows its anchor.

**Pros:** no BTree framework changes; subtree-scoped lifetime expressible; not sensitive to per-tick execution because the active path is the source of truth, not a "I was touched this tick" flag; LOD throttling doesn't affect it as long as the BTree state isn't reset.
**Cons:** requires the cleanup system to inspect BTree state, adding coupling between EQS cleanup and BTree internals (the active-child-pointer layout). The cleanup must be re-thought if the BTree changes how it tracks the active path.

### Option 4 — Hybrid lifecycle hook for resource-owning actions only

A targeted relaxation of "all actions are pure stateless static methods." Most actions stay stateless. A small annotated category — actions that allocate cleanup-requiring resources (sensors, raycasts, target reservations) — get a single additional lifecycle hook:

```csharp
[ResourceOwningAction]
public static class Action_FindCover
{
    public static NodeStatus Tick(in BTreeContext ctx, ref FindCoverParams p) { /* ... */ }
    public static void OnDeactivate(in BTreeContext ctx) { /* remove sensor */ }
}
```

The BTree blob carries a "resource-owning" flag bit per node, set at blueprint compile time by the annotation. The BTree, when its execution pointer leaves a resource-owning node (for any reason: completion, abort, branch switch), calls the `OnDeactivate` static method. The vast majority of BTree nodes pay nothing — no instance state, no callback. Only the small fraction of nodes that own resources participate.

The "active path" tracking already exists in some form because the BTree must know what to execute each tick. Detecting transitions in this path costs one comparison per tick per active branch.

**Pros:** the AAA-standard solution, properly scoped. Authors of resource-owning actions write the cleanup code explicitly, which is correct because resource cleanup is genuinely action-specific knowledge. No discipline burden — `OnDeactivate` is called automatically.
**Cons:** introduces a per-frame "active path delta" computation to FastBTree, which the architect must validate against the engine's performance budget. It is the largest BTree change of the four options.

---

## Recommendation

If FastBTree's per-frame overhead can absorb the active-path tracking required, **Option 4 (hybrid resource-owning actions)** is the AAA-correct solution and would put the engine on equal footing with Unreal-style lifecycle. It is the cleanest design.

If active-path tracking is unacceptable but BTree-state inspection from a cleanup system is acceptable, **Option 3 (composite-anchored leases)** offers subtree scoping at near-zero BTree cost.

If BTree changes of any kind are out of scope for v1, **Option 2 (tick-lease with `SensorScope` helper)** captures the value of the architect's proposal while removing the discipline burden. It is a strictly better presentation of Option 1.

**Option 1 as written should not ship.** The discipline burden and the silent-failure mode are real correctness hazards, and the helper in Option 2 costs almost nothing to add.

---

## Questions for the architect

1. Is the per-tick overhead of "compute active-path delta" feasible within FastBTree's budget? If yes, Option 4 is on the table.

2. Does FastBTree's blueprint blob already store enough information about each composite's currently active child that a cleanup system could walk the active path in O(depth) per entity? If yes, Option 3 is feasible.

3. If neither, is Option 2's `SensorScope` helper + Roslyn analyzer an acceptable v1 solution, with Option 3 or 4 deferred to a future BTree iteration?

4. Is there an existing pattern in the engine for "resource-owning" leaf nodes that I have missed? (Channel actions like `LocomotionChannel` arbitrate via `BehaviorInstanceId`, but that catches behavior switches, not intra-behavior branch shifts. Same problem applies to sensors.)

5. The LOD/throttle interaction with the tick-lease (silent removal under intermittent BTree ticking) — is this a real concern for the engine's expected deployment patterns, or is BTree ticking guaranteed to be uninterrupted while a behavior is active?







I agree with your assessment; the tick-lease pattern as originally proposed has fatal flaws, specifically its pathological interaction with LOD throttling and its inability to elegantly handle subtree-scoped queries. Option 2 mitigates the developer discipline burden by using a `SensorScope` ref struct, but it fundamentally fails to solve the frame-skip and subtree limitations.

While Option 3 (Composite-anchored leases) avoids changing the BTree framework, it introduces unacceptable architectural coupling. Having an external ECS cleanup system directly inspect the BTree's internal active-path state violates our strict module boundaries and would require the cleanup logic to be rewritten if the BTree state layout ever changes.

Therefore, **Option 4 (Hybrid lifecycle hook)** is the approved path forward. It represents the AAA-correct solution for managing resource-heavy operations like EQS sensors, raycasts, and target reservations. Because our engine heavily utilizes Roslyn source generation, implementing this hybrid approach aligns perfectly with our architecture. You will introduce an annotation that sets a "resource-owning" flag bit in the BTree blob at compile time. The runtime will check this bit and invoke the static `OnDeactivate` method only when the execution pointer leaves a flagged node. This completely preserves the blazing-fast, stateless execution model for the vast majority of your action nodes, while guaranteeing deterministic, immediate cleanup of the `EqsSensor` component when a branch aborts or switches.



cool, can we dig deeper into Option 4?


Option 4 provides a targeted relaxation of our strict "all actions are pure stateless static methods" rule to specifically handle resource-heavy operations like EQS sensors, raycasts, and target reservations. Instead of forcing every node to carry instance state or lifecycle callbacks, most actions remain perfectly stateless.

Here is a deeper breakdown of how we would implement this hybrid approach:

**1. Compile-Time Annotation and Blob Flagging**
You will introduce a specific annotation to mark actions that require cleanup. At blueprint compile time, our Roslyn source generator will detect this annotation and set a dedicated "resource-owning" flag bit directly in the compiled behavior tree blob for that specific node.

**2. The Static `OnDeactivate` Hook**
For these flagged nodes, you will implement an additional static `OnDeactivate` lifecycle hook. The cleanup code is explicitly written by the action's author, which is the correct place for it and guarantees correctness without placing a constant discipline burden on developers to manually manage tick leases. 

**3. Active-Path Delta Tracking**
At runtime, the BTree framework already inherently tracks the "active path" from the root to the current leaf to know what it must execute each tick. We will modify the BTree executor to compute a delta on this active path each frame. When the execution pointer leaves a node that has the "resource-owning" bit set—whether due to natural completion, an abort, or a higher-priority branch switch—the framework will automatically invoke its `OnDeactivate` method.

**4. Performance Profile**
The elegance of this design is that the vast majority of BTree nodes pay absolutely no penalty, meaning they have no instance state and require no callback. Only the small fraction of nodes that actually own resources participate. The runtime cost for the framework is minimal, requiring only one comparison per tick per active branch to detect these path transitions. 

As long as FastBTree's per-frame performance budget can absorb this active-path tracking cost, it represents the AAA-standard solution and the cleanest overall design for our engine.



ok, lets go even deeper. let's focus on changing the BTree executor and how to register the OnDeactivate lifecycle hook. What whould it look like? (example of the registration of the hook and the hook itself)


To implement the hybrid lifecycle hook under Option 4, we must first define a static, stateless deactivation delegate that mirrors our standard action signature but returns `void`. Because FastBTree actions receive a `BTreeContext` containing the `EntityRepository` and the entity handle (`Self`), your hook will use this context to physically remove the `EqsSensor` component. You will annotate this cleanup method so our Roslyn source generator can discover it alongside your actions at compile time. 

Here is what the hook and its annotation will look like in your domain code:

```csharp
[BTreeDeactivator(TargetAction = "Action_FindCover")]
public static void Deactivate_FindCover(
    ref BrainBlackboard bb,
    ref BehaviorTreeState state,
    ref BTreeContext ctx,
    int paramIndex)
{
    if (ctx.World.HasComponent<EqsSensor>(ctx.Self))
    {
        ctx.World.RemoveComponent<EqsSensor>(ctx.Self);
    }
}
```

To wire this into the runtime, we will extend the `ActionRegistry<TBlackboard, TContext>` to map string names to these deactivation delegates. During hot-reload and initial staging, the Roslyn-generated `[BlueprintRegistrar]` class will automatically emit calls to `registry.RegisterDeactivator("Action_FindCover", Deactivate_FindCover)`, preserving our uniform, allocation-free registration pipeline.

Inside the BTree framework, the compiler will set a dedicated "resource-owning" flag bit within the 8-byte `NodeDefinition` struct for any node mapped to a deactivator. During execution, the `Interpreter.Tick` method already tracks the execution path from the root to the current leaf using the `BehaviorTreeState.NodeIndexStack` and `RunningNodeIndex`. We will modify the interpreter to compute a delta between the previous frame's active path and the newly evaluated path. 

When the framework detects that the execution pointer has left a node possessing the "resource-owning" bit—whether due to natural completion, an observer abort, or a higher-priority branch switch—it will retrieve the deactivator from the registry array via the node's `PayloadIndex` and invoke your static hook. This ensures your EQS sensors are swept deterministically on the exact frame they are abandoned, without adding instance state or virtual dispatch overhead to the rest of the behavior tree.




ok, can you pls detail the necessary btree modification and suggest new tests that prove it works? Plain modification of FastBTree library and its tests. To first prove it works in isolation aon only then we will start integrating into oour engine and start using roslyn generators.



To implement the hybrid lifecycle hook in isolation within the `FastBTree` library, we need to modify the registry, the bytecode definition, and the interpreter's execution loop. Because `FastBTree` heavily restricts memory layout to maintain its performance budget, the active-path delta tracking must be strictly $O(depth)$ and zero-allocation.

Here is the exact modification plan for the `FastBTree` library.

### 1. `FastBTree` Library Modifications

**A. Define the Deactivator Delegate and Update the Registry**
First, introduce the stateless deactivation delegate signature and add it to `ActionRegistry<TBlackboard, TContext>`.

```csharp
namespace Fbt
{
    public delegate void NodeDeactivatorDelegate<TBlackboard, TContext>(
        ref TBlackboard blackboard,
        ref BehaviorTreeState state,
        ref TContext context,
        int paramIndex)
        where TBlackboard : struct
        where TContext : struct, IAIContext;
}
```
In `ActionRegistry.cs`:
*   Add `private readonly Dictionary<string, NodeDeactivatorDelegate<TBlackboard, TContext>> _deactivators = new();`
*   Add `public void RegisterDeactivator(string methodName, NodeDeactivatorDelegate<TBlackboard, TContext> deactivator)`
*   Add `public bool TryGetDeactivator(string methodName, out NodeDeactivatorDelegate... )`.

**B. Update the Bytecode and Interpreter Initialization**
We must flag nodes that own resources without breaking the cache-friendly nature of the bytecode. 

In `NodeDefinition.cs`:
*   We can steal the highest bit of the 32-bit `PayloadIndex` to act as the `IsResourceOwning` flag, keeping the struct perfectly packed at 8 bytes. Alternatively, if you prefer explicit fields for v1, expand the struct to 12 bytes by adding `public bool IsResourceOwning;`.

In `Interpreter.cs`:
*   Add a secondary delegate array: `private readonly NodeDeactivatorDelegate<TBlackboard, TContext>[] _deactivatorDelegates;`
*   In the constructor, iterate through the `blob.Nodes` and resolve any declared deactivators from the registry, storing them in `_deactivatorDelegates` at their matching `PayloadIndex`.

**C. Implement Active-Path Delta Tracking in `Interpreter.Tick`**
The `BehaviorTreeState` already tracks the `NodeIndexStack` (maximum depth of 8) and the `RunningNodeIndex`. We leverage this to compute exactly which nodes were exited this frame.

Inside `Interpreter.Tick`:
1.  **Snapshot Old Path:** Before executing `ExecuteNode(0, ...)`, read the `StackPointer` and copy the active `NodeIndexStack` into a small stack-allocated array `Span<ushort> oldPath = stackalloc ushort;`. Also capture the `oldRunningNode`.
2.  **Tick:** Execute the tree normally.
3.  **Compute Delta:** After execution, compare `oldPath` to the new `NodeIndexStack` (and new `RunningNodeIndex`). 
4.  **Invoke Hooks:** For any node index that was in the old path but is *no longer* in the new active path, check if `_blob.Nodes[nodeIndex].IsResourceOwning` is true. If it is, invoke `_deactivatorDelegates[payloadIndex]`.

*Architectural Note:* Because the maximum stack depth is 8, this comparison is just a few integer equality checks per tick. It easily fits within our 4.0ms soft wall-clock budget.

### 2. Proof-of-Concept Tests (`Fbt.Tests`)

To prove this works flawlessly in isolation before touching Roslyn or the engine, add a new test class `HybridLifecycleTests.cs` to the `Fbt.Tests` project.

**Test 1: Natural Completion Deactivation**
*   **Arrange:** Manually construct a `BehaviorTreeBlob` consisting of a `Sequence` and a mock resource-owning `Action` node. Register a normal action delegate (returns `Success`) and a deactivator delegate that increments a local `deactivationCount` integer.
*   **Act:** Tick the interpreter once.
*   **Assert:** The tree returns `Success`. Assert that `deactivationCount == 1`, proving the hook fired when the execution pointer left the successfully completed node.

**Test 2: Higher-Priority Abort Deactivation (The Branch Switch)**
*   **Arrange:** Construct an `ObserverSelector` with two children: 
    *   High priority: A `Condition` that returns `Failure` on Tick 1, but `Success` on Tick 2.
    *   Low priority: A mock resource-owning `Action` that always returns `Running`.
*   **Act:** 
    *   Tick 1: The condition fails, so the fallback action runs.
    *   Tick 2: The condition succeeds, causing the `ObserverSelector` to abort the running fallback action and switch branches.
*   **Assert:** Assert that `deactivationCount == 1` specifically during Tick 2. This proves that an intra-behavior branch switch successfully sweeps the orphaned resource.

**Test 3: Stateless Performance Guarantee**
*   **Arrange:** Create a deep tree (e.g., 8 levels) of standard, non-resource-owning nodes. 
*   **Act:** Run a benchmark loop of 10,000 ticks. 
*   **Assert:** Ensure zero garbage collection allocations occurred (`GC.CollectionCount`) and that execution time remains equivalent to the `FastBTree` baseline. This proves the delta-tracking overhead is negligible for the 99% of nodes that do not own resources.

By implementing and passing these three isolated tests in `Fbt.Tests`, we scientifically validate the Option 4 lifecycle hook. Once verified, we can confidently move on to integrating it with `Hrot.AI.Behaviors` and writing the Roslyn generators.







To integrate the hybrid lifecycle hook into the engine's compilation pipeline, we must extend our existing Roslyn source generator, `BTreeActionGenerator`, located in the `Fdp.Toolkits.Analyzers` project. This generator is already responsible for incrementally scanning the syntax tree for AI delegates and emitting the centralized `FbtActionRegistrar.g.cs`.

Here is the exact modification plan for the Roslyn integration.

### 1. Define the Attribute in `Fbt.Kernel`

First, define the annotation in the `Fbt.Kernel` assembly so it is available to domain code without introducing downward dependencies.

```
namespace Fbt
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class BTreeDeactivatorAttribute : Attribute
    {
        public string TargetAction { get; set; } = string.Empty;
    }
}
```

### 2. Extend `BTreeActionGenerator` Extraction

In `BTreeActionGenerator.cs`, the `GetMethodInfo` method currently checks for `[BTreeAction]`, `[BTreeCondition]`, and the `[SharedAi...]` attribute family. We must extend this semantic extraction to detect the new deactivator attribute.

Update the `BTreeMethodInfo` struct (or create a secondary `DeactivatorInfo` struct) to capture the target action name:

```
public class BTreeMethodInfo
{
    // Existing fields...
    public bool IsDeactivator { get; set; }
    public string TargetAction { get; set; } = string.Empty;
}
```

Inside `GetMethodInfo`, extract the named argument:

```
var deactivatorAttr = symbol.GetAttributes()
    .FirstOrDefault(a => a.AttributeClass?.Name == "BTreeDeactivatorAttribute");

if (deactivatorAttr != null)
{
    string targetAction = deactivatorAttr.NamedArguments
        .FirstOrDefault(kvp => kvp.Key == "TargetAction").Value.Value?.ToString() ?? string.Empty;

    return new BTreeMethodInfo
    {
        MethodName = symbol.Name,
        FullQualifiedMethodName = symbol.ContainingType.ToDisplayString() + "." + symbol.Name,
        TBlackboardType = tbType,
        TContextType = tcType4,
        IsDeactivator = true,
        TargetAction = targetAction
    };
}
```

### 3. Update the `GenerateRegistrar` Emission

The generator groups methods by their `TBlackboard` and `TContext` signatures to emit strongly-typed `RegisterAll` overloads. We will modify `GenerateRegistrar` to emit the registration calls for the deactivators into the same centralized `FbtActionRegistrar` class.

Inside the emission loop for a specific group, append the deactivator emission:

```
foreach (var m in group.Deactivators)
{
    sb.AppendLine($"            registry.RegisterDeactivator(\"{m.TargetAction}\", global::{m.FullQualifiedMethodName});");
}
```

### 4. How It Connects at Runtime

Because we are injecting this seamlessly into the existing `FbtActionRegistrar.RegisterAll` signature, the runtime integration requires absolutely no changes to your application startup or hot-reload coordinator.

When the engine boots or when `AiHotReloadCoordinator` loads a new patch ALC, it invokes `AiBehaviorFactory.BuildRegistrationAction` on a background thread. This factory allocates the empty `ActionRegistry<BrainBlackboard, BTreeContext>` and calls our source-generated `FbtActionRegistrar.RegisterAll(actionRegistry)`.

The populated registry is then passed directly into the `Interpreter` constructor. Because your `FastBTree` library modification already looks inside the registry for deactivators and flags the bytecode, the EQS `EqsSensor` cleanup logic is now fully wired, memory-safe, and capable of surviving seamless hot-reloads.





how could we demonstrate this all works in the hrot.editor? can we use the deactivator concept in some existing behavior, maybe in place of the current solution where the deactivator solution is more elegant?





We can brilliantly demonstrate the power of the `[BTreeDeactivator]` concept in the `Hrot.Editor` by fixing a subtle bug that currently exists in the `UrbanCombatNewScenario`.

Right now, the engine relies on the `ChannelArbitrationSystem` to clean up stale actuator channels (like the `WeaponChannel`), but this system only arbitrates when a full behavior switch occurs (i.e., when `BehaviorInstanceId` changes). It is completely blind to intra-behavior branch shifts.

If you look at the Insurgent's `Ambush_BT` behavior, it uses a Selector that evaluates `Condition_HasTarget` and, if successful, runs `Action_AimAndFire`. This action writes `CombatConstants.ActionIdAimAndFire` directly into the entity's `WeaponChannel` and returns `NodeStatus.Running`.

If the target suddenly dies or breaks line of sight, `Condition_HasTarget` will return `Failure` on the next tick, causing the Selector to abort `Action_AimAndFire` and shift the active execution path to `Action_HoldPosition`. Because FastBTree action nodes are completely stateless, `Action_AimAndFire` never receives a callback to clear the channel. Consequently, the Insurgent will be stuck perpetually firing at nothing because the `WeaponChannel.ActiveAction` remains set.

Here is exactly how we can use the `Hrot.Editor` to demonstrate the `[BTreeDeactivator]` elegantly solving this problem live:

### 1. The Code Implementation

Instead of relying on the engine to magically clean up the channel, or polluting `Action_HoldPosition` with manual cleanup logic, you will add the new deactivator directly to `InsurgentNodes.cs`:

```
[BTreeDeactivator(TargetAction = "Action_AimAndFire")]
public static void Deactivate_AimAndFire(
    ref BrainBlackboard bb,
    ref BehaviorTreeState state,
    ref BTreeContext ctx,
    int paramIndex)
{
    if (ctx.World.HasComponent<WeaponChannel>(ctx.Self))
    {
        ref var channel = ref ctx.World.GetComponentRW<WeaponChannel>(ctx.Self);
        if (channel.ActiveAction == CombatConstants.ActionIdAimAndFire)
        {
            channel.ActiveAction = 0;
            unchecked { channel.ActionInstanceId++; }
        }
    }
}
```

### 2. The Editor Demonstration Workflow

Because the `Hrot.Editor` is fully wired for deterministic previewing and AI hot-reloading, you can prove the fix seamlessly without restarting the tool:

1. **Observe the Bug in Preview Mode:** Launch the `Hrot.Editor` and load the `UrbanCombatNewScenario`. Open the `EntityWatchPanel` (via the "Inspect..." context menu) and pin it to the Insurgent entity to monitor its `WeaponChannel`. Click the "Preview" button. The `EditorPreviewAdapter` will take an in-memory ECS snapshot and start the simulation. Once the Insurgent engages the APC, manually delete the APC entity via the map context menu. You will see the Insurgent's BTree branch shift to `Action_HoldPosition`, but the `WeaponChannel` inspector will show it is still actively firing.
2. **Hot-Reload the Fix:** Exit Preview Mode. The editor will automatically rewind the ECS repository back to the pristine snapshot. Leaving the editor open, paste the `Deactivate_AimAndFire` method into your IDE and compile `Hrot.AI.Behaviors.dll`.
3. **Atomic ALC Swap:** The editor's `AiHotReloadCoordinator` file-watcher will detect the newly built DLL in the background, load it into a fresh `AssemblyLoadContext`, invoke your Roslyn-generated registrars to stage the new deactivator delegate, and atomically swap the pointers on the main thread.
4. **Verify the Deactivator:** Enter Preview Mode again and repeat the test. This time, when the APC is deleted and the BTree framework evaluates the active-path delta, it will detect that the execution pointer has left `Action_AimAndFire`. The interpreter will instantly invoke your `Deactivate_AimAndFire` hook, and you will watch the `WeaponChannel` safely reset to `0` in the inspector.

This proves end-to-end that the new lifecycle hook successfully replaces manual channel cleanup, runs perfectly inside the FastBTree delta-tracking pipeline, and survives our ALC hot-reload architecture.





what about the hill attack behavior, are there opportunities for using the deactivator?





The `HullDownAttackRun` subordinate tank behavior is an excellent candidate for the `[BTreeDeactivator]`.

Currently, inside `HillAttackTankNodes.cs`, the `Action_CreepToAndBeyondSlot` method must manually clear the `LocomotionChannel` by setting `ActiveAction = 0` when the tank overshoots its firing slot. The source code explicitly notes that this manual channel cleanup is forced upon the developer because our Roslyn source generator currently only emits automatic `[WritesChannel]` cleanup thunks for 4-parameter delegates, not the 3-parameter form used in these nodes.

Similarly, the `Action_AimAndFireSpecific` node manually zeroes out the locomotion channel upon entry and relies on a manual `ClearWeaponActionIfActive` call when the tank exhausts its `MaxRounds` quota.

By applying the `[BTreeDeactivator]` attribute to these actions, you can strip all of this manual channel-reset boilerplate out of your core logic. If the tank overshoots and the BTree framework shifts the execution path to the `Action_AbortEngagement` or `Action_ReverseToBaseline` branches, the active-path delta tracker will automatically invoke your deactivator hook to sweep the actuators safely.

Furthermore, there is an opportunity in the commander's `PlatoonHillAttack` behavior. The `Action_RequestAreaQuery` node submits an asynchronous area query and caches the resulting `CachedEqsRequestId` into the `Blackboard1024` heavy state component. If a higher-level mission switch forcefully clears the commander's behavior while this query is still in flight, that ID is orphaned. You could attach a deactivator to this node to guarantee `CachedEqsRequestId` is safely reset to `-1` the moment the branch is aborted.





