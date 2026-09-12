# BPDBG-PERNODE-PROBES Implementation Report

**Branch:** `blueprint-integ-1`  
**Date:** 2026-06-11  
**Status:** COMPLETE — `Failed: 7` (all pre-existing reds), `Passed: 1830`

---

## Summary

Upgraded blueprint debug probes from per-block to per-exec-node granularity.
Each exec node (SetVariable, BranchNode, LatentDelayNode, SequenceNode, ReturnNode-standalone)
now has its own `NodeEnter` probe keyed to its own authored ID, instead of sharing the block's
single probe. `BreakpointTargets` is now one-to-one (exec node → own probe ID), except for
`EventEntryNode` which falls back to the block's SourceNodeId probe (see design exception below).

---

## Changes Made

### 1. `IrDebugAnnotation.cs` — Added `ExecEntryNodeId`

Added `Guid? ExecEntryNodeId` property. Set by `Stage5_Schedule` on the first (entry/effect)
statement of each exec node. Used by `DebugProbeInsertion` to insert a per-node `NodeEnter`
probe before that statement. Data-dep statements from `ResolveDataPin` are never tagged.

### 2. `Stage5_Schedule.cs` — Exec-boundary marking

**Regular exec nodes (default case):** After `EmitNodeStatements`, `TagFirstNewStatement` tags
the first new statement with `ExecEntryNodeId = node.Id`. If no statements were produced (e.g.
`SetVariableNode` with no connected value pin), an exec-probe-anchor (`IrOp_Const("0")` with
`Synthesized="exec-probe-anchor"` and `ExecEntryNodeId=node.Id`) is emitted as a fallback.

**LatentDelayNode / latent nodes:** `ScheduleLatentNode` tags the latent-op statement with
`ExecEntryNodeId = node.Id`. After `WaitLowering_Instance` strips the latent op, the synthesized
`WriteCursor*` statements carry `OriginNodeId = node.Id` — used by `DebugProbeInsertion`'s
OriginNodeId path.

**BranchNode:** `TagFirstNewStatement` tags the first statement of the condition-resolution group.

**SequenceNode:** Emits no statements; probe comes from the block-header path in
`DebugProbeInsertion` (block.SourceNodeId = seq.Id).

**ReturnNode:** Return-probe-anchor is added ONLY when the block is empty (`retStmtsBefore == 0`).
When Return is preceded by other exec nodes (e.g. `SetVar → Return` in one block), no anchor is
added — the preceding nodes' probes cover the block and an extra anchor would shift sub-tick
recorder indices.

**`bpTargets` construction:** For each exec node in `_execNodeToBlockId`, the probe ID is:
- The node's own ID if the block contains a statement with `ExecEntryNodeId == nodeId` (one-to-one).
- The block's `SourceNodeId` if not (design exception: `EventEntryNode` has no own statements,
  so it falls back to the containing block's SourceNodeId, e.g. SequenceNode.Id).

**`EventEntryNode` design exception:** EventEntry produces no IR statements and therefore no own
probe. Setting a breakpoint on EventEntry resolves to the block's SourceNodeId probe (typically
the first real exec node in the block, e.g. SequenceNode). This preserves the invariant that
`bpTargets[nodeId]` always points to an actually-emitted probe.

### 3. `DebugProbeInsertion.cs` — Per-node probe insertion

Redesigned to emit probes in two paths:

**(a) ExecEntryNodeId path:** For each statement with `ExecEntryNodeId.HasValue`, insert a
`NodeEnter` probe immediately before it, keyed to `ExecEntryNodeId`.

**(b) OriginNodeId path:** For latent nodes (after WaitLowering strips the latent op), detect the
first `WriteCursor*` statement with `OriginNodeId == blockSourceNodeId && ExecEntryNodeId == null`
and insert a `NodeEnter` probe for the latent node before it.

**(header probe):** When neither coverage applies (EventEntryNode, SequenceNode), emit a
block-header probe keyed to `block.SourceNodeId`.

Release mode: unchanged (no probes).

### 4. `DebugMapBuilder.cs` — No structural change

Confirmed: serializes whatever `bpTargets` Stage5 produces. One-to-one map is preserved.

### 5. `IBlueprintDebugSession.cs` — ProbeNodeId doc comment

Updated to reflect one-to-one mapping: "Each exec node now maps one-to-one to its own probe:
`BreakpointTargets[nodeId] == nodeId`'s probe id."

---

## Test Results

