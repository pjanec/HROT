# BATCH-03 Report

## Summary

All 6 tasks completed, build clean (0 errors), tests passing (only pre-existing failures remain).

---

## Tasks Completed

### CT-2 -- Remove UnitHierarchySystem from CgfLogicPack

**Status: DONE**

`UnitHierarchySystem` was already integrated into `HrotCoreLogicPack` (where it belongs). It was incorrectly also registered in `CgfLogicPack`, creating a duplicate execution each tick. Removed the duplicate.

**Modified:**
- `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs` -- removed `UnitHierarchySystem` field, constructor initialisation, and `simList.Add`. SimulationSystems count is now 16 (down from 17), total pack systems count 18 (down from 19).
- `Hrot/Subsystems/Hrot.SimHost.Tests/CgfLogicPackTests.cs` -- updated count assertions to 16/18.
- `Hrot/Engine/Hrot.Common/Systems/UnitHierarchySystem.cs` -- updated stale XML comment to reflect current behaviour.

---

### CS008 -- Add TacticalDesignation to DDS EntityInfo struct

**Status: DONE**

The DDS wire struct for entity info needed a `TacticalDesignation` field so that subordination state can be replicated to remote nodes.

**Modified:**
- `Hrot/Network/Hrot.Network.NED/GenericDescriptors.cs` -- added `public eTacticalDesignation TacticalDesignation;` to the `Hrot.NED.Descriptors.EntityInfo` partial struct.

---

### CS009 -- Remove CommanderId from ECS EntityInfo component

**Status: DONE**

`Fdp.Core.EntityInfo.CommanderId` was a stale denormalized field. Subordination state is now authoritative in `UnitSubordinate`; `CommanderId` is redundant and was removed.

**FDP submodule changes (committed separately, commit fbb5d73):**
- `FDP/Engine/Fdp.Core/Components/EntityInfo.cs` -- removed `public int CommanderId;`

**Parent repo cascades:**
- `Hrot/Subsystems/Hrot.CGF/Systems/CreateEntityRequestSystem.cs` -- removed `CommanderId = (int)pending.NetworkId` from EntityInfo initialiser
- `Hrot/Subsystems/Hrot.SimHost/UI/SimHostScenarioManager.cs` -- removed 6 occurrences of `CommanderId = 0,`
- `Hrot/Network/Hrot.Network.NED/Replication/Map/Utils/DescriptorMapper.cs` -- removed `CommanderId` usages from `dtEntityInfo` case and ref-based setter
- `Hrot/Subsystems/Hrot.Editor/Adapters/EditorOrbatAdapter.cs` -- reads `UnitSubordinate.Commander.Index` instead of the removed `info.CommanderId`
- `Hrot/Subsystems/Hrot.SimHost.Tests/AttributeCompilerFactoryTests.cs` -- removed `Assert.Equal(42, igData.CommanderId)`
- `Hrot/Subsystems/Hrot.IG.Tests/IgEntityDataTests.cs` -- removed `Assert.Equal(0, data.CommanderId)`
- `Hrot/Subsystems/Hrot.Editor.Tests/Adapters/AdapterTests.cs` -- registered `UnitSubordinate`, removed `CommanderId` usages, added `UnitSubordinate` component to test entities

---

### CS010 -- EntityInfoEgressTranslator reads UnitSubordinate

**Status: DONE**

The egress translator now fills `CommanderId` and `TacticalDesignation` on the DDS wire struct from the `UnitSubordinate` ECS component instead of the removed `EntityInfo.CommanderId` field.

**Modified:**
- `Hrot/Network/Hrot.Network.NED/Replication/Map/Egress/EntityInfoEgressTranslator.cs` -- full rewrite:
  - Uses `IDdsWriter<Hrot.NED.Descriptors.EntityInfo>` field for testability.
  - Public constructor delegates to internal testable constructor via `DdsWriterAdapter<>`.
  - `ScanAndPublish` reads `UnitSubordinate` via `view.GetComponentRO<UnitSubordinate>` to populate `CommanderId` (from `sub.Commander.Index`) and `TacticalDesignation` (via `TacticalDesignationMapper`).
  - When `UnitSubordinate` is absent: `CommanderId = 0`, `TacticalDesignation = Undefined`.

**New file:**
- `Hrot/Engine/Hrot.Map.Common.Tests/Replication/Egress/EntityInfoEgressTranslatorTests.cs` -- 3 tests:
  - `UnitSubordinate_Present_CommanderIdAndDesignationPublished` -- CommanderId=10, Wingman designation propagated correctly.
  - `NoUnitSubordinate_CommanderIdZeroAndDesignationUndefined` -- absent component yields zero/undefined defaults.
  - `CommanderNotInEntityMap_CommanderIdZeroNoException` -- commander entity without NetworkIdentity excluded from query; asserts CommanderId=0, no exception thrown.

