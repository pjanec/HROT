# BATCH-06 Instructions

**Batch:** BATCH-06
**Status:** Not Started
**Assigned:** Developer
**Phase:** 7 — Remaining Combat and Perception ACL Leaks

---

## Objective

Remove the last three anti-pattern footprints in the codebase:

| Task | Summary |
|------|---------|
| PACK-D001 | Replace `long HitEntityId` in `DamageAssessedEvent` with `Entity HitEntity`; move `NetworkEntityMap` responsibility to the translator boundary |
| PACK-A001 | Split `AudioPerceptionSystem` from `TargetMemory`; define `TargetHeardEvent` and extend `ThreatEvaluationSystem` |
| PACK-M003 | Delete `EntityMissionHolder` and `IgMissionHolder`; introduce `ActiveMissionPlan` POCO |

**Toolchain:** .NET 9 / C# / xUnit. Build via `dotnet build IOS-IG-SimHost.sln`; test via `dotnet test <project>.csproj`.

---

## Preliminary Reading

Before coding, read the following files in full:

- `d:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits\FDP.Toolkit.Combat\Events\DetonationEvents.cs` — current `DamageAssessedEvent`
- `d:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits\FDP.Toolkit.Combat\Systems\DamageCalculationSystem.cs`
- `d:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits\FDP.Toolkit.Combat\Systems\HealthApplicationSystem.cs`
- `d:\Work\IOS-IG-SimHost-FDP-2\Hrot.SimHost\Network\Egress\DamageAssessedEgressTranslator.cs`
- `d:\Work\IOS-IG-SimHost-FDP-2\Hrot.SimHost\Network\Ingress\EntityHitDamageIngressTranslator.cs`
- `d:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits\FDP.Toolkit.Perception\Events\PerceptionEvents.cs`
- `d:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits\FDP.Toolkit.Perception\Systems\AudioPerceptionSystem.cs`
- `d:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits\FDP.Toolkit.Perception\Systems\ThreatEvaluationSystem.cs`
- `d:\Work\IOS-IG-SimHost-FDP-2\FDP\Toolkits\FDP.Toolkit.Perception\PerceptionConstants.cs`
- `d:\Work\IOS-IG-SimHost-FDP-2\Hrot.SimHost\Components\EntityMissionHolder.cs`
- `d:\Work\IOS-IG-SimHost-FDP-2\Hrot.IG\Components\IgMissionHolder.cs`
- `d:\Work\IOS-IG-SimHost-FDP-2\Hrot.SimHost\Systems\MissionControlExecutionSystem.cs`
- `d:\Work\IOS-IG-SimHost-FDP-2\Hrot.SimHost\Systems\MissionAdapterSystem.cs`
- `d:\Work\IOS-IG-SimHost-FDP-2\Hrot.IG\Translators\IgMissionIngressTranslator.cs`
- `d:\Work\IOS-IG-SimHost-FDP-2\Hrot.IG\Systems\MissionRenderLayer.cs`
- `d:\Work\IOS-IG-SimHost-FDP-2\Hrot.NED\MissionDescriptors.cs` — `MissionTask` and `MissionPlan` DDS structs
- `d:\Work\IOS-IG-SimHost-FDP-2\Hrot.Map.Definitions\HrotComponentIds.cs` — component ID registry

---

## PACK-D001 — Purify DamageAssessedEvent

**Goal:** `DamageAssessedEvent` carries a domain-layer `Entity` handle, not a network `long`. `NetworkEntityMap` is used only at the translator boundary.

### Step 1 — Change the event struct

**File:** `FDP/Toolkits/FDP.Toolkit.Combat/Events/DetonationEvents.cs`

Replace `long HitEntityId` with `Entity HitEntity`:

