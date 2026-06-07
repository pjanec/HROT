# BF-BATCH-SEQ1: Implement SequenceNode branch scheduling in Stage 5
**Tasks:** SEQ1 (scheduler), SEQ1-T (BP1412 test reconciliation)   **Phase:** Blueprint compiler   **Est:** ~14h
**Dependencies:** BF-BATCH-EXECFANOUT (BP1411), BF-BATCH-DIAGFAIL-REBUILD (BP1412) — both committed.

## Background — the gap
`SequenceNode` exists in the node registry (`BuiltInNodeRegistry.SequencePins` → exec-in + `Then0`/`Then1` exec-outs),
the JSON schema, and the editor palette — but **`Stage5_Schedule` never schedules its branches.** `ScheduleBlock`
(`Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Stages/Stage5_Schedule.cs`) has no `case SequenceNode`,
so a Sequence falls into `default`: it emits no statements (the `case SequenceNode:` at ~line 821 in the
statement switch returns nothing) and `GetSingleExecSuccessor` returns null (Sequence has ≠1 exec-out pin), so the
exec chain **dead-ends** — every branch is dropped and the `Tick` body comes out empty. (Since BF-BATCH-DIAGFAIL this
now correctly raises **BP1412** instead of silently miscompiling — this batch makes Sequence actually *work*, after
which BP1412 must NOT fire for a properly-scheduled Sequence.)

