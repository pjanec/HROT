# BF-BATCH-SEQ2-FIX: Corrective — remove forbidden pragma, green the suite, fix pre-existing failures
**Tasks:** C0 (pragma→real fix), C1 (goldens), C2 (pre-existing failures), C3 (restore Count4)   **Est:** ~10h
**Dependencies:** BF-BATCH-SEQ2-LATENTEMIT (NOT yet committed — your changes are in the working tree). This batch
corrects that work in place; the lead commits the combined result once the suite is fully green.

## Context
The SEQ2-A/B core fixes are correct (verified: the 5 `SequenceEmitIntegrationTests` pass — latent-Sequence compiles,
fresh tick increments, cross-block CS0103 gone). But the batch left **guardrail violations + a non-green suite**, and
the user wants the suite **fully clean (0 failures)** going forward. Fix all of the following.

## Corrective Task 0 (P1): Remove the forbidden CS0162/CS0164 pragma — fix the real cause
The batch instructions **explicitly forbade pragma-suppressing CS0162/CS0164**. You added
`#pragma warning disable CS0162, CS0164` in `InstanceEmitter.cs`. Remove it — and fix the real cause instead.
- **Root cause:** the wait-lowering (`WaitLowering_Instance.cs` / `WaitLowering_AiPrimitive.cs`) assembles **dead
  blocks** for some wait types (e.g. `*_resume_*_not_running_unused`, `*_failure_unused`) whose labels are never
  referenced → CS0164, and whose bodies are unreachable → CS0162.
- **Real fix:** during final block assembly in the wait-lowering, **do not include blocks that are unreachable**
  (no `IrTerm_Goto`/`Branch`/`Suspend` edge anywhere targets their `IrBlockId`, and they are not the entry). Compute
  the set of referenced block ids from all terminators starting at `Entry`; drop unreferenced blocks before
  `graph with { Blocks = ... }`. This eliminates the dead blocks at the source.
- Then **remove BOTH pragmas**: the new one in `InstanceEmitter.cs` **and** the pre-existing CS0162 pragma in
  `AiPrimitiveEmitter.cs` (the dead-block filtering makes it unnecessary too). 
- **Why this matters:** the broad pragma also suppresses CS0162/CS0164 for the *whole* graph body — it would hide a
  future regression where the real entry becomes unreachable again (the exact SEQ2-A bug). The signal must stay live.
- **Verify:** generated source for a latent-Sequence blueprint compiles with **zero** CS diagnostics and **no
  pragmas** anywhere in the emit. Add an assertion to the SEQ2 tests (or a new test) that the generated source
  contains no `#pragma warning disable`.

## Corrective Task 1: Green the 3 brace-shifted goldens (prove semantic equivalence)
Removing per-block `{ }` (SEQ2-B) legitimately shifted 3 golden snapshots
(`Library_EmitMatchesGoldenSource`, `AiPrimitive_EmitMatchesGoldenSource(MoveToAndFire)`,
`AiPrimitive_EmitMatchesGoldenSource(HasVisibleTarget)`).
- Before regenerating, **prove the diff is ONLY brace/indentation removal** (no statement/terminator/value changes):
  capture the old vs new generated source and confirm the only differing lines are removed `{`/`}` and re-indentation.
  State this in the report with the actual diff summary.
- Regenerate the goldens (`BLUEPRINT_REGENERATE_SNAPSHOTS=1`), then **re-run the full suite WITHOUT that env var** for
  the true baseline (regen mode writes instead of compares — it masks failures; never report green from a regen run).

## Corrective Task 2: Fix the 4 long-standing pre-existing failures (user directive — clean suite)
These have failed for several batches; the user wants them fixed so the suite is fully clean. Fix the real cause;
do not skip/suppress silently.
1. **`Library_EmitMatchesGoldenSource` / `LibraryMath_GeneratedSource_Snapshot` / `MoveToAndFire_GeneratedSource_Snapshot`
   — CRLF/LF mismatch:** the golden comparison fails on line-ending differences. Fix by **normalizing line endings on
   both sides of the snapshot comparison** (e.g. `.Replace("\r\n","\n")` before compare) and/or add a `.gitattributes`
   marking the golden `.txt`/snapshot files `-text`/`eol=lf`. Must pass regardless of checkout line endings. (Note:
   some of these may already be covered by Task 1's regeneration — confirm which, and make the comparison
   newline-robust so it stays green cross-platform.)
2. **`Synthesize_EqsResult_ScoreCrossed_IncludesThreshold` — locale decimal separator** (`"0.8"` vs `"0,8"` on a
   `cs-CZ` machine): find whether the **production formatting** is culture-dependent (then fix it to
   `CultureInfo.InvariantCulture`) or only the test is. Prefer fixing the production code if it formats numbers for
   generated output/messages with the current culture — that would be a real latent bug.
3. **`TickFrame_1000Frames_AllocatesZeroBytes` — 3200 bytes allocated vs 0:** investigate what allocates in the
   1000-frame tick path. If it is a **real allocation regression** in the runtime/tick code, fix it (the suite asserts
   zero-alloc by design). If it is genuinely test-harness/JIT/environment noise, make the test deterministic
   (e.g. warm-up + measure steady-state) — do **not** just delete the assertion. Document your finding precisely; if
   you cannot make it reliably green, leave it failing and explain exactly why (do not mask it).

