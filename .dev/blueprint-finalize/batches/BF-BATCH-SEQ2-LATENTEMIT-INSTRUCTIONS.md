# BF-BATCH-SEQ2-LATENTEMIT: Fix SequenceNode × latent/data emit bugs
**Tasks:** SEQ2-A (resume-dispatch entry), SEQ2-B (cross-block value scope)   **Phase:** Blueprint compiler/emit   **Est:** ~12h
**Dependencies:** BF-BATCH-SEQ1 (11964f6b) — Sequence scheduling. This fixes two emit bugs SEQ1 exposed.

## Background — two real bugs (reproduced by the user)
A blueprint `EventEntry → Sequence(Then0 → increment chain, Then1 → <X>)`:
- `<X>` = a single non-latent node → **compiles & works.**
- `<X>` = our demo action node → **CS0103** `The name '__t3'/'__t4'/'__t5' does not exist in the current context`.
- `<X>` = a `LatentDelay` (1s) → **CS0162 Unreachable code** + **CS0164 label not referenced**, and at runtime the
  counter never increments.

### Bug B (SEQ2-A) — resume dispatch skips the real entry (the latent case)
The generated `Count4_F44891A7_Bp.g.cs` `Tick` opens with a resume-dispatch prologue:
```csharp
{   uint __t5 = s.Cursor.ResumeAt;  var __t6 = 0u;  var __t7 = (__t5 == __t6);
    if (__t7) goto __block_seq_5f22e816_then1;        // <-- FRESH START jumps into the LATENT branch
    else      goto __block_resume_1_delay_check; }
__block_entry: { goto __block_seq_5f22e816_then0; }   // <-- UNREACHABLE (nothing gotos __block_entry)
__block_seq_5f22e816_then0: { /* increment */ goto __block_seq_5f22e816_then1; }  // <-- UNREACHABLE
__block_seq_5f22e816_then1: { /* delay setup */ return; }
```
On a **fresh tick** (`ResumeAt == 0`) the dispatch jumps straight to `seq_then1` (the latent/delay block), **skipping
`__block_entry` → `seq_then0` (the increment)**. So those blocks are unreachable (CS0162/CS0164) and the counter
never runs. **Root cause:** the wait-lowering's dispatch block uses the wrong "fresh start" target. Pre-SEQ1 the
graph's entry *was* the first latent's pre-suspend block, so "fresh → that block" worked. With SEQ1 there are now
blocks **before** the latent (the Sequence dispatch + earlier branches); the fresh-start branch must goto the graph's
**original `Entry` block**, and all pre-latent blocks must be retained & reachable.
- **File:** `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Compiler/Compiler/Lowering/WaitLowering_Instance.cs`
  (Instance dispatch — this asset is `Dispatch=Instance`). Apply the same fix to
  `WaitLowering_AiPrimitive.cs` (phase-byte dispatch) which has the identical structure (`dispatchBlockId`,
  fresh-vs-resume branch). The dispatch's fresh-start edge currently targets the latent's block; it must target the
  pre-lowering graph `Entry` (the block the original graph started at — `seq_...` entry chain), so execution runs
  the pre-latent blocks first, reaches the latent, suspends, and resumes via the resume edge.

### Bug A (SEQ2-B) — cross-block local temps go out of scope (CS0103)
Each IR block is emitted wrapped in its own `{ }` (see `AiPrimitiveEmitter`/the block/statement emitter), so a
`var __tN = …` declared in block X is **not in scope** in block Y. SEQ1's multi-block split makes a value produced in
one block and consumed in another common (e.g. a value computed before the Sequence/branch split and used in a later
branch block) → CS0103. **Root cause:** SSA temp locals are block-scoped but referenced across the goto state
machine's blocks.
- **Fix direction (Zoo decides exact impl):** in a `goto`-based state machine, locals shared across labels must live
  in the **method scope**, not per-block braces. Either (a) **hoist** all `__tN` declarations to the top of `Tick`
  (declare `T __tN;` once; assign `__tN = …` inside blocks), or (b) stop wrapping each block body in `{ }` so all
  blocks share the method scope. Preserve current behavior for single-block graphs (no regressions to existing
  goldens). Whatever you choose, the generated C# must compile.
- **Files:** the block/statement emitter — `Compiler/Emit/StatementEmitter.cs`, `Compiler/Emit/AiPrimitiveEmitter.cs`,
  and/or wherever blocks/values are rendered (`EmissionContext`, the function-body emitter). Find where `{`/`}` wrap a
  block and where `var __tN` is emitted.

## Onboarding (read in order)
1. `.dev/.guides/DEV-GUIDE.md` — your contract (esp. §3 test quality: the **gold standard is compile+run**).
2. This file. The generated-code evidence above is the spec.
3. `.dev/blueprint-finalize/reviews/BF-BATCH-SEQ1-REVIEW.md` — context on the SEQ1 IR + the debt this addresses.

