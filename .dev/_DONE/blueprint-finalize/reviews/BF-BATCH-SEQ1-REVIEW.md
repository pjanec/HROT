# BF-BATCH-SEQ1 Review
**Status:** ✅ APPROVED (lead added 2 hardening tests)   **Date:** 2026-06-07   **Agent:** Zoo (experimental)

## Summary
`SequenceNode` branch scheduling is correctly implemented: `ScheduleSequenceNode` allocates a block per connected
`Then` successor, chains them with `IrTerm_Goto` via a centralized `SealFallThrough` + `_fallThroughTarget` redirect,
and **propagates the continuation through latent resume blocks, Branch arms, When exits, and nested Sequences** — so
after a branch (even a suspending one) completes, control continues to the next branch. BP1412 reconciliation is
correct. Verified: full suite 1638 pass / 4 pre-existing fail / 8 skipped.

## Code: correct
`ScheduleSequenceNode` (ordered Then pins, skip-unconnected, Goto-chain, outer-target→last-branch for nesting) and the
propagation hooks all check out by trace. The empty-resume path now seals explicitly (was implicit). `netstandard2.0`
portability respected (`[Count-1]` not `[^1]`).

## Issues Found
### Issue 1 (P1, lead-fixed in-batch): the propagation tests didn't test propagation
**Files:** `SequenceSchedulingTests.cs` scenarios 5 (latent) & 7 (branch-inside). **Problem:** both wired the inner
branches to end in `ReturnNode`, which short-circuits the chain **before** the fall-through-to-next-branch
propagation is ever exercised — so the trickiest, most error-prone logic (continuation through latent/branch splits)
was effectively **unverified**, while the test names implied otherwise. **Fix (lead):** added two hardening tests with
**fall-through** branches and the discriminating assertion — `ScheduleSequenceNode` always *schedules* the Then1
block, so propagation is the only thing that makes anything *jump* to it; if broken, Then1 is an unreachable orphan:
- `Schedule_LatentInSequence_FallThrough_ResumeReachesThen1` — asserts the latent resume Gotos Then1 (reachable).
- `Schedule_BranchInSequence_FallThrough_BothArmsReachThen1` — asserts Then1 has ≥2 incoming Gotos (both arms).
Both pass → propagation is genuinely correct.

### Issue 2 (P2, debt): no gold-standard compile+run test
The batch asked for a runtime test (compile a Sequence blueprint, execute, assert both branches' side effects). Zoo
delivered IR-structural tests only. The IR assertions + Goto-emit (proven by existing Branch goldens) give high
confidence, but a true runtime test is owed — fold into a later batch.

### Minor: `BP1413` defined but never emitted (full propagation made the safety-valve unnecessary). Reserved +
documented in `KnownNotYetEmittedCodes`; acceptable as a reserved code.

## Agent note (Zoo, 3rd data point)
Strong design (correct CPS-continuation reasoning, nested/latent handling) — but the **recurring weakness this time was
test honesty**: plausible-looking tests that don't exercise the critical path and set their own (weak) success
conditions. Going forward, **prescribe exact assertions in the batch**; don't let Zoo define success criteria.

## Verdict
APPROVED. Lead added 2 hardening tests; runtime test deferred to debt.

## Commit Message
```
feat(blueprints): SequenceNode branch scheduling in Stage 5 (BF-BATCH-SEQ1)

Implements SequenceNode: schedule each connected Then branch in its own block, chained with
IrTerm_Goto via a centralized SealFallThrough + _fallThroughTarget redirect. Continuation
propagates through latent resume blocks, Branch arms, WhenNode exits, and nested Sequences,
so control continues to the next branch after each (incl. suspending) branch completes.
Reconcile BP1412: a correctly-scheduled Sequence no longer trips dropped-successor. Reserve
BP1413 (latent-in-Sequence safety valve; unused — propagation handles it).
Tests: 7 (Zoo) + 2 lead-added hardening (latent/branch propagation reach Then1); suite 1638 pass / 4 pre-existing.
```
