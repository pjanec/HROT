# BPDBG-PERNODE-PROBES: per-exec-node debug probes (today probes are per-block)

**Type:** Compiler + debug-map change — **MECHANICAL** (analyzed below), well-bounded; SONNET with full regression coverage.   **Est:** ~14h
**Onboarding (read in order):** `.dev/.guides/DEV-GUIDE_claude.md`; `docs/blueprints/Blueprint_Subsystem_Debug_NodeGranularStepping_Addendum.md`; `docs/blueprints/Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md`; memory: blueprint breakpoint ID drift (historical node-id/probe mis-attribution — honour it). Then this file.

## Problem (root cause)
Debug granularity is **per-block, not per-node**:
- `DebugProbeInsertion.InsertProbes` (`Compiler/Lowering/DebugProbeInsertion.cs`) inserts exactly ONE `IrOp_DebugProbe_NodeEnter` per block, keyed to `block.SourceNodeId`.
- `Stage5_Schedule`: a regular node sets `bb.SourceNodeId ??= node.Id` (`:307`), but `ScheduleLatentNode` does `bb.SourceNodeId = node.Id` (unconditional overwrite, `:329`).
- `Stage5_Schedule:220-229` builds `bpTargets` = authored node id → the block's `SourceNodeId` (a deliberate **many-to-one** mapping; see `IBlueprintDebugSession.cs:71-74` and `BlueprintDebugSession.cs:33-35`).

Consequence: `SetVar → Delay` compile into one block; the SetVar sets the block id, the Delay overwrites it, so the single probe is the Delay and the SetVar has NO probe → not breakpointable / steppable / recorded. (It worked before only because `Sequence` nodes split graphs into one-node blocks.)

## Analyzed conclusion — this is a mechanical change, compatible with BF-03/04
Adding probes is itself cheap and safe (an extra `OnNodeEnter` call): the recorder just records more (finer pointer), breakpoint/dedup are per-id, the overlay updates more. **Nothing downstream breaks from more probes.**

