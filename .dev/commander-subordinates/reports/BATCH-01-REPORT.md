# BATCH-01 Report

**Batch:** BATCH-01 — Foundational Components, Events, Formation Renames
**Tasks:** CS001, CS002, CS003, CS004, CS015, CS005, CS006
**Status:** COMPLETE

---

## 1. Task Completion Status

| Task | Description | Status |
|------|-------------|--------|
| CS001 | TacticalDesignation dual-enum + TacticalDesignationMapper | COMPLETE (pre-existing) |
| CS015 | CmdAssignSubordinate / CmdRemoveSubordinate / CmdAssignSubordinateRejected events | COMPLETE (pre-existing) |
| CS002 | UnitSubordinate component | COMPLETE (pre-existing, size comment fixed) |
| CS003 | UnitRoster unsafe component | COMPLETE (pre-existing, size comment fixed) |
| CS004 | HrotComponentIds constants + component registrations | COMPLETE (registrations added this batch) |
| CS005 | FormationRoster renamed to FormationController | COMPLETE (pre-existing) |
| CS006 | FormationMember renamed to FormationFollower | COMPLETE (pre-existing, note below) |

Most type definitions (CS001-CS003, CS015, CS005, CS006) and the HrotComponentIds constants were
already created in a prior partial session and committed. This batch session's work:

1. Identified and fixed **wrong size assertions** in `CommandHierarchyTests.cs` (see Design Decisions).
2. Fixed **wrong size comments** in `UnitSubordinate.cs` and `UnitRoster.cs`.
3. Added **UnitRoster and UnitSubordinate component registrations** to `SimHostComponentRegistry`.
4. Created **TacticalDesignationMapperTests.cs** in `Hrot.Network.NED.Tests` (cross-enum + mapper roundtrip).
5. Added **CS004 integration test** in `SimHostCoreLogicPackTests.cs`.
6. Added `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` to `Hrot.Core.Tests.csproj` (required for unsafe tests).

---

## 2. Test Results

### Hrot.Core.Tests
```
Passed! - Failed: 0, Passed: 119, Skipped: 0, Total: 119
```
- 18 CommandHierarchy tests (CS001-CS004, CS015) all pass.
- 1 ComponentIdTests.HrotComponentIds_NoDuplicates passes.

### Hrot.Network.NED.Tests
```
Passed! - Failed: 0, Passed: 65, Skipped: 0, Total: 65
```
- 7 new TacticalDesignationMapperTests tests pass (cross-enum parity + mapper roundtrips).

### Fdp.Toolkits.Tests (formation rename coverage)
```
Formation filter: Passed! - Failed: 0, Passed: 18, Skipped: 0, Total: 18
```
- FormationController and FormationFollower component ID tests pass.
- JoinFormation_SetsFormationFollowerAndMode passes.

### Hrot.SimHost.Tests
```
Passed! - Failed: 2, Passed: 473, Skipped: 3, Total: 478
```
- New integration test `SimHostComponentRegistry_RegisterAll_ProvidesUnitHierarchyComponentTables` passes.
- 2 pre-existing failures (MissionPlanTranslatorTests) confirmed pre-existing by baseline check.

**Total new tests added: 26** (18 CommandHierarchy + 7 mapper + 1 integration)

---

## 3. Issues Encountered

### Issue 1: Wrong size values in test and comments
The batch spec stated `UnitSubordinate` = 16 bytes and `UnitRoster` = 164 bytes. Actual runtime
values are **12 bytes** and **168 bytes** respectively.

Root cause: The spec assumed `Entity` is `int Index + uint Generation` (8 bytes with 8-byte
alignment). The actual `Entity` struct is `int Index (4B) + ushort Generation (2B) + 2B pad = 8B`
with **4-byte** alignment (max of int=4, ushort=2). This gives:

- `UnitSubordinate`: Entity(8B, 4-align) + TacticalDesignation(2B) + 2B pad = **12B**
- `UnitRoster`: Count(4B) + 4B align-pad before long[] + long[16](128B) + ushort[16](32B) = **168B**

Resolution: Updated test assertions and XML comments to use actual values. The struct definitions
themselves are correct; only the expected sizes in tests/comments were wrong.

