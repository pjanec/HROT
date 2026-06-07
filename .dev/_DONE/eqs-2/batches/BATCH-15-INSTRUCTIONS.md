# BATCH-15 INSTRUCTIONS — EQS-037 + EQS-038

**Batch number:** 15
**Tasks:** TASK-EQS-037, TASK-EQS-038
**Phase:** 12 — Multi-sensor child-entity support (Part A)
**Goal:** Declare the `EqsSensorHandle` wrapper struct and relax the solver + replication
pipeline to support child-entity sensors that lack their own `NetworkIdentity`.

**Design references:**
- `.dev/eqs-2/TASK-DETAIL.md` — sections `TASK-EQS-037` and `TASK-EQS-038` (read them in full)
- `.dev/eqs-2/EQS_Design_v1.3_final.md` — §2, §3.1, §11
- `.dev/eqs-2/IMPLEM_DETAILS.md`
- `.dev/eqs-2/ONBOARDING.md`

---

## Overview

**EQS-037** adds a single new struct file — a typed wrapper around `Entity` so Blueprint
variable pickers can filter to "sensor handles" vs. arbitrary entities. Tiny but needed first
because the When-node iteration imports it.

**EQS-038** is the main structural change. Currently `EqsSolverSystem` requires every sensor
entity to have `NetworkIdentity`. That blocks multi-sensor agents where child sensors are
spawned via ECB and never go through the full DDS entity-lifecycle handshake. EQS-038:
1. Drops `NetworkIdentity` from the solver query.
2. Adds a three-branch identity-resolution inside `EvaluateSensor`.
3. Changes the result-event key from a single `SensorNetworkId` to `(ParentNetworkId, LocalChildIndex)`.
4. Changes `EqsSensorConfigTopic`'s `[DdsKey]` from the sensor's own `NetworkId` to the compound key.
5. Makes ingress maintain a `Dictionary<(long,int),Entity>` cache for child ghost lookup / creation.
6. Makes the reverse-lookup in `EqsResultIngressTranslator` symmetric.
7. Adds local-only (offline/editor) fast-path for `ParentNetworkId == 0`.

---

## TASK-EQS-037: `EqsSensorHandle` wrapper struct

### What to create

**New file:** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsSensorHandle.cs`

```csharp
using System;
using System.Runtime.InteropServices;
using FDP.Core;       // Entity lives here — check actual namespace from EqsComponents.cs

namespace FDP.Eqs;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct EqsSensorHandle : IEquatable<EqsSensorHandle>
{
    public readonly Entity ChildId;
    public EqsSensorHandle(Entity childId) => ChildId = childId;
    public bool Equals(EqsSensorHandle other) => ChildId.Equals(other.ChildId);
    public override bool Equals(object? obj) => obj is EqsSensorHandle other && Equals(other);
    public override int GetHashCode() => ChildId.GetHashCode();
    public static bool operator ==(EqsSensorHandle a, EqsSensorHandle b) => a.Equals(b);
    public static bool operator !=(EqsSensorHandle a, EqsSensorHandle b) => !a.Equals(b);
    public bool IsValid => ChildId.Id != 0;
}
```

*Verify the `Entity` namespace by checking `EqsComponents.cs` or any existing usage of `Entity`
in `Fdp.Toolkits`. Adjust the `using` if needed.*

### Tests for EQS-037

Add to `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EqsSensorHandleTests.cs` (new file):

```csharp
// T-SH1: ChildId round-trips through the constructor
// T-SH2: Two handles with the same Entity are Equals and have equal hash codes
// T-SH3: default(EqsSensorHandle).IsValid == false
// T-SH4: Two handles with different Entities are != (not equal)
```

---

## TASK-EQS-038: Relax solver query + rekey sensor replication

This task touches many files. Read **TASK-EQS-038** in `TASK-DETAIL.md` in full before starting.

### A. `EqsResultPool.cs` — rename field in `EqsResultEvent`

In `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsResultPool.cs`, change `EqsResultEvent`:

```csharp
// BEFORE
public long SensorNetworkId;

// AFTER — replace with compound key
public long ParentNetworkId;   // network ID of the parent agent (or sensor itself for legacy path)
public int  LocalChildIndex;   // PartMetadata.InstanceId for child; 0 for legacy single-sensor
```

Update the `[StructLayout(LayoutKind.Sequential)]` comment to note the size increase.

### B. `EqsDdsTopics.cs` — rekey `EqsSensorConfigTopic`

In `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsDdsTopics.cs`, change the DDS key:

```csharp
// BEFORE
[DdsKey] public long EntityId;

