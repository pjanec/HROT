# BATCH-04 Instructions

**Batch:** BATCH-04  
**Tasks:** TASK-TI010, TASK-TI011  
**Phase:** 6 - Commander BTree Integration and Example Mapper

---

## Prerequisites

Read these files before writing any code:

- `.dev/tactical-intent/DESIGN.md` — full architecture overview
- `.dev/tactical-intent/TASK-TRACKER.md` — task status
- `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CgfNodes.cs` — BTree action node patterns
- `Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj` — project references
- `FDP/Toolkits/Fdp.Toolkits/Behavior/BTreeContext.cs` — BTreeContext struct (`World` is `EntityRepository`)
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/BehaviorComponents.cs` — BrainBlackboard layout
- `FDP/Toolkits/Fdp.Toolkits/Behavior/TacticalOrderMapper/ITacticalOrderMapper.cs` — mapper interface
- `FDP/Toolkits/Fdp.Toolkits/Behavior/Events/AssignTacticalIntentEvent.cs` — the event type
- `FDP/Toolkits/Fdp.Toolkits/Replication/Components/TkbIdentity.cs` — TkbIdentity struct (`TkbType: long`)
- `Hrot/Engine/Hrot.Core/MapDefinitions/TkbEntityTypes.cs` — `MilitaryApc = 503L`, `InfantrySoldier = 504L`
- `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/BehaviorIds.cs` — existing behavior ID constants
- `Hrot/Subsystems/Hrot.SimHost.Tests/TacticalIntentResolutionSystemTests.cs` — test harness pattern
- `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` — composition root (see how TacticalIntentMapperRegistry is constructed)
- `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs` — see TacticalIntentMapperRegistry parameter

---

## TASK-TI010 — Reference Commander BTree Action

**Goal:** Demonstrate how a Commander AI node publishes `AssignTacticalIntentEvent` for a subordinate.

### New File: `Hrot/Subsystems/Hrot.AI.Behaviors/Brains/CommanderNodes.cs`

Create a new static class `CommanderNodes` in namespace `Hrot.AI.Behaviors.Brains`.

**Required usings:**
```csharp
using System.Runtime.InteropServices;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Events;
```

**Blackboard wrapper and params DTO (value-type only; strings cannot be stored in unmanaged blackboard):**

```csharp
/// <summary>Typed blackboard wrapper for the IssueTacticalIntent Commander action.</summary>
[StructLayout(LayoutKind.Sequential)]
public struct IssueTacticalIntentBlackboard { public IssueTacticalIntentParams Params; }

