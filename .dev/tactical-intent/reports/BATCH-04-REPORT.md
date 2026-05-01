# BATCH-04 Report

**Batch:** BATCH-04  
**Developer:** GitHub Copilot  
**Date:** 2026-05-02  
**Status:** Complete

---

## Task Completion

| Task ID    | Status   | Notes                                                                              |
|------------|----------|------------------------------------------------------------------------------------|
| TASK-TI010 | Complete | `CommanderNodes` + `IssueTacticalIntentParams` created; 2/2 tests passing         |
| TASK-TI011 | Complete | `DefendAreaMapper` created and registered in `CgfSubsystem`; 5/5 tests passing    |

---

## Testing Results

**Targeted tests (CommanderNodes + DefendAreaMapper):** 7 / 7 passed  
**Full `Hrot.SimHost.Tests` suite:** 472 passed, 2 failed (pre-existing), 3 skipped

**Pre-existing failures (unrelated to this batch):**
- `Hrot.SimHost.Tests`: 2 failures in `MissionPlanTranslatorTests` — present before any changes in this batch.

**Key Test Scenarios Verified:**

TASK-TI010 (`CommanderNodesTests`):
- [x] `Action_IssueTacticalIntent_WithValidSubordinate_PublishesEvent` — returns Success; one `AssignTacticalIntentEvent` with correct Entity and IntentId == "DefendArea"
- [x] `Action_IssueTacticalIntent_WithZeroPacked_ReturnsFailure` — returns Failure; no event published

TASK-TI011 (`DefendAreaMapperTests`):
- [x] `TargetIntentId_IsDefendArea` — property returns "DefendArea"
- [x] `TryMap_MilitaryApc_ReturnsConvoyEscort` — MilitaryApc (503L) maps to "ConvoyEscort"; returns true; entity matches
- [x] `TryMap_InfantrySoldier_ReturnsInfantryCombat` — InfantrySoldier (504L) maps to "InfantryCombat"; returns true; entity matches
- [x] `TryMap_UnknownTkbType_ReturnsFalse` — TkbType 999L returns false
- [x] `TryMap_NoTkbIdentity_ReturnsFalse` — entity without TkbIdentity component returns false

---

## Files Changed

### New Files

| File | Purpose |
|------|---------|
| `Hrot/Subsystems/Hrot.AI.Doctrines/Brains/CommanderNodes.cs` | BTree action node for issuing tactical intents to subordinates (TASK-TI010) |
| `Hrot/Subsystems/Hrot.AI.Doctrines/Mappers/DefendAreaMapper.cs` | ITacticalOrderMapper implementation for "DefendArea" intent (TASK-TI011) |
| `Hrot/Subsystems/Hrot.SimHost.Tests/CommanderNodesTests.cs` | Tests for TASK-TI010 (2 tests) |
| `Hrot/Subsystems/Hrot.SimHost.Tests/DefendAreaMapperTests.cs` | Tests for TASK-TI011 (5 tests) |

### Modified Files

| File | Change |
|------|--------|
| `Hrot/Subsystems/Hrot.AI.Doctrines/Hrot.AI.Doctrines.csproj` | Added `<ProjectReference>` to `Hrot.Core` for `TkbEntityTypes` constants |
| `Hrot/Subsystems/Hrot.CGF/Hrot.CGF.csproj` | Added `<ProjectReference>` to `Hrot.AI.Doctrines` for mapper registration |
| `Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs` | Added `using Hrot.AI.Doctrines.Mappers;`; extracted `mapperRegistry` local and called `mapperRegistry.Register(new DefendAreaMapper())` before passing it to `CgfLogicPack` |

---

## Implementation Notes

### TASK-TI010 — CommanderNodes

`CommanderNodes` follows the same static-class + nested-struct pattern as `CgfNodes.cs`. The `IssueTacticalIntentParams` DTO stores `SubordinatePacked` (long) and `IntentTypeOrdinal` (int) for unmanaged blackboard storage. The `[BTreeAction]`-decorated `Action_IssueTacticalIntent` method:
- Returns `NodeStatus.Failure` when `SubordinatePacked == 0` (no subordinate resolved yet)
- Reconstructs the `Entity` from the packed value via `new Entity((ulong)p.SubordinatePacked)`
- Publishes `AssignTacticalIntentEvent { Entity = subordinate, IntentId = "DefendArea", JsonParams = "" }` on `ctx.World.Bus`
- Returns `NodeStatus.Success`

No dependency on `Hrot.Core` was needed for this task; all types come from `Fdp.Core` and `Fdp.Toolkit.Behavior`.

### TASK-TI011 — DefendAreaMapper

`DefendAreaMapper` implements `ITacticalOrderMapper`. Key details discovered before implementation:
- `TkbEntityTypes` is in namespace `Hrot.Map.Common` (file at `Hrot/Engine/Hrot.Core/MapDefinitions/TkbEntityTypes.cs`)
- `TkbEntityTypes.MilitaryApc = 503L`, `TkbEntityTypes.InfantrySoldier = 504L`
- `AssignDoctrineEvent` field is `DoctrineName` (not `Name`)
- `ITacticalOrderMapper.TryMap` first parameter is `Entity self` (matched in implementation)
- `repo.GetComponent<TkbIdentity>(self)` used for read-only TkbType access

**Project reference chain added:**
- `Hrot.AI.Doctrines` ← `Hrot.Core` (for `TkbEntityTypes`)
- `Hrot.CGF` ← `Hrot.AI.Doctrines` (for `DefendAreaMapper` at composition root)

The `CgfSubsystem.cs` mapper registration was placed between creating the `TacticalIntentMapperRegistry` and passing it to `CgfLogicPack`, following the pattern specified in the instructions.