```csharp
/// <summary>
/// Published by <c>DamageCalculationSystem</c> within the Damage Assessment Module
/// after HP loss has been computed for a bullet impact.
/// </summary>
[EventId(CombatConstants.DamageAssessedEventId)]
[StructLayout(LayoutKind.Sequential)]
public struct DamageAssessedEvent
{
    /// <summary>ECS handle of the struck entity.</summary>
    public Entity HitEntity;

    /// <summary>Total computed HP loss.</summary>
    public float TotalDamage;
}
```

Note: `Entity` is a value type (blittable). `[StructLayout(LayoutKind.Sequential)]` without `Pack = 1` is fine — remove `Pack = 1` since `Entity` is typically 8 bytes aligned.

### Step 2 — Update DamageCalculationSystem

**File:** `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageCalculationSystem.cs`

Currently the system:
1. Gets `var targetEntity = evt.Target` (Entity)
2. Resolves `_entityMap.TryGetNetworkId(targetEntity, out long targetNetId)`
3. Publishes `DamageAssessedEvent { HitEntityId = targetNetId, ... }`

After the change:
- **Remove** `NetworkEntityMap` constructor parameter and field
- **Remove** `_entityMap.TryGetNetworkId(...)` call and its surrounding `if (!...) continue;`
- **Change** the publish to: `World.Bus.Publish(new DamageAssessedEvent { HitEntity = targetEntity, TotalDamage = ... })`
- **Remove** `using FDP.Toolkit.Replication.Services;`

The authority gate (`NetworkAuthority` check) stays intact — it uses `World.HasComponent<NetworkAuthority>(targetEntity)` which does not need `NetworkEntityMap`.

### Step 3 — Update HealthApplicationSystem

**File:** `FDP/Toolkits/FDP.Toolkit.Combat/Systems/HealthApplicationSystem.cs`

Currently the system:
1. Calls `_entityMap.TryGetEntity(evt.HitEntityId, out var targetEntity)`
2. Uses `targetEntity`

After the change:
- **Remove** `NetworkEntityMap` constructor parameter and field
- **Replace** `_entityMap.TryGetEntity(evt.HitEntityId, out var targetEntity)` with `var targetEntity = evt.HitEntity`
- The `World.IsAlive(targetEntity)` check stays — it correctly guards against stale handles
- The authority gate stays
- **Remove** `using FDP.Toolkit.Replication.Services;`

### Step 4 — Update DamageAssessedEgressTranslator

**File:** `Hrot.SimHost/Network/Egress/DamageAssessedEgressTranslator.cs`

This translator is the new location for `NetworkEntityMap`.

- **Add** `NetworkEntityMap _entityMap` field
- **Add** `NetworkEntityMap entityMap` parameter to the production constructor `DamageAssessedEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)`
- Update the internal (testable) constructor to also accept `NetworkEntityMap`:
  ```csharp
  internal DamageAssessedEgressTranslator(IDdsWriter<EntityHitDamage> writer, NetworkEntityMap entityMap)
  ```
- In `ScanAndPublish`, resolve `evt.HitEntity → long` before writing:
  ```csharp
  foreach (ref readonly var evt in events)
  {
      if (!_entityMap.TryGetNetworkId(evt.HitEntity, out long netId)) continue;
      _writer.Write(new EntityHitDamage
      {
          HitEntityId = netId,
          TotalDamage = evt.TotalDamage,
      });
  }
  ```
- Add `using FDP.Toolkit.Replication.Services;`

### Step 5 — Update EntityHitDamageIngressTranslator

**File:** `Hrot.SimHost/Network/Ingress/EntityHitDamageIngressTranslator.cs`

The translator already has `NetworkEntityMap _entityMap`. Change `ProcessSample`:

```csharp
internal void ProcessSample(in EntityHitDamage msg, IEntityCommandBuffer cmd, ISimulationView view)
{
    if (!_entityMap.TryGetEntity(msg.HitEntityId, out var hitEntity)) return;

    cmd.PublishEvent(new DamageAssessedEvent
    {
        HitEntity   = hitEntity,
        TotalDamage = msg.TotalDamage,
    });
}
```

