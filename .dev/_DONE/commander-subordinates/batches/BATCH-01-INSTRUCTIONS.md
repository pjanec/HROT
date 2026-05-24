# BATCH-01: Foundational Components, Events, and Formation Renames

**Batch Number:** BATCH-01
**Tasks:** TASK-CS001, TASK-CS002, TASK-CS003, TASK-CS004, TASK-CS015, TASK-CS005, TASK-CS006
**Phase:** Phase 1 (Core Components) + Phase 5.1 (Command Events) + Phase 2 partial (Renames)
**Estimated Effort:** 12-16 hours
**Priority:** HIGH — foundational; all other batches depend on this
**Dependencies:** None (first batch)

---

## 📋 Onboarding & Workflow

### Required Reading (IN ORDER)
1. **Design Document:** `.dev/commander-subordinates/DESIGN.md` — Read Phase 1, Phase 2.1, Phase 2.2, Phase 5.1 sections
2. **Task Definitions:** `.dev/commander-subordinates/TASK-DETAIL.md` — Read TASK-CS001, CS002, CS003, CS004, CS015, CS005, CS006 in full

### Source Code Locations
- **New components (Phase 1):** `Hrot/Engine/Hrot.Core/CommandHierarchy/` (create this folder)
- **Event structs (Phase 5.1):** `Hrot/Engine/Hrot.Core/CommandHierarchy/CommandHierarchyEvents.cs` (create)
- **Component IDs:** `Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs`
- **DDS enum + mapper:** `Hrot/Network/Hrot.Network.NED/GenericDescriptors.cs` (extend) and `Hrot/Network/Hrot.Network.NED/Replication/Map/TacticalDesignationMapper.cs` (create)
- **Rename (Formation):** `FDP/Toolkits/Fdp.Toolkits/CarKinem/Formation/FormationRoster.cs` → rename struct to `FormationController`, `FDP/Toolkits/Fdp.Toolkits/CarKinem/Formation/FormationMember.cs` → rename struct to `FormationFollower`
- **Component ID constants (Fdp.Core):** `FDP/Engine/Fdp.Core/GlobalComponentIds.cs`
- **SimHost registry:** `Hrot/Subsystems/Hrot.SimHost/SimHostComponentRegistry.cs` (or `SimHostCoreLogicPack`)
- **Tests:** `FDP/Toolkits/Fdp.Toolkits.Tests/`, `Hrot/Engine/Hrot.Core.Tests/`

### Report Submission
**When done, submit your report to:**
`.dev/commander-subordinates/reports/BATCH-01-REPORT.md`

**If you have questions, create:**
`.dev/commander-subordinates/questions/BATCH-01-QUESTIONS.md`

---

## Context

This is the first batch of the Commander-Subordinate Infrastructure workstream.  
See `.dev/commander-subordinates/DESIGN.md` for the full architectural picture.

This batch lays the foundation that all other batches build on:
- New ECS component types (`UnitSubordinate`, `UnitRoster`, `TacticalDesignation`)
- The command events used to mutate the hierarchy (`CmdAssignSubordinate`, `CmdRemoveSubordinate`, `CmdAssignSubordinateRejected`)
- Renaming existing formation components (`FormationRoster` → `FormationController`, `FormationMember` → `FormationFollower`) so the structural split described in the design is reflected in code

