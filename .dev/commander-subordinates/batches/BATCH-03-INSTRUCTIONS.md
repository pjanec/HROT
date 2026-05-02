# BATCH-03 Instructions — Network ACL + CT-2 Fix

## Context

**Project:** `d:\Work\IOS-IG-SimHost-FDP-2` (Windows, .NET 8, C#)

**Build command:** `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet`
**Test command:** `dotnet test IOS-IG-SimHost.sln --no-build --nologo`

**FDP submodule:** `d:\Work\IOS-IG-SimHost-FDP-2\FDP` — has its own `git` history. Commit FDP
changes separately with `cd FDP ; git add -A ; git commit -m "..."` before committing the parent.

**Known pre-existing test failures (do not fix):**
- `Hrot.SimHost.Tests.MissionPlanTranslatorTests` — 2 failures
- `Fdp.Toolkits.Tests` — 22 failures

**Architecture summary (read before touching any file):**
- `Fdp.Core.CommandHierarchy` namespace: `TacticalDesignation`, `UnitSubordinate` (ComponentId=183),
  `UnitRoster` (ComponentId=182), `CmdAssignSubordinate` (EventId=2200), `CmdRemoveSubordinate`
  (EventId=2201), `CmdAssignSubordinateRejected` (EventId=2202). All live in
  `FDP/Engine/Fdp.Core/CommandHierarchy/`.
- `Hrot.NED.Descriptors.EntityInfo` = DDS-wire descriptor (in `Hrot.Network.NED`).
  Its `CommanderId` int field and `TacticalDesignation eTacticalDesignation` field live on the
  DDS side and are **not** the ECS component.
- `Fdp.Core.EntityInfo` = ECS component (`FDP/Engine/Fdp.Core/Components/EntityInfo.cs`).
  Currently has `Name`, `ForceId`, `CommanderId`. CS009 removes `CommanderId`.
- `Bus.Read<T>()` returns a `ReadOnlySpan<T>` from a double-buffered stream. It is **non-draining**
  — all callers see the same events in the same tick. Double-registration of a system WILL cause
  double execution.

---

## Tasks

### CT-2 — Remove UnitHierarchySystem from CgfLogicPack [P0 — do this first]

**File:** `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs`

**Problem:** `UnitHierarchySystem` was added to both `SimHostCoreLogicPack.simList` AND
`CgfLogicPack.simList`. In `EditorSubsystem` (`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`
lines ~462-466) both packs' simulation lists are merged into a single `EditorSimulationModule`,
causing the system to run twice per tick and process every event twice.

**Fix — remove three lines from `CgfLogicPack.cs`:**
1. `private readonly UnitHierarchySystem _unitHierarchySystem;` field declaration
2. `_unitHierarchySystem = new UnitHierarchySystem();` in constructor
3. `simList.Add(_unitHierarchySystem);` in `BuildSimulationSystems`

Also remove the `using Hrot.Common.Systems;` directive if it becomes unused.

**Test update:** In `Hrot/Subsystems/Hrot.SimHost.Tests/CgfLogicPackTests.cs`, update the
simulation-system count assertion from 17 back to **16** (remove 1 system). Update the total
system count from 19 back to **18**.

Do **not** touch `SimHostCoreLogicPack.cs` or `IgApplication.cs` — their registrations are correct.

---

### CS008 — Extend EntityInfo DDS descriptor [P1]

**File:** `Hrot/Network/Hrot.Network.NED/GenericDescriptors.cs`

**Reference:** [TASK-CS008](../TASK-DETAIL.md#task-cs008--extend-entityinfo-dds-descriptor)

Add `public eTacticalDesignation TacticalDesignation;` to the `Hrot.NED.Descriptors.EntityInfo`
partial struct. Add it at the **end** of the struct to preserve wire-format field order. Do not
reorder any existing fields.

`eTacticalDesignation` is already defined in the same file (added in BATCH-02/CS001).

No separate test is required for CS008 — the field is covered by the egress/ingress tests in CS010
and CS011.

---

### CS009 — Remove CommanderId from Fdp.Core.EntityInfo [P1]

**Reference:** [TASK-CS009](../TASK-DETAIL.md#task-cs009--remove-commanderid-from-fdpcoreentityinfo)

**Primary change:**
Remove `public int CommanderId;` from `FDP/Engine/Fdp.Core/Components/EntityInfo.cs`.

**Cascading compile fixes — all files that must change:**

1. **`Hrot/Subsystems/Hrot.CGF/Systems/CreateEntityRequestSystem.cs` line ~358:**
   Remove `CommanderId = (int)pending.NetworkId` from the child `EntityInfo` initializer.
   The `InitialUnitSubordinateIntent` added on line ~363 already carries the commander network ID.

2. **`Hrot/Subsystems/Hrot.SimHost/UI/SimHostScenarioManager.cs`:**
   Remove all `CommanderId = 0,` lines (~6 occurrences). These are default-value assignments
   that become unnecessary once the field is gone.

3. **`Hrot/Network/Hrot.Network.NED/Replication/Map/Utils/DescriptorMapper.cs`:**
   - Around line 104: remove `CommanderId = d.EntityInfo.CommanderId` from the
     `Fdp.Core.EntityInfo` initializer inside the `dtEntityInfo` case.
   - Around line 281: remove `ei.CommanderId = d.EntityInfo.CommanderId;` — this was
     a post-compile set on the `Fdp.Core.EntityInfo` ref.

4. **`Hrot/Subsystems/Hrot.Editor/Adapters/EditorOrbatAdapter.cs`:**
   Replace `info.CommanderId` (ECS field read) with a `UnitSubordinate` lookup.
   Pattern:
   ```csharp
   int cmdId = _world.HasComponent<UnitSubordinate>(entity)
       ? _world.GetComponent<UnitSubordinate>(entity).Commander.Index
       : 0;
   ```
   Add `using Fdp.Core.CommandHierarchy;` to the using directives.
   Update the class-level XML doc comment to say "derived from `UnitSubordinate.Commander`
   (no component = root)" instead of "derived from `EntityInfo.CommanderId`".

5. **`Hrot/Network/Hrot.Network.NED/Replication/Map/Egress/EntityInfoEgressTranslator.cs`:**
   Remove `CommanderId = data.CommanderId,` from `_writer.Write(...)`. CS010 will add the
   proper replacement logic in `ScanAndPublish`.

6. **`Hrot/Network/Hrot.Network.NED/Replication/Map/Ingress/EntityInfoIngressTranslator.cs`:**
   Remove `CommanderId = info.CommanderId` from `igData` initializer in `ProcessSample` and
   from `ApplyToEntity`. CS011 will add the proper replacement logic.

7. **`Hrot/Subsystems/Hrot.SimHost.Tests/AttributeCompilerFactoryTests.cs`:**
   In `DescriptorMapper_WithCompiler_DtEntityInfoProducesIgEntityData`:
   - Remove `CommanderId = 42` from the `Hrot.NED.Descriptors.EntityInfo` initializer. Wait —
     that is the DDS-side struct, which still has `CommanderId`. Keep that line.
   - Remove `Assert.Equal(42, igData.CommanderId);` since `Fdp.Core.EntityInfo` no longer has
     the field. The test still validates `Name` and `ForceId`.

**Note about NedIgNetworkAdapter.cs line ~223:** The line
`EntityInfo = new EntityInfo { CommanderId = commanderEntityId }` uses
`Hrot.NED.Descriptors.EntityInfo` (DDS descriptor), NOT `Fdp.Core.EntityInfo`. The DDS
descriptor retains its `CommanderId` field. Do NOT touch this line.

**Note about NedExConIngressTranslators.cs:** `CommanderId = d.CommanderId` sets the ExCon DER
layer's `EntityInfoDescriptor.CommanderId`, not `Fdp.Core.EntityInfo`. Do NOT touch this line.

**After all removals the solution must compile with 0 errors.**

---

### CS010 — Update EntityInfoEgressTranslator [P1]

**File:** `Hrot/Network/Hrot.Network.NED/Replication/Map/Egress/EntityInfoEgressTranslator.cs`

**Reference:** [TASK-CS010](../TASK-DETAIL.md#task-cs010--update-entityinfoegresstranslator)

Rewrite the `_writer.Write(...)` call inside `ScanAndPublish` to populate `CommanderId` and
`TacticalDesignation` from `UnitSubordinate` instead of `EntityInfo.CommanderId`.

Replace the existing write block with:

```csharp
long commanderNetId = 0;
var designation = eTacticalDesignation.Undefined;
if (view.HasComponent<UnitSubordinate>(entity))
{
    var sub = view.GetComponent<UnitSubordinate>(entity);
    if (!_entityMap.TryGetNetworkId(sub.Commander, out commanderNetId))
    {
        FdpLog<EntityInfoEgressTranslator>.Debug(
            "[Node-{0}] Commander entity for sub {1} not found in NetworkEntityMap; sending CommanderId=0.",
            _localNodeId, netId.Value);
        commanderNetId = 0;
    }
    designation = TacticalDesignationMapper.ToDds(sub.Designation);
}

_writer.Write(new Hrot.NED.Descriptors.EntityInfo
{
    EntityId             = (int)netId.Value,
    Name                 = data.Name.ToString(),
    ForceIdentifier      = MapForceId(data.ForceId),
    CommanderId          = (int)commanderNetId,
    TacticalDesignation  = designation,
});
```

Add `using Fdp.Core.CommandHierarchy;` and `using Hrot.Network.NED.Replication.Map;` (or
wherever `TacticalDesignationMapper` lives) to the using directives.

Also add `using Fdp.Toolkit.Replication.Components;` if not already present (needed for
`NetworkIdentity`).

**Important constraints from TASK-CS010:**
- Do NOT add `UnitSubordinate` to the entity query filter — that would silently exclude
  commanders, standalone vehicles, and civilians.
- The authority guard (`HasAuthority`) and dirty-state gate (`SmartEgressUtil.ShouldPublish`)
  must remain in place.

**Update the stale comment** in `UnitHierarchySystem.cs` line 20 from
"broadcasts updated `CommanderId` fields" to
"broadcasts updated subordination state to remote nodes".

**No new test file needed.** Add tests to the existing
`Hrot/Subsystems/Hrot.IG.Tests/EntityInfoTranslatorTests.cs` or create a new file
`Hrot/Subsystems/Hrot.IG.Tests/EntityInfoEgressTranslatorTests.cs` with at least:
1. `UnitSubordinate_Present_CommanderIdAndDesignationPublished` — entity with `UnitSubordinate`
   pointing to a commander in `NetworkEntityMap`; assert written descriptor has correct
   `CommanderId` and `TacticalDesignation`.
2. `NoUnitSubordinate_CommanderIdZeroUndefined` — entity with only `EntityInfo`, no
   `UnitSubordinate`; assert `CommanderId == 0` and `TacticalDesignation == Undefined`.
3. `CommanderNotInEntityMap_CommanderIdZeroNoException` — `UnitSubordinate.Commander` points to
   entity absent from `NetworkEntityMap`; assert `CommanderId == 0`, no exception.

---

### CS011 — Update EntityInfoIngressTranslator (deferred queue) [P1]

**File:** `Hrot/Network/Hrot.Network.NED/Replication/Map/Ingress/EntityInfoIngressTranslator.cs`

**Reference:** [TASK-CS011](../TASK-DETAIL.md#task-cs011--update-entityinfoingresstranslator-with-deferred-queue)

This is the most complex task in the batch. Read TASK-CS011 in full before writing any code.

**Add two new private fields:**
```csharp
// Keyed by commander net ID. Value = list of subordinate entities waiting for that commander.
private readonly Dictionary<long, List<(Entity Subordinate, TacticalDesignation Designation)>> _pendingSubordinates = new();

// Keyed by subordinate net ID. Used when the subordinate itself has not yet spawned.
private readonly Dictionary<long, (long CommanderNetId, TacticalDesignation Designation)> _pendingUnspawnedSubordinates = new();

// Fired by NetworkEntityMap when a new entity is registered. Used to drain both pending queues.
private readonly List<long> _recentlyRegistered = new();
```

**Constructor changes:**
Subscribe to `_entityMap.EntityRegistered` in the constructor:
```csharp
_entityMap.EntityRegistered += OnEntityRegistered;
```

Add a private handler:
```csharp
private void OnEntityRegistered(long netId)
{
    _recentlyRegistered.Add(netId);
}
```

**Rewrite `ProcessSample`:**

```csharp
internal void ProcessSample(Hrot.NED.Descriptors.EntityInfo info, long netId, EntityRepository? repo = null)
{
    // Always apply Name + ForceId to the ECS entity when present.
    var igData = new Fdp.Core.EntityInfo
    {
        Name    = info.Name,
        ForceId = (ForceId)(int)info.ForceIdentifier,
    };

    if (repo != null && _entityMap.TryGetEntity(netId, out var entity))
    {
        repo.SetComponent(entity, igData);
    }
    else
    {
        _eventBus.PublishManaged(new UpdateEntityCommand
        {
            NetworkId          = netId,
            ComponentsToUpdate = new List<object> { igData },
            RequestId          = Guid.Empty,
        });
    }

    // Commander assignment / removal.
    long commanderNetId = info.CommanderId;
    var designation     = TacticalDesignationMapper.ToEcs(info.TacticalDesignation);

    if (commanderNetId == 0)
    {
        // Only publish CmdRemoveSubordinate if the entity currently has a UnitSubordinate.
        if (repo != null && _entityMap.TryGetEntity(netId, out var subEntity)
            && repo.HasComponent<UnitSubordinate>(subEntity))
        {
            _eventBus.Publish(new CmdRemoveSubordinate { Subordinate = subEntity });
        }
        return;
    }

    // Scrub this subordinate from all existing pending queues before re-queuing.
    RemoveFromAllPendingQueues(netId);

    // Case 1: subordinate entity is not yet spawned.
    if (!_entityMap.TryGetEntity(netId, out _))
    {
        _pendingUnspawnedSubordinates[netId] = (commanderNetId, designation);
        return;
    }

    // Case 2: subordinate is alive, commander is also alive.
    if (_entityMap.TryGetEntity(commanderNetId, out var cmdEntity)
        && _entityMap.TryGetEntity(netId, out var subEntity2))
    {
        _eventBus.Publish(new CmdAssignSubordinate
        {
            Subordinate = subEntity2,
            Commander   = cmdEntity,
            Designation = designation,
        });
        return;
    }

    // Case 3: subordinate is alive, commander is not yet spawned — defer by commander.
    if (!_pendingSubordinates.TryGetValue(commanderNetId, out var list))
    {
        list = new List<(Entity, TacticalDesignation)>();
        _pendingSubordinates[commanderNetId] = list;
    }
    if (_entityMap.TryGetEntity(netId, out var subEntity3))
        list.Add((subEntity3, designation));
}
```

**Rewrite `PollIngress`** to drain `_recentlyRegistered` after DDS reads:

```csharp
public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
{
    if (_reader is null) return;
    using var loan = _reader.Take();
    foreach (var sample in loan)
    {
        if (!sample.IsValid) continue;
        if (sample.Info.InstanceState != CycloneDDS.Runtime.DdsInstanceState.Alive) continue;
        ReceivedSampleCount++;
        var info   = sample.Data;
        long netId = info.EntityId;
        var repo   = view as EntityRepository;

        if (!_entityMap.TryGetEntity(netId, out _))
        {
            if (repo == null)
            {
                FdpLog<EntityInfoIngressTranslator>.Warn(
                    "[Node-{0}] Cannot create ghost for NetID {1}: view is read-only.", _localNodeId, netId);
                continue;
            }
            _ghostCreationSystem.CreateGhost(repo, netId);
        }

        ProcessSample(info, netId, repo);
    }

    // Drain recently registered entities — resolve any pending queues.
    foreach (var registeredId in _recentlyRegistered)
        DrainPendingForRegistered(registeredId, view as EntityRepository);
    _recentlyRegistered.Clear();
}
```

**Add `DrainPendingForRegistered` helper:**

```csharp
private void DrainPendingForRegistered(long registeredNetId, EntityRepository? repo)
{
    // 1. If a previously un-spawned subordinate just appeared:
    if (_pendingUnspawnedSubordinates.TryGetValue(registeredNetId, out var pending))
    {
        _pendingUnspawnedSubordinates.Remove(registeredNetId);
        if (!_entityMap.TryGetEntity(registeredNetId, out var subEntity)) return;

        if (_entityMap.TryGetEntity(pending.CommanderNetId, out var cmdEntity))
        {
            // Commander already alive — publish immediately.
            _eventBus.Publish(new CmdAssignSubordinate
            {
                Subordinate = subEntity,
                Commander   = cmdEntity,
                Designation = pending.Designation,
            });
        }
        else
        {
            // Commander not yet alive — move to deferred-by-commander queue.
            if (!_pendingSubordinates.TryGetValue(pending.CommanderNetId, out var list))
            {
                list = new List<(Entity, TacticalDesignation)>();
                _pendingSubordinates[pending.CommanderNetId] = list;
            }
            list.Add((subEntity, pending.Designation));
        }
        return;
    }

    // 2. If a commander just appeared — resolve all its waiting subordinates:
    if (!_pendingSubordinates.TryGetValue(registeredNetId, out var subs)) return;
    if (!_entityMap.TryGetEntity(registeredNetId, out var cmdEnt)) return;

    foreach (var (sub, desig) in subs)
    {
        _eventBus.Publish(new CmdAssignSubordinate
        {
            Subordinate = sub,
            Commander   = cmdEnt,
            Designation = desig,
        });
    }
    _pendingSubordinates.Remove(registeredNetId);
}
```

**Add `RemoveFromAllPendingQueues` helper:**

```csharp
private void RemoveFromAllPendingQueues(long subordinateNetId)
{
    _pendingUnspawnedSubordinates.Remove(subordinateNetId);

    foreach (var list in _pendingSubordinates.Values)
    {
        if (_entityMap.TryGetEntity(subordinateNetId, out var subEnt))
            list.RemoveAll(e => e.Subordinate.Equals(subEnt));
    }
}
```

**Rewrite `ApplyToEntity`** (ghost promotion — no CommanderId):

```csharp
public void ApplyToEntity(Entity entity, object data, EntityRepository repo)
{
    if (data is Hrot.NED.Descriptors.EntityInfo info)
    {
        repo.SetComponent(entity, new Fdp.Core.EntityInfo
        {
            Name    = info.Name,
            ForceId = (ForceId)(int)info.ForceIdentifier,
        });
    }
}
```

**Rewrite `Dispose(long networkEntityId)`:**

```csharp
public void Dispose(long networkEntityId)
{
    // Remove as a pending commander.
    _pendingSubordinates.Remove(networkEntityId);

    // Remove as a pending un-spawned subordinate.
    _pendingUnspawnedSubordinates.Remove(networkEntityId);

    // Remove as a spawned-but-deferred subordinate in any commander's list.
    if (_entityMap.TryGetEntity(networkEntityId, out var subEnt))
    {
        foreach (var list in _pendingSubordinates.Values)
            list.RemoveAll(e => e.Subordinate.Equals(subEnt));
    }
}
```

**Unsubscribe from `EntityRegistered`** — add a proper cleanup method or use the existing
`Dispose(long)` overload pattern. The translator has a `Dispose(long)` for entity disposal but
no `IDisposable`. Add `internal void Shutdown() => _entityMap.EntityRegistered -= OnEntityRegistered;`
if a clean shutdown hook exists; otherwise leave it (weak event reference is acceptable for this
architecture).

**Add `using Fdp.Core.CommandHierarchy;`** to the using directives.

**Tests:** Add to `Hrot/Subsystems/Hrot.IG.Tests/EntityInfoTranslatorTests.cs` (or create
`EntityInfoIngressTranslatorTests.cs`) covering all 11 success conditions from TASK-CS011.
Key scenarios:
1. Commander present — immediate `CmdAssignSubordinate`.
2. Commander absent — deferred in `_pendingSubordinates`.
3. Deferred resolved on `EntityRegistered`.
4. Commander update scrubs old queue.
5. `Dispose` cleans pending subordinate.
6. `CommanderId == 0` with existing `UnitSubordinate` — publishes `CmdRemoveSubordinate`.
7. `CommanderId == 0` without `UnitSubordinate` — no event.
8. Subordinate arrives before entity spawns — queued in `_pendingUnspawnedSubordinates`.
9. Entity spawns while commander alive — immediate `CmdAssignSubordinate`.
10. Entity spawns while commander also missing — moves to `_pendingSubordinates`.
11. `Dispose` cleans `_pendingUnspawnedSubordinates`.

---

### CS023 — Component registry integration test update [P2]

**Reference:** [TASK-CS023](../TASK-DETAIL.md#task-cs023--component-registry-integration-test-update)

Find the component registry integration test file. Search for test classes/methods that register
components and verify no ID collisions. Update them to assert:
- `world.GetComponentTable<UnitRoster>()` is not null.
- `world.GetComponentTable<UnitSubordinate>()` is not null.
- All registered component IDs remain unique.

If no dedicated `ComponentRegistryTests` class exists, add the assertions to the nearest
existing test that registers `SimHostComponentRegistry` or `HrotSharedComponentRegistry`.

---

## Ordering Requirement

Implement in this order:
1. **CT-2** — standalone fix, no dependencies.
2. **CS008** — adds the DDS field needed by CS010 and CS011.
3. **CS009** — removes `Fdp.Core.EntityInfo.CommanderId` and fixes all cascading compile errors
   (including the partial CS010/CS011 changes needed just to compile). After CS009 the build
   must be 0-error, though CS010/CS011 logic may not yet be complete.
4. **CS010** — adds full egress logic.
5. **CS011** — adds full ingress logic.
6. **CS023** — add registry test assertions.

---

## Success Criteria

- `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet` → 0 errors.
- `dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build --nologo`
  → passes (only pre-existing 2 `MissionPlanTranslatorTests` failures remain).
- `CgfLogicPackTests` simulation-system count = 16, total = 18.
- At least 14 new tests pass (3 from CS010 egress + 11 from CS011 ingress).
- `typeof(Fdp.Core.EntityInfo).GetField("CommanderId")` returns null.

---

## Report

Commit all changes with the message convention used in previous batches. Place the report at:
`.dev/commander-subordinates/reports/BATCH-03-REPORT.md`

For FDP submodule changes (CS008, and any FDP-side changes): commit the FDP submodule first,
then commit the parent repo.