Remove the old guard `if (!_entityMap.TryGetEntity(msg.HitEntityId, out _)) return;` since it's now merged into the single `TryGetEntity` call above.

### Step 6 — Update construction sites (SimHostModule or SimHostNetworkAdapters)

Search for where `DamageCalculationSystem`, `HealthApplicationSystem`, and `DamageAssessedEgressTranslator` are constructed. Update constructor calls to match the new signatures (add/remove `entityMap` argument as required).

Look in:
- `Hrot.SimHost/Modules/SimHostModule.cs`
- `Hrot.SimHost/Network/SimHostNetworkAdapters.cs`

### Step 7 — Update tests

**Projects:** `Hrot.SimHost.Tests` and `Hrot.SimHost.Integration.Tests`

Find all test files that:
- Create `DamageCalculationSystem` with `NetworkEntityMap` → remove the parameter
- Create `HealthApplicationSystem` with `NetworkEntityMap` → remove the parameter
- Create `DamageAssessedEgressTranslator` → add `NetworkEntityMap` argument to internal constructor
- Assert `DamageAssessedEvent.HitEntityId` → change to `HitEntity`

### PACK-D001 Success Conditions

1. `grep -r "NetworkEntityMap" FDP/Toolkits/FDP.Toolkit.Combat/Systems/` → zero results
2. All `Hrot.SimHost.Tests` tests pass
3. All `Hrot.SimHost.Integration.Tests` tests pass (if any touch damage)

---

## PACK-A001 — Fix AudioPerceptionSystem Split-Brain

**Goal:** `AudioPerceptionSystem` publishes a `TargetHeardEvent`; `ThreatEvaluationSystem` consumes it and mutates `TargetMemory`. Zero `TargetMemory` writes in `AudioPerceptionSystem`.

### Step 1 — Add event ID to constants

**File:** `FDP/Toolkits/FDP.Toolkit.Perception/PerceptionConstants.cs`

Add after `TargetVisibleEventId = 4003`:

```csharp
/// <summary>Event ID for <see cref="Events.TargetHeardEvent"/>.</summary>
public const int TargetHeardEventId = 4004;
```

### Step 2 — Define TargetHeardEvent

Add to **`FDP/Toolkits/FDP.Toolkit.Perception/Events/PerceptionEvents.cs`** (or create a new `TargetHeardEvent.cs` in the same `Events/` folder):

```csharp
// ── TargetHeardEvent ──────────────────────────────────────────────────────────

/// <summary>
/// Published by <see cref="Systems.AudioPerceptionSystem"/> when an entity successfully
/// detects an audio stimulus.
/// Consumed by <see cref="Systems.ThreatEvaluationSystem"/> to update
/// <see cref="Components.TargetMemory"/> on the Brain tier.
/// </summary>
[EventId(PerceptionConstants.TargetHeardEventId)]
[StructLayout(LayoutKind.Sequential)]
public struct TargetHeardEvent
{
    /// <summary>The entity that heard the sound.</summary>
    public Entity Listener;

    /// <summary>Entity index of the entity that produced the sound (same as <see cref="AudioStimulusEvent.SourceEntityIndex"/>).</summary>
    public int SourceEntityIndex;

    // Pad to align Origin to 8-byte boundary (implicit from Entity + int = 12 bytes → 4-byte pad).

    /// <summary>World-space origin of the detected sound.</summary>
    public Vector3 Origin;
}
```

### Step 3 — Purify AudioPerceptionSystem

**File:** `FDP/Toolkits/FDP.Toolkit.Perception/Systems/AudioPerceptionSystem.cs`

In the per-candidate loop (currently inside `for (int i = 0; i < candidateCount; i++)`), make the following changes:

**Remove** the guard:
```csharp
if (!World.HasComponent<TargetMemory>(listener)) continue;
```

**Remove** the `TargetMemory` mutation block:
```csharp
ref var mem = ref World.GetComponentRW<TargetMemory>(listener);
TargetMemory.AddOrUpdateTarget(
    ref mem,
    entityId:   evt.SourceEntityIndex,
    posX:       evt.Origin.X,
    posY:       evt.Origin.Y,
    scoreBoost: 20f,
    tick:       tick);
```

