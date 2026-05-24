# Design: EQS Sensor Lifecycle — Hybrid BTree Lifecycle Hook (Option 4)

Derived from `.dev/eqs-2/EQS_Sensor_Lifecycle_Options.md`.

---

## Background

The EQS v1.3 design (`EQS_Design_v1.3_final.md`) makes `EqsSensor` a plain ECS component
placed on the Brain-side entity. Sensor lifetime equals component lifetime: removing the
component causes the Muscle solver to drop the query on its next tick.

The open question resolved by this design talk is **who removes the `EqsSensor` component**
when the BTree execution pointer leaves the action that created it — in particular during
intra-behavior branch switches driven by `ObserverSelector`.

The architect reviewed four options (tick-lease as-is, tick-lease with SensorScope helper,
composite-anchored leases, hybrid lifecycle hook) and approved **Option 4: Hybrid Lifecycle
Hook** as the production path. This document records the approved design.

### Why the other options were rejected

| Option | Rejection reason |
|---|---|
| Option 1 — tick-lease as-is | Silent-failure mode when lease is not stamped; pathological interaction with LOD throttling; subtree-scoped ownership impossible. |
| Option 2 — tick-lease + SensorScope | Eliminates the discipline burden but does not fix the LOD/throttle issue or subtree-scoped ownership. Acceptable as a temporary fallback only. |
| Option 3 — composite-anchored leases | Requires ECS cleanup system to inspect BTree internal active-path state, violating module boundaries. |

---

## Architectural decision

BTree action nodes that allocate resources requiring deterministic cleanup (EQS sensors,
actuator channels, raycasts, reservations) are annotated with `[BTreeDeactivator]`. For
each such annotated action a companion static `OnDeactivate` method is provided. The BTree
framework invokes the deactivator automatically when the execution pointer leaves the flagged
node for any reason (natural completion, abort, branch switch).

This is the approach used by `UBTTaskNode.AbortTask` in Unreal Engine. The critical constraint
for this engine is that the vast majority of BTree actions remain pure stateless static
delegates — only the small fraction that own cleanup-requiring resources opt into the hook.
Per-frame overhead for nodes without a deactivator is zero.

---

## Phase 1: FastBTree Library (Fbt.Kernel — isolated)

**Goal:** Add deactivator support to the FastBTree library with no engine dependencies.
All capability is proven by `Fbt.Tests` before any engine integration begins.

### 1.1 NodeDeactivatorDelegate

Define a new delegate type in `Fbt.Kernel` (same assembly as `NodeLogicDelegate`):

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

Signature mirrors `NodeLogicDelegate` but returns `void`.

### 1.2 BTreeDeactivatorAttribute

Define `BTreeDeactivatorAttribute` in `Fbt.Kernel` alongside `BTreeActionAttribute`:

```csharp
namespace Fbt
{
    [AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
    public sealed class BTreeDeactivatorAttribute : Attribute
    {
        public string TargetAction { get; }
        public BTreeDeactivatorAttribute(string targetAction) { TargetAction = targetAction; }
    }
}
```

`TargetAction` is the fully qualified method name of the action being paired (matches the
registration key used in `ActionRegistry.Register`).

### 1.3 ActionRegistry extension

`ActionRegistry<TBlackboard, TContext>` gains a parallel deactivator dictionary:

- `RegisterDeactivator(string targetActionKey, NodeDeactivatorDelegate<TBlackboard, TContext>)`
- `TryGetDeactivator(string targetActionKey, out NodeDeactivatorDelegate<TBlackboard, TContext>?)`

The key is identical to the key used by `Register` for the corresponding action so the
interpreter can resolve both by method name.

### 1.4 Interpreter: deactivator array and delta tracking

The design talk describes setting a "resource-owning flag bit in the BTree blob at compile
time." This implementation departs from that description intentionally: `NodeDefinition`'s
8-byte layout is preserved unchanged, and the `IsResourceOwning` flag is represented
implicitly by a non-null entry in a parallel delegate array resolved at interpreter
construction time. The semantic result is identical. A blob-flag approach would require
changes to `BTreeBuilder`, `BTreeBlob`, and serialization formats; deferring to a later
optimisation pass keeps Phase 1 minimal and self-contained.

The `Interpreter<TBlackboard, TContext>` gains a secondary delegate array parallel to
`_actionDelegates`:

```
private readonly NodeDeactivatorDelegate<TBlackboard, TContext>?[] _deactivatorDelegates;
```

Array length equals `blob.MethodNames.Length` (same as `_actionDelegates`). Populated in
the constructor: for each method name, attempt `registry.TryGetDeactivator(name, out ...)` and
store the result (null if no deactivator registered for that method).