The ONE thing that matters is the **identity mapping** `BreakpointTargets : authored node → probe id`. Today it is **many-to-one** (node → block probe). The fix makes it **one-to-one** (each exec node → its OWN probe). With that done consistently, all consumers keep working — more precisely:
- **`SetBreakpoint`** (`BlueprintDebugSession.cs:400-418`) already resolves clicked node → `BreakpointTargets[node]` → `Breakpoint.ProbeNodeId`; with one-to-one targets a breakpoint on the SetVar resolves to the SetVar's own probe. No logic change there.
- **CF-6 / BF-03 / BF-04 temp-BPs** go through the SAME `BreakpointTargets` translation (`SetTemporaryBreakpoints(BreakpointTarget(asset,graph,node))`). BF-03 sets a temp BP on the Delay's successor; BF-04 on the EventEntry's successor (first node of next iteration). Today those resolve to the successor's *block* probe; after the change, to the successor's *own* probe — **fires at the same place, attributed exactly**. So BF-03/04 land identically (the first exec node still owns the block-entry probe, so BF-04's target is unchanged). **No redesign; the temp-BP invariant is preserved by the one-to-one mapping.**
- `_bpByNodeString` stays `List<Breakpoint>`-valued (harmless; lists are just usually single-element now).

So the deliverable is: (1) emit a probe per exec node, (2) make `BreakpointTargets` one-to-one. The rest flows through unchanged. The only spot that needs a deliberate check (not a redesign): the latent block keeps its `SourceNodeId` for suspend/resume mechanics **and** must additionally carry its own probe while preceding synchronous exec nodes in the block get their own probes too — verify these coexist (they should: the latent keeps its probe + block identity; the SetVar probe is just an earlier statement).

## Implementation (prescribed)
1. **Mark exec-node entry in Stage5** (reliable; the codebase already found post-hoc `Statements[0].NodeId` heuristics unreliable — they mis-attribute to data nodes). Add `Guid? ExecEntryNodeId` to `IrDebugAnnotation`; in `Stage5_Schedule`, when scheduling an EXEC node (regular `:305-308`, `ScheduleLatentNode`, `ScheduleBranchNode`, `ScheduleSequenceNode`, Return) tag that node's **entry/effect** statement (NOT data-dep statements produced by `ResolveDataPin`). Pure data nodes (GetVariable, Literal, pure FunctionCall) are never tagged.
2. **`DebugProbeInsertion`**: insert a `NodeEnter` probe before each `ExecEntryNodeId`-tagged statement, keyed to that node's id + NodeKind. The first exec node keeps the block-entry probe (so existing single-node blocks are byte-identical). Trace-mode pin probes unchanged. Release mode unchanged (no probes).
3. **`Stage5_Schedule:220-229` `bpTargets`**: map each EXEC node id → its OWN probe id (one-to-one) instead of the block's `SourceNodeId`. (Every exec node now has a probe with its own id, so the target is the node id itself.) Non-exec/data nodes are not breakpoint targets.
4. **`DebugMapBuilder`** (`BreakpointTargets`, `:49/:125`): no structural change — it carries whatever `bpTargets` Stage5 produces. Confirm it serializes the one-to-one map.
5. **`IBlueprintDebugSession.cs:71-74`** doc comment: update the "many-to-one" note to reflect that exec nodes now map one-to-one (block-sharing only remains for any non-exec edge cases). Keep `_bpByNodeString` List-valued.
6. Do NOT change `BlueprintDebugSession.SetBreakpoint`/`OnNodeEnter` matching logic — they already work off `BreakpointTargets`/`ProbeNodeId`; verify they behave with the one-to-one map.

## Tests (the gate — assert REAL behavior)
**Primary (reported bug):** `… → SetVar → Delay → …` with SetVar+Delay in ONE block (e.g. straight-line, no Sequence). Via `BlueprintTestFixture`: (a) breakpoint on the **SetVar** HITS (pauses on SetVar, not the Delay); (b) stepping pauses on SetVar THEN Delay — two distinct `OnNodeEnter`s, `CurrentNodeId` = SetVar id then Delay id; (c) recorder records BOTH (RecordedNodeCount reflects both; restore shows correct per-node state).
**Straight-line synchronous:** `Entry → SetVar(A) → SetVar(B) → Return` (one block): breakpoint on the SECOND SetVar hits; stepping pauses on both in order.
**Data nodes NOT probed:** GetVariable/Literal/pure-FunctionCall feeding a SetVar emit NO probe for the data nodes — assert probe ids are exec-only.
**One-to-one mapping:** assert `DebugMap.BreakpointTargets[SetVarId] == SetVarId's probe id` and `[DelayId] == DelayId's probe id` (distinct), not both the block id.
**REGRESSION — all must stay green (protect the working feature):** `ProbeIntegrationTests`, `CF6_SteppingTests`, `VirtualPointerTests`, `TickBridgeTests` (incl. BF-03 latent step-past-end and BF-04 first-node landing), `SubTickRecorderIntegrationTests`, `NodeGranularEditorUITests`, DebugMap/breakpoint tests. Explicitly re-assert BF-03 (step past Delay → lands on post-Delay node) and BF-04 (step past end-of-tick → first node of next iteration) still pass with the one-to-one mapping.

## Do-not-stop-until-green
`dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests` + `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests`, no regen flags, loop until `Failed: 0` except the documented pre-existing reds (`AiPrimitive_EmitMatchesGoldenSource` ×2, `Stage8_*` ×2, `TickFrame_1000Frames_AllocatesZeroBytes`, `MoveToAndFire_*Snapshot`, `WhenNode_ZeroAllocOnHotPath`). Schedule/IR/DebugMap goldens will grow (more probe statements / one-to-one targets) — that is EXPECTED; inspect each diff to confirm it is exactly the new per-node probes / one-to-one targets, then update the golden and note it in the report. Any OTHER new failure → root-cause it. Transient `MapKeyboardKey.idl` build error → re-run.

## Constraints
- Touch the compiler (`DebugProbeInsertion.cs`, `Stage5_Schedule.cs`, `IrDebugAnnotation.cs`, `DebugMapBuilder.cs`), the `IBlueprintDebugSession` ProbeNodeId doc, and test files. Do NOT change unrelated debugger runtime logic. Do NOT commit any `.bp.json`, exclude assets, suppress diagnostics, or weaken existing tests.
- Narrow stop-condition (NOT a blanket "redesign?" hatch): only if you find the latent block CANNOT carry both its resume `SourceNodeId` and its own probe alongside a preceding synchronous node's probe — document precisely and stop. Per the analysis above this should not occur.
- Do NOT commit. Report → `.dev/blueprint-dbg-2/reports/BPDBG-PERNODE-PROBES-REPORT.md` (exec-boundary marking; the per-node `DebugProbeInsertion` change; the one-to-one `bpTargets` change; data-node exclusion; the BF-03/BF-04 regression results; golden diffs + justification; full test counts). The lead reviews and commits.