**Replace** with an event publication:
```csharp
World.Bus.Publish(new TargetHeardEvent
{
    Listener          = listener,
    SourceEntityIndex = evt.SourceEntityIndex,
    Origin            = evt.Origin,
});
```

The `PerceptionReceptor` hearing-range check (`if (dist > receptor.HearingRange) continue;`) stays — it is still needed to filter listeners.

Remove the `tick` local variable (previously needed for `AddOrUpdateTarget`) if it is no longer used elsewhere in `OnUpdate`.

Add `using FDP.Toolkit.Perception.Events;` if not already present.

### Step 4 — Extend ThreatEvaluationSystem

**File:** `FDP/Toolkits/FDP.Toolkit.Perception/Systems/ThreatEvaluationSystem.cs`

After Step 2 (the existing `TargetVisibleEvent` loop), add a Step 3 for `TargetHeardEvent`:

```csharp
// ── Step 3: Boost scores from confirmed heard events ──────────────────────
var heardEvents = view.ConsumeEvents<TargetHeardEvent>();
foreach (ref readonly var evt in heardEvents)
{
    if (!view.IsAlive(evt.Listener))
        continue;

    if (!view.HasComponent<TargetMemory>(evt.Listener))
        continue;

    ref readonly var memRO = ref view.GetComponentRO<TargetMemory>(evt.Listener);
    TargetMemory mem = memRO;

    TargetMemory.AddOrUpdateTarget(
        ref mem,
        entityId:   evt.SourceEntityIndex,
        posX:       evt.Origin.X,
        posY:       evt.Origin.Y,
        scoreBoost: 20f,
        tick:       tick);

    ecb.SetComponent(evt.Listener, mem);
}
```

Add `using FDP.Toolkit.Perception.Events;` to ThreatEvaluationSystem if not already present.

### Step 5 — Add network translators

**AudioTargetDetectedEgressTranslator** (Perception/SimHost Node → DDS)

First, add `AudioTargetDetected` to `Hrot.NED/SimDescriptors.cs`, following the same hand-written partial struct pattern as existing entries:

```csharp
// ── Perception CQRS messages ────────────────────────────────────────────────

/// <summary>
/// DDS wire message carrying a single audio-detection event from the Perception node
/// to the Brain node.
/// </summary>
[DdsTopic("AudioTargetDetected")]
[DdsIdlFile("hrot-sim-msg")]
[DdsQos(Reliability = DdsReliability.BestEffort, Durability = DdsDurability.Volatile,
        HistoryKind = DdsHistoryKind.KeepLast, HistoryDepth = 1)]
public partial struct AudioTargetDetected
{
    public long ListenerEntityId;
    public int  SourceEntityIndex;
    public float OriginX;
    public float OriginY;
    public float OriginZ;
}
```

Then create **`Hrot.SimHost/Network/Egress/AudioTargetDetectedEgressTranslator.cs`**:

```csharp
using Hrot.NED.Messages; // or SimDescriptors namespace - check where AudioTargetDetected lives
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace Hrot.SimHost.Network.Egress
{
    public sealed class AudioTargetDetectedEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "AudioTargetDetected";

        private readonly IDdsWriter<AudioTargetDetected> _writer;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName        => DdsTopicName;
        public long   DescriptorOrdinal => 84; // next available after 83 (DamageAssessed)

        public AudioTargetDetectedEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap)
            : this(new DdsWriterAdapter<AudioTargetDetected>(participant, DdsTopicName), entityMap)
        {
        }

        internal AudioTargetDetectedEgressTranslator(IDdsWriter<AudioTargetDetected> writer, NetworkEntityMap entityMap)
        {
            _writer    = writer    ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            var events = view.ConsumeEvents<TargetHeardEvent>();
            foreach (ref readonly var evt in events)
            {
                if (!_entityMap.TryGetNetworkId(evt.Listener, out long listenerId)) continue;
                _writer.Write(new AudioTargetDetected
                {
                    ListenerEntityId  = listenerId,
                    SourceEntityIndex = evt.SourceEntityIndex,
                    OriginX           = evt.Origin.X,
                    OriginY           = evt.Origin.Y,
                    OriginZ           = evt.Origin.Z,
                });
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
```