## Semantics to implement (Unreal-style Sequence)
A `SequenceNode` runs its connected `Then` outputs **in order**: `Then0`'s exec chain runs to completion, then
`Then1`'s, etc. After the **last** connected branch completes, the Sequence's execution ends (falls through to
whatever the Sequence node's own continuation was — for a top-level Sequence that is the end of the tick). A branch
that ends in a `ReturnNode` terminates the graph early (subsequent branches do not run — correct). Unconnected `Then`
pins are skipped. Branches may themselves contain Branch/Sequence/latent nodes.

## Architecture you'll use (already present — study it first)
- **`IrTerm_Goto(IrBlockId Target)`** (`Compiler/Ir/IrBlock.cs:10`) — unconditional jump; the emitter renders it as
  `goto __block_<label>;` (`Compiler/Emit/TerminatorEmitter.cs:12`). The whole emit model is a labelled-block goto
  state machine, so chaining blocks with `IrTerm_Goto` is the idiom.
- **`ScheduleBranchNode`** (`Stage5_Schedule.cs:~390-426`) is your template: it `AllocBlock`s per successor, sets a
  terminator on the current block, and `_bfsQueue.Enqueue((block, successorNode))` for each branch. Follow this shape.
- **`ScheduleLatentNode`** (`~290-341`) shows how a node that suspends allocates a **resume block** and enqueues its
  continuation there — relevant to the latent-branch case below.
- **`GetSingleExecSuccessor`** (`~1330`) and `GetBranchSuccessors` (`~1340`) show how to resolve a successor node
  from a specific exec-out pin's outgoing link.

## Tasks
Complete in sequence; do NOT start SEQ1-T until SEQ1 is implemented and its tests are written.

### Task 1: Schedule SequenceNode branches (SEQ1) — file: `Stage5_Schedule.cs` (UPDATE)
- Add `case SequenceNode seq:` to the `ScheduleBlock` switch → call a new `ScheduleSequenceNode(seq, bb)` and `return`
  (mirror how `BranchNode`/`WhenNode` are dispatched).
- `ScheduleSequenceNode`:
  1. Resolve the ordered list of connected `Then` successors: for each exec-out pin of the node **in ascending pin
     order** (`Then0`, `Then1`, …; order by the numeric suffix of the pin Name, fall back to Pins-list order), find
     its single outgoing link's target node. Skip pins with no link.
  2. If there are **zero** connected branches → seal the current block as fall-through (see the helper in step 5) and
     return.
  3. Allocate one block per connected branch (`AllocBlock($"seq_{idShort}_then{i}")`).
  4. Set the current block's terminator to `IrTerm_Goto(firstBranchBlock)`.
  5. **Chain the branches:** after branch *i*'s chain *falls through*, control must continue to branch *i+1*'s block;
     after the **last** branch falls through, control falls through (ends). Implement this with a
     **fall-through redirect**: add `private readonly Dictionary<int, IrBlockId> _fallThroughTarget = new();`,
     register `_fallThroughTarget[branchBlock_i] = branchBlock_{i+1}` for every branch except the last, and
     `_bfsQueue.Enqueue((branchBlock_i, successor_i))` for each.
  6. **Centralize fall-through sealing:** introduce a helper, e.g.
     `private void SealFallThrough(int blockId, BlockBuilder bb)` that sets
     `bb.Terminator = _fallThroughTarget.TryGetValue(blockId, out var t) ? new IrTerm_Goto(t) : new IrTerm_FallThrough()`.
     Replace **every** site in `ScheduleBlock`/latent handling that currently assigns `new IrTerm_FallThrough { … }`
     (the `EventEntryNode`-null case ~line 240, the `default`-null case ~line 285, and the latent "empty resume
     block" path ~line 338) with a call to this helper so the redirect is honoured wherever a branch's chain ends.
     (`BlockBuilder` exposes its block id — use it; if not, pass the id through.)
- **Latent / Branch nodes inside a Sequence branch (REQUIRED, do not silently miscompile):** if branch *i* contains a
  node that *splits* the block (a latent suspend/resume or a nested Branch/Sequence), the fall-through that ends
  branch *i* happens in a **later** block (the resume/continuation block), not `branchBlock_i`. Propagate the
  continuation: when a split allocates a downstream block whose fall-through represents "end of this branch", carry
  the `_fallThroughTarget` to that block (e.g. in `ScheduleLatentNode`, after allocating the resume block,
  `if (_fallThroughTarget.TryGetValue(currentBlockId, out var t)) _fallThroughTarget[resumeBlockId] = t;` — and
  likewise for nested Branch/Sequence exit blocks). Verify nested cases with tests.
  - If, and only if, you hit a genuine architectural blocker making correct latent-in-Sequence propagation infeasible
    in this batch, do NOT miscompile: add a new diagnostic `BP1413` (next free code) — *"A latent/suspending node
    inside a Sequence branch is not yet supported"* — emitted from the scheduler, and document the deferral in the
    report. (Prefer making it work; this is the safety valve, not the goal.)
- Do not change `GetSingleExecSuccessor` (Sequence is handled by its own case now).
**Tests required** (`Hrot.Blueprints.Tests`, `Stage5_ScheduleTests`):
- **Two synchronous branches run in order:** `EventEntry → Sequence`, `Then0 → SetVariable A`, `Then1 → SetVariable B`.
  Assert the scheduled IR chains the blocks correctly (current block `Goto` then0; then0 block ends `Goto` then1;
  then1 ends fall-through) **and** that **no BP1412** is emitted. Then an **emit/golden** assertion that the generated
  C# performs A then B (both statements present, A before B).
- **Runtime (gold standard, where feasible):** compile an Instance or Library blueprint with a Sequence of two
  `SetVariable` branches, execute the tick, and assert **both** variables hold their set values (proves order +
  both run). Use the existing test fixtures/harness for compile-and-run (see other Stage5/runtime tests).
- **Unconnected Then pin:** `Then1` not linked → only `Then0` runs; **no BP1412**, no crash.
- **Branch ends in Return short-circuits:** `Then0 → Return` (Success), `Then1 → SetVariable` → assert `Then1`'s
  statement is NOT emitted/reachable (Return terminates; the post-Then0 Goto is not produced).
- **Nested Sequence:** `Then1`'s successor is another Sequence → assert both inner branches run after `Then0`, and the
  outer sequence still terminates cleanly (continuation propagates).
- **Latent branch (per chosen approach):** a Sequence whose `Then0` is a latent node (e.g. `WaitForChannelNode` or an
  inline-latent action) followed by `Then1` synchronous → assert the resume path continues to `Then1` (if
  implemented), OR assert **BP1413** is emitted (if deferred). Do not leave this untested.

### Task 2: Reconcile the BP1412 test (SEQ1-T) — file: `BP1412_DroppedExecSuccessorsTests.cs` (UPDATE)
- `Schedule_SequenceNode_LinkedExecOuts_Dropped_EmitsBP1412_Error` and
  `Schedule_DroppedSuccessor_DiagnosticHasNodeId` were written when a linked Sequence was a *dropped-successor* error.
  After SEQ1 a linked Sequence is **correctly scheduled**, so these must change: a Sequence with connected `Then`
  pins must **NOT** emit BP1412. Update them so BP1412's coverage is preserved via the **still-valid** trigger —
  the **unresolved exec link** case (`Schedule_UnresolvedExecLink_EmitsBP1412_Error` stays) — and convert the
  Sequence-based assertions to assert correct scheduling (no BP1412). Ensure `[CoversDiagnosticCode("BP1412")]`
  remains on at least one test (the unresolved-link test) so the coverage meta-test passes.

## Success Criteria
- [ ] A `SequenceNode` with connected `Then0`/`Then1` schedules both branches in order; generated `Tick` runs branch 0
      then branch 1; proven by IR + emit + (where feasible) runtime tests.
- [ ] No BP1412 for a correctly-scheduled Sequence; BP1412 still fires for unresolved exec links.
- [ ] Unconnected pins, Return-short-circuit, nested Sequence, and the latent-branch case all covered by tests
      (latent either works or raises BP1413, documented).
- [ ] Full `Hrot.Blueprints.Tests` suite green (4 known pre-existing failures may remain: 2 CRLF snapshot, 1 locale
      decimal, 1 zero-alloc — confirm the count is unchanged); no new warnings.
- [ ] Report at `.dev/blueprint-finalize/reports/BF-BATCH-SEQ1-REPORT.md`, every section filled.

## Report Requirements
Per `.dev/.guides/DEV-GUIDE.md`: implementation summary; how branch chaining + fall-through redirect works; how the
latent-branch case was handled (implemented vs BP1413-deferred, with reasoning); deviations (WHAT/WHY/BENEFIT/RISK);
**actual test-run counts** + the new scenarios; weak points; suggested one-line commit message. Do NOT ask
comprehension questions.

## Autonomy & guardrails
Finish in one go: implement, run the **full** suite, fix root causes to green, then write the report. Do not stop for
permission. **Never swallow an error, drop a branch, or edit/exclude/neuter a blueprint asset (or csproj include) to
make a build pass** — fix the real code. Fail loud (a diagnostic), never silent. Only stop on a genuine breaking
design flaw (document it and stop).
