# BATCH-12 — HideInCover BTree (EQS-030, EQS-031)

## Scope
Implement the final two EQS tasks:
- **EQS-030** — `MoveToOptimalCoverParams` struct + `Action_MoveToOptimalCover` BTree action node
- **EQS-031** — `HideInCoverBlackboard` struct + `HideInCover_BT` tree definition

Refer to:
- `.dev/eqs-2/TASK-DETAIL.md` §TASK-EQS-030 and §TASK-EQS-031 for success conditions
- `.dev/eqs-2/IMPLEM_DETAILS.md` L:3680–3880 for reference implementation
- `docs/AI_DEV_GUIDE.md` for BTree authoring patterns

---

## 1. New File: `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/EqsCombatNodes.cs`

**Namespace:** `Hrot.AI.Behaviors.Brains`

### 1a. `MoveToOptimalCoverParams` struct

```csharp
using System.Runtime.InteropServices;

namespace Hrot.AI.Behaviors.Brains
{
    /// <summary>
    /// Blackboard parameters for <see cref="EqsCombatNodes.Action_MoveToOptimalCover"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct MoveToOptimalCoverParams
    {
        /// <summary>Desired travel speed (m/s).</summary>
        public float Speed;
        /// <summary>Distance from the cover point that counts as arrival (m).</summary>
        public float ArrivalRadius;
    }
}
```

### 1b. `EqsCombatNodes` static class

Usings required:
```
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;
```

**`Condition_HasTarget`** — checks `TargetMemory` for any entry with `ThreatScore > 0`:
```csharp
[BTreeCondition]
public static NodeStatus Condition_HasTarget(
    ref MoveToOptimalCoverParams p,
    ref BehaviorTreeState state,
    ref BTreeContext ctx)
{
    if (!ctx.World.HasComponent<TargetMemory>(ctx.Self))
        return NodeStatus.Failure;
    ref readonly var mem = ref ctx.World.GetComponentRO<TargetMemory>(ctx.Self);
    unsafe
    {
        for (int i = 0; i < mem.Count; i++)
            if (mem.ThreatScores[i] > 0f) return NodeStatus.Success;
    }
    return NodeStatus.Failure;
}
```

**`Action_MoveToOptimalCover`** — reads `EqsCognitiveBuffer.GetTop()`, drives `LocomotionChannel`:
```csharp
[BTreeAction]
public static unsafe NodeStatus Action_MoveToOptimalCover(
    ref MoveToOptimalCoverParams p,
    ref BehaviorTreeState state,
    ref BTreeContext ctx)
{
    // 1. Guard: require both components
    if (!ctx.World.HasComponent<EqsCognitiveBuffer>(ctx.Self) ||
        !ctx.World.HasComponent<LocomotionChannel>(ctx.Self))
        return NodeStatus.Failure;

    // 2. Buffer must be ready and non-empty
    ref readonly var buffer = ref ctx.World.GetComponentRO<EqsCognitiveBuffer>(ctx.Self);
    if (!buffer.IsReady || buffer.Count == 0)
        return NodeStatus.Failure;

    var bestCover = buffer.GetTop();
    var targetPos = new Vector2(bestCover.PositionX, bestCover.PositionY);

    ref var channel = ref ctx.World.GetComponentRW<LocomotionChannel>(ctx.Self);

    // 3. Propagate behavior instance ID to prevent channel arbitration stomping
    if (ctx.World.HasComponent<BehaviorState>(ctx.Self))
    {
        var behavior = ctx.World.GetComponent<BehaviorState>(ctx.Self);
        channel.BehaviorInstanceId = behavior.InstanceId;
    }

    // 4. Forward terminal status from the executor
    if (channel.ActiveAction == NavigationConstants.ActionIdMoveTo)
    {
        if (channel.Status == NodeStatus.Success) return NodeStatus.Success;
        if (channel.Status == NodeStatus.Failure) return NodeStatus.Failure;
    }

    // 5. Activate or update the locomotion channel
    bool needsActivation = channel.ActiveAction != NavigationConstants.ActionIdMoveTo ||
                           channel.Status == NodeStatus.Failure;

    if (needsActivation)
    {
        unchecked { channel.ActionInstanceId++; }
        channel.ActiveAction = NavigationConstants.ActionIdMoveTo;
        channel.Status = NodeStatus.Running;

        var moveToParams = new MoveToParams
        {
            Destination  = targetPos,
            ArrivalRadius = p.ArrivalRadius,
            Speed        = p.Speed,
            ReverseAllowed = 0,
        };

        fixed (byte* dst = channel.Params)
        {
            *(MoveToParams*)dst = moveToParams;
        }
    }

    return NodeStatus.Running;
}
```

