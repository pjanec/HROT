# BATCH-02 Instructions — Commander-Subordinate: VehicleCommandSystem, UnitHierarchySystem, Intent DTO, TKB

**Topic:** commander-subordinates  
**Batch:** 02  
**Status:** READY

---

## Context

You are implementing tasks from the **Commander-Subordinate Infrastructure** workstream.
Read these files FIRST before writing any code:

- Design: `.dev/commander-subordinates/DESIGN.md` — sections 2.3, 4.1, 5.2, 7.1
- Task details: `.dev/commander-subordinates/TASK-DETAIL.md` — sections CT-0, CS007, CS016, CS012, CS022
- Existing code baseline: BATCH-01 is fully committed. The code on `main` is the starting point.

**Build:** `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet`  
**Tests:** `dotnet test IOS-IG-SimHost.sln --no-build --nologo`  
**Pre-existing failures:** 2 in `Hrot.SimHost.Tests/MissionPlanTranslatorTests`, 22 in `Fdp.Toolkits.Tests`. Ignore these; do not introduce new ones.

---

## Task List (in implementation order)

### CT-1 — Move Command Hierarchy Types to Fdp.Core (BLOCKING ARCHITECTURAL FIX, P0)

**Why this is needed:** `Hrot.Core.csproj` depends on `Fdp.Toolkits.csproj`. Adding the reverse
reference (so `VehicleCommandSystem` and `FormationTargetSystem` in `Fdp.Toolkits` could use
types from `Hrot.Core`) would create a circular dependency. The command hierarchy types must
therefore live in `Fdp.Core`, which sits below both layers.

**What to move:**
Move these four files from `Hrot/Engine/Hrot.Core/CommandHierarchy/` to
`FDP/Engine/Fdp.Core/CommandHierarchy/` (create the folder):

