# BATCH-02 Report

## Summary

All 6 tasks completed, build clean (0 errors), tests passing (only pre-existing failures remain).

---

## Tasks Completed

### CT-1 — Move CommandHierarchy types from Hrot.Core to Fdp.Core

**Status: DONE**

Motivation: `Fdp.Toolkits` cannot reference `Hrot.Core` (circular dependency). Moving the types to `Fdp.Core` breaks the cycle and makes them available throughout the dependency chain.

**New files created in `FDP/Engine/Fdp.Core/CommandHierarchy/`:**
- `TacticalDesignation.cs` — `public enum TacticalDesignation : ushort { Undefined=0, Commander=1, SquadLeader=2, Wingman=3, Support=4 }`
- `CommandHierarchyEvents.cs` — events `[EventId(2200)] CmdAssignSubordinate`, `[EventId(2201)] CmdRemoveSubordinate`, `[EventId(2202)] CmdAssignSubordinateRejected`
- `UnitSubordinate.cs` — `[ComponentId(183)] struct UnitSubordinate { Entity Commander; TacticalDesignation Designation; }`
- `UnitRoster.cs` — `[ComponentId(182)] [DataPolicy(DataPolicy.NoSave)] unsafe struct UnitRoster { const int Capacity=16; int Count; fixed long SubordinateEntities[16]; fixed ushort TacticalDesignations[16]; }`

**`FDP/Engine/Fdp.Core/GlobalComponentIds.cs`:** Added `UnitRoster=182`, `UnitSubordinate=183`, `InitialUnitSubordinateIntent=184`

**Old files deleted from `Hrot/Engine/Hrot.Core/CommandHierarchy/`:** `TacticalDesignation.cs`, `CommandHierarchyEvents.cs`, `UnitSubordinate.cs`, `UnitRoster.cs`

**Consumers updated (namespace `Hrot.Core.CommandHierarchy` → `Fdp.Core.CommandHierarchy`):**
- `Hrot/Subsystems/Hrot.SimHost/KinematicComponentRegistry.cs`
- `Hrot/Subsystems/Hrot.SimHost/SimHostComponentRegistry.cs`
- `Hrot/Network/Hrot.Network.NED/Replication/Map/TacticalDesignationMapper.cs`
- `Hrot/Engine/Hrot.Core.Tests/CommandHierarchyTests.cs`
- `Hrot/Network/Hrot.Network.NED.Tests/TacticalDesignationMapperTests.cs`

---

### CT-0 — Remove FormationFollower.LeaderEntity

**Status: DONE**

`FormationFollower.LeaderEntity` (an `Entity` field) was removed. The leader is now looked up through `UnitSubordinate.Commander`, eliminating the redundant denormalized reference that could become stale.

**Modified:**
- `FDP/Toolkits/Fdp.Toolkits/CarKinem/Formation/FormationFollower.cs` — removed `public Entity LeaderEntity;`
- `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/FormationTargetSystem.cs` — replaced `follower.LeaderEntity` with `UnitSubordinate` component lookup
- `FDP/Toolkits/Fdp.Toolkits.Tests/CarKinem/Formation/FormationTargetSystemTests.cs` — test setup updated to add `UnitSubordinate` instead of setting `LeaderEntity`
- `FDP/Toolkits/Fdp.Toolkits.Tests/CarKinem/Commands/FormationCreationTests.cs` — removed `FormationFollower.LeaderEntity` assertions, replaced with `CmdAssignSubordinate` bus verification

---

### CS007 — VehicleCommandSystem and FormationTargetSystem publish/handle hierarchy events

**Status: DONE**

`VehicleCommandSystem` now publishes hierarchy events instead of writing `FormationFollower` directly:
- `JoinFormation` command → publishes `CmdAssignSubordinate` (with `HasFormationSlot=1`, `SlotIndex` from command), keeps `nav.Mode=Formation`
- `LeaveFormation` command → publishes `CmdRemoveSubordinate`, sets `nav.Mode=None`
- New `ProcessAssignSubordinateRejected` phase → reads `CmdAssignSubordinateRejected`, sets `LocomotionChannel.Status = NodeStatus.Failure`