---

### CS011 -- EntityInfoIngressTranslator handles subordination via deferred queues

**Status: DONE**

Complete rewrite of the ingress translator to handle network-replicated subordination state. Uses three deferred queues to handle all ordering cases (both entities alive, subordinate arrives before commander, subordinate unspawned when sample arrives).

**Modified:**
- `Hrot/Network/Hrot.Network.NED/Replication/Map/Ingress/EntityInfoIngressTranslator.cs` -- full rewrite:
  - Three private queues: `_pendingSubordinates` (deferred by missing commander), `_pendingUnspawnedSubordinates` (deferred by missing subordinate entity), `_recentlyRegistered` (flush trigger).
  - Constructor subscribes to `NetworkEntityMap.EntityRegistered`.
  - `PollIngress`: DDS reads inside `if (_reader is not null)` block; queue drain unconditional (enables test-mode callers without a real reader).
  - `ProcessSample`: 3 cases -- (1) entity unspawned -> `_pendingUnspawnedSubordinates`; (2) both alive -> immediate `CmdAssignSubordinate`; (3) subordinate alive but commander absent -> `_pendingSubordinates`.
  - `CommanderId==0` path: publishes `CmdRemoveSubordinate` if entity has `UnitSubordinate`.
  - `DrainPendingForRegistered`: resolves both pending queues when an entity registers.
  - `RemoveFromAllPendingQueues`: scrubs all queues when a subordinate receives a new sample.
  - `internal void Shutdown()`: unsubscribes `EntityRegistered`.

**Modified (test file):**
- `Hrot/Subsystems/Hrot.IG.Tests/EntityInfoTranslatorTests.cs` -- 11 new tests added (14 total):
  - `CS011_CommanderPresent_ImmediateCmdAssignSubordinate`
  - `CS011_CommanderAbsent_NoImmediateEvent_DeferredByCommander`
  - `CS011_DeferredResolvedOnEntityRegistered`
  - `CS011_CommanderUpdate_ScrubsOldPendingQueue`
  - `CS011_Dispose_ClearsPendingSubordinate`
  - `CS011_CommanderIdZero_WithExistingUnitSubordinate_PublishesCmdRemove`
  - `CS011_CommanderIdZero_WithoutUnitSubordinate_NoEvent`
  - `CS011_SubordinateUnspawned_QueuesInPendingUnspawned`
  - `CS011_SubordinateSpawns_CommanderAlive_ImmediateAssign`
  - `CS011_SubordinateSpawns_CommanderMissing_MovesToPendingByCommander`
  - `CS011_Dispose_ClearsPendingUnspawnedSubordinate`

---

### CS023 -- Component registry test: verify UnitRoster and UnitSubordinate registered, IDs unique

**Status: DONE**

Added assertions to `ComponentRegistryTests.cs` to verify that the two new CommandHierarchy components are properly registered after `SimHostComponentRegistry.RegisterAll`, and that no two registered components share the same ID.

**Modified:**
- `Hrot/Subsystems/Hrot.SimHost.Tests/ComponentRegistryTests.cs`:
  - Added `using Fdp.Core.CommandHierarchy;`
  - Extended `SimHostComponentRegistry_RegisterAll_StillProvidesCognitiveComponents` to assert `world.GetComponentTable<UnitRoster>()` and `world.GetComponentTable<UnitSubordinate>()` are not null.
  - New test `SimHostComponentRegistry_RegisterAll_ComponentIdsAreUnique`: calls `ComponentTypeRegistry.GetAllTypeIds()` after `RegisterAll` and asserts that all IDs are distinct (no collisions).

---

## Test Results

| Test project | Before | After | Delta |
|---|---|---|---|
| `Hrot.SimHost.Tests` | 484 pass / 2 fail (pre-existing) | 486 pass / 2 fail | +2 pass |
| `Hrot.IG.Tests` | 423 pass | 434 pass | +11 pass |
| `Hrot.Map.Common.Tests` | 40 pass / 2 fail (pre-existing) | 43 pass / 2 fail (pre-existing) | +3 pass |

Pre-existing failures not introduced by this batch:
- `Hrot.SimHost.Tests.MissionPlanTranslatorTests` -- 2 failures (unrelated to commander-subordinates)
- `Hrot.Map.Common.Tests.NavigationIntentEgressTranslatorTests` -- 2 failures (pre-existing parallelism/static-registry interaction, present before this batch)

---

## Commit

Parent repo: `58a518c` -- BATCH-03: CT-2 CS008 CS009 CS010 CS011 CS023
FDP submodule: `fbb5d73` -- CS009: remove CommanderId from EntityInfo