## Corrective Task 3: Restore Count4.bp.json; never modify user experiment assets
You modified `Hrot/Subsystems/Hrot.AI.Behaviors/Blueprints/Count4.bp.json` (+73 lines, added Sequence/Delay). That is
a **user experiment asset** — do not edit it. **Revert it to HEAD** (`git checkout HEAD -- .../Count4.bp.json`). Your
tests already build assets **in-memory** (`SequenceEmitIntegrationTests`), so they don't need it — confirm no test
depends on the modified Count4. (General rule, repeated: never edit/exclude/neuter a user blueprint asset or csproj
include to make anything pass.)

## Corrective Task 4 (P1): latent-in-Sequence never loops — the delay-complete path is a dead no-op
**Symptom (user-reproduced live):** a Sequence `Then0 → increment Count`, `Then1 → Delay(1s)` increments Count to 1
on the first tick, then **freezes at 1 forever** — it never increments again.
**Root cause (from the generated `Count4_F44891A7_Bp.g.cs`):** when the delay elapses, `__block_resume_1_delay_check`
gotos `__block_wait_resume_0`, which is **empty — just `return;`**. It does **not** reset `s.Cursor.ResumeAt = 0` and
does **not** continue execution. So `ResumeAt` stays `1` permanently; every subsequent tick re-enters the delay-check,
sees "elapsed", hits the empty `wait_resume_0`, and no-ops. The latent's **completion/continuation path** is broken.
Compare `__block_resume_1_failure_unused`, which correctly does `s.Cursor.ResumeAt = 0; return;`.
- **Expected behavior:** after the delay completes, execution continues past the latent (here: the Sequence's last
  branch ends → graph completes) and, for a per-tick **Instance** blueprint, the cursor resets (`ResumeAt = 0`) so the
  next tick re-runs from entry → Count increments again. Net: Count climbs ~once per delay period.
- **Investigate + fix in the wait-lowering** (`WaitLowering_Instance.cs`, and `_AiPrimitive.cs` for parity): the
  delay-elapsed branch must route to the latent node's **real continuation** (its exec-out chain / the SEQ1
  `_fallThroughTarget`), and a graph that completes after a latent must reset the cursor for re-entry. Do not leave a
  dead empty resume block. Also verify `WaitUntilTime` is **relative** (`time + duration`), not the absolute literal
  (`1f`) — if it's absolute, the "wait 1 second" is wrong after sim time passes the literal; fix if confirmed.
- **PRESCRIBED test (multi-tick — this is the gap that hid the bug): `Sequence_LatentDelay_LoopsAndReincrements`.**
  Build `EventEntry → Sequence(Then0 → SetVariable Count = Count+1, Then1 → Delay(d))`, `Dispatch=Instance`.
  Compile+load. Tick repeatedly while **advancing `time`** across several delay periods (call `Tick` with increasing
  `time`, e.g. d=0.5: tick at t=0 → Count==1 and suspended; tick at t=0.25 (mid-delay) → Count still 1; tick at
  t=0.6 (delay elapsed) → continuation + cursor reset; next tick at t=0.7 → **Count==2**; continue to assert Count==3
  after another full period). Assert Count strictly increases by 1 per completed delay period (NOT frozen at 1).
  This must drive `Tick` directly (bypassing the host) so it tests the generated control flow, not editor dispatch.

## Success Criteria (prescribed — do not weaken)
- [ ] No `#pragma warning disable CS0162`/`CS0164` anywhere in the emit; dead blocks filtered at the wait-lowering;
      latent-Sequence generated source compiles with zero CS diagnostics.
- [ ] **Full `Hrot.Blueprints.Tests` suite is 100% green — 0 failed** (run without `BLUEPRINT_REGENERATE_SNAPSHOTS`).
      All 4 previously-"pre-existing" failures fixed (or, for zero-alloc only, a documented hard blocker if truly
      unavoidable — with evidence).
- [ ] The 5 `SequenceEmitIntegrationTests` still pass; goldens regenerated with proven brace-only diff.
- [ ] C4: a latent Delay inside a Sequence **loops** — `Sequence_LatentDelay_LoopsAndReincrements` proves Count climbs
      by 1 per completed delay period (not frozen at 1); the delay-complete path resets the cursor + continues; no dead
      empty resume block; `WaitUntilTime` relative not absolute (if that was wrong).
- [ ] `Count4.bp.json` reverted to HEAD; no user asset/csproj neutering.
- [ ] Report at `.dev/_DONE/blueprint-finalize/reports/BF-BATCH-SEQ2-FIX-REPORT.md`: the dead-block-filtering approach; the
      golden diff proof; root cause + fix for each of the 4 pre-existing failures; final suite counts (must be 0
      failed); suggested commit message.

## Autonomy & guardrails
Finish in one go to a fully green suite. **Do not** use pragmas, skips, neutered assets, or weakened assertions to get
there — fix the real causes. Fail loud only where a case is genuinely unsupported, with evidence. Read
`.dev/.guides/DEV-GUIDE.md` first.