**`Action_HoldPosition`** — stub; holds entity in place, always Running:
```csharp
[BTreeAction]
public static NodeStatus Action_HoldPosition(
    ref MoveToOptimalCoverParams p,
    ref BehaviorTreeState state,
    ref BTreeContext ctx)
{
    return NodeStatus.Running;
}
```

**`Action_Wander`** — stub; wanders indefinitely, always Running:
```csharp
[BTreeAction]
public static NodeStatus Action_Wander(
    ref MoveToOptimalCoverParams p,
    ref BehaviorTreeState state,
    ref BTreeContext ctx)
{
    return NodeStatus.Running;
}
```

> **Note:** `Action_HoldPosition` and `Action_Wander` are intentional stubs here. The EQS-031 BTree uses them as low-priority fallback/terminal nodes. Full locomotion integration is tracked in Phase 7 debt. They must compile and return `NodeStatus.Running`.

---

## 2. New File: `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/HideInCoverBehavior.cs`

**Namespace:** `Hrot.AI.Behaviors.Brains`

Usings required:
```
using System.Runtime.InteropServices;
using Fbt;
using Fdp.Toolkit.Behavior;
```

### 2a. `HideInCoverBlackboard` struct

```csharp
/// <summary>
/// Unmanaged blackboard memory for the <c>HideInCover_BT</c> behavior.
/// Must use sequential layout for deterministic Blueprint offset generation.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct HideInCoverBlackboard
{
    /// <summary>EQS query parameters — consumed by <c>EqsLifecycleNodes</c> actions.</summary>
    public EqsParams EqsConfig;

    /// <summary>Locomotion parameters — consumed by <c>EqsCombatNodes</c> actions.</summary>
    public MoveToOptimalCoverParams MoveConfig;
}
```

### 2b. `TacticsNodes` static class with `BuildHideInCoverTree`

```csharp
/// <summary>
/// Fluent BTree definitions for high-level tactical behaviors.
/// </summary>
public static class TacticsNodes
{
    /// <summary>
    /// HideInCover_BT — agent seeks optimal cover when a threat is present.
    ///
    /// Tree structure:
    ///   ObserverSelector
    ///     [High] Sequence
    ///       Condition_HasTarget          (returns Failure if no live threat)
    ///       Parallel(RequireOne)
    ///         Action_MaintainEqsSensor   (resource owner — always Running)
    ///         Sequence
    ///           Action_WaitForSensor     (polls buffer until IsReady)
    ///           Action_MoveToOptimalCover
    ///           Action_HoldPosition
    ///     [Low]
    ///       Action_Wander
    /// </summary>
    [BTreeDefinition("HideInCover_BT")]
    public static BTreeBuilder<HideInCoverBlackboard, BTreeContext> BuildHideInCoverTree()
    {
        return new BTreeBuilder<HideInCoverBlackboard, BTreeContext>()
            .ObserverSelector(obs => obs
                .Sequence(seq => seq
                    .Condition(bb => bb.MoveConfig, EqsCombatNodes.Condition_HasTarget)
                    .Parallel(Policy.RequireOne, par => par
                        .Action(bb => bb.EqsConfig,  EqsLifecycleNodes.Action_MaintainEqsSensor)
                        .Sequence(tactics => tactics
                            .Action(bb => bb.EqsConfig,  EqsLifecycleNodes.Action_WaitForSensor)
                            .Action(bb => bb.MoveConfig, EqsCombatNodes.Action_MoveToOptimalCover)
                            .Action(bb => bb.MoveConfig, EqsCombatNodes.Action_HoldPosition)
                        )
                    )
                )
                .Action(bb => bb.MoveConfig, EqsCombatNodes.Action_Wander)
            );
    }
}
```

**Important:** `[BTreeDefinition]` is scanned by the Fbt source generator (`Fbt.SourceGen`) at compile time. The method must be `public static`, must return `BTreeBuilder<TBlackboard, TContext>`, and must be in a `public static class`. Do not rename or make it non-static.

---

## 3. New File: `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsCombatNodesTests.cs`

**[Collection("EqsIntegrationTests")]** (required — same collection as `EqsLifecycleNodesTests`)