**Related Tasks:**
- [TASK-CS001](../TASK-DETAIL.md#task-cs001--tacticaldesignation-dual-enum-definitions) — dual-enum TacticalDesignation
- [TASK-CS002](../TASK-DETAIL.md#task-cs002--unitsubordinate-component) — UnitSubordinate component
- [TASK-CS003](../TASK-DETAIL.md#task-cs003--unitroster-component) — UnitRoster component
- [TASK-CS004](../TASK-DETAIL.md#task-cs004--component-id-registration) — component ID registration
- [TASK-CS015](../TASK-DETAIL.md#task-cs015--cmdassignsubordinate-and-cmdremovesubordinate-events) — command events
- [TASK-CS005](../TASK-DETAIL.md#task-cs005--rename-formationroster-to-formationcontroller) — FormationRoster → FormationController
- [TASK-CS006](../TASK-DETAIL.md#task-cs006--rename-formationmember-to-formationfollower) — FormationMember → FormationFollower

---

## 🎯 Batch Objectives

Produce all the new C# type definitions needed for subsequent batches to compile and integrate:
1. Dual-enum `TacticalDesignation` / `eTacticalDesignation` with a mapper
2. Blittable `UnitSubordinate` and `UnitRoster` structs registered in the ECS world
3. Unmanaged command event structs for the hierarchy event bus
4. Renamed formation components (remove dead fields, keep IDs)

After this batch the solution must build without errors and all existing tests must still pass.

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **CS001 → CS015:** Enums + events (no component deps) → all tests pass
2. **CS002 → CS003 → CS004:** Components + registration → all tests pass
3. **CS005 → CS006:** Formation renames → all tests pass

**DO NOT** move to the next task until:
- Current task implementation complete
- Current task tests written
- **ALL tests passing** (including previous task tests)

**After EVERY task** run:
```
dotnet build IOS-IG-SimHost.sln --no-restore -v quiet
```
Fix any error CS before proceeding. No batch is done until `Build succeeded` and all tests pass.

---

## ✅ Tasks

### Task 1: TacticalDesignation Dual-Enum Definitions (TASK-CS001)

**Task Definition:** See [TASK-DETAIL.md §TASK-CS001](../TASK-DETAIL.md#task-cs001--tacticaldesignation-dual-enum-definitions)

**Files to create/modify:**
- NEW: `Hrot/Engine/Hrot.Core/CommandHierarchy/TacticalDesignation.cs`
- MODIFY: `Hrot/Network/Hrot.Network.NED/GenericDescriptors.cs` — add `eTacticalDesignation` enum
- NEW: `Hrot/Network/Hrot.Network.NED/Replication/Map/TacticalDesignationMapper.cs`

**Key constraints** (details in TASK-DETAIL.md):
- Both enums derive from `ushort`; values must be identical (Undefined=0..Support=4)
- Sync comment on both: `/// IMPORTANT: Must be kept in sync with ...`
- Mapper uses only casts — no lookup tables

**Tests required** (in `Hrot.Core.Tests` or `Hrot.Network.NED.Tests` — whichever exists):
- Verify `(ushort)TacticalDesignation.SquadLeader == (ushort)eTacticalDesignation.SquadLeader`
- Verify mapper round-trips (ToDds, ToEcs)
- Verify `default(TacticalDesignation) == TacticalDesignation.Undefined`

---

### Task 2: CmdAssignSubordinate and CmdRemoveSubordinate Events (TASK-CS015)

**Task Definition:** See [TASK-DETAIL.md §TASK-CS015](../TASK-DETAIL.md#task-cs015--cmdassignsubordinate-and-cmdremovesubordinate-events)

**Files to create:**
- NEW: `Hrot/Engine/Hrot.Core/CommandHierarchy/CommandHierarchyEvents.cs`

**Key constraints** (details in TASK-DETAIL.md):
- All three event structs must be `unmanaged` value types
- Use event IDs in range **2200–2299** (check no conflicts in existing code first)
- `CmdAssignSubordinate` must carry: `Entity Subordinate`, `Entity Commander`, `TacticalDesignation Designation`, `byte HasFormationSlot`, `ushort SlotIndex`
- `CmdRemoveSubordinate` must carry only: `Entity Subordinate`
- `CmdAssignSubordinateRejected` must carry only: `Entity Subordinate`

**Tests required:**
- All three structs satisfy `where T : unmanaged` constraint
- Event IDs are distinct from each other and from known IDs (2104, 2105, 9003, etc.)
- `CmdAssignSubordinateRejected` is unmanaged and has only `Entity Subordinate` field

---

### Task 3: UnitSubordinate Component (TASK-CS002)

**Task Definition:** See [TASK-DETAIL.md §TASK-CS002](../TASK-DETAIL.md#task-cs002--unitsubordinate-component)

**Files to create/modify:**
- NEW: `Hrot/Engine/Hrot.Core/CommandHierarchy/UnitSubordinate.cs`
- MODIFY: `Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs` — add `UnitSubordinate = 183`
- MODIFY: SimHost component registry (locate via `grep_search "RegisterComponent" Hrot/Subsystems/Hrot.SimHost`)

**Key constraints** (details in TASK-DETAIL.md):
- `Commander` field is `Fdp.Core.Entity` (8 bytes, generation-safe) — NOT `int`
- Must have `[StructLayout(LayoutKind.Sequential)]` and `[ComponentId(HrotComponentIds.UnitSubordinate)]`
- `Entity.Null` is the valid "no commander" sentinel
- `Marshal.SizeOf<UnitSubordinate>()` must equal **16 bytes** (Entity=8, Designation=ushort=2, padding=6)

**Tests required:**
- `Marshal.SizeOf<UnitSubordinate>() == 16`
- `ComponentId` attribute value equals `HrotComponentIds.UnitSubordinate` (183)
- Fresh `EntityRepository`: register + `GetComponentTable<UnitSubordinate>() != null`
- `new UnitSubordinate().Commander == Entity.Null` and `.Designation == TacticalDesignation.Undefined`

---

### Task 4: UnitRoster Component (TASK-CS003)

**Task Definition:** See [TASK-DETAIL.md §TASK-CS003](../TASK-DETAIL.md#task-cs003--unitroster-component)

**Files to create/modify:**
- NEW: `Hrot/Engine/Hrot.Core/CommandHierarchy/UnitRoster.cs`
- MODIFY: `Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs` — add `UnitRoster = 182`
- MODIFY: SimHost component registry — register `UnitRoster`

**Key constraints** (details in TASK-DETAIL.md):
- Must be `unsafe struct`
- Must carry `[DataPolicy(DataPolicy.NoSave)]`
- `public const int Capacity = 16` inside the struct
- `fixed long SubordinateEntities[Capacity]` and `fixed ushort TacticalDesignations[Capacity]`
- `sizeof(UnitRoster)` must equal **164 bytes** = 4 (Count) + 128 (16*8) + 32 (16*2)

**Tests required:**
- `Unsafe.SizeOf<UnitRoster>() == 164`
- `DataPolicy` attribute has `NoSave` set
- `UnitRoster.Capacity == 16`
- Boundary write test: writing to index 15 does not corrupt adjacent memory

---

### Task 5: Component ID Registration (TASK-CS004)

**Task Definition:** See [TASK-DETAIL.md §TASK-CS004](../TASK-DETAIL.md#task-cs004--component-id-registration)

**Files to modify:**
- `Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs` — verify IDs 182, 183, 184 are added with doc comments matching existing format
- All ECS world setups that register components: SimHostCoreLogicPack (already done in Task 3/4), EditorSubsystem, Editor integration-test harness (search for `RegisterComponent<` in Editor tests)

**Note:** `InitialUnitSubordinateIntent = 184` is added here as a constant only; the class itself is created in a later batch (TASK-CS012). Add the constant but do NOT add the registration call yet.

**Tests required:**
- All `public const byte` fields in `HrotComponentIds` have unique values (add or extend test in `Hrot.Core.Tests`)
- Integration assertion: SimHostCoreLogicPack world has `UnitRoster` and `UnitSubordinate` tables non-null

---

### Task 6: Rename FormationRoster → FormationController (TASK-CS005)

**Task Definition:** See [TASK-DETAIL.md §TASK-CS005](../TASK-DETAIL.md#task-cs005--rename-formationroster-to-formationcontroller)

**Files to modify:**
- `FDP/Toolkits/Fdp.Toolkits/CarKinem/Formation/FormationRoster.cs` — rename struct, remove `Count`, `MemberEntities[16]`, `SlotIndices[16]` fields; rename file to `FormationController.cs`
- `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` — rename constant `FormationRoster = 33` to `FormationController = 33`
- All usages across `FDP/Toolkits/Fdp.Toolkits/` and `FDP/Toolkits/Fdp.Toolkits.Tests/`

**Key constraints:**
- Component ID value stays 33 (no change to the number)
- Remaining fields preserved: `TemplateId`, `Type` (`FormationType`), `Params` (`FormationParams`)
- All existing tests that reference `FormationRoster` must be updated to `FormationController`
- After the rename, zero references to `FormationRoster` must remain in `*.cs` files

**Tests required:**
- `typeof(FormationController).GetCustomAttribute<ComponentIdAttribute>().Id == 33`
- All existing `FormationRoster` tests renamed and passing

---

### Task 7: Rename FormationMember → FormationFollower (TASK-CS006)

**Task Definition:** See [TASK-DETAIL.md §TASK-CS006](../TASK-DETAIL.md#task-cs006--rename-formationmember-to-formationfollower)

**Files to modify:**
- `FDP/Toolkits/Fdp.Toolkits/CarKinem/Formation/FormationMember.cs` — rename struct to `FormationFollower`, remove `LeaderEntityId` field; rename file to `FormationFollower.cs`
- `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` — rename constant `FormationMember = 45` to `FormationFollower = 45`
- All usages across `FDP/Toolkits/Fdp.Toolkits/` and `FDP/Toolkits/Fdp.Toolkits.Tests/`

**Key constraints:**
- Component ID value stays 45
- Fields `SlotIndex`, `State` (`FormationMemberState`), `IsInFormation`, `SlotDistFiltered`, `RejoinTimer` preserved
- `LeaderEntityId` field removed entirely (commander link moves to `UnitSubordinate.Commander`)
- Rename the test `JoinFormation_SetsFormationMemberAndMode` to `JoinFormation_SetsFormationFollowerAndMode`
- After rename, zero references to `FormationMember` in `*.cs` files

**Tests required:**
- `typeof(FormationFollower).GetCustomAttribute<ComponentIdAttribute>().Id == 45`
- Renamed test `JoinFormation_SetsFormationFollowerAndMode` passes

---

## 🧪 Testing Requirements

- **Minimum:** 15 unit tests total across all tasks
- Build must succeed: `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet` → `Build succeeded`
- Run tests with: `dotnet test IOS-IG-SimHost.sln --no-build --nologo`
- All pre-existing tests must continue to pass (no regressions)

**Test quality standards:**
- Tests must verify actual values (sizes, IDs, attribute values) — not just "object exists"
- Component size tests must use exact expected byte counts
- Event ID uniqueness test must check against all known IDs in the codebase

---

## 📊 Report Requirements

Create `.dev/commander-subordinates/reports/BATCH-01-REPORT.md` with:

1. **Task completion status** — which tasks completed
2. **Test results** — total tests run and passed
3. **Issues Encountered** — what problems arose, how you resolved them
4. **Design Decisions** — any choices made beyond the spec
5. **Weak Points Spotted** — anything in the existing codebase worth noting
6. **Edge Cases Discovered** — scenarios not in the spec
7. **Suggested commit message** — what this batch accomplished

---

## 🎯 Success Criteria

- [ ] `TacticalDesignation` and `eTacticalDesignation` enums exist with matching values
- [ ] `TacticalDesignationMapper` with `ToDds` and `ToEcs` methods
- [ ] `UnitSubordinate` struct with `Entity Commander` and `TacticalDesignation Designation`; size = 16 bytes
- [ ] `UnitRoster` unsafe struct with fixed buffers; size = 164 bytes
- [ ] `HrotComponentIds.UnitRoster = 182`, `UnitSubordinate = 183`, `InitialUnitSubordinateIntent = 184` (constant only)
- [ ] Both components registered in SimHostCoreLogicPack and EditorSubsystem
- [ ] `CmdAssignSubordinate`, `CmdRemoveSubordinate`, `CmdAssignSubordinateRejected` event structs
- [ ] `FormationRoster` renamed to `FormationController` (no `Count`, `MemberEntities`, `SlotIndices`)
- [ ] `FormationMember` renamed to `FormationFollower` (no `LeaderEntityId`)
- [ ] Build succeeds; all existing tests pass; new unit tests pass

---

## ⚠️ Common Pitfalls

- `UnitSubordinate` size: `Entity` is 8 bytes (Index `int` + Generation `uint`). C# aligns `ushort` to 2 bytes within the struct, giving total 10 bytes of data but **16 bytes aligned** due to the 8-byte Entity field. Test for 16, not 10.
- `UnitRoster` must be `unsafe` (required for `fixed` buffers). The project file for `Hrot.Core` must allow unsafe code (check `.csproj` for `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` — add it if missing).
- When renaming `FormationRoster` → `FormationController`: the struct file can be renamed, but beware of all the compilation errors that will cascade across tests and usages. Fix them all before committing.
- `CmdAssignSubordinate` must use `byte HasFormationSlot` (not `bool`) to remain unmanaged.

---

## 📚 Reference Materials
- **Design:** `.dev/commander-subordinates/DESIGN.md` — Phase 1 (§1.1–1.4), Phase 2 (§2.1–2.2), Phase 5 (§5.1)
- **Task Details:** `.dev/commander-subordinates/TASK-DETAIL.md` — TASK-CS001 through CS006, CS015
- **Existing component example:** `Hrot/Engine/Hrot.Core/Components/Map/EntityInfo.cs`
- **Existing event example:** `FDP/Toolkits/Fdp.Toolkits/CarKinem/Commands/CommandEvents.cs`
- **Existing fixed-buffer struct example:** `FDP/Toolkits/Fdp.Toolkits/CarKinem/Formation/FormationRoster.cs` (current file, before rename)
- **HrotComponentIds:** `Hrot/Engine/Hrot.Core/MapDefinitions/HrotComponentIds.cs`
- **GlobalComponentIds:** `FDP/Engine/Fdp.Core/GlobalComponentIds.cs`
