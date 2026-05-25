# BATCH-16 INSTRUCTIONS — EQS-039 + EQS-040

**Batch number:** 16
**Tasks:** TASK-EQS-039, TASK-EQS-040
**Phase:** 12 — Multi-sensor child-entity support (Part B)
**Goal:** BTree actions for spawning/destroying child sensor entities (EQS-039), plus the
multi-sensor integration test and `HideInCover_BT_v2` recipe (EQS-040).

**Design references:**
- `.dev/eqs-2/TASK-DETAIL.md` — sections `TASK-EQS-039` and `TASK-EQS-040` (read them in full)
- `.dev/eqs-2/EQS_Design_v1.3_final.md` — §11
- `.dev/eqs-2/ONBOARDING.md`

---

## Overview

**EQS-038** (BATCH-15) landed the solver and network plumbing for child-entity sensors.
**EQS-039** now adds the BTree side: two new actions in `EqsLifecycleNodes.cs` that let a
BTree node spawn or destroy child sensor entities using the deferred command buffer.

**EQS-040** adds:
1. An offline integration test confirming two concurrent child sensors produce independent
   result buffers.
2. `HideInCover_BT_v2` in `HideInCoverBehavior.cs` — a canonical child-entity recipe.
3. A small backwards-compat addition to `Action_MoveToOptimalCover`: an `EqsSensorHandle`
   blackboard field that lets it read from a child sensor's buffer instead of `ctx.Self`.

---

## TASK-EQS-039: Child-sensor spawn/destroy BTree actions

### A. New params struct `EqsSpawnParams`