Now create **`Hrot.IG/Translators/AudioTargetDetectedIngressTranslator.cs`**:

```csharp
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using FDP.Toolkit.Perception.Events;
using FDP.Toolkit.Replication.Services;
using ModuleHost.Core.Abstractions;

namespace Hrot.IG.Translators
{
    public sealed class AudioTargetDetectedIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "AudioTargetDetected";

        private readonly DdsReader<AudioTargetDetected>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName        => DdsTopicName;
        public long   DescriptorOrdinal => 84;

        public AudioTargetDetectedIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _reader    = participant is not null
                ? new DdsReader<AudioTargetDetected>(participant, DdsTopicName)
                : null;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                var data = sample.Data;
                if (!_entityMap.TryGetEntity(data.ListenerEntityId, out var listenerEntity)) continue;
                cmd.PublishEvent(new TargetHeardEvent
                {
                    Listener          = listenerEntity,
                    SourceEntityIndex = data.SourceEntityIndex,
                    Origin            = new System.Numerics.Vector3(data.OriginX, data.OriginY, data.OriginZ),
                });
            }
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
```

**Important:** Check whether existing `PerceptionTranslators.cs` in `Hrot.SimHost/Network/` already has audio-related translators to avoid duplication. If the new files conflict, integrate appropriately.

**Note on NED codegen:** Adding a new struct to `SimDescriptors.cs` may trigger code generation that creates a new `.g.cs` file. Run `dotnet build Hrot.NED` first to verify the DDS scaffolding compiles. If the NED project uses attribute-based generation, the new struct must be added to the source `.cs` file that is already picked up by the CodeGen target. Verify the correct `[DdsIdlFile]` value by looking at what similar message types use (e.g., `EntityHitDamage` in `GenericMessages.cs`).

### PACK-A001 Success Conditions

1. `grep -r "TargetMemory" FDP/Toolkits/FDP.Toolkit.Perception/Systems/AudioPerceptionSystem.cs` → zero results
2. `grep -r "GetComponentRW<TargetMemory>" FDP/Toolkits/FDP.Toolkit.Perception/` → results only in `ThreatEvaluationSystem.cs`
3. Unit test: Tick `AudioPerceptionSystem` with one listener in range of one audio stimulus. Assert `TargetHeardEvent` is on the bus. Assert `TargetMemory` was NOT mutated.
4. Unit test: Publish `TargetHeardEvent`; tick `ThreatEvaluationSystem`. Assert `TargetMemory` of `Listener` has a non-zero entry for `SourceEntityIndex`.
5. All existing perception tests pass unchanged.

---

## PACK-M003 — Remove DDS Structs from ECS Components (Mission Holders)

**Goal:** Delete `EntityMissionHolder` and `IgMissionHolder` (raw DDS wrapper components). Replace with `ActiveMissionPlan` POCO component that has no dependency on `Hrot.NED`.

### Step 1 — Define DomainMissionPlan POCO

Create new file **`FDP/Toolkits/FDP.Toolkit.Behavior/Components/DomainMissionPlan.cs`**:

```csharp
using System;
using System.Collections.Generic;
using Fdp.Kernel;

namespace FDP.Toolkit.Behavior.Components
{
    /// <summary>
    /// Pure-domain representation of a single mission task.
    /// No dependency on Hrot.NED or CycloneDDS.
    /// </summary>
    public class DomainMissionTask
    {
        public Guid   TaskId          { get; set; }
        public string ExecutingEngine { get; set; } = string.Empty;
        public string BehaviorId      { get; set; } = string.Empty;
        public string BehaviorParams  { get; set; } = string.Empty;
    }

    /// <summary>
    /// Pure-domain representation of a mission plan (ordered task list + active task pointer).
    /// </summary>
    public class DomainMissionPlan
    {
        public Guid              ActiveTaskId { get; set; }
        public List<DomainMissionTask> Tasks { get; set; } = new();
    }

    /// <summary>
    /// Managed ECS component holding the current active mission plan for an entity.
    /// Populated by <c>MissionControlExecutionSystem</c> on receipt of a mission intent.
    /// </summary>
    [ComponentId(BehaviorComponentIds.ActiveMissionPlan)]
    public class ActiveMissionPlan
    {
        public DomainMissionPlan Plan { get; set; } = new();
    }
}
```

**Note:** Check `FDP/Toolkits/FDP.Toolkit.Behavior/BehaviorConstants.cs` or similar for
`BehaviorComponentIds`. If that class doesn't have an `ActiveMissionPlan` constant, add:
```csharp
public const int ActiveMissionPlan = 162; // reusing id from deleted EntityMissionHolder
```
to the appropriate constants/IDs class in `FDP.Toolkit.Behavior`. If `ComponentId` attribute
comes from `Hrot.Map.Definitions`, you may alternatively register without `[ComponentId]` and
use the `RegisterManagedComponent<ActiveMissionPlan>()` call plus `Hrot.Map.Definitions.HrotComponentIds.EntityMissionHolder = 162` (same ID, type replaced). Verify which approach is used for existing FDP managed components like `BrainBlackboard`.

### Step 2 — Delete the holder files

Delete (do not just comment out):
- `Hrot.SimHost/Components/EntityMissionHolder.cs`
- `Hrot.IG/Components/IgMissionHolder.cs`

### Step 3 — Update MissionControlExecutionSystem

**File:** `Hrot.SimHost/Systems/MissionControlExecutionSystem.cs`

In the `CMD_REPLACE_MISSION` case, replace the `EntityMissionHolder` set:

```csharp
// OLD
repo.SetComponent(entity, new Hrot.SimHost.Components.EntityMissionHolder
{
    Mission = new Hrot.NED.Descriptors.EntityMission
    {
        EntityId = intent.TargetEntityId,
        Plan     = plan
    }
});
```

with `ActiveMissionPlan`:

```csharp
// NEW
var domainPlan = new DomainMissionPlan
{
    ActiveTaskId = plan.ActiveTaskId,
    Tasks        = plan.Tasks?.ConvertAll(t => new DomainMissionTask
    {
        TaskId          = t.TaskId,
        ExecutingEngine = t.ExecutingEngine ?? string.Empty,
        BehaviorId      = t.BehaviorId      ?? string.Empty,
        BehaviorParams  = t.BehaviorParams  ?? string.Empty,
    }) ?? new List<DomainMissionTask>()
};
repo.SetComponent(entity, new FDP.Toolkit.Behavior.Components.ActiveMissionPlan
{
    Plan = domainPlan
});
```

In the `CMD_ABORT_ALL` case:
```csharp
// OLD
repo.RemoveComponent<Hrot.SimHost.Components.EntityMissionHolder>(entity);

// NEW
repo.RemoveComponent<FDP.Toolkit.Behavior.Components.ActiveMissionPlan>(entity);
```

Remove usings for `Hrot.NED.Descriptors` and `Hrot.SimHost.Components` if no longer needed.
Add `using FDP.Toolkit.Behavior.Components;`.

### Step 4 — Update MissionAdapterSystem

**File:** `Hrot.SimHost/Systems/MissionAdapterSystem.cs`

Change the component access from `EntityMissionHolder` to `ActiveMissionPlan`:

```csharp
// OLD
var missionHolder = World.GetComponent<EntityMissionHolder>(entity);
...
if (missionHolder != null)
{
    var plan = missionHolder.Mission.Plan;
    if (plan.Tasks != null && queue.CurrentPhase < plan.Tasks.Count)
    {
        var task = plan.Tasks[queue.CurrentPhase];
        jsonParams = task.BehaviorParams ?? "{}";
    }
}
```

