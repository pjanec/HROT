# BATCH-34 Report

**Batch:** BATCH-34
**Tasks:** SC-P7-01, SC-P7-02, SC-P7-03
**Developer:** Claude Sonnet 4.6
**Status:** COMPLETE

---

## Files Created

### 1. `Hrot/Diagnostics/Hrot.Diagnostics.Overlays/SquadCoordinationOverlaySource.cs`

`internal sealed unsafe class SquadCoordinationOverlaySource : IGizmoSource`

- **P7-01 (member element color + role labels):**
  - 4-entry static palette `s_elementColors` indexed by `elemIdx % 4`
  - Per-member `DrawText` label `E{elemIdx}R{roleId}` using the element color
  - `EmitDangerAreaObb` renders the active danger area as 12 `DrawLine` calls (4 bottom + 4 top + 4 verticals), with `ZFloor`/`ZCeiling` mapped to the Y axis

- **P7-02 (divergence lines + veto labels):**
  - Solid `DrawLine` per member (always shown, green)
  - Dashed `DrawLine` + `DrawTextLong("VETO:{optionId}")` when member has `UtilityTraceWorkingMemory1024` with `RecordCount > 0`
  - Uses mutable copy of `mem` before calling `LatestSelected()` (non-readonly method)

- **P7-03 (phase label + contact spheres):**
  - `DrawTextLong("Phase:{PhaseId} T0:{PhaseEnteredTick}")` every frame
  - `DrawSphere(radius=1.5f, purple)` per entry in `state.Contacts` up to `Contacts.Count`

- **InlineArray access pattern:** All inline-array spans use `MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<...>(ref Unsafe.AsRef(in ...)))` to avoid defensive copies.
- **Budget guard:** `_budget.IsPermitted(AiOverlayFlags.SquadAssignment)` checked in `Emit` before any work.

### 2. `Hrot/Diagnostics/Hrot.Diagnostics.Overlays.Tests/SquadCoordinationOverlaySourceTests.cs`

11 tests in `SquadCoordinationOverlaySourceTests`:

| Test | Task | Assertion |
|---|---|---|
| `FlagSet_EmitsAtLeastOne` | SC-P7-01-1 | Flag present → at least 1 draw call |
| `FlagAbsent_EmitsZero` | SC-P7-01-1 | Flag absent → 0 draw calls |
| `ElementColorPersistence_SameElementIndex_SameEmitCountAcrossTicks` | SC-P7-01-2 | Same emit count across consecutive ticks |
| `DangerAreaObb_ZExtentDiffers_GroundVsBridgeDeck` | SC-P7-01-3 | ZFloor/ZCeiling map to correct Y min/max for two fixtures |
| `OnTaskMember_NoDivergence_EmitsSolidLineOnly` | SC-P7-02-1 | No dashed lines, no VETO: labels |
| `VetoingMember_EmitsDashedLineAndLabel` | SC-P7-02-2 | At least 1 dashed line + VETO: label containing the option id |
| `VetoLabel_UpdatesWhenOptionIdChanges` | SC-P7-02-3 | Label text differs when winning option id changes |
| `PhaseLabel_UpdatesOnTransition` | SC-P7-03-1 | Phase: label changes on transition |
| `PhaseEntryTick_ResetsOnTransition` | SC-P7-03-2 | T0: label reflects new entry tick after transition |
| `ContactPool_EmitsSpheres_WhenContactsPresent` | SC-P7-03-3 | Sphere count equals contact pool count; positions match |
| `BudgetShedding_50Squads_ChannelsShedFirst` | SC-P7-03-4 | Channels shed (RecordAndCheck=false) while SquadAssignment still permitted |

New helper: `LineCapturingDrawBuilder` — records `Lines` (with `LineStyle`), `LongTexts`, and `SpherePositions`.
Reuses `CountingDrawBuilder` from `OverlaySourceTests.cs` (same namespace, no duplication).

---

## Build Results

```
Build succeeded.
0 Error(s)
11 Warning(s)  (pre-existing, unrelated to BATCH-34)
```

## Test Results

```
Total tests: 29
     Passed: 29
 Total time: 1.25 s
```

18 existing overlay tests: all pass (no regression).
11 new squad coordination tests: all pass.

---

## Deviations from Instructions

None. All code follows BATCH-34-INSTRUCTIONS.md patterns exactly.
`Entity commander` parameter was not threaded into `EmitMemberOverlays` (it is unused there; only `EmitDangerAreaObb` needs it), keeping the signature minimal.