// AFTER — compound key
[DdsKey] public long ParentNetworkId;
[DdsKey] public int  LocalChildIndex;
```

Also add a matching key to `EqsResultTopic` if it currently carries `SensorNetworkId` as its
own top-level key (check the file and replace consistently).

### C. `EqsSolverSystem.cs` — drop query requirement + identity-read branch

File: `Hrot/Subsystems/Hrot.SimHost/Systems/EqsSolverSystem.cs`

1. **Change the sensor query** to NOT require `NetworkIdentity`:
   ```csharp
   // Find: _sensorQuery = repo.Query().With<EqsSensor>().With<NetworkIdentity>()...
   // Replace so NetworkIdentity is NOT in the .With<>() chain:
   _sensorQuery = repo.Query().With<EqsSensor>().WithLifecycle(EntityLifecycle.All).Build();
   ```
   *(Check actual query construction pattern in the file — the exact API may differ slightly.)*

2. **Rewrite the identity-read in `EvaluateSensor`** (or wherever `SensorNetworkId` was being
   assigned). Replace the single `GetComponentRO<NetworkIdentity>()` read with the three-branch
   pattern from TASK-EQS-038 §A in TASK-DETAIL.md:
   ```csharp
   long parentNetworkId;
   int  localChildIndex;
   if (repo.HasComponent<PartMetadata>(sensorEntity))
   {
       var meta   = repo.GetComponentRO<PartMetadata>(sensorEntity);
       var parent = meta.ParentEntity;
       if (!repo.IsAlive(parent) || !repo.HasComponent<NetworkIdentity>(parent))
           return; // parent gone or local-only
       parentNetworkId = repo.GetComponentRO<NetworkIdentity>(parent).Value;
       localChildIndex = meta.InstanceId;
   }
   else if (repo.HasComponent<NetworkIdentity>(sensorEntity))
   {
       parentNetworkId = repo.GetComponentRO<NetworkIdentity>(sensorEntity).Value;
       localChildIndex = 0;
   }
   else
   {
       // Purely local sensor (offline / editor).
       parentNetworkId = 0;
       localChildIndex = sensorEntity.Index;
   }
   ```

3. **Update both `EqsResultEvent` publish sites** — everywhere `EqsResultEvent` is populated,
   replace `SensorNetworkId = ...` with:
   ```csharp
   ParentNetworkId = parentNetworkId,
   LocalChildIndex = localChildIndex,
   ```

### D. `EqsSensorConfigEgressTranslator.cs` — compound-key egress

File: `Hrot/Network/Hrot.Network.NED/SimHost/EqsSensorConfigEgressTranslator.cs`

Apply the same identity-resolution branch (identical to §C step 2 above) to derive
`ParentNetworkId` and `LocalChildIndex` for the DDS topic key.

Replace the old single `EntityId = sensorNetworkId` assignment with:
```csharp
ParentNetworkId = parentNetworkId,
LocalChildIndex = localChildIndex,
```

Sensors on entities without `NetworkIdentity` anywhere in the chain (`parentNetworkId == 0`) are
**local-only** and should be **skipped** in the egress translator (no DDS publish — they will be
evaluated locally by the solver and results consumed directly without DDS).

### E. `EqsSensorConfigIngressTranslator.cs` — dictionary-cached child ghost

File: `Hrot/Network/Hrot.Network.NED/SimHost/EqsSensorConfigIngressTranslator.cs`

1. Add a private field:
   ```csharp
   private readonly Dictionary<(long ParentNetId, int ChildIndex), Entity> _childGhostCache = new();
   ```

2. In `PollIngress`, for each incoming `EqsSensorConfigTopic` sample:
   - Look up `parentGhost = _entityMap.Resolve(sample.ParentNetworkId)`.
     If `parentGhost` is `Entity.Null`, skip (parent not yet landed; Reliable QoS will redeliver).
   - Look up `(sample.ParentNetworkId, sample.LocalChildIndex)` in `_childGhostCache`.
   - **Cache miss** → spawn carrier ghost:
     ```csharp
     var ecb   = view.GetCommandBuffer();
     var child = ecb.CreateEntity();
     ecb.AddComponent(child, new PartMetadata
     {
         ParentEntity      = parentGhost,
         InstanceId        = sample.LocalChildIndex,
         DescriptorOrdinal = 0,
     });
     ecb.AddComponent(child, BuildSensor(sample));
     ecb.AddComponent(child, default(EqsCognitiveBuffer));
     _childGhostCache[(sample.ParentNetworkId, sample.LocalChildIndex)] = child;
     ```
     **No `NetworkIdentity`, `TkbIdentity`, or `GhostStateTracker`** on the carrier.
   - **Cache hit** → `view.GetCommandBuffer().SetComponent(cachedChild, BuildSensor(sample))`.

3. For `NotAliveDisposed` samples:
   ```csharp
   if (_childGhostCache.Remove((sample.ParentNetworkId, sample.LocalChildIndex), out var dead))
       view.GetCommandBuffer().DestroyEntity(dead);
   ```

4. **Legacy single-sensor path** (`LocalChildIndex == 0`): when the parent ghost is the sensor
   host itself (legacy `Action_MaintainEqsSensor` on `ctx.Self`), the dictionary still caches
   `(parentNetId, 0) → parentGhost`. The sensor component is applied directly to the parent ghost
   instead of spawning a child.

   Distinguish the two cases: if `sample.LocalChildIndex == 0`, treat the parent ghost as the
   target entity (no child spawn). If `sample.LocalChildIndex != 0`, spawn/reuse child carrier.

5. **Do NOT** scan `repo.Query().With<PartMetadata>()` inside the polling loop. Use only the
   dictionary for entity lookup.

> **Reference:** see `MultiInstanceCycloneTranslator<T>` in the codebase for the established
> pattern for a dictionary-cached translator.

### F. `EqsResultEventEgressTranslator.cs` — update event field reference

File: `Hrot/Network/Hrot.Network.NED/SimHost/EqsResultEventEgressTranslator.cs`

Replace `evt.SensorNetworkId` references with `evt.ParentNetworkId`.
The DDS result topic key should become `(ParentNetworkId, LocalChildIndex)` (or just carry these
as payload fields — check the existing wire format and be consistent).

### G. `EqsResultIngressTranslator.cs` — reverse lookup with dictionary cache

File: `Hrot/Network/Hrot.Network.NED/Cgf/EqsResultIngressTranslator.cs`

1. Add a private field mirroring the Muscle-side cache:
   ```csharp
   private readonly Dictionary<(long ParentNetId, int ChildIndex), Entity> _childEntityCache = new();
   ```
   This cache is populated lazily on first miss by a one-shot scan of `PartMetadata` entities
   filtered by `ParentEntity`'s `NetworkIdentity`. After that, it's O(1).

2. In `PollIngress`:
   - If `evt.ParentNetworkId == 0` → offline path: resolve `sensorEntity` by local index
     (`evt.LocalChildIndex`), then publish `EqsResultUpdateEvent`.
   - Otherwise → look up `(ParentNetworkId, LocalChildIndex)` in `_childEntityCache`.
     On miss, scan once:
     ```csharp
     Entity? found = null;
     foreach (var e in repo.Query().With<PartMetadata>().Build())
     {
         var meta = repo.GetComponentRO<PartMetadata>(e);
         if (meta.InstanceId == evt.LocalChildIndex &&
             repo.HasComponent<NetworkIdentity>(meta.ParentEntity) &&
             repo.GetComponentRO<NetworkIdentity>(meta.ParentEntity).Value == evt.ParentNetworkId)
         {
             found = e;
             break;
         }
     }
     if (found.HasValue)
         _childEntityCache[(evt.ParentNetworkId, evt.LocalChildIndex)] = found.Value;
     ```
     On hit: publish `EqsResultUpdateEvent` targeting the cached child entity.

### H. `EqsResultUpdateSystem.cs` — handle all three routing shapes

File: `Hrot/Subsystems/Hrot.CGF/Systems/EqsResultUpdateSystem.cs`

The system receives `EqsResultUpdateEvent`. Ensure it routes correctly regardless of whether the
target entity is a child carrier, the legacy parent-host, or a local-only entity. The event
should already carry the resolved entity handle from the ingress translator; this system does not
need to re-resolve the key. Verify no existing code assumes `SensorNetworkId` on the event and
update field references if needed.

---

## Tests for EQS-038

All tests may use `EditorHarness` unless noted.

### T-38-1: Local-only sensor (no NetworkIdentity, no PartMetadata)

```
Setup:  spawn entity without NetworkIdentity, attach EqsSensor
Action: pump EqsSolverSystem ticks
Assert: EqsResultEvent emitted with ParentNetworkId == 0
Assert: no exception thrown (no "component missing" error)
```

### T-38-2: Child entity sensor (PartMetadata present)

```
Setup:  parent entity with NetworkIdentity.Value = 12345
        child entity with PartMetadata { ParentEntity = parent, InstanceId = 42 }
        EqsSensor on child