### Issue 2: CS006 FormationFollower still has Entity LeaderEntity
The design specifies `FormationFollower` should have no leader reference (moved to `UnitSubordinate.
Commander`). However, `VehicleCommandSystem` and `FormationTargetSystem` still read
`FormationFollower.LeaderEntity`. Removing the field now would break CS007-territory consumers.

Resolution: Kept `Entity LeaderEntity` in `FormationFollower`. The original `int LeaderEntityId`
(generation-unsafe) was already upgraded to `Entity LeaderEntity` in the prior session. The full
removal is deferred to CS007 which updates VehicleCommandSystem and FormationTargetSystem.

### Issue 3: Mapper tests needed a project that references both Hrot.Core and Hrot.Network.NED
`Hrot.Core.Tests` only references `Hrot.Core` and `Fdp.Core`, not `Hrot.Network.NED`. Adding NED
to core test project violates the layering.

Resolution: Added mapper tests to `Hrot.Network.NED.Tests` (the dedicated NED test project) which
already references NED and transitively has access to `Hrot.Core` via NED's own project references.

---

## 4. Design Decisions

### Struct sizes (vs. spec)
The spec numbers (16B, 164B) were based on incorrect Entity field type assumptions. Corrected to
match runtime reality (12B, 168B). No downstream consequences since no existing code serializes
these components at fixed offsets.

### UnitRoster alignment padding
The 4 bytes of padding between `int Count` and `fixed long SubordinateEntities[Capacity]` exist
because `long` requires 8-byte alignment in the CLR's sequential layout. If exact 164-byte size
is required in future, it can be achieved with `[StructLayout(LayoutKind.Sequential, Pack = 4)]`,
but this could cause unaligned reads on ARM. Current 168B is safe.

### TacticalDesignationMapper location
The mapper static class is in the `Hrot.Map.Common.Replication` namespace within the
`Hrot.Network.NED` project, matching the pattern of other ACL mappers in that project
(e.g., ClampingModeMapper, NavigationModeMapper).

### CS004 integration test location
Placed in `SimHostCoreLogicPackTests.cs` (Hrot.SimHost.Tests) rather than Hrot.Core.Tests,
because verifying that `SimHostComponentRegistry.RegisterAll` registers the components requires
a reference to `Hrot.SimHost`, which Core.Tests does not have.

---

## 5. Weak Points Spotted

- `SimHostCoreLogicPackTests.CreateEmptyWorld()` has 30+ manual component registrations that will
  drift from `SimHostComponentRegistry.RegisterAll` over time. A comment linking the two would
  prevent silent mismatches in test coverage.
- The spec's size calculations for struct layout should have accounted for `alignof(long)=8` causing
  padding before fixed arrays. Worth noting in future task specs.

---

## 6. Edge Cases Discovered

- `Entity` has `ushort Generation` (not `uint` as the batch spec assumed). With 4-byte struct
  alignment, this means `Marshal.SizeOf<Entity>()` = 8 (4+2+2pad), not 8 with 8-byte alignment.
  Any code that assumes 8-byte-aligned Entity will behave correctly but may confuse reviewers
  who count on the "Entity = 8B with 8-byte alignment" mental model.
- The boundary write test for `UnitRoster` is sensitive to the actual struct size. Using the
  literal 164 caused the test to write beyond the struct boundary (into guard bytes) which would
  corrupt memory silently rather than catching the overflow. Fixed by using constant `structSize = 168`.

---

## 7. Suggested Commit Message

```
feat(commander-subordinates): BATCH-01 foundational components, events, formation renames (CS001-CS006, CS015)

CS001: TacticalDesignation dual-enum (Hrot.Core + NED), TacticalDesignationMapper (cast-only)
CS015: CmdAssignSubordinate (2200), CmdRemoveSubordinate (2201), CmdAssignSubordinateRejected (2202)
CS002: UnitSubordinate (Entity Commander + TacticalDesignation Designation, 12 B, ID 183)
CS003: UnitRoster unsafe (fixed long[16] + ushort[16], 168 B, ID 182, NoSave)
CS004: HrotComponentIds 182/183/184; register UnitRoster+UnitSubordinate in SimHostComponentRegistry
CS005: FormationRoster -> FormationController (ID 33, no member arrays)
CS006: FormationMember -> FormationFollower (ID 45, Entity LeaderEntity kept pending CS007)

26 new tests added. Build: 0 errors. Hrot.Core.Tests 119/119, NED.Tests 65/65.
```