## Tasks
Complete in sequence; do NOT start SEQ2-B until SEQ2-A is implemented and its tests pass.

### Task 1 (SEQ2-A): fix fresh-start dispatch target — files: `WaitLowering_Instance.cs`, `WaitLowering_AiPrimitive.cs`
Make the dispatch block's fresh-start edge goto the graph's **original Entry block** (so all pre-latent blocks run),
not the latent's block. Retain every original block. After the fix, for a Sequence-with-latent graph: fresh tick runs
entry → all pre-latent branches → reaches the latent → suspends; resume tick takes the resume edge. No unreachable
blocks, no unreferenced labels.

### Task 2 (SEQ2-B): fix cross-block local scope — emitter
Ensure SSA temp locals referenced across blocks are in scope (hoist to method scope, or share method scope). No CS0103.

## Tests required — PRESCRIBED (implement exactly these assertions; do NOT substitute weaker ones)
Use the existing **compile-the-generated-source** harness (the one the golden/runtime tests use — e.g.
`BlueprintTestFixture` / whatever compiles `.g.cs` via Roslyn and loads it). The whole point is to catch CS0103/
CS0162/CS0164, which IR-only tests miss. Put these in a new `Hrot.Blueprints.Tests` file.

1. **`Sequence_LatentBranch_GeneratedSourceCompilesCleanly`** — graph: `EventEntry → Sequence(Then0 → SetVariable
   "Count" via AddInt(Count,1); Then1 → LatentDelay(1.0s))`, `Dispatch=Instance`. Compile the generated C# with Roslyn
   **as the build does (warnings-as-errors / at least flag CS0162 & CS0164)** and assert **zero diagnostics** — no
   CS0162, no CS0164, no CS0103. (This is the exact failing repro.)
2. **`Sequence_LatentBranch_FreshTick_RunsPreLatentBranch`** — compile+load the above and invoke `Tick` once with a
   fresh state (`ResumeAt == 0`); assert `State.Count == 1` (the increment branch ran on the fresh tick — proves the
   dispatch reaches the real entry, not the latent block).
3. **`Sequence_DataValueCrossesBranchBlocks_CompilesCleanly`** — a Sequence where a value computed in/Before one
   branch is consumed in a later block (reproduce the CS0103 shape: e.g. Then0 increments and Then1 reads `Count` into
   a node input, or a pre-split pure value used in a branch). Compile the generated C#; assert **no CS0103**.
4. **`Sequence_TwoSyncBranches_BothSideEffectsRun`** (pays the SEQ1 runtime debt) — `Sequence(Then0 → SetVariable A=1,
   Then1 → SetVariable B=2)`; compile+load; invoke `Tick`; assert **both** `A==1` and `B==2` after one tick (order +
   both run). 
5. **AiPrimitive parity:** repeat test 1 for a `Dispatch=AiPrimitive` blueprint (the `WaitLowering_AiPrimitive` path)
   with a latent branch — assert the generated source compiles cleanly.

If a compile-and-load harness does not already exist, say so in the report and use the closest existing mechanism
(e.g. the golden-source Roslyn compile used by `LibraryEmitGoldenTests`); do NOT downgrade to string-contains
assertions as the primary check.

## Success Criteria
- [ ] SEQ2-A: Sequence-with-latent generates reachable entry; fresh tick runs the pre-latent branch; no CS0162/CS0164;
      counter increments. Instance + AiPrimitive.
- [ ] SEQ2-B: cross-block values compile; no CS0103.
- [ ] All 5 prescribed tests pass and actually compile the generated source via Roslyn.
- [ ] Full `Hrot.Blueprints.Tests` suite green (4 known pre-existing failures may remain — confirm count unchanged;
      note that 2 are CRLF-snapshot goldens which your emit change may shift: if your change alters generated output,
      regenerate those goldens deliberately and say so, do NOT mask a real diff). No new warnings.
- [ ] Report at `.dev/blueprint-finalize/reports/BF-BATCH-SEQ2-LATENTEMIT-REPORT.md`, every section filled.

## Report Requirements
Per `DEV-GUIDE.md`: root-cause confirmation for each bug (with the before/after generated snippet); the fix chosen for
SEQ2-B (hoist vs shared-scope) and why; **actual test-run counts**; whether any golden was regenerated and the diff
rationale; deviations; weak points; suggested commit message. Do NOT ask comprehension questions.

## Autonomy & guardrails
Finish in one go: implement, run the **full** suite, fix root causes to green, then report. **Never** neuter/exclude a
blueprint asset or csproj include, suppress CS0162/CS0164/CS0103 via pragmas, or weaken the prescribed assertions to
make a build pass — fix the real emit. Fail loud where a case is genuinely unsupported. Only stop on a real breaking
design flaw (document it and stop).