Add to `EqsLifecycleNodes.cs` (alongside the existing `EqsParams`):

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct EqsSpawnParams
{
    /// <summary>EQS query parameters — copied to the child EqsSensor.</summary>
    public EqsParams  SensorConfig;
    /// <summary>Discriminates multiple child sensors on the same parent.
    /// Values 0..254 allowed; 255 is reserved.</summary>
    public byte  ChildSlotIndex;
    /// <summary>Output: handle to the spawned child entity.</summary>
    public EqsSensorHandle SpawnedHandle;
}
```

The `SpawnedHandle` field doubles as both an **output** (written on spawn) and a
**persistent cache** (checked on re-entry to avoid double-spawn).

### B. `Action_SpawnEqsSensorChild`

Add to `EqsLifecycleNodes`:

```csharp
[BTreeAction]
public static NodeStatus Action_SpawnEqsSensorChild(
    ref EqsSpawnParams p,
    ref BehaviorTreeState state,
    ref BTreeContext ctx)
{
    // Deterministic LocalChildIndex: stable across ticks for the same (parent, slot) pair.
    int localChildIndex = (int)(((uint)ctx.Self.Index << 8) | p.ChildSlotIndex);

    // Idempotency: if previously spawned and still alive, reuse existing handle.
    if (p.SpawnedHandle.IsValid && ctx.World.IsAlive(p.SpawnedHandle.ChildId))
        return NodeStatus.Success;

    // Fallback idempotency scan on first entry (or after a BTree restart that cleared the
    // blackboard). Uses a cached module-level query — never rebuilt per tick.
    // (See Constraint #2 — the query is built once in static initializer or lazy field.)
    Entity existingChild = FindExistingChild(ctx.World, ctx.Self, localChildIndex);
    if (!existingChild.IsNull)
    {
        p.SpawnedHandle = new EqsSensorHandle(existingChild);
        return NodeStatus.Success;
    }

    // Spawn new child via ECB (deferred structural mutation — BTree runs in Simulation phase).
    var ecb   = ctx.World.GetCommandBuffer();
    var child = ecb.CreateEntity();

    ecb.AddComponent(child, new PartMetadata
    {
        ParentEntity      = ctx.Self,
        InstanceId        = localChildIndex,
        DescriptorOrdinal = 0,
    });
    ecb.AddComponent(child, new EqsSensor
    {
        BlueprintId         = p.SensorConfig.BlueprintId,
        Epoch               = 1,
        SearchRadius        = p.SensorConfig.SearchRadius,
        FactionFilter       = p.SensorConfig.FactionFilter,
        ThreatThreshold     = p.SensorConfig.ThreatThreshold,
        ScoreDeltaThreshold = p.SensorConfig.ScoreDeltaThreshold,
        ContextSlot0        = p.SensorConfig.ContextSlot0,
        ContextSlot1        = p.SensorConfig.ContextSlot1,
        ContextSlot2        = p.SensorConfig.ContextSlot2,
    });
    ecb.AddComponent(child, default(EqsCognitiveBuffer));

    p.SpawnedHandle = new EqsSensorHandle(child);
    return NodeStatus.Success;
}
```

**`FindExistingChild` helper** (private static in `EqsLifecycleNodes`):
- Uses a **module-level cached `EntityQuery`** (built once, never rebuilt per call):
  ```csharp
  private static EntityQuery? _childScanQuery;
  private static Entity FindExistingChild(ISimulationView world, Entity parent, int instanceId)
  {
      _childScanQuery ??= world.Query().With<PartMetadata>().Build();
      foreach (var candidate in _childScanQuery)
      {
          var meta = world.GetComponentRO<PartMetadata>(candidate);
          if (meta.ParentEntity.Equals(parent) && meta.InstanceId == instanceId)
              return candidate;
      }
      return Entity.Null;
  }
  ```

> **Note on `_childScanQuery`:** `ISimulationView.Query()` returns a builder; calling
> `.Build()` allocates and registers the query once. Store it as `static EntityQuery?`
> on the `EqsLifecycleNodes` class. Check the `EntityQuery` type — it may be a struct
> (just store it) or a class (null-check before reuse is sufficient). Look at existing
> usage of `EntityQuery` in `EqsSolverSystem.cs` for the exact pattern.

### C. `Deactivate_SpawnEqsSensorChild`

```csharp
[BTreeDeactivator("Hrot.AI.Behaviors.Brains.EqsLifecycleNodes.Action_SpawnEqsSensorChild@0")]
public static void Deactivate_SpawnEqsSensorChild(
    ref EqsSpawnParams p,
    ref BehaviorTreeState state,
    ref BTreeContext ctx)
{
    if (p.SpawnedHandle.IsValid && ctx.World.IsAlive(p.SpawnedHandle.ChildId))
    {
        var ecb = ctx.World.GetCommandBuffer();
        ecb.DestroyEntity(p.SpawnedHandle.ChildId);
    }
    p.SpawnedHandle = default;
}
```

### Tests for EQS-039

Add to a **new file** `Hrot/Subsystems/Hrot.AI.Behaviors.Tests/Eqs/EqsChildSensorActionTests.cs`
using `EditorHarness`:

| Test ID | Scenario | Key assertion |
|---|---|---|
| T-CS-A1 | Spawn action, slot 1 | child entity exists with matching PartMetadata; SpawnedHandle.ChildId == child |
| T-CS-A2 | Spawn same slot twice (re-entry) | exactly 1 child (idempotent) |
| T-CS-A3 | Spawn two different slots | 2 children, different entities |
| T-CS-A4 | Deactivate after spawn | child entity destroyed; SpawnedHandle.IsValid == false |
| T-CS-A5 | Parent killed, SubEntityCleanupSystem runs | child cleaned up automatically |

> **Harness:** use `EditorHarness` from the existing test project. Check what test project
> exists for `Hrot.AI.Behaviors` — it may already exist or may need to be created. If it
> does not exist, add tests to
> `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsChildSensorActionTests.cs`
> instead and note the deviation in your report.

---

## TASK-EQS-040: Multi-sensor integration test + HideInCover_BT_v2

### A. `Action_MoveToOptimalCover` — `EqsSensorHandle` input

Modify `MoveToOptimalCoverParams` in `EqsCombatNodes.cs`:

```csharp
[StructLayout(LayoutKind.Sequential)]
public struct MoveToOptimalCoverParams
{
    public float Speed;
    public float ArrivalRadius;
    /// <summary>
    /// Optional: if valid, read EqsCognitiveBuffer from the child sensor entity.
    /// If invalid (default), fall back to reading from ctx.Self.
    /// </summary>
    public EqsSensorHandle SensorHandle;
}
```

Update `Action_MoveToOptimalCover` to use the handle:

```csharp
// Resolve the entity to read the buffer from.
Entity bufferEntity = p.SensorHandle.IsValid && ctx.World.IsAlive(p.SensorHandle.ChildId)
    ? p.SensorHandle.ChildId
    : ctx.Self;

// Replace all "ctx.Self" references for EqsCognitiveBuffer access with "bufferEntity".
if (!ctx.World.HasComponent<EqsCognitiveBuffer>(bufferEntity) ||
    !ctx.World.HasComponent<LocomotionChannel>(ctx.Self))    // LocomotionChannel stays on ctx.Self
    return NodeStatus.Failure;

