# BATCH-01 Review — Commander-Subordinate Foundation

**Reviewer:** Dev Lead  
**Date:** 2025-07-14  
**Batch report:** `.dev/commander-subordinates/reports/BATCH-01-REPORT.md`  
**Status: APPROVED WITH CORRECTIVE TASK**

---

## Overall Assessment

BATCH-01 is approved. The foundational components, events, and formation renames are in place,
the build is green, and 26 new tests all pass. One design deviation in CS006 (FormationFollower
still carries a leader reference) is accepted as a pragmatic bridge — it keeps the build
compilable while VehicleCommandSystem and FormationTargetSystem are migrated in CS007.
The corrective task below mandates the removal of that field at the start of BATCH-02.

---

## Task-by-Task Results

| Task  | Status | Notes |
|-------|--------|-------|
| CS001 | PASS   | TacticalDesignation dual enum + TacticalDesignationMapper. 4 tests pass. |
| CS015 | PASS   | All three command events with correct EventIds (2200–2202). Struct tests pass. |
| CS002 | PASS   | UnitSubordinate (12 B actual; spec said 16 B — expected, see below). |
| CS003 | PASS   | UnitRoster (168 B actual; spec said 164 B — expected). Fixed-array boundary test correct. |
| CS004 | PASS   | HrotComponentIds 182/183/184 added; SimHostComponentRegistry registers both components. |
| CS005 | PASS   | FormationRoster → FormationController; FormationRosterExtensions removed; all consumers updated. |
| CS006 | PARTIAL | `Entity LeaderEntity` field kept in FormationFollower rather than removed. See CT-0. |

---

## Issues

### CT-0 (Corrective Task, P1) — FormationFollower.LeaderEntity must be removed

**Specification** (TASK-DETAIL §CS006):
> "Fields `SlotIndex`, `State`, `IsInFormation`, `SlotDistFiltered`, `RejoinTimer` must be
> preserved. `LeaderEntityId` field removed."

**DESIGN.md §2.2** lists FormationFollower fields as:
> `SlotIndex, State, IsInFormation, SlotDistFiltered, RejoinTimer` — no leader reference.

**What was done:** The developer converted `int LeaderEntityId` (generation-unsafe) to
`Entity LeaderEntity` to keep the build compilable. VehicleCommandSystem and FormationTargetSystem
still read this field.

**Resolution:** BATCH-02 must begin with CT-0: remove `FormationFollower.LeaderEntity` and
update all callers (`VehicleCommandSystem`, `FormationTargetSystem`) to compile without it.
CS007 (same batch) will then finish the migration by pointing those callers at
`UnitSubordinate.Commander`.

This sequencing is safe: CT-0 and CS007 are in the same batch, so no intermediate broken build.

---

## Notes on Struct Size Discrepancies

The spec's byte counts (UnitSubordinate=16 B, UnitRoster=164 B) were based on an incorrect
assumption that `Entity` has 8-byte alignment. The runtime layout is:

- `Entity` = `int Index` + `ushort Generation` = 6 bytes used, 8 bytes aligned to `int` (4B) → `sizeof(Entity) = 8`, `alignof(Entity) = 4`
- `UnitSubordinate` = `Entity`(8B) + `TacticalDesignation`(2B) + 2B pad = **12 B**
- `UnitRoster` = `int Count`(4B) + 4B pad (before `long` which needs 8B alignment) + `long[16]`(128B) + `ushort[16]`(32B) = **168 B**

The developer correctly identified this and updated the test assertions. The TASK-DETAIL spec
will remain as-is (design debt) — the tests are the authoritative source of truth for sizes.

---

## Pre-existing Test Failures

- `Hrot.SimHost.Tests/MissionPlanTranslatorTests` — 2 failures: pre-existing, confirmed via
  stash revert check.
- `Fdp.Toolkits.Tests` — 22 failures: confirmed pre-existing via stash revert check (same count
  before and after BATCH-01). Not introduced by this batch.

---

## Uncommitted-at-Report Time

Several files were committed in a follow-up commit after the BATCH-01 report was filed:

- `HrotComponentIds.cs` (IDs 182–184)
- `GenericDescriptors.cs` (eTacticalDesignation)
- `CgfComponentRegistry.cs`, `KinematicComponentRegistry.cs` (renamed type registrations)
- `SimHostInstance.cs`, `SimHostVehicleVisualizer.cs`, `ComponentRegistryTests.cs`
- FDP submodule pointer update

These are now committed. The dev lead committed them during review. No functional gaps remain.

---

## Approved Commits

- `8f02b25` — `feat(commander-subordinates): BATCH-01 foundational components...`
- `041171e` — `docs(commander-subordinates): BATCH-01 report`
- `94a2a18` — `refac(commander-subordinates): BATCH-01 formation renames...` (FDP)
- `0a821c8` — `refac(commander-subordinates): BATCH-01 remaining changes...`

---

## Next Batch

BATCH-02 must begin with **CT-0** (remove `FormationFollower.LeaderEntity`) before implementing
CS007, CS016, CS012, CS022. See BATCH-02 instructions.