`FormationTargetSystem` was updated to look up leader via `UnitSubordinate` component (CT-0 follow-on).

**Modified:**
- `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/VehicleCommandSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits/CarKinem/Systems/FormationTargetSystem.cs`
- `FDP/Toolkits/Fdp.Toolkits.Tests/CarKinem/Commands/VehicleCommandSystemTests.cs` — 3 new tests: `JoinFormation_PublishesCmdAssignSubordinate_NotFormationFollower`, `LeaveFormation_PublishesCmdRemoveSubordinate_SetsModeToNone`, `RejectedSubordinate_SetsLocomotionFailure`

---

### CS016 — Create UnitHierarchySystem

**Status: DONE**

`UnitHierarchySystem` is the single authority that reads hierarchy events and applies them to components atomically.

**New file: `Hrot/Engine/Hrot.Common/Systems/UnitHierarchySystem.cs`**

Behaviour:
- `ProcessDestructionOrders`: reads `DestructionOrder` — if destroyed entity is a commander, strips `UnitSubordinate` + `FormationFollower` from all roster members and marks them dirty; if destroyed entity is a subordinate, removes it from its commander's roster
- `ProcessRemoveSubordinates`: reads `CmdRemoveSubordinate` — strips `UnitSubordinate` and `FormationFollower` from subordinate, removes from commander roster, marks dirty
- `ProcessAssignSubordinates`: reads `CmdAssignSubordinate` — if roster is at capacity, publishes `CmdAssignSubordinateRejected` and skips; otherwise writes `UnitSubordinate` + `UnitRoster` atomically; if `HasFormationSlot==1`, also writes `FormationFollower`; marks subordinate dirty

**Wired into three hosts:**
- `Hrot/Subsystems/Hrot.SimHost/SimHostCoreLogicPack.cs` — added to `simList` (count 7 → 8)
- `Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs` — added to `simList` (count 16 → 17)
- `Hrot/Subsystems/Hrot.IG/IgApplication.cs` — registered via `_kernel.RegisterModule(new IgUnitHierarchyModule(new UnitHierarchySystem()))` (private nested wrapper class)

**Hrot.Common.csproj**: Added `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` (required for `fixed` buffer access in `UnitRoster`)

**New test file: `Hrot/Subsystems/Hrot.SimHost.Tests/UnitHierarchySystemTests.cs`** — 12 tests:
1. `Assign_AtomicTwoWrite_UnitSubordinateAndRosterBothSet`
2. `Assign_MultipleSubordinates_RosterOrderPreserved`
3. `Assign_Reassign_MovesFromOldToNewCommander`
4. `Remove_MiddleEntry_OrderPreservingShift`
5. `Assign_CapacityExceeded_NoPartialWrite`
6. `Destruction_Commander_ReleasesAllSubordinatesAndMarksDirty`
7. `Destruction_Subordinate_RemovedFromCommanderRoster`
8. `Assign_Success_MarksSubordinateDirty`
9. `Remove_AlsoRemovesFormationFollower`
10. `Assign_WithFormationSlot_FormationFollowerWritten`
11. `Assign_CapacityExceededWithFormationSlot_NeitherComponentWritten`
12. `Assign_CapacityExceeded_PublishesCmdAssignSubordinateRejected`

**Updated test counts:**
- `SimHostCoreLogicPackTests.cs`: 7 → 8 for SimulationSystems count
- `CgfLogicPackTests.cs`: 16 → 17 for SimulationSystems count, 18 → 19 for total count

---

### CS012 — Add InitialUnitSubordinateIntent

**Status: DONE**

`InitialUnitSubordinateIntent` is a genesis intent component that seeds a unit's initial commander assignment before the simulation begins. It is consumed downstream (e.g. by a genesis/spawn system) to publish `CmdAssignSubordinate` at world load time.

