# BF-BATCH-SEQ2-FIX2: Corrective round 2 — fix the CS0159 regression + finish the unfinished items
**Est:** ~8h   **Dependencies:** the uncommitted SEQ2-LATENTEMIT + SEQ2-FIX working-tree changes (lead has NOT committed).

## Why this round exists
The previous corrective batch (SEQ2-FIX) **regressed** the working state and left 4 items unfinished. Current full
`Hrot.Blueprints.Tests` result: **6 failed** (was passing the latent compile before). Fix ALL of the following to a
**100% green suite (0 failed), no regen flag, no pragmas, no scope creep.** Read `.dev/.guides/DEV-GUIDE.md` first.

## P0 — REGRESSION you introduced: CS0159 dangling goto (dead-block filter is over-aggressive)
`Sequence_LatentDelay_LoopsAndReincrements` and `Sequence_LatentBranch_FreshTick_RunsPreLatentBranch` now FAIL with:
`BP7001: Roslyn: CS0159 No such label '__block_block_1' within the scope of the goto statement`.
Your C0 dead-block filtering removed a block that is **still a `goto`/branch/suspend target**, leaving a dangling
jump. **Root cause:** the reachability computation is wrong/incomplete.
- **Fix:** compute the reachable block set by BFS/DFS from `graph.Entry`, following **every** terminator successor
  edge: `IrTerm_Goto.Target`, `IrTerm_Branch.IfTrue` + `IfFalse`, `IrTerm_Suspend.ResumeBlock` (and any other
  terminator that names a block). Only drop blocks **not** in that reachable set. A block that any retained
  terminator targets must be retained. After filtering, **no terminator may reference a dropped block.**
- **Prescribed guard test** `WaitLowering_NoDanglingGotoTargets`: for a representative latent + latent-in-Sequence
  graph, after Stage 8 emit, assert the generated source has **no `goto __block_X` whose label `__block_X:` is
  absent** (and/or assert Roslyn compile is clean). This must make CS0159 impossible to reintroduce.

## P0 — C4 STILL NOT WORKING: latent-in-Sequence must loop
Once CS0159 is fixed, `Sequence_LatentDelay_LoopsAndReincrements` must **pass**: build
`EventEntry → Sequence(Then0 → Count=Count+1, Then1 → Delay(d))`, Instance; drive `Tick` directly across several
delay periods advancing `time`; assert Count climbs 1→2→3 (one increment per completed delay period), NOT frozen at 1.
The delay-complete path must reset the cursor (`ResumeAt=0`) + continue (no dead empty resume block); confirm
`WaitUntilTime` is relative (`time + duration`). `Sequence_LatentBranch_FreshTick_RunsPreLatentBranch` (Count==1 on
fresh tick) must also pass again.

## C1 — goldens STILL red (not done last round)
`AiPrimitive_EmitMatchesGoldenSource(MoveToAndFire)`, `(HasVisibleTarget)`, `MoveToAndFire_GeneratedSource_Snapshot`
fail. Regenerate after proving the diff is ONLY brace removal (+ any intended wait-lowering block-set change from the
reachability fix — explain it), then re-run the suite WITHOUT `BLUEPRINT_REGENERATE_SNAPSHOTS` and confirm green.

## C2 — zero-alloc STILL red (not done last round)
`TickFrame_1000Frames_AllocatesZeroBytes` still fails (you edited `AllocationFreeTests.cs` but it's not green). Either
fix the real allocation in the tick path, or make the measurement deterministic (warm-up + steady-state). Do NOT
delete the assertion. If genuinely unfixable, leave it failing with a precise documented reason — but try first.

## Revert the scope creep (you were NOT asked to touch these)
- **`FullRebuildService.cs`:** revert your `--no-incremental` change. REBUILDREFRESH was already fixed (committed) via
  `<UpToDateCheckInput>` — that is the agreed design; `--no-incremental` re-litigates a committed decision and makes
  every Full Rebuild a slow full-solution build. Restore the original `build` / `build {target}` args.
- **`.dev/blueprint-finalize/reports/BF-BATCH-DIAGFAIL-REBUILD-REPORT.md`:** you rewrote this **committed** report.
  Restore it: `git checkout HEAD -- .dev/blueprint-finalize/reports/BF-BATCH-DIAGFAIL-REBUILD-REPORT.md`.
- **Duplicate script:** you added `.dev/blueprint-finalize/verify-rebuildrefresh.ps1` — the committed repro is
  `BF-REBUILDREFRESH-REPRO.ps1`. Delete the duplicate unless it adds something the committed one lacks (then explain).
- Write the report at the CORRECT path this time: `.dev/blueprint-finalize/reports/BF-BATCH-SEQ2-FIX-REPORT.md`
  (do not edit any other batch's report).

## Success Criteria (prescribed — do not weaken)
- [ ] Full `Hrot.Blueprints.Tests` = **0 failed** without the regen env var.
- [ ] No CS0159/CS0162/CS0164/CS0103 in any generated source; no `#pragma warning disable` in the emit; no dangling
      goto (guard test passes).
- [ ] `Sequence_LatentDelay_LoopsAndReincrements` + the other 4 SequenceEmitIntegrationTests pass.
- [ ] FullRebuildService + DIAGFAIL report reverted; Count4.bp.json still at HEAD; no asset/csproj neutering.
- [ ] `BF-BATCH-SEQ2-FIX-REPORT.md` written at the correct path with: the reachability fix, golden diff proof, C2
      finding, final suite counts (0 failed).

## Autonomy & guardrails
Stay strictly in scope (the items above). Do NOT modify committed files from other batches, change committed design
decisions, suppress diagnostics with pragmas, neuter assets, or weaken assertions. Fix real causes; fully green suite.

## DO NOT STOP UNTIL VERIFIED GREEN (mandatory)
You must **run the full `Hrot.Blueprints.Tests` suite yourself** (the exact command:
`dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests` — **without** `BLUEPRINT_REGENERATE_SNAPSHOTS`) and
read the result. **The batch is NOT complete until that run reports `Failed: 0`.** If ANY test fails, you are not
done: diagnose the root cause, fix it, and **re-run the full suite again**. Repeat this loop until `Failed: 0`. Do
**not** write the report, do **not** declare success, and do **not** return while any test is red. Reporting "complete"
with failing tests (as the previous round did — it stopped at 6 failed) is a batch failure. The final action before
writing the report must be a full-suite run pasted into the report showing `Failed: 0`. The 5 SequenceEmitIntegration
tests + `Sequence_LatentDelay_LoopsAndReincrements` must each pass. If after genuine, repeated effort a single test is
truly unfixable (e.g. an environment-only zero-alloc artifact), that is the ONLY case you may stop with it red — and
you must document the exact evidence; everything else must be green.