**Active-path delta tracking in `Tick`:**

The delta tracker must capture the **full execution stack** — not just `RunningNodeIndex`
— because an `ObserverSelector` may abort a branch and reset the stack before any leaf ever
sets `RunningNodeIndex`. Tracking only the leaf misses those cases entirely.

Before calling `ExecuteNode(0, ...)`, snapshot the full active path into a stack-allocated
buffer (fixed size 9: 8 `NodeIndexStack` entries plus `RunningNodeIndex`):

```csharp
// 8 NodeIndexStack slots + RunningNodeIndex = 9 entries total.
Span<ushort> oldPath = stackalloc ushort[9];
MemoryMarshal.CreateSpan(ref state.NodeIndexStack[0], 8).CopyTo(oldPath);
oldPath[8] = state.RunningNodeIndex;
```

**Ordering requirement:** The path snapshot must be taken *before* any structural
hot-reload bounds-check that may reset `RunningNodeIndex` to 0. See §3.5 for the full
ordering constraint.

After `ExecuteNode` returns, build the new path and sweep for exited nodes:

```csharp
Span<ushort> newPath = stackalloc ushort[9];
MemoryMarshal.CreateSpan(ref state.NodeIndexStack[0], 8).CopyTo(newPath);
newPath[8] = state.RunningNodeIndex;

for (int i = 0; i < 9; i++)
{
    ushort old = oldPath[i];
    if (old == 0) continue;
    if (newPath.Contains(old)) continue;
    InvokeDeactivatorIfRegistered(old, ref blackboard, ref state, ref context);
}
```

`InvokeDeactivatorIfRegistered` applies a mandatory node-type guard before using
`PayloadIndex`. Non-Action/Condition nodes (Selector, Sequence, Parallel, decorators) use
`PayloadIndex` for `FloatParams`/`IntParams` rather than `MethodNames`, so indexing
`_deactivatorDelegates` with their `PayloadIndex` would be incorrect:

```csharp
private void InvokeDeactivatorIfRegistered(ushort nodeIndex,
    ref TBlackboard blackboard, ref BehaviorTreeState state, ref TContext context)
{
    ref var node = ref _blob.Nodes[nodeIndex];
    if (node.Type is not (NodeType.Action or NodeType.Condition)) return;
    int pi = node.PayloadIndex;
    if ((uint)pi >= (uint)_deactivatorDelegates.Length) return;
    var deactivator = _deactivatorDelegates[pi];
    if (deactivator != null)
        deactivator(ref blackboard, ref state, ref context, pi);
}
```

**`Parallel` node handling:** `ExecuteParallel` overwrites `RunningNodeIndex` with its own
node index at each tick (`state.RunningNodeIndex = (ushort)nodeIndex`), erasing the active
leaf inside each child branch from the `NodeIndexStack`. If a Parallel child is itself a
composite (e.g., a `Sequence` containing the resource-owning `Action`), checking only the
immediate child node silently misses the leaf deep inside that subtree.

When the path sweep detects that a `NodeType.Parallel` node exited the active path (present
in `oldPath`, absent from `newPath`), for each child whose completion bit in `LocalRegisters`
is NOT set (still running), sweep the child's **entire definition block**: iterate every
node index in the range `[childIndex, childIndex + childNode.SubtreeOffset)` and call
`InvokeDeactivatorIfRegistered` on each one. Because all deactivators guard against a
cleared or absent channel before acting, this blanket range sweep is idempotent and safe.
It guarantees no orphaned resources regardless of how deeply nested the resource-owning
leaf is within the Parallel branch.

**Tree completion cleanup:** When `Tick` returns `Success` or `Failure`, `RunningNodeIndex`
resets to 0 and the stack clears. The path sweep fires deactivators correctly because every
non-zero `oldPath` entry is absent from `newPath`.

**Delta-tracking cost for trees with no resource-owning nodes:** two 9-element
`stackalloc` copies (18 `ushort` writes on the call stack) plus one 9-element linear scan.
No heap allocation.

### 1.5 Fbt.Tests — proof-of-concept tests

New test class `HybridLifecycleTests.cs` in `Fbt.Tests/Unit/`:

**Test L-01 — Natural completion fires deactivator.**
Construct a Sequence with a single resource-owning Action that always returns Success.
Register a matching deactivator that increments a counter. Tick once. Assert counter == 1.

**Test L-02 — Branch switch fires deactivator (the critical case).**
Construct an ObserverSelector with two children:
- Child 0 (high priority): Condition returning Failure on Tick 1, Success on Tick 2.
- Child 1 (low priority): resource-owning Action always returning Running.