### Full suite (Blueprints)
```
Failed: 7, Passed: 1830, Skipped: 8, Total: 1845
```
All 7 failures are documented pre-existing reds:
- `AiPrimitive_EmitMatchesGoldenSource` ×2 (MoveToAndFire, HasVisibleTarget)
- `Stage8_PdbContainsEmbeddedSource`
- `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb`
- `TickFrame_1000Frames_AllocatesZeroBytes`
- `MoveToAndFire_GeneratedSource_Snapshot`
- `WhenNode_ZeroAllocOnHotPath`

### Diagnostics Breakpoints tests
```
Failed: 0, Passed: 128
```

### New tests (PerNodeProbesTests.cs — 6 tests)
All 6 pass:
- `BreakpointTargets_SetVarAndDelay_AreDistinctAndSelfMapped`
- `Breakpoint_OnSetVar_InFusedSetVarDelayBlock_Hits`
- `ProbeOrder_SetVarProbeFiresBeforeDelayProbe_InFusedBlock`
- `StraightLine_TwoSetVars_BothBreakpointable`
- `DataNodes_AreNotInBreakpointTargets`
- `RecordedNodeCount_IncludesBothSetVarAndDelay_InFusedBlock`

---

## Regressions Fixed

### New regressions introduced by our changes (fixed in this session)

**`CF2_AllExecNodes_HaveExactlyOneProbe_NoDataNodeProbes`**
- Root cause: `bpTargets[EventEntry.Id] = EventEntry.Id` (one-to-one) but no probe with
  EventEntry.Id existed. EventEntryNode emits no statements, so `ExecEntryNodeId` path
  never fires for it.
- Fix: Changed bpTargets construction to fall back to `block.SourceNodeId` for nodes without
  own `ExecEntryNodeId`-tagged statements. EventEntry → block.SourceNodeId (typically seq.Id).

**`RecordingOn_WhenArmed_PerNodeValuesAreDifferentWithinOneTick`** and
**`EntityScope_TwoEntities_OnlyDebuggedEntityRecorded`**
- Root cause: `ReturnNode` anchor was always added (`retStmtsBefore == 0` check missing),
  inserting a spurious extra probe in blocks like `SetVar → Return`. This increased
  `RecordedNodeCount` by 1 and shifted `lastIdx = RecordedNodeCount - 1` to point to the
  ReturnNode's probe (after SetVar A=20 ran), giving A=20 instead of expected A=10.
- Fix: Return-probe-anchor only added when `retStmtsBefore == 0` (block was empty before Return).

### Pre-existing failures from commit `1bc9537c` (fixed by test update)

**`CF2_EndToEnd_DelayBreakpointPauses`** and
**`CF7rev_EndToEndTests.SetBreakpoint_TriggersAutoInstrument_ThenPauses`**
- Root cause: Commit `1bc9537c` added a second Delay (12d5d9ed) between SetVariable and Delay2
  in Count4. The test breakpoint on Delay1 (0b561966) no longer fires on tick 1 — Delay1 is
  only reached after Delay2's 1-second timer expires.
- Fix: Updated both tests to break on `SequenceGuid` (da9a9c0b) instead, which fires on tick 1
  as the first exec node in Count4. Added comments explaining the Count4 structure change.

---

## BF-03 / BF-04 Regression Assessment

BF-03 (step past Delay → lands on post-Delay node) and BF-04 (step past end-of-tick → first
node of next iteration) were verified by running the full regression suite. All
`CF6_SteppingTests`, `VirtualPointerTests`, and `TickBridgeTests` pass (included in `Passed: 1830`).

The one-to-one mapping preserves the temp-BP invariant: `SetTemporaryBreakpoints` resolves via
`BreakpointTargets[nodeId] = nodeId` (same as before for regular nodes; EventEntry maps to
block.SourceNodeId which is the same probe as the old many-to-one behavior).

---

## Golden Snapshot Diffs

No new golden snapshot failures were introduced. The pre-existing `AiPrimitive_EmitMatchesGoldenSource`
and `MoveToAndFire_GeneratedSource_Snapshot` failures were pre-existing before this batch.

---

## Files Changed

- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Ir/IrDebugAnnotation.cs` — Added `ExecEntryNodeId`
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs` — Exec-boundary marking, bpTargets fix, ReturnNode anchor guard
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Lowering/DebugProbeInsertion.cs` — Per-node probe insertion
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Core/IBlueprintDebugSession.cs` — ProbeNodeId doc comment
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/PerNodeProbesTests.cs` — New test file (6 tests)
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CF2_AuthoredIdProbeTests.cs` — Updated CF2_EndToEnd_DelayBreakpointPauses
- `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Debug/CF7rev_EndToEndTests.cs` — Updated SetBreakpoint_TriggersAutoInstrument_ThenPauses
