# ONBOARDING — Commander-Subordinate Infrastructure

## Project Overview

This workstream refactors the FDP/Hrot engine to cleanly separate two concerns that are
currently mixed together in `FormationRoster` / `FormationMember`:

1. **Generic command hierarchy** — who is the commander of an entity, which entities does a
   commander control. Needed by AI (BTree/HSM) to issue tactical intents.
2. **Kinematic formation steering** — formation type, slot assignments, steering state. Needed
   by the high-rate `FormationTargetSystem` / `CarKinematicsSystem`.

After the refactor:

- New **`UnitSubordinate`** and **`UnitRoster`** components carry the generic hierarchy.
- Existing `FormationRoster` is renamed to **`FormationController`** (keeps only formation config).
- Existing `FormationMember` is renamed to **`FormationFollower`** (keeps only kinematic state).
- `FormationMember.LeaderEntityId` (unsafe raw `int`) is replaced by
  `UnitSubordinate.Commander` (generation-safe `Entity` struct).
- `Fdp.Core.EntityInfo.CommanderId` is removed; only `UnitSubordinate` carries the relationship
  in the local ECS.
- A new `UnitHierarchySystem` is the sole authority for mutating command relationships at runtime.
- A new `UnitSubordinateTranslator` and an extended `GenesisMaterializationSystem` keep the
  hierarchy consistent across scenario save/load.
- ORBAT UI panels gain drag-drop support for reassigning subordination.

---

## Planning Artifacts

| Document | Purpose |
|----------|---------|
| [DESIGN.md](./DESIGN.md) | Architecture, phases, data-flow, and architectural decisions |
| [TASK-DETAIL.md](./TASK-DETAIL.md) | Per-task scope, constraints, and success conditions |
| [TASK-TRACKER.md](./TASK-TRACKER.md) | Progress checklist — check off tasks as they complete |

---

## Folder Layout

### New files to create

```
Hrot/Engine/Hrot.Core/CommandHierarchy/
    TacticalDesignation.cs           -- CS001: ECS enum
    UnitSubordinate.cs               -- CS002: subordinate component
    UnitRoster.cs                    -- CS003: commander roster component
    CommandHierarchyEvents.cs        -- CS015: CmdAssignSubordinate, CmdRemoveSubordinate

Hrot/Engine/Hrot.Common/Serializers/
    GenesisIntentComponents.cs       -- CS012: add InitialUnitSubordinateIntent (existing file)

Hrot/Subsystems/Hrot.SimHost/Serializers/
    UnitSubordinateTranslator.cs     -- CS013: IEntityScenarioTranslator impl

Hrot/Subsystems/Hrot.SimHost/Systems/
    UnitHierarchySystem.cs           -- CS016: runtime CRUD system

Hrot/Network/Hrot.Network.NED/Replication/Map/
    TacticalDesignationMapper.cs     -- CS001: ACL enum conversion helper
```

### Existing files to modify

```
Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs       -- CS001-CS004, CS012
Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/TkbCompositionDef.cs -- CS022
Fdp/Engine/Fdp.Core/Components/EntityInfo.cs                   -- CS009: remove CommanderId
Fdp/Engine/Fdp.Core/GlobalComponentIds.cs                      -- CS005, CS006: rename constants
Fdp/Toolkits/Fdp.Toolkits/CarKinem/Formation/FormationRoster.cs  -- CS005: rename to FormationController
Fdp/Toolkits/Fdp.Toolkits/CarKinem/Formation/FormationMember.cs  -- CS006: rename to FormationFollower
Fdp/Toolkits/Fdp.Toolkits/CarKinem/Systems/VehicleCommandSystem.cs -- CS007
Hrot/Network/Hrot.Network.NED/GenericDescriptors.cs            -- CS001, CS008
Hrot/Network/Hrot.Network.NED/Replication/Map/Egress/EntityInfoEgressTranslator.cs -- CS010
Hrot/Network/Hrot.Network.NED/Replication/Map/Ingress/EntityInfoIngressTranslator.cs -- CS009, CS011
Hrot/Subsystems/Hrot.SimHost/Serializers/HrotScenarioSerializerFactory.cs -- CS013
Hrot/Subsystems/Hrot.SimHost/Systems/GenesisMaterializationSystem.cs -- CS014
Hrot/Engine/Hrot.UI.Common/Models/OrbatNodeViewModel.cs        -- CS017
Hrot/Engine/Hrot.UI.Common/Facades/IOrbatController.cs        -- CS018
Hrot/Engine/Hrot.UI.Common/Panels/SharedOrbatPanel.cs         -- CS019
Hrot/Subsystems/Hrot.Editor/Adapters/EditorOrbatAdapter.cs    -- CS009, CS020
Hrot/Subsystems/Hrot.ExCon/Adapters/ExConOrbatAdapter.cs      -- CS021
```

---

## Build and Run Tests

```powershell
# Build the full solution
dotnet build IOS-IG-SimHost.sln --no-restore -v quiet

# Run all tests
dotnet test IOS-IG-SimHost.sln --no-build --nologo

# Run only the command-hierarchy relevant test projects
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj --no-build
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj --no-build

# Run a specific test by name filter
dotnet test Hrot/Subsystems/Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj `
    --no-build --filter "FullyQualifiedName~UnitHierarchy"
```

---

## Development Workflow

This project uses a **batch-based development workflow**. Before starting implementation:

Read `.dev-workstream/guides/DEV-GUIDE.md` to understand:

- How to receive a batch instruction file
- How to write a batch report after completing work
- Quality standards for code and tests
- How to handle review feedback

Batch instructions are found under `.dev/commander-subordinates/batches/`.

---

## Key Engine Concepts to Understand First

Before implementing, familiarise yourself with:

1. **`Entity` struct** (`Fdp.Core`) — 8-byte generation-safe handle. `Entity.IsNull` is the
   null check. Never compare entities by raw index alone.

2. **`EntityRepository` vs `ISimulationView`** — `EntityRepository` is the writable ECS world.
   `ISimulationView` is the read-only abstraction. Systems that mutate components must cast with
   `if (view is not EntityRepository repo) return;`.

3. **`ComponentId` + `DataPolicy` attributes** — every component must carry both. IDs 0–159 are
   FDP/toolkit-owned; 160–199 are application-owned (use `HrotComponentIds` for new values).
   `DataPolicy.NoSave` excludes a component from scenario JSON serialization.

4. **Event bus pattern** — `repo.Bus.Read<TEvent>()` in a system; `_bus.Publish(new TEvent{...})`
   from UI/adapters. Events are processed in the same tick they are published.

5. **`MapRouteIngressTranslator`** (`Hrot.Network.NED`) — the reference implementation for a
   deferred-queue ingress translator. Read it before implementing CS011.

6. **`GenesisMaterializationSystem`** (`Hrot.SimHost`) — the reference implementation for the
   retry-until-resolved intent pattern. Read `MaterializePassengers` and `MaterializeHierarchy`
   before implementing CS014.

7. **`VehicleCommandSystem.ProcessJoinFormationCommands`** — the reference pattern for an
   atomic two-component write (sets `FormationMember` on follower AND appends to
   `FormationRoster` on leader). CS007 and CS016 follow this pattern exactly.