Tick 1: condition fails, fallback action runs (RunningNodeIndex = action node index).
Tick 2: condition succeeds; execution switches to child 0; RunningNodeIndex changes.
Assert deactivator counter == 1 after Tick 2.

**Test L-03 — Tree failure fires deactivator.**
Construct a Sequence: running Action returns Running on Tick 1, Failure on Tick 2.
Assert deactivator fires on Tick 2 (tree fails, RunningNodeIndex resets to 0).

**Test L-04 — No deactivator registered: no exception, no allocation.**
Register an action with no companion deactivator. Tick 1000 times.
Assert no exception and `GC.CollectionCount(0)` unchanged.

**Test L-05 — Multiple resource-owning nodes: only exited one fires.**
Construct a Selector with two resource-owning Actions, each with distinct deactivators.
Let Action A run for one tick, then force a branch switch to Action B on Tick 2.
Assert deactivator-A fires once; deactivator-B has not fired.

**Test L-06 — Subtree abort before leaf: deactivator swept from full path.**
Construct an ObserverSelector whose low-priority child is a Sequence containing a
resource-owning Action. On Tick 1 the action is Running and appears in both
`NodeIndexStack` (as the Sequence path entry) and `RunningNodeIndex`. On Tick 2 the
high-priority condition succeeds; the interpreter aborts the Sequence branch. The full-path
sweep must detect the action left the path and fire its deactivator.
Assert deactivator fires exactly once on Tick 2.

**Test L-07 — Parallel child abort fires per-child deactivators (subtree sweep).**
Construct a `Parallel` node with two children, each of which is a `Sequence` containing
a resource-owning `Action` with a distinct deactivator. On Tick 1 both leaf Actions are
Running but only the `Parallel` node appears in `NodeIndexStack` (the inner Sequences and
Actions are tracked via `LocalRegisters`, not the stack). On Tick 2 an external abort
causes the `Parallel` node to exit the active path. Assert both deactivators were each
fired exactly once, confirming the range sweep traverses the `Sequence` subtrees rather
than stopping at the immediate child node.

**Test L-08 — Hot-reload bounds-check does not orphan resources.**
Construct a tree whose resource-owning Action is Running. Simulate a structural hot-reload
by replacing the interpreter's blob with a shorter one in which the old `RunningNodeIndex`
is out of bounds. Assert the deactivator fires before `RunningNodeIndex` is reset to 0,
and that `RunningNodeIndex` is 0 after the call returns.

---

## Phase 2: Roslyn Generator Extension (Fdp.Toolkits.Analyzers)

**Goal:** Wire `[BTreeDeactivator]` into the existing `BTreeActionGenerator` so that
deactivator registration is emitted into `FbtActionRegistrar.g.cs` automatically, with no
manual registration required.

### 2.1 BTreeMethodInfo extension

Add to the `BTreeMethodInfo` record/class:

```csharp
public bool IsDeactivator { get; set; }
public string TargetAction { get; set; } = string.Empty;
```

### 2.2 GetMethodInfo detection

In `GetMethodInfo`, detect `[BTreeDeactivatorAttribute]` alongside the existing attribute checks:

```csharp
var deactivatorAttr = symbol.GetAttributes()
    .FirstOrDefault(a => a.AttributeClass?.Name == "BTreeDeactivatorAttribute");
if (deactivatorAttr != null)
{
    string target = deactivatorAttr.ConstructorArguments[0].Value?.ToString() ?? string.Empty;
    return new BTreeMethodInfo
    {
        MethodName = symbol.Name,
        FullQualifiedMethodName = ...,
        TBlackboardType = ...,
        TContextType    = ...,
        IsDeactivator   = true,
        TargetAction    = target
    };
}
```

### 2.3 GenerateRegistrar emission

In the group emission loop, after emitting `registry.Register(...)` calls, emit deactivators:

```csharp
foreach (var m in group.Deactivators)
{
    sb.AppendLine(
        $"            registry.RegisterDeactivator(\"{m.TargetAction}\", " +
        $"global::{m.FullQualifiedMethodName});");
}
```

The group's `Deactivators` list is populated from `BTreeMethodInfo` entries where
`IsDeactivator == true` and whose `(TBlackboardType, TContextType)` matches the group.

### 2.4 Generator diagnostics

Emit diagnostic `BHU-016` (new) if a `[BTreeDeactivator]` is found with an empty or
missing `TargetAction` argument.

Emit diagnostic `BHU-017` (new) if the `TargetAction` string does not match any method
name found in the same compilation that carries `[BTreeAction]` or `[BTreeCondition]`.
This catches typos at build time.