ref readonly var buffer = ref ctx.World.GetComponentRO<EqsCognitiveBuffer>(bufferEntity);
```

### B. `HideInCover_BT_v2` recipe

Add to `HideInCoverBehavior.cs` (alongside the existing `HideInCover_BT`):

```csharp
/// <summary>Blackboard for the child-entity variant HideInCover_BT_v2.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct HideInCoverV2Blackboard
{
    /// <summary>Spawn params including SensorConfig + ChildSlotIndex + SpawnedHandle (output).</summary>
    public EqsSpawnParams SpawnConfig;
    /// <summary>Locomotion params. SensorHandle set from SpawnConfig.SpawnedHandle at runtime.</summary>
    public MoveToOptimalCoverParams MoveConfig;
}
```

BTree definition:

```csharp
[BTreeDefinition("HideInCover_BT_v2")]
public static BTreeBuilder<HideInCoverV2Blackboard, BTreeContext> BuildHideInCoverV2Tree()
{
    return new BTreeBuilder<HideInCoverV2Blackboard, BTreeContext>()
        .ObserverSelector(obs => obs
            .Sequence(seq => seq
                .Condition(bb => bb.MoveConfig, EqsCombatNodes.Condition_HasTarget)
                .Parallel(Policy.RequireOne, par => par
                    .Action(bb => bb.SpawnConfig, EqsLifecycleNodes.Action_SpawnEqsSensorChild)
                    .Sequence(tactics => tactics
                        // Wait for the child sensor buffer to become ready.
                        // Action_WaitForSensor still reads from ctx.Self — add a child-aware
                        // variant if needed; for now use Action_SpawnEqsSensorChild Success as
                        // the gate (it returns Success only after the child is spawned, but the
                        // buffer takes a few solver ticks).
                        // Option B: pass SpawnedHandle to a new WaitForChildSensor action.
                        // Simplest: re-use WaitForSensor against the child entity by calling
                        // Action_WaitForChildSensor (implement below).
                        .Action(bb => bb.SpawnConfig, EqsLifecycleNodes.Action_WaitForChildSensor)
                        .Action(bb => bb.MoveConfig,  EqsCombatNodes.Action_MoveToOptimalCover)
                        .Action(bb => bb.MoveConfig,  EqsCombatNodes.Action_HoldPosition)
                    )
                )
            )
            .Action(bb => bb.MoveConfig, EqsCombatNodes.Action_Wander)
        );
}
```

**`Action_WaitForChildSensor`** — add to `EqsLifecycleNodes`:

```csharp
[BTreeAction]
public static NodeStatus Action_WaitForChildSensor(
    ref EqsSpawnParams p,
    ref BehaviorTreeState state,
    ref BTreeContext ctx)
{
    if (!p.SpawnedHandle.IsValid || !ctx.World.IsAlive(p.SpawnedHandle.ChildId))
        return NodeStatus.Running;
    if (!ctx.World.HasComponent<EqsCognitiveBuffer>(p.SpawnedHandle.ChildId))
        return NodeStatus.Running;
    ref readonly var buf = ref ctx.World.GetComponentRO<EqsCognitiveBuffer>(p.SpawnedHandle.ChildId);
    return buf.IsReady ? NodeStatus.Success : NodeStatus.Running;
}
```

> **Linking SpawnedHandle → MoveConfig.SensorHandle**: The v2 tree needs `MoveConfig.SensorHandle`
> to point to the spawned child. Since both are in the same blackboard, you can add an
> intermediary action that copies `SpawnConfig.SpawnedHandle` → `MoveConfig.SensorHandle`, or
> you can use a lambda accessor that reads from `SpawnConfig`. Choose whichever approach
> compiles cleanly with the BTree builder API. If the lambda approach is used, ensure the
> `EqsSensorHandle` field on `MoveToOptimalCoverParams` is still populated before
> `Action_MoveToOptimalCover` runs. Simplest approach: add an `Action_BindSensorHandle`
> action to `EqsLifecycleNodes` that copies SpawnedHandle into the MoveConfig.SensorHandle.

### C. Multi-sensor integration test

Add to a new file `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsMultiTemplateTests.cs`:

```
Test name: Eqs_MultiSensor_OneAgentTwoConcurrentQueries

