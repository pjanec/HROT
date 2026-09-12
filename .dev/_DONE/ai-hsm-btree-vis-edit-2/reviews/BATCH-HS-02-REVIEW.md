# BATCH-HS-02 Review — TASK-HS-02 delete state (full cascade)

**Reviewer:** Dev Lead · **Date:** 2026-06-12 · **Status:** ✅ APPROVED · **Impl:** Zoo

## Verification (independent — read diff, re-ran suite)
- **ApplyRemoveNodes** rewritten correctly: (1) subtree collected via stack + visited set (cycle-safe); (2) incident transitions removed — outgoing snapshotted per state, incoming found by scanning `AllTransitions` for `Target ∈ subtree`, deduped by `VisualId`; (3) every subtree state `UnregisterState`'d (detaches from parent too). Order = transitions-then-states. Matches spec.
- **RemoveTransitionInternal** shared helper added (BB1 auto-var cleanup mirroring ApplyRemoveLinks + `UnregisterTransition` for full map removal). HS-04 will refactor ApplyRemoveLinks onto it — `ApplyRemoveLinks` left untouched this batch (correct scope).
- **No cheating:** touched only `HsmCommandSink.cs` + new test file (+ tracker line). No new HsmAsset mutators. No suppressions/weakened asserts.
- **Tests (12, behavioral):** leaf delete (gone from AllStates + both maps + parent.Children); composite delete removes all descendants; incoming + outgoing transition removal (FindByVisualId null, source.Outgoing empty); source-delete preserves target; internal/external transitions on composite delete; **auto-managed var removed vs shared var preserved**. Assert refs/counts/map-nulls, not strings.
- **Re-run (no regenerate flag):** `Hrot.Hsm.Editor.Tests` **402/0** (12 new, 0 pre-existing failures). Build 0 errors.

## Issues
None.

## Verdict
APPROVED. Deleting states/composites leaves the model consistent — no dangling transitions or stale identity-map entries.

## Commit message
```
feat(hsm-editor): delete-state full cascade (BATCH-HS-02 / TASK-HS-02)

ApplyRemoveNodes only detached a state from its parent. Now it deletes the whole
subtree: collect state + transitive children (cycle-safe), remove every incident
transition (outgoing + incoming, deduped) via a new shared RemoveTransitionInternal
helper (BB1 auto-managed-variable cleanup + UnregisterTransition), then
UnregisterState each. Leaves no dangling transitions or stale identity-map entries.
+12 headless tests (leaf/composite cascade, in/out/internal/external transitions,
auto-var removed vs shared-var preserved).

Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
```
