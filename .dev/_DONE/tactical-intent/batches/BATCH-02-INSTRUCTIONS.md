# BATCH-02 Instructions

**Batch:** BATCH-02  
**Status:** Ready for implementation  
**Tasks:** TASK-TI004, TASK-TI005, TASK-TI006  
**Goal:** Wire MissionAdapterSystem into the new intent pipeline; add Commander category; add DefendAreaIntentDto example.

---

## Mandatory Reading

Before writing any code, read:

1. `.dev/tactical-intent/DESIGN.md` — architecture overview and rationale
2. `.dev/tactical-intent/TASK-DETAIL.md` — detailed success conditions for TI004, TI005, TI006
3. `Hrot/Subsystems/Hrot.CGF/Systems/MissionAdapterSystem.cs` — current implementation to modify
4. `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/BehaviorCategory.cs` — enum to extend
5. `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/ConvoyEscortParamsJsonDto.cs` — pattern for TI006
6. `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/BehaviorIds.cs` — IDs to extend
7. `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/BehaviorCatalog.cs` — auto-discovers DTOs via reflection
8. `Hrot/Engine/Hrot.Presentation/Behavior/BehaviorSchemaDiscovery.cs` — auto-registers via reflection
9. `FDP/Toolkits/Fdp.Toolkits/Behavior/Components/DomainMissionPlan.cs` — DomainMissionTask.BehaviorId
10. `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs` — MissionAdapterSystem construction site

---

## Sequence

Implement tasks in this order. Fix any build or test failures before continuing.

1. TASK-TI005 (smallest, isolated change, no dependencies)
2. TASK-TI006 (depends on TI005 adding Commander flag)
3. TASK-TI004 (depends on TI001 from BATCH-01; modifies MissionAdapterSystem)

---

## TASK-TI005 — Add Commander Flag to BehaviorCategory

**File:** `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/BehaviorCategory.cs`

**Change:** Add `Commander = 1 << 4` to the `[Flags]` enum, after `Insurgent`.

**Constraints:**
- `AllMilitary` value must NOT change (remains `MilitaryApc | Infantry | Insurgent`).
- `Commander` must NOT be part of `AllMilitary`.
- No other values may be renumbered.

**After the change the file looks like:**

```csharp
namespace Hrot.Map.Definitions.Behavior
{
    [Flags]
    public enum BehaviorCategory
    {
        None        = 0,
        Civilian    = 1 << 0,
        MilitaryApc = 1 << 1,
        Infantry    = 1 << 2,
        Insurgent   = 1 << 3,
        AllMilitary = MilitaryApc | Infantry | Insurgent,
        Commander   = 1 << 4,
    }
}
```

**Tests for TI005:**

Add to `Hrot/Engine/Hrot.Core.Tests/BehaviorCategoryTests.cs` (new file):

```csharp
// SC-1: Commander value is 16
[Fact]
public void Commander_Value_Is16()
{
    Assert.Equal(16, (int)BehaviorCategory.Commander);
}

// SC-2: Commander is NOT part of AllMilitary
[Fact]
public void AllMilitary_DoesNotContain_Commander()
{
    Assert.False(BehaviorCategory.AllMilitary.HasFlag(BehaviorCategory.Commander));
}
```

Test project: `Hrot/Engine/Hrot.Core.Tests/Hrot.Core.Tests.csproj`

---

## TASK-TI006 — Add DefendAreaIntentDto Example

**Overview:** Create a single intent DTO demonstrating the pattern. The DTO decorates itself with `[BehaviorContract]` so it is auto-discovered by `BehaviorCatalog` and `BehaviorSchemaDiscovery.AutoRegister`.

### Step 1 — Reserve intent ID in BehaviorIds.cs

**File:** `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/BehaviorIds.cs`

Add a new section at the bottom (inside the class, before the closing brace):

```csharp
// Tactical Intent DTOs (1000-1099) — generic intents resolved by mappers
public const int DefendArea_Intent = 1000;
```

The value 1000 is intentionally outside the CGF behavior range (3001-3099) to avoid collisions.

### Step 2 — Create DefendAreaIntentDto

**File:** `Hrot/Engine/Hrot.Core/MapDefinitions/Behavior/Intents/DefendAreaIntentDto.cs` (new file, new directory)