### 2.5 Support for 3-param bridge deactivators

The 3-param (`[BTreeAction]`/`[BTreeCondition]`) bridge form uses a compound key of
`"{fullMethodName}@0"` rather than just `fullMethodName`. A deactivator for a 3-param
action must use the same key so the registry look-up resolves correctly.

The generator must emit the `@0` compound key for deactivators whose target method is
detected as a 3-param bridge method.

---

## Phase 3: Engine Integration

**Goal:** Demonstrate the hook end-to-end with two existing behaviors where channel
cleanup is currently either absent or manual.

### 3.1 WeaponChannel cleanup — InsurgentNodes (Fdp.Examples.UrbanCombat)

`InsurgentNodes.Action_AimAndFire` (4-param, registered under fully qualified name)
writes `CombatConstants.ActionIdAimAndFire` into `WeaponChannel.ActiveAction` and returns
`NodeStatus.Running` while the target is alive. Currently, if `Condition_HasTarget` returns
Failure on the next tick, the BTree branch switches to `Action_HoldPosition` but
`WeaponChannel.ActiveAction` remains set — the Insurgent fires at nothing indefinitely.

A deactivator `Deactivate_AimAndFire` is added to `InsurgentNodes`:

```csharp
[BTreeDeactivator("Fdp.Examples.UrbanCombat.Brains.InsurgentNodes.Action_AimAndFire")]
public static void Deactivate_AimAndFire(
    ref BrainBlackboard bb, ref BehaviorTreeState state,
    ref BTreeContext ctx, int paramIndex)
{
    if (!ctx.World.HasComponent<WeaponChannel>(ctx.Self)) return;
    ref var ch = ref ctx.World.GetComponentRW<WeaponChannel>(ctx.Self);
    if (ch.ActiveAction == CombatConstants.ActionIdAimAndFire)
    {
        ch.ActiveAction = 0;
        unchecked { ch.ActionInstanceId++; }
    }
}
```

The Roslyn generator emits `registry.RegisterDeactivator(...)` automatically; no manual
change to `AiBehaviorFactory` or test scenario setup code is required.

### 3.2 LocomotionChannel cleanup — HillAttackTankNodes (Hrot.AI.Behaviors)

`HillAttackTankNodes.Action_CreepToAndBeyondSlot` (3-param bridge form) currently clears
the `LocomotionChannel` explicitly inside the action body only on the `Failure` path. If the
BTree branches away (target visible, wave abort) without returning `Failure`, the channel
remains set. The cleanup inside the method body is also fragile because it runs as part of
the action's return path, not as a guaranteed hook.

A companion deactivator is added to `HillAttackTankNodes`. Because this is a 3-param
bridge, the `TargetAction` key must use the `@0` compound-key convention. The generator
handles this automatically when it detects the paired method is a bridge form.

### 3.3 WeaponChannel cleanup — HillAttackTankNodes

`HillAttackTankNodes.Action_AimAndFireSpecific` clears `WeaponChannel` via the private
`ClearWeaponActionIfActive` helper only on the `MaxRounds` exhaustion path. Branch aborts
leave the channel set. A deactivator is added identical in structure to 3.2 but targeting
`Action_AimAndFireSpecific`.

### 3.4 EqsRequestId cleanup — HillAttackCommanderNodes (Hrot.AI.Behaviors)

`HillAttackCommanderNodes.Action_RequestAreaQuery` submits an asynchronous area query and
caches the resulting request ID as `HillAttackMutableState.CachedEqsRequestId` in the
`Blackboard1024` heavy-state component. If a mission-level branch abort clears the
commander's behavior while the query is in flight, `CachedEqsRequestId` is orphaned
(the query result never consumed; the slot in the pool leaks until the pool wraps).

A deactivator `Deactivate_RequestAreaQuery` resets `CachedEqsRequestId` to `-1`.

### 3.5 Hot-reload compatibility

The deactivators are registered by the Roslyn-generated `FbtActionRegistrar.RegisterAll`.
When `AiHotReloadCoordinator` performs an ALC swap, the new ALC's `FbtActionRegistrar`
registers all deactivators into the new `ActionRegistry`. The `Interpreter` is reconstructed
with the new registry and blob, so `_deactivatorDelegates` is always consistent with the
currently loaded ALC.

**Ordering constraint:** `Interpreter.Tick` contains a structural safety-net that resets
`RunningNodeIndex` to 0 if the hot-reloaded tree is smaller and leaves the index
out-of-bounds. If the delta sweep runs *after* this reset, the old execution path is already
erased and any resources held by the previous ALC's actions (active `WeaponChannel`
reservations, in-flight EQS request IDs) are permanently orphaned on the entity.

