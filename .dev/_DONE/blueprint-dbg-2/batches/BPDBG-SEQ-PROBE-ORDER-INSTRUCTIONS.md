# BPDBG-SEQ-PROBE-ORDER: Sequence (and Branch) node probe must fire in EXECUTION order, not prepended as a block header

**Type:** Compiler fix (Stage5 + verify DebugProbeInsertion) — MECHANICAL, well-bounded. SONNET.
**Onboarding (read in order):** `.dev/.guides/DEV-GUIDE_claude.md`; `docs/blueprints/Blueprint_Subsystem_Debug_NodeGranularStepping_Addendum.md`; the BPDBG-PERNODE-PROBES report `.dev/_DONE/blueprint-dbg-2/reports/BPDBG-PERNODE-PROBES-REPORT.md`; the BPDBG-STEPOVER-LATENT-SEQUENCE report `.dev/_DONE/blueprint-dbg-2/reports/BPDBG-STEPOVER-LATENT-SEQUENCE-REPORT.md`; this file. You MAY use codebase-memory MCP first.
**DO NOT COMMIT.** Report to `.dev/_DONE/blueprint-dbg-2/reports/BPDBG-SEQ-PROBE-ORDER-REPORT.md`. Lead reviews + commits.

## Problem (root cause — already diagnosed; verify then fix)
When an exec node precedes a `SequenceNode` in the SAME scheduled block (e.g. `SetVarB → Sequence S1`), the recorder/probe order is WRONG: the Sequence's `NodeEnter` fires BEFORE the SetVar that actually executes first.

Why: `Stage5_Schedule.ScheduleSequenceNode` (`:561`) does `bb.SourceNodeId = seq.Id;` — an **unconditional overwrite** of the block's SourceNodeId (clobbering the `??= svBId` set by the preceding `ScheduleSetVariableNode`), and it tags **no** statement with `ExecEntryNodeId = seq.Id` (it only sets a `Goto` terminator). Then in `DebugProbeInsertion` (per BPDBG-PERNODE-PROBES): `blockSourceNodeId = s1Id`, `coveredByExecEntryId(s1Id) = false` (only `svBId` is tagged), so `needsHeaderProbe = true` → the `s1Id` header probe is **prepended to the FRONT of the block**, ahead of `svBId`'s per-node probe.

Result: probe/record order = `[s1Id, svBId, …]`; execution order = `[svBId, s1Id, …]`. The two are swapped. This makes Step Over (which lands on the first recorded node of the resumed tick — see BPDBG-STEPOVER-LATENT-SEQUENCE) land on the cascaded Sequence node instead of the SetVar that ran first. User-confirmed expected: land on the SetVar first.

## Fix (prescribed)
Make the Sequence node's probe emit at its **in-block execution position** (after the preceding exec node's statements), not as a prepended header — exactly like other exec nodes carry their own `ExecEntryNodeId` tag.

In `ScheduleSequenceNode` (`Stage5_Schedule.cs:558`):
1. Change `bb.SourceNodeId = seq.Id;` → `bb.SourceNodeId ??= seq.Id;` (do NOT clobber a preceding exec node's block source; keep the sequence as block source only when it is the block's first exec node).
2. Emit a tagged **exec-probe-anchor** for the sequence at the CURRENT block position (BEFORE setting the `Goto` terminator), so `DebugProbeInsertion` inserts the sequence's probe in order. Mirror the existing anchor pattern (see the `exec-probe-anchor` / `return-probe-anchor` in `ScheduleBlock`):
   ```csharp
   bb.Statements.Add(new IrStatement {
       Operation = new IrOp_Const("0", Stage5_Schedule.Int32Type),
       Debug = new IrDebugAnnotation {
           GraphId = _graph.Id, NodeId = seq.Id,
           Synthesized = "seq-probe-anchor", ExecEntryNodeId = seq.Id,
       },
   });
   ```
   - When the sequence IS the block's first exec node (no preceding statements / it owns SourceNodeId): the anchor sits at position 0 and behaves like the old header probe — byte-equivalent probe identity, just emitted via the ExecEntryNodeId path instead of the header path. Confirm single-sequence blocks keep one probe for the sequence (no double probe — if `coveredByExecEntryId` now true, `needsHeaderProbe` must be false; verify the header path no longer also fires for it).