/// <summary>
/// Blackboard layout for the IssueTacticalIntent Commander action.
/// <para>
/// IntentId and JsonParams cannot be embedded as strings in the unmanaged blackboard.
/// The IntentId is encoded as an integer ordinal resolved from the intent registry at
/// tree-build time by the AiBehaviorFactory (TODO: wire registry lookup).
/// JsonParams are pre-serialized as a fixed-length UTF-8 blob when the tree is authored
/// (TODO: implement fixed-buffer encoding).
/// </para>
/// <para>
/// For this reference implementation, the IntentId is hardcoded to the first registered
/// intent ordinal and JsonParams is empty.
/// </para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct IssueTacticalIntentParams
{
    /// <summary>
    /// Packed ECS entity value of the subordinate to command.
    /// 0 means no subordinate has been resolved yet — action returns Failure.
    /// TODO: extend to a fixed-size list of subordinates (formation roster).
    /// </summary>
    public long SubordinatePacked;

    /// <summary>
    /// Integer ordinal of the tactical intent to issue, resolved from the intent
    /// registry at tree authoring time. Maps to a string IntentId at runtime.
    /// TODO: wire to a registered intent-type lookup table in AiBehaviorFactory.
    /// </summary>
    public int IntentTypeOrdinal;
}
```

**BTree action method:**

```csharp
/// <summary>
/// BTree action node for issuing a tactical intent to a single subordinate entity.
/// <para>
/// Publishes <see cref="AssignTacticalIntentEvent"/> on the local event bus.
/// The event is consumed either by <c>TacticalIntentResolutionSystem</c> (if the
/// subordinate is local) or by <c>TacticalIntentEgressTranslator</c> (if remote).
/// </para>
/// <para>
/// This is a reference implementation. See <see cref="IssueTacticalIntentParams"/>
/// for the TODO items needed for full production use.
/// </para>
/// </summary>
[BTreeAction]
public static NodeStatus Action_IssueTacticalIntent(
    ref IssueTacticalIntentParams p,
    ref BehaviorTreeState state,
    ref BTreeContext ctx)
{
    if (p.SubordinatePacked == 0)
        return NodeStatus.Failure;

    var subordinate = new Entity((ulong)p.SubordinatePacked);

    // TODO: resolve IntentId string from a registered intent-type lookup table
    // (keyed by p.IntentTypeOrdinal). For the reference implementation, "DefendArea"
    // is used as a compile-time constant.
    const string intentId = "DefendArea";

    ctx.World.Bus.PublishManaged(new AssignTacticalIntentEvent
    {
        Entity     = subordinate,
        IntentId   = intentId,
        JsonParams = string.Empty
    });

    return NodeStatus.Success;
}
```

**Constraints:**
- Class must be `public static` so Fbt.SourceGen can discover it.
- Method must be `[BTreeAction]`, `public static`, return `NodeStatus`.
- First parameter must be the typed DTO (the `ref struct_name params` pattern).
- No dependency on `Hrot.Core` needed for TI010 — `AssignTacticalIntentEvent` and `Entity` are from FDP only.
- Must compile without errors.

**Tests for TI010:**

Create `Hrot/Subsystems/Hrot.SimHost.Tests/CommanderNodesTests.cs`.

Test: `Action_IssueTacticalIntent_WithValidSubordinate_PublishesEvent`
- Setup: EntityRepository with AssignTacticalIntentEvent registered on bus; create entity for subordinate.
- Create `IssueTacticalIntentParams { SubordinatePacked = (long)subordinate.PackedValue, IntentTypeOrdinal = 0 }`.
- Call `CommanderNodes.Action_IssueTacticalIntent(ref p, ref state, ref ctx)`.
- `SwapBuffers()`.
- Assert: `ReadManaged<AssignTacticalIntentEvent>()` returns one event; `Entity == subordinate`; `IntentId == "DefendArea"`.

Test: `Action_IssueTacticalIntent_WithZeroPacked_ReturnsFailure`
- Setup: `IssueTacticalIntentParams { SubordinatePacked = 0 }`.
- Assert: returns `NodeStatus.Failure`; no event published.

---

## TASK-TI011 — DefendAreaMapper

**Goal:** First concrete `ITacticalOrderMapper` implementation that maps the "DefendArea" intent to unit-type-specific behaviors.

### Modify: `Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj`

Add `Hrot.Core` project reference (needed for `TkbEntityTypes` constants):

```xml
<!-- Hrot.Core provides: TkbEntityTypes constants (MilitaryApc, InfantrySoldier, etc.) -->
<ProjectReference Include="..\..\..\Hrot\Engine\Hrot.Core\Hrot.Core.csproj" />
```

Wait — paths relative to `Hrot.AI.Behaviors.csproj` location. The csproj is at:
`Hrot/Subsystems/Hrot.AI.Behaviors/Hrot.AI.Behaviors.csproj`

`Hrot.Core.csproj` is at:
`Hrot/Engine/Hrot.Core/Hrot.Core.csproj`

Relative path: `..\..\Engine\Hrot.Core\Hrot.Core.csproj`

Add inside the existing `<ItemGroup>`:
```xml
<!-- Hrot.Core provides: TkbEntityTypes constants used by mapper implementations. -->
<ProjectReference Include="..\..\Engine\Hrot.Core\Hrot.Core.csproj" />
```

### New File: `Hrot/Subsystems/Hrot.AI.Behaviors/Mappers/DefendAreaMapper.cs`

Create a new `Mappers/` directory. File:

```csharp
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.TacticalOrderMapper;
using Fdp.Toolkit.Replication.Components;
using Hrot.Map.MapDefinitions;