```csharp
using Hrot.Map.Definitions.Behavior;

namespace Hrot.Map.Definitions.Behavior.Intents
{
    /// <summary>
    /// Intent DTO for the "DefendArea" tactical intent.
    /// Decorated with <see cref="BehaviorContractAttribute"/> so it is
    /// auto-discovered by <c>BehaviorSchemaDiscovery.AutoRegister</c> and
    /// <c>BehaviorCatalog</c>.
    ///
    /// <para>
    /// <b>BehaviorId:</b> "DefendArea" — this string must match the
    /// <c>TargetIntentId</c> of the <c>DefendAreaMapper</c> (TASK-TI011).
    /// </para>
    /// </summary>
    [BehaviorContract(BehaviorIds.DefendArea_Intent, BehaviorId, BehaviorCategory.AllMilitary)]
    public sealed class DefendAreaIntentDto
    {
        public const string BehaviorId = "DefendArea";

        /// <summary>Latitude of the area center.</summary>
        public double CenterLat { get; set; }

        /// <summary>Longitude of the area center.</summary>
        public double CenterLon { get; set; }

        /// <summary>Radius in meters.</summary>
        public float RadiusMeters { get; set; }
    }
}
```

**Tests for TI006:**

Add to `Hrot/Engine/Hrot.Core.Tests/DefendAreaIntentDtoTests.cs` (new file):

```csharp
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using Xunit;

namespace Hrot.Map.Common.Tests;

public class DefendAreaIntentDtoTests
{
    // SC-2: BehaviorCatalog includes "DefendArea" for MilitaryApc (AllMilitary covers it)
    [Fact]
    public void BehaviorCatalog_MilitaryApc_ContainsDefendArea()
    {
        var behaviors = BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.MilitaryApc);
        Assert.Contains("DefendArea", behaviors);
    }

    // SC-3: BehaviorCatalog does NOT include "DefendArea" for Civilian types
    [Fact]
    public void BehaviorCatalog_CivilianCar_DoesNotContainDefendArea()
    {
        var behaviors = BehaviorCatalog.GetValidBehaviors(TkbEntityTypes.CivilianCar);
        Assert.DoesNotContain("DefendArea", behaviors);
    }
}
```

> **Note on SC-1 (BehaviorSchemaDiscovery.AutoRegister):** `BehaviorSchemaDiscovery` lives in `Hrot.Presentation`, which is not a dependency of `Hrot.Core.Tests`. The auto-discovery behavior is exercised transitively by `BehaviorCatalog.BuildMap()` (same reflection pattern over the same assembly), which is what SC-2 and SC-3 test. SC-1 is therefore covered by the existing `BehaviorCatalogTests` pattern rather than requiring a Presentation-layer test. No new test project dependency needed.

> **Important:** `BehaviorCatalog` is a static class with a static constructor. If the static constructor already ran before `DefendAreaIntentDto` was added to the assembly, it will be stale in test processes. This is fine because unit tests get a fresh process per run. However, the static constructor caches lists; since DefendAreaIntentDto is in `Hrot.Core`, it will be found by `typeof(BehaviorContractAttribute).Assembly.GetTypes()` at first initialization.

Test project: `Hrot/Engine/Hrot.Core.Tests/Hrot.Core.Tests.csproj`

---

## TASK-TI004 — Change MissionAdapterSystem to Emit AssignTacticalIntentEvent

### Overview

`MissionAdapterSystem` currently:
1. Looks up behavior definition by ID from `_behaviorRegistry` to get `defName`
2. Publishes `AssignBehaviorEvent { Entity, BehaviorName = defName, JsonParams }`

After this task it must:
1. Read `BehaviorId` from `DomainMissionTask` (already retrieved via `ActiveMissionPlan`)
2. Publish `AssignTacticalIntentEvent { Entity = entity, IntentId = task.BehaviorId, JsonParams = jsonParams }`
3. Skip publishing if `BehaviorId` is null, empty, or whitespace

### Step 1 — Verify _entityMap usage