The implementation must handle this case in the following order inside `Tick`:

1. Snapshot `oldPath` (9-element `stackalloc` as described in §1.4).
2. **Structural bounds-check** with `pathWasReset` flag:
   ```csharp
   bool pathWasReset = false;
   if (state.RunningNodeIndex > 0 && state.RunningNodeIndex >= _blob.Nodes.Length)
   {
       Span<ushort> emptyPath = stackalloc ushort[9]; // zero-initialised
       SweepPath(oldPath, emptyPath);                 // fire all deactivators now
       state.RunningNodeIndex = 0;
       state.StackPointer = 0;
       unchecked { state.TreeVersion++; }
       pathWasReset = true;
       // Do NOT return — continue to ExecuteNode so the tree evaluates this frame.
   }
   ```
3. Call `ExecuteNode(0, ...)`.
4. If `!pathWasReset`: snapshot `newPath` and sweep `oldPath` vs `newPath`. Skip when
   `pathWasReset` is true — the abandoned path was already swept in step 2, so repeating
   it would double-fire deactivators on the same path.

Not returning early preserves `FastBTree`'s seamless hot-reload contract: the tree
evaluates from the root on the very same frame the structural reset is detected, with no
1-frame brain-dead gap.

---

## Phase 4: EqsSensor Integration (Deferred — EQS v1.3 prerequisite)

**Goal:** Apply the deactivator pattern to `EqsSensor` once the EQS v1.3 component exists.

The EQS v1.3 design defines `EqsSensor` as an ECS component on the Brain-side entity.
Sensor lifetime equals component lifetime. Once Phase 1–3 are complete, any BTree action
that adds an `EqsSensor` component simply needs a companion deactivator that removes it:

```csharp
[BTreeDeactivator("...Action_FindCover")]
public static void Deactivate_FindCover(
    ref BrainBlackboard bb, ref BehaviorTreeState state,
    ref BTreeContext ctx, int paramIndex)
{
    if (ctx.World.HasComponent<EqsSensor>(ctx.Self))
        ctx.World.RemoveComponent<EqsSensor>(ctx.Self);
}
```

This eliminates the need for a dedicated `EqsSensorCleanupSystem` and makes sensor lifetime
exactly match the active BTree node that owns the sensor, with no per-tick sweep overhead.

Phase 4 has no design work of its own — it is a straight application of the Phase 1–3 pattern
to the EQS system once that system is implemented.

---

## Architectural constraints

1. **No per-frame overhead for non-resource-owning nodes.** The delta check is two 9-element
   `stackalloc` copies and one 9-element linear scan — all on the call stack with no heap
   allocation. For trees with no resource-owning nodes all nine deactivator slot checks are
   null and exit immediately.

2. **Zero-allocation.** Deactivator delegates are stored in a pre-allocated array. No closures,
   no heap allocation during tick.

3. **No structural change to `NodeDefinition`.** The 8-byte struct layout is preserved.
   The deactivator look-up uses the existing `PayloadIndex` to index into a parallel
   `_deactivatorDelegates` array.

4. **Stateless-delegate signature preserved.** `NodeDeactivatorDelegate` has the same
   4-param signature as `NodeLogicDelegate`; action methods remain plain static methods.
   No instance state or virtual dispatch is added to the common case.

5. **`BTreeDeactivatorAttribute` is in `Fbt.Kernel` (same assembly as `BTreeActionAttribute`)**
   so domain code has a single package dependency for all BTree annotation attributes.

6. **`NodeDeactivatorDelegate` lives in the `Fbt` namespace** (same as `NodeLogicDelegate`)
   to avoid introducing a separate namespace for consumers.

7. **Project dependency chain is unchanged.** `Fbt.Kernel` has no new external dependencies.
   `Fdp.Toolkits.Analyzers` already references `Fbt.Kernel` attributes by name string
   comparison; the new attribute is detected the same way.

---

## Relationship to existing channel cleanup

`[WritesChannel]` cleanup is emitted by the Roslyn generator as a wrapper around the action
delegate: if the action returns `Success` or `Failure`, the wrapper clears the channel.
It does NOT fire when the execution pointer leaves the node from an external abort or branch
switch. `[BTreeDeactivator]` fills this gap for all non-Success/Failure exit paths. The two
mechanisms are complementary, not replacements.

For the 3-param bridge form, `[WritesChannel]` is not currently generated (documented in
`HillAttackTankNodes.cs` comments). The deactivator provides the missing cleanup guarantee
for these nodes.