```csharp
// NEW
var activePlan = World.GetComponent<FDP.Toolkit.Behavior.Components.ActiveMissionPlan>(entity);
...
if (activePlan?.Plan?.Tasks != null && queue.CurrentPhase < activePlan.Plan.Tasks.Count)
{
    var task = activePlan.Plan.Tasks[queue.CurrentPhase];
    jsonParams = task.BehaviorParams ?? "{}";
}
```

Update the `using` statements accordingly.

### Step 5 — Update IgMissionIngressTranslator

**File:** `Hrot.IG/Translators/IgMissionIngressTranslator.cs`

Replace `IgMissionHolder` writes with `ActiveMissionPlan`:

```csharp
// OLD
erepo.SetComponent(entity, new IgMissionHolder { Mission = sample.Data });
...
erepo.RemoveComponent<IgMissionHolder>(entity);
...
repo.SetComponent(entity, new IgMissionHolder { Mission = mission });
```

```csharp
// NEW (mapping DDS → POCO)
erepo.SetComponent(entity, MapToPlan(sample.Data));
...
erepo.RemoveComponent<ActiveMissionPlan>(entity);
...
repo.SetComponent(entity, MapToPlan(mission));
```

Add a private helper in the same class:

```csharp
private static ActiveMissionPlan MapToPlan(EntityMission mission)
{
    var domainPlan = new DomainMissionPlan
    {
        ActiveTaskId = mission.Plan.ActiveTaskId,
        Tasks        = mission.Plan.Tasks?.ConvertAll(t => new DomainMissionTask
        {
            TaskId          = t.TaskId,
            ExecutingEngine = t.ExecutingEngine ?? string.Empty,
            BehaviorId      = t.BehaviorId      ?? string.Empty,
            BehaviorParams  = t.BehaviorParams  ?? string.Empty,
        }) ?? new List<DomainMissionTask>()
    };
    return new ActiveMissionPlan { Plan = domainPlan };
}
```

### Step 6 — Update MissionRenderLayer

**File:** `Hrot.IG/Systems/MissionRenderLayer.cs`

Replace `IgMissionHolder` with `ActiveMissionPlan` in the query and access:

```csharp
// OLD query
_query = repo.Query()
    .WithManaged<IgMissionHolder>()
    .With<SimTransform>()
    .With<SelectionState>()
    .Build();

// NEW query
_query = repo.Query()
    .WithManaged<ActiveMissionPlan>()
    .With<SimTransform>()
    .With<SelectionState>()
    .Build();
```

In `Draw`:
```csharp
// OLD
var holder = _view.GetManagedComponentRO<IgMissionHolder>(entity);
if (holder?.Mission.Plan.Tasks == null) continue;
...
var plan = holder.Mission.Plan;
foreach (var task in plan.Tasks)
{
    if (string.IsNullOrEmpty(task.BehaviorParams)) continue;
    ...
}
```

```csharp
// NEW
var activePlan = _view.GetManagedComponentRO<ActiveMissionPlan>(entity);
if (activePlan?.Plan?.Tasks == null) continue;
...
foreach (var task in activePlan.Plan.Tasks)
{
    if (string.IsNullOrEmpty(task.BehaviorParams)) continue;
    ...
}
```

### Step 7 — Update SimHostComponentRegistry and IgApplication

**File:** `Hrot.SimHost/SimHostComponentRegistry.cs`

Replace:
```csharp
world.RegisterManagedComponent<EntityMissionHolder>();
```
with:
```csharp
world.RegisterManagedComponent<ActiveMissionPlan>();
```

**File:** `Hrot.IG/IgApplication.cs`

Replace:
```csharp
_world.RegisterManagedComponent<Hrot.IG.Components.IgMissionHolder>();
```
with:
```csharp
_world.RegisterManagedComponent<FDP.Toolkit.Behavior.Components.ActiveMissionPlan>();
```