Setup:
  - observer entity (no NetworkIdentity for simplicity)
  - 3 enemy entities placed at (10,0), (20,0), (30,0)
  - 3 cover points at (5,1), (15,1), (25,1)
  - Template A: FindNearestEnemy (EntitiesInRadius generator, blueprintId = 16001)
  - Template B: FindCoverFromTarget (CoverPointsGenerator, blueprintId = 16002)
  - Spawn two child sensors:
      child[0]: PartMetadata{ParentEntity=observer, InstanceId=0}, EqsSensor{BlueprintId=16001}
      child[1]: PartMetadata{ParentEntity=observer, InstanceId=1}, EqsSensor{BlueprintId=16002}

Pump:
  - Run solver ticks until both child[0].EqsCognitiveBuffer.IsReady AND
    child[1].EqsCognitiveBuffer.IsReady (timeout 10 s)

Assert:
  - child[0].EqsCognitiveBuffer.Count > 0 (entity-shaped results: enemies)
  - child[1].EqsCognitiveBuffer.Count > 0 (positional results: cover points)
  - The top result of child[0] has EntityId != 0 (it is an entity candidate)
  - The top result of child[1] has EntityId == 0 and (PositionX != 0 or PositionY != 0)
    (it is a positional candidate)
  - The observer's own EqsCognitiveBuffer does NOT exist or is empty (results go to children,
    not the parent)
```

### D. HideInCover_BT_v2 smoke test

Add to the same `EqsMultiTemplateTests.cs`:

```
Test name: HideInCover_BT_v2_SmokeTest_AgentMovesToCover

Setup:
  - Observer entity with TargetMemory (Count=1, ThreatScores[0]=100, PositionsX/Y[0]=30,0)
    + LocomotionChannel + NetworkIdentity (optional for offline)
  - ManualCoverProvider with one cover point at (5,0)
  - Register FindCoverFromTarget template (blueprintId = 16003, CoverPointsGenerator)
  - Build and run HideInCover_BT_v2 on the observer

Pump 500 ms or until LocomotionChannel.ActiveAction == NavigationConstants.ActionIdMoveTo

Assert:
  - LocomotionChannel.ActiveAction == ActionIdMoveTo
  - MoveToParams.Destination approximately equals (5, 0)
  - Existing HideInCover_BT test (from BATCH-11/12) must still pass (no regression)
```

---

## File Checklist

| File | Change |
|---|---|
| `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/EqsLifecycleNodes.cs` | Add `EqsSpawnParams`, `Action_SpawnEqsSensorChild`, `Deactivate_SpawnEqsSensorChild`, `Action_WaitForChildSensor`, `FindExistingChild` helper |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/EqsCombatNodes.cs` | Add `EqsSensorHandle SensorHandle` to `MoveToOptimalCoverParams`; update `Action_MoveToOptimalCover` to resolve buffer entity from handle |
| `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HideInCoverBehavior.cs` | Add `HideInCoverV2Blackboard`, `BuildHideInCoverV2Tree()`, `Action_BindSensorHandle` (if needed) |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsChildSensorActionTests.cs` | NEW — T-CS-A1..5 |
| `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsMultiTemplateTests.cs` | NEW — `Eqs_MultiSensor_OneAgentTwoConcurrentQueries` + `HideInCover_BT_v2_SmokeTest` |

---

## Constraints (Non-Negotiable)

1. **ECB-only for structural mutation during Simulation phase.** `Action_SpawnEqsSensorChild`
   and `Deactivate_SpawnEqsSensorChild` MUST use `ctx.World.GetCommandBuffer()` for
   `CreateEntity`, `AddComponent`, `DestroyEntity`. Direct mutation during the Simulation
   phase corrupts ECS chunk arrays.

2. **No ECS scan in steady-state path.** `FindExistingChild` is called only on first entry
   or after a BTree restart (when `SpawnedHandle.IsValid == false`). In steady state
   (re-entry with valid handle), the scan is skipped entirely. The cached `EntityQuery` must
   be stored at module level (static field on `EqsLifecycleNodes`), never rebuilt per tick.

3. **Backwards compat.** `HideInCover_BT` must continue to work. `Action_MoveToOptimalCover`
   with `SensorHandle.IsValid == false` must fall back to reading `ctx.Self`'s buffer exactly
   as before. Existing tests must pass unchanged.

4. **`SubEntityCleanupSystem` handles parent death.** Do NOT add explicit parent-death cleanup
   to the actions. The ECS infrastructure already handles it.

---

## Deliverable

Write your batch report to:
`.dev/eqs-2/reports/BATCH-16-REPORT.md`

The report must contain:
- Files changed with one-line description each
- Test results: both FDP toolkit and Hrot integration EQS filter counts
- Any deviations from the spec and why