Before removing `_behaviorRegistry`, scan the `Execute` method body: `_entityMap` is defined as a field but is NOT actually called anywhere in `Execute`. Confirm this, then remove `_entityMap` alongside `_behaviorRegistry` (both fields are unused after the change). The `TASK-DETAIL.md` note says "verify before removing" — verifying in code confirms both are removable.

### Step 2 — Modify MissionAdapterSystem.cs

**File:** `Hrot/Subsystems/Hrot.CGF/Systems/MissionAdapterSystem.cs`

Remove the `using` for `BehaviorRegistry` if it becomes unused.

Remove fields and constructor parameters:
- Remove: `private readonly BehaviorRegistry _behaviorRegistry;`
- Remove: `private readonly NetworkEntityMap _entityMap;`
- Change constructor to: `public MissionAdapterSystem() { }`

Inside `Execute`, replace the behavior-registry lookup block and the `AssignBehaviorEvent` publication:

**Remove this block:**
```csharp
var defName = "Idle";
if (_behaviorRegistry.TryGetDefinition(phase.BehaviorId, out var def))
    defName = def.Name;

// Embrace DRY! Remove ALL direct ECS mutation blocks.
// No writing to BrainBlackboard. No updating BehaviorState. 
// Just publish the managed event and let BehaviorIngressSystem be the single owner!
repo.Bus.PublishManaged(new AssignBehaviorEvent
{
    Entity = entity,
    BehaviorName = defName,
    JsonParams = jsonParams
});
```

**Replace with:**
```csharp
// Publish generic tactical intent; TacticalIntentResolutionSystem resolves it.
if (activePlan?.Plan?.Tasks != null && queue.CurrentPhase < activePlan.Plan.Tasks.Count)
{
    var task = activePlan.Plan.Tasks[queue.CurrentPhase];
    if (!string.IsNullOrWhiteSpace(task.BehaviorId))
    {
        repo.Bus.PublishManaged(new AssignTacticalIntentEvent
        {
            Entity     = entity,
            IntentId   = task.BehaviorId,
            JsonParams = jsonParams,
        });
    }
}
```

> **IMPORTANT:** Note that `jsonParams` is already computed above this block from `task.BehaviorParams`. The `activePlan?.Plan?.Tasks` guard is identical to the existing null-check that already populates `jsonParams`. The code is structured so that the task is already accessed earlier; refactor as needed to avoid double-accessing the task, but keep the guard logic identical.

> **Note on `AssignBehaviorEvent` using:** The `using Fdp.Toolkit.Behavior.Events;` already covers both event types. The using for `BehaviorRegistry` is in `Fdp.Toolkit.Behavior` namespace — after removing `_behaviorRegistry`, check if the `using Fdp.Toolkit.Behavior;` import is still needed for other types (`BrainBTreeState`, etc.). Remove only unused usings; keep usings that are still referenced.

**After the change, the class fields and constructor look like:**
```csharp
[UpdateInPhase(SystemPhase.Simulation)]
public class MissionAdapterSystem : IEcsModuleSystem
{
    public MissionAdapterSystem() { }
    
    public unsafe void Execute(ISimulationView view, float deltaTime)
    {
        ...
    }
}
```

### Step 3 — Update CgfLogicPack.cs

**File:** `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs`

Find the line:
```csharp
_missionAdapterSystem = new MissionAdapterSystem(behaviorRegistry, entityMap);
```

Change to:
```csharp
_missionAdapterSystem = new MissionAdapterSystem();
```

No other changes to `CgfLogicPack.cs` — `behaviorRegistry` and `entityMap` remain as constructor parameters because they are used by other systems.

### Step 4 — Check for other MissionAdapterSystem construction sites

Run:
```
grep -r "new MissionAdapterSystem" --include="*.cs" .
```

Update any remaining call sites that still pass `behaviorRegistry` or `entityMap`.

### Tests for TI004

Fill in `Hrot/Subsystems/Hrot.SimHost.Tests/MissionAdapterSystemTests.cs` (the file exists but is empty):

The tests require constructing `MissionAdapterSystem` directly (no constructor args now) and an `EntityRepository`. They verify the event type published changed from `AssignBehaviorEvent` to `AssignTacticalIntentEvent`.

**Helper setup** (common across tests):

