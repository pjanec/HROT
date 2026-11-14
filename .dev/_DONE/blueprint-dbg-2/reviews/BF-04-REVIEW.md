# BF-04 Review
**Status:** ✅ APPROVED   **Date:** 2026-06-10   (corrects BF-03 `134eb197`)

## Summary
Step-past-end-of-tick now lands on the **first node of the next iteration** instead of the user breakpoint. New `StepFromNodeOrNextIteration`: non-terminal successor → existing BF-03 path (land on successor); all-terminal (end-of-tick, e.g. Delay→Return) → temp BP on the `EventEntryNode`'s exec-successor(s) (first executable node) + resume; degenerate → Continue(). CF-6 `Step()`/`StepFromNode` terminal→Continue unchanged.

## Verification performed (independent)
- **Scope:** only `BlueprintDebugSession.cs` + `TickBridgeTests.cs` + docs. `Count5.bp.json` untracked, NOT committed.
- **Fix diff** read: end-of-tick branch routes to `StepFromNodeOrNextIteration`; first-node target = non-terminal exec-successors of the graph's single `EventEntryNode`; temp BP + resume; degenerate (no entry / no non-terminal entry-successor) → Continue(). Matches the agreed semantics ("step past Delay → first breakpoint-able node, skipping Return"; Continue stays the only path to the breakpoint).
- **Test 8** (`TickBridge_StepPastEndOfTick_LandsOnFirstNode_NotBreakpoint`) — the discriminating test: Count5 shape (BP on `svId` in Then0; `seqId` is the entry successor ≠ BP). Asserts `landingNodeId == seqId` AND `!= svId`. Passes. Test 7 updated (terminal → first-node temp BP, not Continue), Test 9 (non-terminal post-latent → lands on successor, BF-03 preserved). All pass.
- **Full `Hrot.Blueprints.Tests`:** 1746 passed / 7 failed (documented pre-existing reds) / 8 skipped / 1761 total = 1759 + 2 new. Zero new failures. Breakpoints 128/128.

## Notes
- `firstNodes` filters entry-successors to non-terminal; a degenerate single-node graph (Entry→Node→end) would Continue() instead of landing — acceptable (the BP is that node anyway).
- Frame timing: for Delay→Return, the resume/Return runs frame N+1 and Entry re-runs N+2 (per WaitLowering); test reflects this. Verified by the passing landing-on-seqId assertion.

## Verdict
APPROVED — committed. User to confirm via visual smoke that step-past-Delay lands on the first node.