### Step 8 — Update HrotComponentIds

**File:** `Hrot.Map.Definitions/HrotComponentIds.cs`

Update the comment for id 162:
```csharp
/// <summary><c>ActiveMissionPlan</c> — domain POCO mission plan (replaces EntityMissionHolder).</summary>
public const byte ActiveMissionPlan = 162;
```

### Step 9 — Update tests

Find all test files referencing `EntityMissionHolder` or `IgMissionHolder`:
- `Hrot.SimHost.Tests/Systems/MissionControlExecutionSystemTests.cs` — update construction, assertions
- Any integration tests that assert on `EntityMissionHolder` or `IgMissionHolder`

### PACK-M003 Success Conditions

1. `EntityMissionHolder.cs` and `IgMissionHolder.cs` do not exist in the solution
2. `grep -r "EntityMissionHolder\|IgMissionHolder" . --include="*.cs"` → zero results (excluding `.dev/` docs)
3. `dotnet build IOS-IG-SimHost.sln` → 0 errors
4. Unit test — `MissionControlExecutionSystem` sets `ActiveMissionPlan` with correct task count after `CMD_REPLACE_MISSION`
5. Unit test — `MissionAdapterSystem` reads `ActiveMissionPlan.Plan.Tasks[i].BehaviorParams` correctly
6. All `Hrot.SimHost.Tests` pass

---

## Build Order

Suggested order to minimize broken builds:

1. PACK-D001 (FDP Combat changes, then Hrot.SimHost translator changes)
2. PACK-M003 (FDP Behavior POCO, then Hrot.SimHost/IG component and system changes)
3. PACK-A001 (FDP Perception changes, then NED addition, then translator creation)

Build early and often: after each file group, run `dotnet build IOS-IG-SimHost.sln` and fix errors before moving to the next file.

---

## Test Matrix

Run these test projects after all changes are complete:

```powershell
cd d:\Work\IOS-IG-SimHost-FDP-2
dotnet test Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --logger "console;verbosity=minimal"
dotnet test Hrot.SimHost.Integration.Tests/Hrot.SimHost.Integration.Tests.csproj --logger "console;verbosity=minimal"
dotnet test FDP/Toolkits/FDP.Toolkit.Combat.Tests/ --logger "console;verbosity=minimal"  # if it exists
dotnet test FDP/Toolkits/FDP.Toolkit.Perception.Tests/ --logger "console;verbosity=minimal"  # if it exists
dotnet test Hrot.IG.Tests/ --logger "console;verbosity=minimal"  # if it exists
```

Check which test projects exist with:
```powershell
Get-ChildItem -Recurse -Filter "*.Tests.csproj" | Select-Object FullName
```

---

## Report

Write the batch report to:
`d:\Work\IOS-IG-SimHost-FDP-2\.dev\packs-1\reports\BATCH-06-REPORT.md`

Include:
- Task status table (PACK-D001, PACK-A001, PACK-M003)
- Test results for all affected projects
- List of new/modified files
- Any deviations from spec with rationale
- Answers to: (Q1) any issues encountered and how resolved, (Q2) anything unclear in the spec

---

## Notes

- The FDP submodule lives at `d:\Work\IOS-IG-SimHost-FDP-2\FDP\`. Changes there must be committed separately in FDP before the main repo commit.
- The `AudioTargetDetected` DDS struct is new. If NED's code generation requires running a codegen tool after adding to `SimDescriptors.cs`, check for a `.bat` or `.ps1` script in `Hrot.NED/` that runs the generator.
- `Entity` is a blittable value type (typically `uint Index + uint Generation`). It can be used in `[StructLayout(LayoutKind.Sequential)]` structs.
- Do not change `MissionPlanQueue` (the structural/queue component). `ActiveMissionPlan` complements it by carrying the string BehaviorParams that `MissionPlanQueue` cannot hold (packed struct limitation). Both components coexist for the same entity.