**Modified:**
- `Hrot/Engine/Hrot.Common/Serializers/GenesisIntentComponents.cs` — added `public class InitialUnitSubordinateIntent : IGenesisIntentComponent { public string CommanderNetworkId { get; set; } = string.Empty; public TacticalDesignation Designation { get; set; } }`
- `Hrot/Subsystems/Hrot.SimHost/SimHostComponentRegistry.cs` — registered `world.RegisterManagedComponent<InitialUnitSubordinateIntent>()`
- `Hrot/Subsystems/Hrot.CGF/Systems/CreateEntityRequestSystem.cs` — injects `InitialUnitSubordinateIntent` for child entities with a non-`Undefined` `TacticalDesignation` slot

---

### CS022 — Replace TkbChildSlot.RoleTag with Designation enum

**Status: DONE**

Replaced the stringly-typed `RoleTag` field in composition definitions with the type-safe `TacticalDesignation` enum.

**Modified:**
- `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/TkbCompositionDef.cs` — `public string RoleTag` → `public TacticalDesignation Designation`
- `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/BdcTkbCatalog.cs` — 4 slot replacements: `RoleTag = "Tank"/"SquadLeader"/"Rifleman"` → `Designation = TacticalDesignation.Wingman/SquadLeader/Wingman`
- `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/BdcTkbBuilder.cs` — updated `ChildBlueprintDefinition` constructor call to pass `designation: slot.Designation`
- `FDP/Engine/Fdp.Core/Abstractions/ChildBlueprintDefinition.cs` — added `TacticalDesignation Designation { get; set; }` property

---

## Build and Test Results

**Build:** 0 errors (warnings only — pre-existing benchmark CS8618/CS0649 warnings)

**Tests:**
| Project | Passed | Failed | Skipped | Pre-existing failures |
|---|---|---|---|---|
| Hrot.SimHost.Tests | 485 | 2 | 3 | 2 (MissionPlanTranslatorTests) |
| Fdp.Toolkits.Tests | 776 | 22 | 0 | 22 (pre-existing) |

All 12 new `UnitHierarchySystemTests` pass. No regressions introduced.

---

## Commits

**FDP submodule (`d:\Work\IOS-IG-SimHost-FDP-2\FDP`):**
```
4501540 BATCH-02: CT-1 CT-0 CS007 --- move CommandHierarchy to Fdp.Core, remove LeaderEntity, publish/handle hierarchy events
```

**Parent repo (`d:\Work\IOS-IG-SimHost-FDP-2`):**
```
5ccc8ac BATCH-02: CS016 CS012 CS022 + CT-1 consumers --- UnitHierarchySystem, InitialUnitSubordinateIntent, TacticalDesignation enum, update FDP submodule ref
```

---

## Issues Encountered and Resolved

1. **`NodeStatus` namespace**: `NodeStatus` is in the `Fbt` namespace (FastBTree), not `Fdp.Toolkit.Behavior`. Fixed by adding `using Fbt;`.

2. **`ReadOnlySpan<T>` vs `IEnumerable`**: `repo.Bus.Read<T>()` returns `ReadOnlySpan<T>` which cannot be passed to `Assert.Single(IEnumerable)`. Fixed by using `.Length` for count assertions and `[0]` indexer for element access.

3. **`file` keyword on nested type**: `file` modifier is not allowed on nested types in C#. Fixed by using `private` instead.

4. **`ExecutionPolicy` namespace**: `ExecutionPolicy` is in `Fdp.ModuleHost.Abstractions`, not `Fdp.ModuleHost`. Fixed by using fully-qualified name in the nested class.

5. **`AllowUnsafeBlocks` missing**: `Hrot.Common.csproj` did not have `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>`. Added to `PropertyGroup`.

6. **`CgfLogicPackTests` count regression**: Adding `UnitHierarchySystem` to `CgfLogicPack` increased sim system count from 16 to 17. Tests updated.
