# BPDBG-STEPOVER-TEST-RECONCILE: make the suite GENUINELY green after the Step Over + sequence-probe-order fixes — by diagnosis, never by appeasement

**Type:** Test reconciliation + (only if a real bug is found) runtime fix. HIGH care. SONNET.
**Onboarding:** `.dev/.guides/DEV-GUIDE_claude.md`; the two prior reports `.dev/blueprint-dbg-2/reports/BPDBG-STEPOVER-LATENT-SEQUENCE-REPORT.md` and `BPDBG-SEQ-PROBE-ORDER-REPORT.md`; this file. **DO NOT COMMIT.** Report → `.dev/blueprint-dbg-2/reports/BPDBG-STEPOVER-TEST-RECONCILE-REPORT.md`.

## Situation (read carefully)
The working tree already contains two reviewed-good changes (LEAVE the production code as-is unless you PROVE a real bug):
- `BlueprintDebugSession.cs` — `_stepResumePending` Step Over tick-bridge (re-pause on first recorded node of the resumed tick).
- `Stage5_Schedule.cs` — `ScheduleSequenceNode` now `??=` + a tagged `seq-probe-anchor` so the Sequence node's probe fires in EXECUTION order.

**Design decision (user-confirmed, settled): the Sequence node IS a stepping stop** — every exec node is treated uniformly (probe ⟺ breakpointable ⟺ recorded ⟺ steppable). Do NOT special-case the sequence out of recording/stepping.

A prior agent reported the suite green, but that was against a **non-compiling project** (false green). On a COMPILING build there are **7 RED tests** (below). They are NOT pre-existing — they are fallout from the two changes above (the Sequence node is now recorded in EXECUTION order, which re-sequences the per-node pointer ring and changes some landing nodes).

## THE 7 FAILURES (on a compiling build)
1. `TickBridgeTests.TickBridge_StepPastEndOfTick_LandsOnFirstNode_NotBreakpoint` — `BF-04: landing must be seqId (2c82c053…), got 16c14e8d…`
2. `TickBridgeTests.TickBridge_TerminalLastNode_SetsFirstNodeTempBP_NotContinue` — `Expected "6b14a612…", Actual "c685b9ee…"`
3. `TickBridgeTests.TickBridge_InspectorReflectsNewTick_ExactValue` — `At pointer 2 of tick N+1, A must be 10; got 20`
4. `TickBridgeTests.TickBridge_WithinTickStepping_Unaffected` — `Expected 10, Actual 0`
5. `InspectorSnapshotResolutionTests.ResolveInspectorSnapshot_WhenPaused_PointerAt2_ReturnsA10` — `Expected 10, Actual 0`
6. `InspectorSnapshotResolutionTests.ResolveInspectorSnapshot_AcrossPointers_Returns_0_0_10` — `Expected 10, Actual 0`
7. `VirtualPointerTests.Inspector_ReturnsExactPerNodeValues_AcrossStepBackAndForward` — `Expected 10, Actual 0`

## MANDATE — diagnose each, NEVER blind-match (this is the whole point)
For EACH failing test you MUST decide, with evidence, between two outcomes — and you may NOT just edit an expected number/id until the assertion passes:

- **(A) Behavior is now CORRECT, test indices/values are stale.** This happens because the Sequence node is now an in-order recorded stop, so the pointer ring gained/re-ordered an entry. To take this path you must:
  1. Reconstruct the FULL corrected sequence for that test's asset: list every recorded pointer index → authored nodeId → expected inspector value, in execution order.
  2. Confirm the WHOLE sequence is correct for node-granular semantics (each pointer = the right node, "as-of entering" state). The landing nodes must be REAL authored node ids (e.g. the Sequence node or a SetVar) — NOT the synthetic `seq-probe-anchor` const masquerading as a node, and NOT skipping a real node.
  3. Update the test's expected indices/values to the verified sequence, and add an inline comment + a report entry explaining WHY (which node was inserted/re-ordered and the new index map).

- **(B) Behavior is WRONG → real regression → FIX THE CODE** (`BlueprintDebugSession.cs` and/or `Stage5_Schedule.cs`), do NOT change the test to pass. Examples of "wrong": the pointer lands on the synthetic anchor const instead of the Sequence node; a real node is skipped; the inspector returns a value that doesn't match the actual per-node execution state; BF-04 lands somewhere that is NOT the first real executed node of the next iteration.

**Special scrutiny — failures (1) and (2), the landing-node tests.** For each, write out: the asset's node graph, the OLD landing nodeId, the NEW landing nodeId, what each id corresponds to (which authored node), and a clear argument for why the NEW landing is the correct "first real node of the continuation." If you cannot justify the new landing as correct, it is bug (B) — fix the mechanism. Do NOT simply replace the expected id with the actual id.

**For failures (3)-(7) (`Expected 10, got 0/20`):** `0` means the pointer is resolving to a node BEFORE the `A=10` write; `20` means AFTER Then1 ran. Determine whether the correct pointer index simply shifted (path A — update the index, and verify the value at the new index is genuinely 10) or whether the recording/restore now captures the wrong state (path B — fix). Verify by walking the whole ring, not by nudging one index.

## Verification (MANDATORY — a previous run lied; do NOT repeat)
Actually BUILD and RUN. NO regen flags.
- `dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests`
- `dotnet test Hrot/Diagnostics/Hrot.Diagnostics.Breakpoints.Tests`
Quote the REAL summary line for each. The ONLY acceptable failures are the documented pre-existing reds: `AiPrimitive_EmitMatchesGoldenSource`(×2), `Stage8_PdbContainsEmbeddedSource`, `Stage8_RoslynCompiler_ProducesNonEmptyPeAndPdb`, `TickFrame_1000Frames_AllocatesZeroBytes`, `MoveToAndFire_GeneratedSource_Snapshot`, `WhenNode_ZeroAllocOnHotPath`. Flaky `AlcUnloadTests.Fixture_AfterMultipleLoads_…` + transient `MapKeyboardKey.idl` → re-run once. Every one of the 7 above MUST be green. If any other test regresses, root-cause it (same A/B rule). The lead will INDEPENDENTLY re-run both suites and hard-review every "path A" justification — a false green WILL be caught.

## Constraints
- You MAY edit: the failing test files (`TickBridgeTests.cs`, `InspectorSnapshotResolutionTests.cs`, `VirtualPointerTests.cs`) for path-A updates, and `BlueprintDebugSession.cs` / `Stage5_Schedule.cs` for path-B real-bug fixes. Do NOT weaken or delete any unrelated test. Do NOT touch the recorder capture/restore unless a path-B bug is proven there (justify).
- Do NOT load or commit any `.bp.json` (a scratch `Count5.bp.json` is modified — IGNORE it). DO NOT COMMIT.
- Report: a per-test table — failure → verdict (A correct-but-stale / B real-bug) → evidence (the corrected pointer→node→value map, or the bug + code fix) → resolution. Then the TRUE Passed/Failed/Skipped for both suites. Be honest: if you cannot make one genuinely pass without masking, say so and stop.
