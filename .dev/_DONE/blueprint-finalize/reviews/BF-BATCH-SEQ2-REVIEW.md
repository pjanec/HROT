# BF-BATCH-SEQ2 (LATENTEMIT + FIX + FIX2) Review
**Status:** ✅ APPROVED (lead-curated)   **Date:** 2026-06-07   **Agent:** Zoo (3 rounds)

## Summary
Fixes the two SEQ1-exposed emit bugs + makes latent-in-Sequence loop. SEQ2-A: wait-lowering fresh-start dispatch →
`graph.Entry` (was `suspendBlocks[0]`), so pre-latent blocks run. SEQ2-B: dropped per-block `{}` so SSA temps share
method scope (fixes CS0103). Dead-block filtering by reachability from Entry (fixes the round-1 CS0159 regression) —
no pragmas. Latent-complete path resets the cursor + continues, so the Sequence loop re-runs. Verified: full suite
**1 failed** (only the pre-existing zero-alloc), 1647 passed.

## Round history (Zoo struggled with the multi-part scope)
- **LATENTEMIT:** core fixes correct but used a **forbidden pragma** + left 3 goldens red + modified Count4.
- **FIX (round 1):** **regressed** — over-aggressive dead-block filter caused CS0159 dangling goto (broke a passing
  test); C1/C2/C4 unfinished; **scope creep** (FullRebuildService `--no-incremental`, rewrote a committed report).
- **FIX2 (round 2):** fixed CS0159 (correct reachability), C4 loop passes, goldens regenerated, locale fixed, scope
  creep reverted. **Left litter:** a debug `File.WriteAllText` in `InMemoryRoslynCompiler.cs` + a `$null` junk file →
  **lead reverted/deleted** (commit hygiene).

## Outstanding (NOT blockers for this commit)
1. **Delay timing is wrong (real bug, follow-up):** generated `WaitUntilTime = <duration>` is **absolute**, not
   `time + duration`. The loop-freeze is fixed (Count climbs), but after sim-time passes the literal the delay no
   longer waits → increments every tick. The C4 test was too lenient (asserted "Count climbs", not "waits full
   duration each period"). → small focused follow-up batch.
2. **Zero-alloc test red (pre-existing, documented):** `TickFrame_1000Frames_AllocatesZeroBytes` ~3.2 bytes/entity/
   frame, genuine runtime alloc (`EntityQuery.ForEach` closures), unrelated to SEQ2. Carried as pre-existing.
3. **"Count not in runtime inspector"** (user, live) — needs separate diagnosis (likely StructureHash/instance or
   inspector display, or just the fast increment from bug #1). Pending user clarification.

## Verdict
APPROVED for: emit (BlockEmitter, InstanceEmitter, AiPrimitiveEmitter), wait-lowering (Instance, AiPrimitive),
PreviewSynthesizer (locale invariant), AllocationFreeTests/TestData (robustness), SequenceEmitIntegrationTests. Lead
excluded Count4.bp.json (user experiment), reverted InMemoryRoslynCompiler debug line, deleted `$null`.

## Lesson (recorded)
Zoo handles **small focused single-objective tasks** well but loses focus / stops early / creeps scope on multi-part
batches. Future: one objective per batch, crisp prescribed success conditions, explicit do-not-stop-until-green.
