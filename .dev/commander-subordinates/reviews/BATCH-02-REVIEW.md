# BATCH-02 Review

**Status: APPROVED WITH CORRECTIVE TASK (CT-2)**

---

## Summary

All 6 tasks are complete. Build is clean (0 errors). 13 new `UnitHierarchySystem` tests pass in
`Hrot.SimHost.Tests`. Two pre-existing `MissionPlanTranslatorTests` failures remain unchanged.

One P1 regression was found: `UnitHierarchySystem` is registered in BOTH
`SimHostCoreLogicPack.simList` AND `CgfLogicPack.simList`, which causes it to execute twice per
tick inside `EditorSubsystem`. See CT-2 below. This must be fixed before BATCH-03 begins.

---

## Task Assessments

### CT-1 — Move CommandHierarchy types to Fdp.Core

**APPROVED.**

Correct fix for the circular-dependency blocker. Types are in the right namespace
(`Fdp.Core.CommandHierarchy`), `GlobalComponentIds` has the new IDs (182-184), and all consumers
were updated. `HrotComponentIds` retains alias values.

### CT-0 — Remove FormationFollower.LeaderEntity

**APPROVED.**

`FormationFollower.LeaderEntity` removed. `FormationTargetSystem` now reads leader from
`UnitSubordinate.Commander`. Tests updated.

### CS007 — VehicleCommandSystem / FormationTargetSystem

**APPROVED.**

`JoinFormation` publishes `CmdAssignSubordinate` (never writes `FormationFollower` directly).
`LeaveFormation` publishes `CmdRemoveSubordinate`. New `ProcessAssignSubordinateRejected` phase
reads rejection events and sets `LocomotionChannel.Status = NodeStatus.Failure`. Three new tests
cover the new behavior.

### CS016 — UnitHierarchySystem

**APPROVED (with CT-2 fix required).**

Processing order (destruction cascade → remove → assign) is correct. Atomic writes of both
`UnitSubordinate` and `UnitRoster` are correct. `RemoveFromHierarchy` uses an order-preserving
left-shift loop as required.

`IgApplication` registration via a private nested wrapper is acceptable. Registration in
`SimHostCoreLogicPack` and `CgfLogicPack` is the source of the P1 double-execution issue
described in CT-2.

13 tests cover all key scenarios: atomic write, capacity rejection, destruction cascade,
reassignment, dirty marking, and formation-slot integration.

### CS012 — InitialUnitSubordinateIntent

**APPROVED.**

`CommanderNetworkId` is correctly `long` and `Designation` is `TacticalDesignation`. Registered in
`SimHostComponentRegistry`. `CreateEntityRequestSystem` injects it for non-`Undefined` blueprint
slots. `DataPolicy.Transient` attribute is present.

### CS022 — TkbChildSlot.RoleTag -> Designation

**APPROVED.**

Stringly-typed `RoleTag` replaced with `TacticalDesignation Designation`. `BdcTkbCatalog` updated,
`BdcTkbBuilder` updated, `ChildBlueprintDefinition` has the new property.

---

## Issues Found

### CT-2 — UnitHierarchySystem double-registration in EditorSubsystem [P1 — MUST FIX]

**Root cause:**
`UnitHierarchySystem` was added to both `SimHostCoreLogicPack.simList` (correct) and
`CgfLogicPack.simList` (incorrect). In `EditorSubsystem`, lines 464-466 merge both lists into a
single `EditorSimulationModule`:

```csharp
_kernel.RegisterModule(new EditorSimulationModule(
    cgfLogicPackInst.SimulationSystems,   // contains UnitHierarchySystem
    simHostCorePack.SimulationSystems));  // also contains UnitHierarchySystem
```

`Bus.Read<T>()` returns a `ReadOnlySpan<T>` over the double-buffered read buffer. The read is
non-draining — all callers see the same events in the same frame. Both system instances will
therefore process every `CmdAssignSubordinate`, `CmdRemoveSubordinate`, and `DestructionOrder`
event each tick, causing:
- Double writes to `UnitSubordinate` and `UnitRoster` (benign in the assign path but wastes CPU).
- `RemoveComponent` called on a component that was already removed by the first instance in the
  remove/destruction paths — potential exception or silent corruption depending on ECS internals.
- `CmdAssignSubordinateRejected` published twice for each rejected assignment.

**Fix (to be implemented as the first task of BATCH-03):**
Remove `UnitHierarchySystem` from `CgfLogicPack`. The architectural rationale: CGF is the AI
brain that _publishes_ hierarchy commands; SimHost is the muscle that _executes_ them. In a
distributed cluster, commands travel over the network from CGF to SimHost. CGF standalone nodes
do not need to process hierarchy commands locally. In the Editor, the system reaches the
`EditorSimulationModule` through `SimHostCoreLogicPack.SimulationSystems` — no separate
registration needed.

Concrete changes:
- Remove `private readonly UnitHierarchySystem _unitHierarchySystem;` field from `CgfLogicPack`.
- Remove `_unitHierarchySystem = new UnitHierarchySystem();` from constructor.
- Remove `simList.Add(_unitHierarchySystem);` from `BuildSimulationSystems`.
- Update `CgfLogicPackTests` count assertions from 17 back to 16 (SimulationSystems) and 19
  back to 18 (total system count).

### P2 — Stale comment in UnitHierarchySystem.cs

Line 20: "broadcasts updated `CommanderId` fields" — this will be stale after CS009 removes
`CommanderId` from `Fdp.Core.EntityInfo`. Update this comment as part of the CS010/CS011 work
in BATCH-03.

### P3 — BATCH-02-REPORT.md placed in wrong folder

The developer committed the report to `.dev/commander-subordinates/batches/BATCH-02-REPORT.md`
instead of `.dev/commander-subordinates/reports/BATCH-02-REPORT.md`. The file has been
extracted to the correct location in this review's commit.

---

## DEBT-TRACKER update

| ID | Source | Description | Target |
|----|--------|-------------|--------|
| D-01 | BATCH-02 CT-2 | Remove UnitHierarchySystem from CgfLogicPack (P1) | BATCH-03 (CT-2) |

---

## Next Steps

BATCH-03 will cover: CT-2, CS008, CS009, CS010, CS011, CS023.