Follow the exact same pattern as `EqsLifecycleNodesTests.cs`:
- Constructor creates `EntityRepository`, calls `SimHostComponentRegistry.RegisterAll(_repo)`, creates entity
- `IDisposable.Dispose()` disposes the repo (and `EqsResultPool` if present)
- Tests call node methods directly with a real repo + manual `BTreeContext`

Usings:
```
using System.Numerics;
using System.Runtime.InteropServices;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Navigation;
using Fdp.Toolkit.Perception.Components;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.AI.Behaviors.Brains;
using Hrot.SimHost;
using Xunit;
```

### Test: T-COV1 (EQS-030 SC1) — MoveTo activated when buffer is ready

```
[Fact]
public void EqsCombatNodes_MoveToOptimalCover_WritesChannelWithCorrectDestination()
```

Steps:
1. Add `EqsCognitiveBuffer` to entity: `Count=1, LastUpdateTick=1`. Then via `GetSpanRW()[0]` set `PositionX=10f, PositionY=20f, Score=1f`.
   ```csharp
   var buf = new EqsCognitiveBuffer { Count = 1, LastUpdateTick = 1 };
   buf.GetSpanRW()[0] = new EqsResult { PositionX = 10f, PositionY = 20f, Score = 1f };
   _repo.AddComponent(_entity, buf);
   ```
2. Add `LocomotionChannel` to entity (default zero state).
3. Call `EqsCombatNodes.Action_MoveToOptimalCover(ref p, ref state, ref ctx)`.
4. Assert return value == `NodeStatus.Running`.
5. Assert `channel.ActiveAction == NavigationConstants.ActionIdMoveTo`.
6. Read `MoveToParams` from `channel.Params` using unsafe pointer cast, assert `Destination == new Vector2(10f, 20f)`.

```csharp
ref readonly var channel = ref _repo.GetComponentRO<LocomotionChannel>(_entity);
Assert.Equal(NavigationConstants.ActionIdMoveTo, channel.ActiveAction);
unsafe
{
    MoveToParams mp;
    fixed (byte* src = channel.Params) mp = *(MoveToParams*)src;
    Assert.Equal(new Vector2(10f, 20f), mp.Destination);
}
```

### Test: T-COV2 (EQS-030 SC2) — Returns Failure when buffer not ready

```
[Fact]
public void EqsCombatNodes_MoveToOptimalCover_ReturnsFailureWhenBufferNotReady()
```

Steps:
1. Add `EqsCognitiveBuffer` with `Count=0, LastUpdateTick=0` (not ready).
2. Add `LocomotionChannel`.
3. Assert `EqsCombatNodes.Action_MoveToOptimalCover(...)` returns `NodeStatus.Failure`.

### Test: T-COV3 (EQS-030 SC3) — Forwards Success from channel

```
[Fact]
public void EqsCombatNodes_MoveToOptimalCover_ForwardsSuccessFromChannel()
```

Steps:
1. Add `EqsCognitiveBuffer` ready with 1 candidate (PositionX=5f, PositionY=5f).
2. Add `LocomotionChannel` with `ActiveAction = NavigationConstants.ActionIdMoveTo` and `Status = NodeStatus.Success`.
3. Assert `EqsCombatNodes.Action_MoveToOptimalCover(...)` returns `NodeStatus.Success`.

### Test: T-COV4 (EQS-031 SC-related) — Condition_HasTarget

```
[Fact]
public void EqsCombatNodes_ConditionHasTarget_SucceedsWithThreatFailsWithout()
```

Steps:
1. No `TargetMemory` component — assert returns `NodeStatus.Failure`.
2. Add `TargetMemory` with `Count=0` — assert returns `NodeStatus.Failure`.
3. Add threat entry: set `Count=1` and via unsafe `mem.ThreatScores[0] = 1.5f` — assert returns `NodeStatus.Success`.

```csharp
_repo.AddComponent(_entity, new TargetMemory());
Assert.Equal(NodeStatus.Failure,
    EqsCombatNodes.Condition_HasTarget(ref p, ref state, ref ctx));

ref var mem = ref _repo.GetComponentRW<TargetMemory>(_entity);
unsafe { mem.ThreatScores[0] = 1.5f; }
mem.Count = 1;
Assert.Equal(NodeStatus.Success,
    EqsCombatNodes.Condition_HasTarget(ref p, ref state, ref ctx));
```