namespace Hrot.AI.Behaviors.Mappers
{
    /// <summary>
    /// Mapper for the "DefendArea" tactical intent.
    /// Translates <see cref="AssignTacticalIntentEvent.IntentId"/> == "DefendArea"
    /// into a unit-type-specific <see cref="AssignBehaviorEvent"/>:
    /// <list type="bullet">
    ///   <item><see cref="TkbEntityTypes.MilitaryApc"/> → "ConvoyEscort" behavior</item>
    ///   <item><see cref="TkbEntityTypes.InfantrySoldier"/> → "InfantryCombat" behavior</item>
    ///   <item>All other unit types → returns <c>false</c> (pass-through fallback)</item>
    /// </list>
    /// <para>
    /// The JsonParams from the original intent (centre lat/lon, radius) are forwarded
    /// unchanged to the target behavior for further interpretation.
    /// </para>
    /// </summary>
    public sealed class DefendAreaMapper : ITacticalOrderMapper
    {
        public string TargetIntentId => "DefendArea";

        public bool TryMap(
            Entity entity,
            EntityRepository repo,
            string jsonParams,
            out AssignBehaviorEvent assignment)
        {
            assignment = null!;

            if (!repo.HasComponent<TkbIdentity>(entity))
                return false;

            var tkbType = repo.GetComponent<TkbIdentity>(entity).TkbType;

            string behaviorName = tkbType switch
            {
                TkbEntityTypes.MilitaryApc     => "ConvoyEscort",
                TkbEntityTypes.InfantrySoldier => "InfantryCombat",
                _                              => string.Empty
            };

            if (string.IsNullOrEmpty(behaviorName))
                return false;

            assignment = new AssignBehaviorEvent
            {
                Entity      = entity,
                BehaviorName = behaviorName,
                JsonParams   = jsonParams
            };
            return true;
        }
    }
}
```

**Check namespace:** Look at `TkbEntityTypes.cs` for its namespace (likely `Hrot.Map.MapDefinitions` or `Hrot.Core.MapDefinitions`). Adjust the `using` accordingly.

**Check `AssignBehaviorEvent` fields:** Look at `FDP/Toolkits/Fdp.Toolkits/Behavior/Events/AssignBehaviorEvent.cs` for the exact field names (may be `BehaviorName`/`Name` — use whatever the file declares).

**Check `repo.GetComponent<T>` vs `GetComponentRO<T>`:** Use the same pattern as `TacticalIntentResolutionSystem.cs` which already calls `repo.HasAuthority<BehaviorState>`. For read-only access to `TkbIdentity`, use `repo.GetComponent<TkbIdentity>(entity)` or `GetComponentRO<TkbIdentity>` — check which one compiles.

### Modify: `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs`

Register `DefendAreaMapper` in the `TacticalIntentMapperRegistry`:

Find the code in `CgfSubsystem.cs` that creates `TacticalIntentMapperRegistry` and passes it to `CgfLogicPack`. Add:

```csharp
var mapperRegistry = new TacticalIntentMapperRegistry();
mapperRegistry.Register(new DefendAreaMapper());
```

**Check the exact location:** Look for `new TacticalIntentMapperRegistry()` in `CgfSubsystem.cs`. Add the `Register` call immediately after the `new TacticalIntentMapperRegistry()` line, before the registry is passed to `CgfLogicPack`.

**Required using:** Add `using Hrot.AI.Behaviors.Mappers;` to `CgfSubsystem.cs`.

**Check if `CgfSubsystem.cs` already has a reference to `Hrot.AI.Behaviors`:** Look at `Hrot.CGF.csproj` — `Hrot.AI.Behaviors` is NOT currently referenced by `Hrot.CGF`. 

Two options:
1. Add `Hrot.AI.Behaviors` reference to `Hrot.CGF.csproj` and register in `CgfSubsystem.cs`.
2. Register the mapper in a different composition root that already references `Hrot.AI.Behaviors`.

**Preferred option:** Check which projects reference both `Hrot.CGF` and `Hrot.AI.Behaviors`. `Hrot.ClusterRunner` references both. But the best composition root is whichever boots up the CGF subsystem and also has access to the mapper.

**Actually:** Look at `Hrot.SimHost/` — it is likely the top-level composition root. Check `Hrot.SimHost.csproj` for references to both `Hrot.CGF` and `Hrot.AI.Behaviors`. If not, add `Hrot.AI.Behaviors` to `Hrot.CGF.csproj` (since `Hrot.CGF` is already the Brain-tier bundle and `Hrot.AI.Behaviors` contains behavior definitions that CGF uses for hot-reload).

Look at `CgfSubsystem.cs` to understand where `TacticalIntentMapperRegistry` is created. Then find the appropriate composition root that can call `mapperRegistry.Register(new DefendAreaMapper())`.

**If adding to `Hrot.CGF.csproj`:**
```xml
<!-- Hrot.AI.Behaviors: behavior behavior trees and mapper implementations, hot-reloaded -->
<ProjectReference Include="..\Hrot.AI.Behaviors\Hrot.AI.Behaviors.csproj" />
```

**Tests for TI011:**

Create `Hrot/Subsystems/Hrot.SimHost.Tests/DefendAreaMapperTests.cs`.

**Test setup helper:** Create EntityRepository, register `TkbIdentity` component.

Test: `TryMap_MilitaryApc_ReturnsConvoyEscort`
- Create entity; add `TkbIdentity { TkbType = TkbEntityTypes.MilitaryApc }`.
- Call `new DefendAreaMapper().TryMap(entity, repo, "{}", out var assignment)`.
- Assert: returns `true`; `assignment.BehaviorName == "ConvoyEscort"`; `assignment.Entity == entity`.

Test: `TryMap_InfantrySoldier_ReturnsInfantryCombat`
- Create entity; add `TkbIdentity { TkbType = TkbEntityTypes.InfantrySoldier }`.
- Assert: `true`, `BehaviorName == "InfantryCombat"`.

Test: `TryMap_UnknownTkbType_ReturnsFalse`
- Create entity; add `TkbIdentity { TkbType = 999L }`.
- Assert: returns `false`; `assignment` is default.

Test: `TryMap_NoTkbIdentity_ReturnsFalse`
- Create entity WITHOUT adding `TkbIdentity`.
- Assert: returns `false`.

Test: `TargetIntentId_IsDefendArea`
- Assert `new DefendAreaMapper().TargetIntentId == "DefendArea"`.

**Test file namespace:** `Hrot.SimHost.Tests` (same as other test files).

---

## Build and Test Instructions

After implementing both tasks:

```
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet build IOS-IG-SimHost.sln --no-restore -v quiet 2>&1 | Select-String "error CS|Build succeeded|FAILED"
```

Then run targeted tests:
```
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build --nologo --filter "FullyQualifiedName~CommanderNodes|FullyQualifiedName~DefendAreaMapper" 2>&1 | Select-String "Passed!|Failed!"
```

Also run the full SimHost test suite to check for regressions:
```
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build --nologo 2>&1 | Select-String "Passed!|Failed!"
```

All new tests must pass. Pre-existing failures (2 MissionPlanTranslator + 14 Toolkits) are acceptable.

---

## Report

Write the report to: `.dev/tactical-intent/reports/BATCH-04-REPORT.md`

Follow the format of `.dev/tactical-intent/reports/BATCH-01-REPORT.md`.