3. **Check `ScheduleBranchNode` (`:505`) for the same class of bug.** It tags via `TagFirstNewStatement(bb.Statements, branchStmtsBefore, bn.Id)`, but verify: if a Branch is preceded by an exec node in the block and the branch's condition resolves to NO new statements, does it fall back to a prepended header (wrong order)? If so, apply the same anchor fix. If branch is always block-first or always emits a tagged statement, no change — state which in the report.

Do NOT change `DebugProbeInsertion`'s logic unless step (1/2) proves insufficient; the goal is that the sequence node is covered by the `ExecEntryNodeId` path (in-order) rather than the header path (prepended).

## Tests (gate)
1. **Probe-order regression (new or extend PerNodeProbesTests):** asset `Entry → SetVarB → Sequence S1 → …`. Assert the emitted `NodeEnter` probe order (via DebugMap entries / a CapturingDebugSession `NodeEntries`) is `svBId` **then** `s1Id` — NOT `s1Id` then `svBId`. Assert `BreakpointTargets[svBId]==svBId` and `[s1Id]==s1Id` (one-to-one preserved).
2. **Update TickBridgeTests Test 10** (`TickBridge_StepOverSequenceThen0Latent_…`): after this fix the landing after the tick-bridge must be **`svBId`** (first node of Then1), then next step `s1Id`, then `svCId`. Change the assertions from `s1Id`-first to `svBId`-first. (The bug the user reported is "skipped the SetVar"; the correct fix lands on the SetVar first.)
3. **Single-sequence block:** assert a graph where the Sequence is the block's first exec node still has exactly ONE probe for the sequence (no regression / no double probe).
4. Regression: `PerNodeProbesTests`, `CF2_AuthoredIdProbeTests` (Count4 uses a Sequence), `CF6_SteppingTests`, `VirtualPointerTests`, `TickBridgeTests`, `SubTickRecorderIntegrationTests`, `NodeGranularEditorUITests` — all green. Schedule/IR/DebugMap goldens may shift (one extra anchor statement + reordered probe); inspect each diff, confirm it is exactly the in-order sequence probe, update the golden, justify in the report.

## Do-not-stop-until-green (NO regen flags)
`dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests` + `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests`. Only acceptable failures = documented pre-existing reds: `AiPrimitive_EmitMatchesGoldenSource`(×2), `Stage8_PdbContainsEmbeddedSource`, `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb`, `TickFrame_1000Frames_AllocatesZeroBytes`, `MoveToAndFire_GeneratedSource_Snapshot`, `WhenNode_ZeroAllocOnHotPath`. Flaky `AlcUnloadTests.Fixture_AfterMultipleLoads_…` + transient `MapKeyboardKey.idl` → re-run once. Stale source-gen → `dotnet build <consumer>.csproj --no-incremental`.

## Constraints
- Touch `Stage5_Schedule.cs` (ScheduleSequenceNode; ScheduleBranchNode only if it has the same defect), test files, and goldens (only the expected in-order-probe shifts). Do NOT change the debugger runtime, the recorder, or `BlueprintDebugSession` (the step-resume mechanism from BPDBG-STEPOVER-LATENT-SEQUENCE is correct — leave it; just let it land on `svBId` now that the recorded order is fixed). Do NOT weaken existing tests. Do NOT load/commit any `.bp.json`. DO NOT COMMIT.
- Narrow stop-condition: if the anchor approach causes a double-probe for single-sequence blocks that you cannot resolve via `??=` + the ExecEntryNodeId/header mutual-exclusion, document precisely and stop.
- Report: verified root cause; the Stage5 change (+ ScheduleBranchNode finding); how single-sequence blocks stay one-probe; the probe-order test; the Test 10 update (s1Id→svBId landing); every golden diff + justification; full Passed/Failed/Skipped for both suites.