### Test: T-COV5 (EQS-031 SC2+SC3) — HideInCover node sequence smoke test

```
[Fact]
public void HideInCoverBehavior_NodeSequence_SetsChannelThenCleansUpOnThreatRemoval()
```

This test simulates the two key branches of the `HideInCover_BT` without running the full BTree runtime:

**Phase A — threat present, buffer ready → channel set:**
```
var eqsParams  = new EqsParams { BlueprintId = 1, SearchRadius = 50f };
var moveParams = new MoveToOptimalCoverParams { Speed = 5f, ArrivalRadius = 1f };
var state      = new BehaviorTreeState();
var ctx        = new BTreeContext { Self = _entity, World = _repo };

// Step 1: Add TargetMemory with a live threat
var mem = new TargetMemory();
unsafe { mem.ThreatScores[0] = 2f; mem.EntityIds[0] = 99L; }
mem.Count = 1;
_repo.AddComponent(_entity, mem);

// Step 2: Simulate Action_MaintainEqsSensor (adds EqsSensor)
var result = EqsLifecycleNodes.Action_MaintainEqsSensor(ref eqsParams, ref state, ref ctx);
Assert.Equal(NodeStatus.Running, result);
Assert.True(_repo.HasComponent<EqsSensor>(_entity));

// Step 3: Pre-populate EqsCognitiveBuffer (as solver would)
var buf = new EqsCognitiveBuffer { Count = 1, LastUpdateTick = 1 };
buf.GetSpanRW()[0] = new EqsResult { PositionX = 30f, PositionY = 40f, Score = 1f };
_repo.AddComponent(_entity, buf);

// Step 4: Add LocomotionChannel
_repo.AddComponent(_entity, new LocomotionChannel());

// Step 5: Call Action_MoveToOptimalCover
var moveResult = EqsCombatNodes.Action_MoveToOptimalCover(ref moveParams, ref state, ref ctx);
Assert.Equal(NodeStatus.Running, moveResult);
Assert.Equal(NavigationConstants.ActionIdMoveTo,
    _repo.GetComponentRO<LocomotionChannel>(_entity).ActiveAction);
```

**Phase B — threat removed → deactivator clears sensor:**
```
// Remove threat from TargetMemory
ref var memW = ref _repo.GetComponentRW<TargetMemory>(_entity);
memW.Count = 0;
unsafe { memW.ThreatScores[0] = 0f; }

// The ObserverSelector would abort the branch and call the deactivator
EqsLifecycleNodes.Deactivate_MaintainEqsSensor(ref eqsParams, ref state, ref ctx);

Assert.False(_repo.HasComponent<EqsSensor>(_entity),
    "EqsSensor must be removed when the branch is aborted");
Assert.False(_repo.HasComponent<EqsCognitiveBuffer>(_entity),
    "EqsCognitiveBuffer must be removed when the branch is aborted");
```

---

## 4. Build Verification

After implementing all three files, run the full build to ensure `[BTreeDefinition]` compiles cleanly:

```
dotnet build Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj
dotnet build Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj
```

Then run the new tests:
```
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --filter "FullyQualifiedName~EqsCombatNodesTests" --no-build
```

Expected: 5 / 5 PASS.

Finally, run the full EQS test suite to ensure no regressions:
```
dotnet test Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/ --filter "FullyQualifiedName~Eqs" --no-build
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/ --filter "FullyQualifiedName~Eqs" --no-build
dotnet test Hrot/Subsystems/Hrot.IG.Tests/ --filter "FullyQualifiedName~Eqs" --no-build
```

---

## 5. Report

After all tests pass, write `.dev/eqs-2/reports/BATCH-12-REPORT.md` following the standard report template from `.github/skills/developer/SKILL.md`.

---

## Notes and Constraints

- **ASCII only** — no Unicode in comments or string literals.
- **No `[unsafe]` keyword on classes** — use `unsafe` only on specific methods/blocks.
- **`[InlineArray]` write rule** — always use `buffer.GetSpanRW()[i]` to write to `EqsCognitiveBuffer.Results`, never direct index assignment.
- **`[BTreeDefinition]` method must be** `public static`, return `BTreeBuilder<HideInCoverBlackboard, BTreeContext>`, in a `public static class`.
- **Pre-existing ~32 failures in `Hrot.SimHost.Tests`** are unrelated to EQS; ignore them.
- **Minimize diffs** — only create the 3 new files listed above; do not modify any existing files.
- **Do not add XML doc to unchanged code.**