1. `TacticalDesignation.cs` — change namespace to `Fdp.Core.CommandHierarchy`
2. `CommandHierarchyEvents.cs` — change namespace to `Fdp.Core.CommandHierarchy`
3. `UnitSubordinate.cs` — change namespace to `Fdp.Core.CommandHierarchy`;
   replace `[ComponentId(HrotComponentIds.UnitSubordinate)]` with `[ComponentId(183)]`
   (the constant value — `HrotComponentIds` is in `Hrot.Core` and can't be referenced from `Fdp.Core`)
4. `UnitRoster.cs` — change namespace to `Fdp.Core.CommandHierarchy`;
   replace `[ComponentId(HrotComponentIds.UnitRoster)]` with `[ComponentId(182)]`

**Add to GlobalComponentIds** (`FDP/Engine/Fdp.Core/GlobalComponentIds.cs`):
```csharp
// Commander-Subordinate hierarchy components (AI tier, IDs 182-184 reserved in HrotComponentIds)
public const byte UnitRoster                    = 182;
public const byte UnitSubordinate               = 183;
public const byte InitialUnitSubordinateIntent  = 184;
```

**Update all existing consumers** of `Hrot.Core.CommandHierarchy.*` to use
`Fdp.Core.CommandHierarchy.*` instead. At minimum update these files:
- `Hrot/Subsystems/Hrot.SimHost/SimHostComponentRegistry.cs` — change using
- `Hrot/Subsystems/Hrot.SimHost/KinematicComponentRegistry.cs` — change using
- `Hrot/Network/Hrot.Network.NED/Replication/Map/TacticalDesignationMapper.cs` — change using
- `Hrot/Engine/Hrot.Core.Tests/CommandHierarchyTests.cs` — change using
- `Hrot/Network/Hrot.Network.NED.Tests/TacticalDesignationMapperTests.cs` — change using
- `Hrot/Subsystems/Hrot.SimHost.Tests/SimHostCoreLogicPackTests.cs` — change using

**In `HrotComponentIds.cs`**: the constants 182/183/184 may remain as they are (they're the same
numeric values — keeping them avoids breaking any code that uses `HrotComponentIds.UnitRoster`
by name). They now serve as documentation aliases for the `GlobalComponentIds` values.

**After the move, delete the old folder** `Hrot/Engine/Hrot.Core/CommandHierarchy/`
(the four files are now in `Fdp.Core`). The Hrot.Core project file may need updating to remove
the old file references if it was listed explicitly.

**Success conditions for CT-1:**
- Build succeeds with 0 errors.
- All 26 existing CommandHierarchy tests still pass.
- `UnitSubordinate` and `UnitRoster` are in `Fdp.Core.CommandHierarchy` namespace.
- `VehicleCommandSystem.cs` can reference `CmdAssignSubordinate` via `using Fdp.Core.CommandHierarchy;`
  without adding a new `ProjectReference` to `Fdp.Toolkits.csproj`.

---

### CT-0 — Fix CS006: Remove `FormationFollower.LeaderEntity` (CORRECTIVE TASK, P1)

**What is wrong:** `FormationFollower` currently has `public Entity LeaderEntity;` field. This was kept
from BATCH-01 to maintain build compatibility, but the DESIGN requires FormationFollower to have
NO leader reference — the commander link lives exclusively in `UnitSubordinate.Commander`.

**What to do:**

1. Open `FDP/Toolkits/Fdp.Toolkits/CarKinem/Formation/FormationFollower.cs`.
   Remove `public Entity LeaderEntity;` entirely.

2. Find all callers that read or write `LeaderEntity` on a `FormationFollower` instance.
   In `VehicleCommandSystem` (FDP submodule), remove the `member.LeaderEntity = leaderEntity;` line.
   CS007 (below) will replace all remaining leader-reads with `UnitSubordinate.Commander`.

3. Compile and fix any remaining references to `FormationFollower.LeaderEntity` found anywhere
   in the FDP submodule or parent repo.

**Success conditions:**
- `FormationFollower` struct has NO field named `LeaderEntity` or `LeaderEntityId`.
- Build succeeds with 0 errors.
- All 18 existing formation tests still pass.

---

### CS007 — Update VehicleCommandSystem and FormationTargetSystem

**Full spec:** `.dev/commander-subordinates/TASK-DETAIL.md` §TASK-CS007  
**Design ref:** `.dev/commander-subordinates/DESIGN.md` §2.3

**Key behavioral changes:**

1. `ProcessJoinFormationCommands` in `VehicleCommandSystem` (FDP/Toolkits):
   - STOP writing `FormationFollower` directly.
   - Instead, publish `CmdAssignSubordinate { Subordinate = follower, Commander = leader,
     Designation = TacticalDesignation.Undefined, HasFormationSlot = 1, SlotIndex = <from event> }`
     to `repo.Bus`. Use `new CmdAssignSubordinate { ... }` from `Hrot.Core.CommandHierarchy`.
   - The FDP project must add a reference to `Hrot.Core` (or its hosting assembly) so that
     `CmdAssignSubordinate` is visible. Check the existing FDP.sln project references.
     If Hrot.Core is not already referenced, add `<ProjectReference ...>` to `Fdp.Toolkits.csproj`.

2. `ProcessLeaveFormationCommands` (or equivalent CmdLeaveFormation handler):
   - STOP removing `FormationFollower` directly.
   - Instead, publish `CmdRemoveSubordinate { Subordinate = follower }` to `repo.Bus`.

3. Add a handler for `CmdAssignSubordinateRejected` events on the bus:
   - For each rejected entity, set `LocomotionChannel.Status = NodeStatus.Failure`.

4. `FormationTargetSystem` — update the steering-leader lookup:
   - Replace `FormationFollower.LeaderEntityId` or `LeaderEntity` reads (which no longer exist)
     with `repo.GetComponent<UnitSubordinate>(followerEntity).Commander`.
   - Add a null/missing guard: if the entity has no `UnitSubordinate`, skip the steering update
     for that entity (e.g., `if (!repo.HasComponent<UnitSubordinate>(followerEntity)) continue;`).

**Constraints from TASK-DETAIL §CS007 (critical):**
- `VehicleCommandSystem` must NEVER directly write `UnitSubordinate`, `UnitRoster`, or `FormationFollower`.
  That authority belongs exclusively to `UnitHierarchySystem`.
- Do NOT add a capacity pre-check in VehicleCommandSystem (no reading UnitRoster.Count).
- The `CmdJoinFormation` struct (EventId 2104) is unchanged.

**Tests to write (location: FDP/Toolkits/Fdp.Toolkits.Tests/CarKinem/):**

1. `VehicleCommandSystem_JoinFormation_PublishesCmdAssignSubordinate_NotFormationFollower`:
   Create leader + follower. Publish `CmdJoinFormation`. Tick `VehicleCommandSystem` only.
   Assert: follower does NOT have `FormationFollower` yet; a `CmdAssignSubordinate` is on the bus
   with correct Subordinate, Commander, HasFormationSlot=1, SlotIndex.

2. `VehicleCommandSystem_LeaveFormation_PublishesCmdRemoveSubordinate`:
   Setup follower with both `FormationFollower` and `UnitSubordinate`. Publish `CmdLeaveFormation`.
   Tick `VehicleCommandSystem` only.
   Assert: follower still has `FormationFollower` (not yet removed); `CmdRemoveSubordinate` is on bus.

3. `VehicleCommandSystem_RejectedSubordinate_SetsLocomotionFailure`:
   Publish `CmdAssignSubordinateRejected { Subordinate = follower }` directly.
   Tick `VehicleCommandSystem`.
   Assert: `LocomotionChannel.Status == NodeStatus.Failure` on the follower.

4. `FormationTargetSystem_ReadsLeaderFromUnitSubordinate`:
   Setup follower with `UnitSubordinate.Commander = leaderEntity` and a `FormationFollower`
   (no LeaderEntity/LeaderEntityId field — it was removed). Tick `FormationTargetSystem`.
   Assert: steering target is computed relative to the leader's transform.

**Important FDP submodule note:**
All changes to FDP source files (`Fdp.Toolkits`, `Fdp.Toolkits.Tests`) must be made in the
FDP git submodule (`d:\Work\IOS-IG-SimHost-FDP-2\FDP\`). Commit changes there separately, then
update the submodule pointer in the parent repo commit.

---

### CS016 — Create UnitHierarchySystem

**Full spec:** `.dev/commander-subordinates/TASK-DETAIL.md` §TASK-CS016  
**Design ref:** `.dev/commander-subordinates/DESIGN.md` §5.2, 5.3

**File to create:** `Hrot/Engine/Hrot.Common/Systems/UnitHierarchySystem.cs`  
**Namespace:** `Hrot.Common.Systems`

**Attribute:** `[UpdateInPhase(SystemPhase.Simulation)]`  
**Implements:** `IEcsModuleSystem`  
**Execute guard:** `if (view is not EntityRepository repo) return;`

**Per-tick logic (in this order):**

1. **Destruction cascade:** Drain `DestructionOrder` events (EventId 9003).
   For each destroyed entity:
   - If it has `UnitRoster`: iterate its subordinates, call `RemoveFromHierarchy(repo, sub)`
     on each live subordinate (skip dead ones). Call `SmartEgressUtil.MarkDirty(repo, sub,
     EntityInfoDescriptorOrdinal)` for each cleaned subordinate.
   - If it is itself a subordinate (has `UnitSubordinate`): call `RemoveFromHierarchy(repo, entity)`.

2. **Removals:** Drain `CmdRemoveSubordinate` events.
   Call `RemoveFromHierarchy(repo, event.Subordinate)`. Call
   `SmartEgressUtil.MarkDirty(repo, event.Subordinate, EntityInfoDescriptorOrdinal)`.

3. **Assignments:** Drain `CmdAssignSubordinate` events.
   - Liveness: skip if subordinate or commander is dead.
   - If subordinate already has `UnitSubordinate` pointing to a different commander:
     call `RemoveFromHierarchy(repo, subordinate)` first.
   - Capacity check: if the commander's `UnitRoster.Count >= UnitRoster.Capacity`:
     publish `CmdAssignSubordinateRejected { Subordinate = event.Subordinate }` and return
     without writing anything.
   - Atomic writes (all succeed or none):
     a. Set `UnitSubordinate { Commander = event.Commander, Designation = event.Designation }`.
     b. Add subordinate to `UnitRoster.SubordinateEntities[Count]` + record designation,
        increment Count, set back on commander.
     c. If `event.HasFormationSlot == 1`: write `FormationFollower { SlotIndex = event.SlotIndex }`.
   - Call `SmartEgressUtil.MarkDirty(repo, subordinate, EntityInfoDescriptorOrdinal)`.

**`EntityInfoDescriptorOrdinal` constant:**
```csharp
private const long EntityInfoDescriptorOrdinal = (long)EDescriptorType.dtEntityInfo;  // = 1L
```
Import `using Hrot.NED.Descriptors;` for `EDescriptorType` and
`using Hrot.Network.NED.Replication;` for `SmartEgressUtil`. Check existing usages in
`MissionControlExecutionSystem.cs` for the exact import pattern used in Hrot.Common.

**`RemoveFromHierarchy` (private static helper):**
```csharp
private static void RemoveFromHierarchy(EntityRepository repo, Entity subordinate)
```
- Read `UnitSubordinate.Commander` from the subordinate.
- Find the subordinate's slot in `commander.UnitRoster.SubordinateEntities` (linear scan).
- Order-preserving left-shift using a `for` loop over the fixed buffer. DO NOT use
  `System.Array.Copy` — the `fixed` buffer is an unmanaged pointer.
- Zero the last slot, decrement Count, write back the roster.
- `repo.RemoveComponent<UnitSubordinate>(subordinate)`.
- If `repo.HasComponent<FormationFollower>(subordinate)`: `repo.RemoveComponent<FormationFollower>(subordinate)`.

**Registering UnitHierarchySystem in logic packs:**

Registration must happen in EXACTLY FOUR places:

1. **SimHostCoreLogicPack** (`Hrot/Subsystems/Hrot.SimHost/SimHostCoreLogicPack.cs`):
   Add `_unitHierarchySystem = new UnitHierarchySystem();` field, add to `simList`.
   Use `using Hrot.Common.Systems;`.

2. **CgfLogicPack** (`Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs`):
   Add `_unitHierarchySystem = new UnitHierarchySystem();` field, add to `simList`.

3. **EditorSubsystem** (`Hrot/Subsystems/Hrot.Editor/EditorSubsystem.cs`):
   The EditorSubsystem already merges SimHostCoreLogicPack and CgfLogicPack simulation systems
   into `EditorSimulationModule`. If you add UnitHierarchySystem to BOTH packs above, it will
   be registered TWICE in the editor. To avoid this, add the editor registration separately:
   after the `EditorSimulationModule` registration line, add:
   `_kernel.RegisterModule(new SingleSystemModule(new UnitHierarchySystem()));`
   BUT do NOT add UnitHierarchySystem to SimHostCoreLogicPack.simList and CgfLogicPack.simList
   if this would cause double-registration in the editor.
   
   **Safest approach for EditorSubsystem:** Check whether the existing `EditorSimulationModule`
   would run it twice. If so, add a dedicated module registration ONLY in EditorSubsystem and
   NOT in the two packs. If the EditorSubsystem runs brain+muscle as separate process, add to
   both packs without worrying about editor. Look at the actual EditorSimulationModule code to
   decide. The constraint is: exactly ONE UnitHierarchySystem instance must execute per world tick.

4. **IgApplication** (`Hrot/Subsystems/Hrot.IG/IgApplication.cs`):
   There is no IgLogicPack class. Register directly via a lightweight module wrapper.
   Create a private sealed inner class (similar to `GhostDestructionSystem` inner class pattern):
   ```csharp
   // Registers UnitHierarchySystem for local ECS hierarchy maintenance on IG nodes.
   _kernel.RegisterModule(new SingleSystemModule(new UnitHierarchySystem()));
   ```
   Or create a minimal `UnitHierarchyModule : IEcsModule` helper class in Hrot.IG if needed.
   Find a natural place near the other Common systems registration.

**Tests (12 tests) — location: new file `Hrot/Engine/Hrot.Common.Tests/UnitHierarchySystemTests.cs`
or in `Hrot/Engine/Hrot.Core.Tests/CommandHierarchyTests.cs` if that project already references
all needed types. Pick the project that has access to both UnitHierarchySystem AND CarKinem.Formation.**

Write all 12 success conditions listed in TASK-DETAIL §TASK-CS016. See that file for the exact
setup/assert steps.

---

### CS012 — InitialUnitSubordinateIntent Component

**Full spec:** `.dev/commander-subordinates/TASK-DETAIL.md` §TASK-CS012  
**Design ref:** `.dev/commander-subordinates/DESIGN.md` §4.1

**What to add** to `Hrot/Engine/Hrot.Common/Serializers/GenesisIntentComponents.cs`:

```csharp
/// <summary>
/// Managed intent DTO that stores the network commander ID and tactical designation
/// for a subordinate entity during scenario genesis.
///
/// Written by <c>UnitSubordinateTranslator.Inject</c>; resolved to a live
/// <see cref="Hrot.Core.CommandHierarchy.UnitSubordinate"/> by
/// <c>GenesisMaterializationSystem</c>.
/// </summary>
[DataPolicy(DataPolicy.Transient)]
[ComponentId(HrotComponentIds.InitialUnitSubordinateIntent)]
public sealed class InitialUnitSubordinateIntent
{
    /// <summary>Network ID of the commander entity at scenario-load time.</summary>
    public long CommanderNetworkId { get; set; }

    /// <summary>Tactical designation of this subordinate within the commander's roster.</summary>
    public TacticalDesignation Designation { get; set; }
}
```

Add `using Hrot.Core.CommandHierarchy;` at the top of the file (for `TacticalDesignation`).

`HrotComponentIds.InitialUnitSubordinateIntent = 184` is already added (from BATCH-01). The ID
constant is present; only the class definition is missing.

**Registration** — add to `SimHostCoreLogicPack`:
```csharp
// In KinematicComponentRegistry.RegisterAll (or SimHostComponentRegistry.RegisterAll):
world.RegisterComponent<InitialUnitSubordinateIntent>();
```
Check which registry currently registers the other intent DTO components (e.g.
`InitialPassengersIntent`). Register in the same place.

**Tests** (location: `Hrot/Engine/Hrot.Core.Tests/CommandHierarchyTests.cs` or a new
`Hrot/Engine/Hrot.Common.Tests/GenesisIntentComponentsTests.cs`):

1. `InitialUnitSubordinateIntent_HasTransientDataPolicy`:
   `typeof(InitialUnitSubordinateIntent).GetCustomAttribute<DataPolicyAttribute>().Value == DataPolicy.Transient`

2. `InitialUnitSubordinateIntent_RoundTripsViaJson`:
   Serialize `new InitialUnitSubordinateIntent { CommanderNetworkId = 99, Designation = TacticalDesignation.Wingman }`
   to JSON and back; assert both fields preserved.

---

### CS022 — TkbChildSlot: Replace RoleTag with Designation

**Full spec:** `.dev/commander-subordinates/TASK-DETAIL.md` §TASK-CS022  
**Design ref:** `.dev/commander-subordinates/DESIGN.md` §7.1, 7.2

**What to do:**

1. Open `Hrot/Engine/Hrot.Core/MapDefinitions/Tkb/TkbCompositionDef.cs`.
   Locate `TkbChildSlot`. Replace `public string RoleTag` with
   `public TacticalDesignation Designation`.
   Add `using Hrot.Core.CommandHierarchy;` if needed.

2. Search for all construction sites of `TkbChildSlot` (in TKB catalog / builder code,
   tests, and any scenario files). Update `RoleTag = "..."` → `Designation = TacticalDesignation.X`
   (use `Undefined` if there is no meaningful mapping).

3. Locate the composite-spawning system that creates child entities from `TkbChildSlot`.
   Find where it reads `RoleTag`; replace that logic to attach `InitialUnitSubordinateIntent`
   when `Designation != TacticalDesignation.Undefined`:
   ```csharp
   if (slot.Designation != TacticalDesignation.Undefined)
   {
       repo.AddComponent(childEntity, new InitialUnitSubordinateIntent
       {
           CommanderNetworkId = commanderNetworkId,
           Designation        = slot.Designation,
       });
   }
   ```
   The `commanderNetworkId` should come from the commander entity's `NetworkIdentity` component
   (or equivalent field already used in the spawning system).

**Constraints:**
- `RoleTag` must not remain anywhere in `TkbChildSlot` or its usages.
- Do NOT attach `UnitSubordinate` or publish `CmdAssignSubordinate` from the spawner —
  entities are not fully alive at spawn time.

**Tests:**
1. `TkbChildSlot_HasNoRoleTagField` — compile check only (ensure no `RoleTag` in struct).
2. `TkbChildSlot_Designation_DefaultIsUndefined` — `new TkbChildSlot().Designation == TacticalDesignation.Undefined`.
3. `CompositeSpawn_ChildWithDesignation_AttachesInitialUnitSubordinateIntent`:
   Spawn a commander + 1 child slot with `Designation = TacticalDesignation.Wingman`.
   Assert the spawned child entity has `InitialUnitSubordinateIntent { Designation = Wingman }` attached.

---

## Implementation Order

1. **CT-1** — move command hierarchy types to Fdp.Core (unblocks everything — do this FIRST)
2. **CT-0** — remove `LeaderEntity` field from FormationFollower (depends on CT-1)
3. **CS007** — update VehicleCommandSystem + FormationTargetSystem (depends on CT-0 and CT-1)
4. **CS012** — add InitialUnitSubordinateIntent class (independent, can be done after CT-1)
5. **CS022** — TkbChildSlot replacement (depends on CS012 for InitialUnitSubordinateIntent)
6. **CS016** — UnitHierarchySystem (depends on CT-1; complex, can be done in parallel with CS012 + CS022)

---

## FDP Submodule Reminder

`FormationFollower.cs`, `VehicleCommandSystem.cs`, `FormationTargetSystem.cs`, and related test
files all live inside the `FDP/` git submodule (`d:\Work\IOS-IG-SimHost-FDP-2\FDP\`). Changes
there must be committed in that repo. After committing in FDP, also update the submodule pointer
in the parent repo.

---

## Deliverables

When done, file `.dev/commander-subordinates/reports/BATCH-02-REPORT.md` using the standard
report template (see any previous batch report for the structure). Include:
1. Completion status for CT-0, CS007, CS012, CS022, CS016
2. Any design deviations
3. Build result (must be 0 errors)
4. Test counts (new tests added, total pass/fail)
5. Suggested commit message

---

## Definition of Done

- Build: 0 errors
- CT-0: `FormationFollower` has no `LeaderEntity` field
- CS007: `VehicleCommandSystem` publishes events only (never writes ECS directly); 4+ new tests pass
- CS016: `UnitHierarchySystem` created with 12 tests; registered in all 4 node types
- CS012: `InitialUnitSubordinateIntent` class exists with correct attributes; 2 tests pass
- CS022: `TkbChildSlot` has `Designation`, no `RoleTag`; 3 tests pass
- No new test failures introduced
