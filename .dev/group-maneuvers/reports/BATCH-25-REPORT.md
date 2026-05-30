# BATCH-25 Report -- Phase 4 Part 1: Member Considerations, Veto Detection, MovementMode

**Batch:** BATCH-25
**Tasks:** TASK-SQD-P4-01, TASK-SQD-P4-02, TASK-SQD-P4-04
**Status:** COMPLETE

---

## Summary

All Phase 4 Part 1 squad work is implemented and all new tests pass. The
implementation delivers:
- Two new member-side Utility AI input readers (`AssignedRole`, `AssignedSlot`)
- Veto-detection system (`SquadVetoDetectionSystem`) with 3-tick hysteresis
- `MovementMode` enum + `MovementModeIntent` component + broadcast system
  (`SquadMovementModeBroadcastSystem`) propagating squad posture to members
- 11 new passing tests

---

## Files Changed

### Modified

| File | Change |
|---|---|
| `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` | Added `MovementModeIntent = 259` in the Squad block |
| `FDP/Toolkits/Fdp.Toolkits/Squad/State/SquadCognitiveState.cs` | Added `MovementMode` enum (Normal/Covered/Fast); added Flags bit-layout comment documenting bits 8-9 = MovementMode |
| `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/SquadInputs.cs` | Added `AssignedRole = 0x3FD1` and `AssignedSlot = 0x8BC9` constants; registered both in `RegisterAll()`; added two reader methods |

### Created

| File | Purpose |
|---|---|
| `FDP/Toolkits/Fdp.Toolkits/Squad/Components/MovementModeIntentComponent.cs` | `MovementModeIntent` struct component (P4-04) |
| `FDP/Toolkits/Fdp.Toolkits/Squad/Systems/SquadMovementModeBroadcastSystem.cs` | Static system that reads `MovementMode` bits from commander blackboard and writes to each member's `MovementModeIntent` (P4-04) |
| `FDP/Toolkits/Fdp.Toolkits/Squad/Systems/SquadVetoDetectionSystem.cs` | Stateful system detecting when a member's `MovementMode` state diverges from squad intent for >= 3 ticks; emits `PhaseEvent` list (P4-02) |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Inputs/SquadInputsP4Tests.cs` | 5 tests covering SC-P4-01-1 through SC-P4-01-3 |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Systems/SquadVetoDetectionSystemTests.cs` | 3 tests covering SC-P4-02-1 through SC-P4-02-3 |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Systems/SquadMovementModeBroadcastSystemTests.cs` | 3 tests covering SC-P4-04-1 through SC-P4-04-3 |

---

## Implementation Notes

### P4-01: AssignedRole / AssignedSlot (SquadInputs.cs)

New `SquadInputIds` constants (FNV-1a-32 & 0xFFFF):

| Name | Value |
|---|---|
| `AssignedRole` | `0x3FD1` |
| `AssignedSlot` | `0x8BC9` |

Both readers are **member-side** (`ctx.Self` = member, NOT commander). The
algorithm for each:
1. Resolve `UnitSubordinate.Commander` from `ctx.Self`; return 0f on any null/missing.
2. Project `SquadCognitiveState` from the commander's `Blackboard1024`.
3. Locate member's roster index by scanning `roster.SubordinateEntities`.
4. `AssignedRole`: read `roleSpan[memberIndex].RoleId` via `MemoryMarshal`;
   return 1f if it equals `(byte)(ctx.Params.BlueprintId & 0xFF)`.
5. `AssignedSlot`: read slot kind from `SlotState` via roster->element->slot
   lookup; return 1f if it equals `(byte)(ctx.Params.BlueprintId & 0xFF)`.

`UnitRoster.SubordinateEntities` is a `fixed long` buffer requiring `unsafe`
context; both reader methods are in a `public static unsafe class SquadInputs`.

### P4-02: SquadVetoDetectionSystem

- `VetoCounterArray` is an InlineArray(16) of `byte` tracking per-slot divergence.
- `_vetoConfirmTicks = 3` (default); configurable via constructor.
- Each call to `Run(repo, commander, divergentSlots, events)`:
  - Increments counter for each slot in `divergentSlots`; resets others.
  - Fires `PhaseEvent(slot, PhaseEventKind.SlotVeto)` when counter reaches threshold.
  - Appends to the caller-supplied `events` list.

### P4-04: MovementMode + SquadMovementModeBroadcastSystem

- `MovementMode` enum values: `Normal=0`, `Covered=1`, `Fast=2`.
  (Note: `Default` was not used as enum member name; it is an IDL reserved
  keyword that would break the CycloneDDS code-generation pipeline.)
- Bits 8-9 of `SquadCognitiveState.Flags` store the mode:
  `MovementModeMask = 0x0300u`, `MovementModeShift = 8`.
- `SquadMovementModeBroadcastSystem.Run(repo, commander)`:
  - Reads mode from commander's blackboard.
  - Iterates `UnitRoster.SubordinateEntities` (unsafe fixed buffer).
  - Writes `MovementModeIntent.Mode` on each member that has the component.
- `MovementModeIntent` component ID: `GlobalComponentIds.MovementModeIntent = 259`.

---

## Test Results

All 79 Squad tests pass (68 pre-existing + 11 new).

```
Passed!  - Failed: 0, Passed: 79, Skipped: 0, Total: 79, Duration: 2 s
```

Pre-existing failures in Navigation and Diagnostics tests (99 total) are
unrelated to this batch and were present before BATCH-25.