```csharp
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Hrot.CGF.Systems;
using Xunit;

private static EntityRepository CreateWorld()
{
    var repo = new EntityRepository();
    repo.RegisterComponent<MissionPlanQueue>();
    repo.RegisterComponent<BehaviorState>();
    repo.RegisterComponent<Hrot.CGF.Components.MissionAdapterState>();
    repo.RegisterComponent<ActiveMissionPlan>();
    return repo;
}

private static Entity CreateMissionEntity(EntityRepository repo, string behaviorId, string behaviorParams = "{}")
{
    var entity = repo.CreateEntity();
    repo.AddComponent(entity, new MissionPlanQueue { PhaseCount = 1, CurrentPhase = 0 });
    repo.AddComponent(entity, new BehaviorState());
    repo.AddComponent(entity, new ActiveMissionPlan
    {
        Plan = new DomainMissionPlan
        {
            Tasks = new List<DomainMissionTask>
            {
                new DomainMissionTask
                {
                    BehaviorId     = behaviorId,
                    BehaviorParams = behaviorParams,
                }
            }
        }
    });
    // Populate the phase (BehaviorId=0 is fine for this test since registry lookup is gone)
    unsafe
    {
        ref var queue = ref repo.GetComponentRW<MissionPlanQueue>(entity);
        queue.Phases[0] = new MissionPhase { BehaviorId = 0 };
    }
    return entity;
}
```

> **Note on MissionPlanQueue and MissionPhase:** These are ECS structs with unsafe fixed-size arrays. Look at existing tests in `Hrot.SimHost.Tests` that construct them (e.g., `MissionPlanTranslatorTests.cs`) to confirm the exact syntax for populating `Phases[0]`.

**Test SC-1: BehaviorId present → AssignTacticalIntentEvent published, no AssignBehaviorEvent**

```csharp
[Fact]
public void Execute_ValidBehaviorId_PublishesAssignTacticalIntentEvent()
{
    using var repo = CreateWorld();
    var entity = CreateMissionEntity(repo, "WanderMilitary");
    var system = new MissionAdapterSystem();

    system.Execute(repo, 0.016f);
    repo.Bus.SwapBuffers();

    var intentEvents   = repo.Bus.ReadManaged<AssignTacticalIntentEvent>();
    var behaviorEvents = repo.Bus.ReadManaged<AssignBehaviorEvent>();

    Assert.Single(intentEvents);
    Assert.Equal("WanderMilitary", intentEvents[0].IntentId);
    Assert.Equal(entity, intentEvents[0].Entity);
    Assert.Empty(behaviorEvents);
}
```

**Test SC-3: Empty BehaviorId → no event published**

```csharp
[Fact]
public void Execute_EmptyBehaviorId_NoEventPublished()
{
    using var repo = CreateWorld();
    CreateMissionEntity(repo, string.Empty);
    var system = new MissionAdapterSystem();

    system.Execute(repo, 0.016f);
    repo.Bus.SwapBuffers();

    var intentEvents = repo.Bus.ReadManaged<AssignTacticalIntentEvent>();
    Assert.Empty(intentEvents);
}
```

> **Note on registered event types:** The `EntityRepository` / `FdpEventBus` requires managed event types to be registered before they can be published/read. Look at how existing tests register `AssignBehaviorEvent` and register `AssignTacticalIntentEvent` the same way. Check `Hrot.CGF.CgfComponentRegistry.cs` or `TacticalIntentResolutionSystemTests.cs` to see the pattern.

Test project: `Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj`

---

## Build and Test Sequence

After each task:

```
dotnet build IOS-IG-SimHost.sln --no-restore -v quiet 2>&1 | Select-String "error CS|Build succeeded|FAILED"
```

After all tasks, run:

```
dotnet test Hrot/Engine/Hrot.Core.Tests/Hrot.Core.Tests.csproj --no-build --nologo 2>&1 | Select-String "Passed!|Failed!"
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build --nologo 2>&1 | Select-String "Passed!|Failed!"
```

All new tests must pass. Pre-existing failures (2 MissionPlanTranslator + 14 Toolkits) are OK.

---

## Report

Write report to: `.dev/tactical-intent/reports/BATCH-02-REPORT.md`

Follow the same format as BATCH-01-REPORT.md.
