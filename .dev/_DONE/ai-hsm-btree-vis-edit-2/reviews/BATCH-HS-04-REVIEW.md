# BATCH-HS-04 Review — TASK-HS-04 delete transition + container collapse

**Reviewer:** Dev Lead · **Date:** 2026-06-13 · **Status:** ✅ APPROVED · **Impl:** Zoo

## Verification (independent — read diff, re-ran suite)
- **ApplyRemoveLinks refactor:** body replaced with `FindTransitionByVisualId` → `RemoveTransitionInternal(transition)`. Dedupes the BB1 var-cleanup logic onto the HS-02 helper AND fixes the latent bug — the old code removed only from `Source.OutgoingTransitions`, leaving the transition in `_visualIdToTransition`/`_flatIndexToTransition` (still findable). Now `UnregisterTransition` clears all maps + the outgoing list.
- **ApplySetContainerCollapsed:** `FindStateByStableId(cmd.ContainerId.Value)` → `state.IsCollapsed = cmd.IsCollapsed`. Correct field names (confirmed against the record).
- **No cheating:** touched only the two named methods + new test file. No other handlers changed.
- **Tests (9, behavioral):** full transition removal incl. **`FindTransitionByVisualId` null** (the regression guard for the map bug); source/target states survive; BB1 auto-var removed vs shared-var preserved; unknown-id no-op; collapse true/false round-trip. Assert maps/flags/counts, not strings.
- **Re-run (no regenerate flag):** `Hrot.Hsm.Editor.Tests` **417/0** (9 new, 0 pre-existing failures). Build 0 errors.

## Issues
None.

## Verdict
APPROVED. **Completes the HSM command-sink create/delete ops (HS-01..04):** create state, delete state cascade, draw transition, delete transition + collapse. Transitions delete cleanly (no dangling map entries); composite collapse persists via the layout method.

## Commit message
```
feat(hsm-editor): delete-transition map cleanup + container collapse (BATCH-HS-04 / TASK-HS-04)

ApplyRemoveLinks only removed a transition from its source's outgoing list, leaving
it in the identity maps (still resolvable via FindTransitionByVisualId — a latent
dangling-reference bug). Refactor it onto the shared RemoveTransitionInternal helper
(BB1 auto-var cleanup + UnregisterTransition = full map + outgoing-list removal).
Implement ApplySetContainerCollapsed (StateNode.IsCollapsed from the command).
+9 headless tests (full removal incl. map-null guard, state survival, auto-var vs
shared-var, unknown-id no-op, collapse round-trip). Completes HSM command-sink
create/delete ops (HS-01..04).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
