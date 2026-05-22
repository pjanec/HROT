# BATCH-01 Review

**Batch:** BATCH-01  
**Reviewer:** Development Lead  
**Date:** 2026-05-23  
**Status:** APPROVED

---

## Summary

All 6 kernel prerequisite tasks completed. 42 new tests (requirement: 15). Both test suites pass except pre-existing failures documented in the report.

---

## Issues Found

### Issue 1: `BehaviorTreeState.InstanceFlags` overlays `AsyncHandles[2]`

**File:** `FDP/ExtDeps/FastBTree/src/Fbt.Kernel/BehaviorTreeState.cs` (Line 56)  
**Problem:** `InstanceFlags` is placed at `[FieldOffset(56)]` which is the same memory as `AsyncHandles[2]`. This is a union layout. The comment says the slot is "reserved and never written in production code" — but this is an implicit contract that future developers may not know about.  
**Verdict:** Accepted for now — the struct is `[Explicit, Size=64]` so there was no other free space. The overlay is documented in a comment. Record in DEBT-TRACKER for a proper reserved bytes block when the struct is next redesigned.

---

## Test Quality Assessment

Tests are excellent. Key observations:

- `PausedFlagTests` verifies actual non-advancement (phase + active leaf unchanged after multiple ticks while paused), not just flag existence.
- `BTreeNewFeaturesTests` K-05 tests verify `ctx.CallCount == 0` while paused (action genuinely not called) and `== 1` after resume.
- `BTreeNewFeaturesTests` K-06 tests verify runtime behavior (ForceSuccess/ForceFailure actually inverting node status, UntilSuccess/UntilFailure looping with tick counts).
- `BuilderVisualIdTests` verifies explicit Guid round-trips through the builder pipeline, not just that the field exists.

---

## Verdict

**Status: APPROVED**

All requirements met. Pre-existing failures confirmed unrelated to this batch.

---

## 📝 Commit Message

```
feat: kernel prerequisites for AI editor (BATCH-01)

Completes TASK-K-01, TASK-K-02, TASK-K-03, TASK-K-04, TASK-K-05, TASK-K-06

Adds editor-facing identity and debug-control hooks to FastHSM and FastBTree
kernels. All changes are additive with backwards-compatible defaults.

FastHSM changes:
- HsmActionAttribute.Lane property (CommandLane.None default) for OutputLaneMask inference
- HsmBuilder.State() and StateBuilder.Child() accept optional Guid stableId
- TransitionBuilder.GoTo() and new HsmBuilder.GlobalTransition() accept optional Guid visualId
- TransitionNode.VisualId property added
- InstanceFlags.Paused flag (bit 7); ValidateInstance skips paused instances

FastBTree changes:
- BehaviorInstanceFlags enum with Paused bit; stored in BehaviorTreeState at offset 56
- Interpreter.Tick returns NodeStatus.Running immediately when Paused
- Added ObserverSelector, ForceSuccess, ForceFailure, UntilSuccess, UntilFailure, Subtree
  builder methods with visualId parameter
- NodeType enum extended with ObserverSelector, ForceSuccess, ForceFailure, UntilSuccess,
  UntilFailure, Subtree values

Tests: 42 new tests covering pause semantics, Guid round-trips, and runtime node-type behavior
```

---

**Next Batch:** BATCH-02 (Phase 1 — Shared infrastructure foundation)
