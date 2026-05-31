# BATCH-OFX-01 Review

**Batch:** BATCH-OFX-01  
**Reviewer:** Development Lead  
**Date:** 2026-06-03  
**Status:** APPROVED

---

## Summary

9 anim-ctrl tasks completed. 236 tests passing (193 animation + 43 replication). 35 new tests added, 3 existing tests updated to match corrected behavior.

---

## Issues Found

No issues found.

---

## Test Quality Assessment

- **OFX-002**: `NotifyEmitter_FootstepKind_EmitsFootstepEvent` -- sets up a footstep marker, runs the system, reads typed events and asserts `FootstepEvent` present. Same for HitWindowOpened and Generic. Actual event type verification.
- **OFX-005**: `BlendWeight_IsZero_BeforeBlendInCompletes` asserts `== 0f`; `BlendWeight_IsOne_DuringHoldPhase` asserts `== 1f`; `BlendWeight_DecreasesUnderOne_DuringBlendOut` asserts `InRange(0.01f, 0.99f)`. Concrete numeric assertions.
- **OFX-006**: Validator tests use real `BlueprintGraphIr(typeof(EnqueueMontageNode))` -- actual node types, not hard-coded booleans. Positive AND negative cases per rule (`ANIM008_EnqueueAloneWarns` / `ANIM008_EnqueueWithPlayChainDoesNotWarn` / `ANIM008_NoEnqueue_DoesNotWarn`).
- **OFX-004**: `StopMontageOnSlot_WithBlendOut_SetsInBlendOutWindow_SlotStillActive` verifies slot is still active (not hard-cleared) after stop. `SlotCompletesNaturally` verifies completion after blend time.
- **OFX-012**: `AnimChannelIntentEgress_PublishesOnActionParamsChange_WhenSameInstanceId` -- mutates a Params byte while keeping same InstanceId, asserts second publish happens.

---

## Verdict

**Status: APPROVED**

All requirements met. Ready to merge.

---

## Commit Message

```
fix: anim-ctrl fixes (BATCH-OFX-01)

Completes OFX-002, OFX-003, OFX-004, OFX-005, OFX-006, OFX-009, OFX-012, OFX-022, OFX-023

- OFX-002: NotifyEventEmitterSystem switches on Kind for typed event dispatch
- OFX-003: FakeAnimationBackend state injected into FakeAnimBackendState ECS component
- OFX-004: StopMontageOnSlot triggers blend-out window (not immediate clear)
- OFX-005: AdvanceSlots computes BlendWeight (ramp-in/hold/ramp-out)
- OFX-009: MontageQueueAdvanceSystem triggers on InBlendOutWindow via CrossfadeMontageOnSlot
- OFX-012: AnimationChannelIntentEgressTranslator includes ActionParams blob in dirty check
- OFX-006: ANIM008/009/010/011 validators implemented with real BlueprintGraphIr; 12 real tests
- OFX-022: AdvanceFootsteps resets distance accumulation when stationary
- OFX-023: ANC-P1-06 aim/stance unit tests added

Tests: 236 passing (193 anim + 43 replication). 35 new tests.
```

---

**Next: BATCH-OFX-02 (navig-2)**
