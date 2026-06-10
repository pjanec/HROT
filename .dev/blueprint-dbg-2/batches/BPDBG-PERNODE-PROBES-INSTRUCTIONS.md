# BPDBG-PERNODE-PROBES: per-exec-node debug probes (true node-granular stepping; today it's per-block)

**Type:** Compiler + debug-map feature (HIGH blast radius — changes a working feature; SONNET, careful, strong regression protection)   **Est:** ~16h
**Onboarding (read in order):** `.dev/.guides/DEV-GUIDE_claude.md`; `docs/blueprints/Blueprint_Subsystem_Debug_NodeGranularStepping_Addendum.md`; `docs/blueprints/Blueprint_Subsystem_Debug_Protocol_Detailed_Design.md` (probe/debug-map model); memory note: blueprint breakpoint ID drift (historical node-id/probe mis-attribution — be careful). Then this file.

## Problem
Debug granularity is **per-block, not per-node**. `DebugProbeInsertion.InsertProbes` (`Compiler/Lowering/DebugProbeInsertion.cs`) inserts exactly ONE `IrOp_DebugProbe_NodeEnter` per block, keyed to `block.SourceNodeId`. When multiple exec nodes share a block — e.g. `SetVar → Delay` (synchronous SetVar then a latent that ends the block) — only one probe fires. Worse, `Stage5_Schedule.ScheduleLatentNode` does `bb.SourceNodeId = node.Id` (unconditional overwrite, `Stage5_Schedule.cs:329`) while a regular node uses `bb.SourceNodeId ??= node.Id` (`:307`), so the **Delay overwrites the SetVar** as the block's probe identity → the SetVar has NO probe → breakpoints/stepping/recording cannot see it. (It worked before only because `Sequence` nodes split graphs into one-node blocks.)

## Goal
Insert a `NodeEnter` probe before **each EXEC node** within a block (not just at block entry), keyed to that exec node's authored id, so every exec node is breakpointable / steppable / recorded. Pure DATA nodes (GetVariable, Literal, pure FunctionCall) must NOT get probes (the existing `DebugProbeInsertion` comment removed a tier specifically because it mis-attributed to data nodes). This completes true node-granular stepping for ALL graphs.

## Approach (prescribed; refine mechanism but keep the contract)
1. **Mark exec-node boundaries reliably in Stage5** (preferred over post-hoc heuristics, which the codebase already found unreliable). When `Stage5_Schedule` schedules an EXEC node (the regular-node case `:305-308`, `ScheduleLatentNode`, `ScheduleBranchNode`, `ScheduleSequenceNode`, Return), tag the statement that represents that node's **entry** (its effect statement, after its data-dep reads) with the exec node's authored id — e.g. add `Guid? ExecEntryNodeId` to `IrDebugAnnotation`, or set a dedicated marker. Data-dep statements (from `ResolveDataPin`) must NOT be tagged.
2. **`DebugProbeInsertion`** inserts a `NodeEnter` probe before each exec-entry-tagged statement, keyed to that node's id (and `NodeKind`). Keep the block-entry probe behavior for the FIRST exec node (so existing single-node blocks are unchanged). Trace-mode pin probes unaffected.
3. **`BreakpointTargets` (authored node id → probe id):** today `Stage5_Schedule:220-229` maps every node to its **block's** `SourceNodeId`. With per-node probes, each exec node must map to **its own** probe id so breakpoints and CF-6 temp-BPs resolve to the right node. Update this mapping. Verify `SetTemporaryBreakpoints`/`BreakpointTarget` translation (used by CF-6 stepping and BF-03/BF-04 latent step-past-end) still resolves correctly.
4. **`DebugMapBuilder`** / debug map: ensure the per-node probes are represented so node-id resolution, structure-hash, and the editor mapping stay correct.
5. **Latent block identity preserved:** `ScheduleLatentNode` may keep setting `SourceNodeId` for the resume/suspend mechanics, but the latent must ALSO get its own exec-entry probe AND any preceding synchronous exec node in the same block must get its own probe. Net: both the SetVar and the Delay are independently probed.

Do NOT regress: Release mode inserts no probes; data nodes get no probes; node-id correctness (no drift / no mis-attribution).

## Tests required (the gate — assert REAL behavior)
**Primary (the reported bug):** a graph with `… → SetVar → Delay → …` where SetVar and Delay are in the SAME block. Via `BlueprintTestFixture`: (a) a breakpoint on the **SetVar** HITS (pauses on SetVar); (b) stepping pauses on the SetVar AND then the Delay (two distinct `OnNodeEnter`/recordings, distinct `CurrentNodeId`s = SetVar id then Delay id); (c) the recorder records BOTH nodes (RecordedNodeCount reflects both; restoring each shows the correct per-node state).
**Straight-line synchronous:** `Entry → SetVar(A) → SetVar(B) → Return` (one block, no latent): a breakpoint on the SECOND SetVar hits; stepping pauses on both SetVars in order. (Proves per-node probing beyond the latent case.)
**Data nodes NOT probed:** a node chain with GetVariable/Literal/pure-FunctionCall feeding a SetVar emits NO probe for the data nodes — only the exec SetVar. Assert probe count / node ids.
**REGRESSION (protect the working feature — all must stay green):**
- Existing `ProbeIntegrationTests`, `CF6_SteppingTests`, `VirtualPointerTests`, `TickBridgeTests`, `SubTickRecorderIntegrationTests`, `NodeGranularEditorUITests`, `DebugMap`/breakpoint tests.
- BF-03/BF-04 latent step-past-end still lands correctly (temp-BP-on-successor / first-node-of-next-iteration via the updated BreakpointTargets).
- Sequence-split graphs still step the same way.
- Breakpoint hit-attribution unchanged for existing one-node-block cases.

## Do-not-stop-until-green
`dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests` + `Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests`, no regen flags, loop until `Failed: 0` except the documented pre-existing reds (`AiPrimitive_EmitMatchesGoldenSource` ×2, `Stage8_*` ×2, `TickFrame_1000Frames_AllocatesZeroBytes`, `MoveToAndFire_*Snapshot`, `WhenNode_ZeroAllocOnHotPath`). If a Schedule/IR golden changes because probes are now per-node, inspect+justify (it's expected — more probe statements); update only if correct, explain in the report. Any OTHER new failure → root-cause it. Transient `MapKeyboardKey.idl` build error → re-run.

## Constraints
- Touch the compiler (`DebugProbeInsertion.cs`, `Stage5_Schedule.cs`, `IrDebugAnnotation.cs`, `DebugMapBuilder.cs` as needed) and test files. Do NOT change unrelated debugger runtime logic beyond the BreakpointTargets mapping needed here. Do NOT commit any `.bp.json`, exclude assets, suppress diagnostics, or weaken existing tests.
- If you discover the per-node probe model genuinely cannot preserve BF-03/04 latent stepping or breakpoint attribution without a deeper redesign, STOP and document precisely rather than half-implementing.
- Do NOT commit. Report → `.dev/blueprint-dbg-2/reports/BPDBG-PERNODE-PROBES-REPORT.md` (the exec-boundary marking mechanism, the DebugProbeInsertion + BreakpointTargets + DebugMap changes, how data nodes are excluded, any golden changes + justification, the full regression list you ran + counts). The lead reviews and commits.