Action: pump EqsSolverSystem ticks
Assert: EqsResultEvent.ParentNetworkId == 12345
Assert: EqsResultEvent.LocalChildIndex == 42
```

### T-38-3: Legacy single-sensor backward compat (HideInCover_BT unchanged)

```
Setup:  existing HideInCover_BT test scenario (single sensor on ctx.Self with NetworkIdentity)
Action: run scenario end-to-end
Assert: all existing assertions pass (no regression)
Assert: EqsResultEvent.LocalChildIndex == 0 for the legacy sensor
```

### T-38-4: Offline multi-sensor (EditorHarness, no DDS)

```
Setup:  observer entity (no NetworkIdentity)
        three child sensor entities each with PartMetadata{InstanceId = 0, 1, 2}
        different BlueprintId per child
Action: pump solver 3 ticks
Assert: three EqsCognitiveBuffer components populated (one per child entity)
Assert: no exceptions
```

### T-38-5: Distributed carrier ghost (integration, HrotRunnerHarness)

```
Setup:  Brain side: parent entity + NetworkIdentity + child sensor entity (PartMetadata)
Action: egress translator publishes config topic
        Muscle ingress translator receives sample
Assert: exactly one carrier ghost entity exists on Muscle side
Assert: carrier has PartMetadata, EqsSensor, EqsCognitiveBuffer
Assert: carrier does NOT have NetworkIdentity, TkbIdentity, GhostStateTracker
```

**Domain ID for T-38-5:** use 210 (unused range, consistent with EQS distributed tests).

---

## File Checklist

| File | Change |
|---|---|
| `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsSensorHandle.cs` | NEW — EqsSensorHandle struct |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EqsSensorHandleTests.cs` | NEW — T-SH1..4 |
| `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsResultPool.cs` | Rename SensorNetworkId → ParentNetworkId, add LocalChildIndex |
| `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsDdsTopics.cs` | Rekey EqsSensorConfigTopic to (ParentNetworkId, LocalChildIndex) |
| `Hrot/Subsystems/Hrot.SimHost/Systems/EqsSolverSystem.cs` | Drop NetworkIdentity from query; 3-branch identity-read; update publish sites |
| `Hrot/Network/Hrot.Network.NED/SimHost/EqsSensorConfigEgressTranslator.cs` | 3-branch identity-read; compound key; skip local-only sensors |
| `Hrot/Network/Hrot.Network.NED/SimHost/EqsSensorConfigIngressTranslator.cs` | Dictionary cache; carrier ghost spawn; legacy LocalChildIndex==0 path |
| `Hrot/Network/Hrot.Network.NED/SimHost/EqsResultEventEgressTranslator.cs` | Update SensorNetworkId → ParentNetworkId field references |
| `Hrot/Network/Hrot.Network.NED/Cgf/EqsResultIngressTranslator.cs` | Reverse lookup dictionary; offline fast-path |
| `Hrot/Subsystems/Hrot.CGF/Systems/EqsResultUpdateSystem.cs` | Field reference update if needed |

---

## Constraints (Non-Negotiable)

1. **No `NetworkIdentity` on carrier ghosts.** Adding it triggers the standard DDS entity-lifecycle
   handshake and will cause spurious destroy commands.
2. **No ECS query inside the ingress polling loop.** The `_childGhostCache` dictionary is O(1)
   in steady state. The one-shot miss-path scan in the result translator is acceptable (fires once
   per new sensor, then never again for that sensor).
3. **ECB-only for structural mutation during Simulation phase** (will matter more in EQS-039; for
   now, the ingress translator runs outside the hot ECS iteration path, so ECB is still required
   per the existing pattern but not for ECS-corruption reasons specifically here).
4. **Backwards compat:** `HideInCover_BT` and all existing EQS tests must continue to pass
   without modification.

---

## Deliverable

Provide a BATCH-15-REPORT.md at `.dev/eqs-2/reports/BATCH-15-REPORT.md` containing:
- List of files changed with a one-line description of each change
- Test results: `dotnet test ... --filter "FullyQualifiedName~Eqs" -c Debug`
  showing count of passed/failed
- Any deviations from the spec and why
