# BATCH-OFX-01 Report

**Batch:** BATCH-OFX-01  
**Tasks:** OFX-002, OFX-003, OFX-004, OFX-005, OFX-006, OFX-009, OFX-012, OFX-022, OFX-023  
**Status:** COMPLETE  
**Test Result:** All tests passing -- 193 Animation tests, 43 Replication tests (236 total)  
**Baseline:** 174 tests before batch

---

## Summary

All 9 tasks implemented with production code fixes and behavioral tests. No regressions introduced.

---

## Task Results

### OFX-002 -- NotifyEventEmitterSystem typed event dispatch

**Fix:** Added constructors to `FootstepEvent`, `HitWindowOpenedEvent`, `HitWindowClosedEvent` in `AnimationEvents.cs`. Updated `NotifyEventEmitterSystem.Execute` to switch on `n.Kind` and publish the correct typed event (`FootstepEvent`, `HitWindowOpenedEvent`, `HitWindowClosedEvent`, or `AnimNotifyEvent` for Generic).

**Files changed:**
- `Hrot\Subsystems\Hrot.MuscleCharacter.Animation\Events\AnimationEvents.cs` -- added constructors
- `Hrot\Subsystems\Hrot.MuscleCharacter.Animation\Systems\NotifyEventEmitterSystem.cs` -- switch dispatch

**Tests added (Phase3SystemTests.cs):**
- `NotifyEmitter_FootstepKind_EmitsFootstepEvent` -- footstep marker at 0.1s fires FootstepEvent
- `NotifyEmitter_HitWindowOpenedKind_EmitsHitWindowOpenedEvent` -- HitWindowOpened marker fires HitWindowOpenedEvent
- `NotifyEmitter_GenericKind_EmitsAnimNotifyEvent` -- generic marker fires AnimNotifyEvent

---

### OFX-003 -- FakeAnimationBackend ECS state injection

**Fix:** Added `SetEntityRepository(EntityRepository repo)` to `FakeAnimationBackend`. When called before `RegisterEntity`, each subsequent `RegisterEntity` call also writes a `FakeAnimBackendState` ECS component (with `Generation` field) to the entity. `ResetWorld()` removes the component on cleanup.

**Files changed:**
- `Hrot\Subsystems\Hrot.MuscleCharacter.Animation.Fake\FakeAnimationBackend.cs`

**Tests added (Phase1BackendBehaviorTests.cs):**
- `SetEntityRepository_RegisterEntity_AddsEcsComponent`
- `ResetWorld_RemovesEcsComponents`

---

### OFX-004 -- StopMontageOnSlot blend-out window

**Fix:** `StopMontageOnSlot` now sets `InBlendOutWindow = 1` and advances `ElapsedSeconds` to `Max(current, total - blendOut)` instead of hard-clearing the slot. Natural completion in `AdvanceSlots` deactivates the slot once elapsed >= total.

**Files changed:**
- `Hrot\Subsystems\Hrot.MuscleCharacter.Animation.Fake\FakeAnimationBackend.cs`

**Tests added (Phase1BackendBehaviorTests.cs):**
- `StopMontageOnSlot_WithBlendOut_SetsInBlendOutWindow_SlotStillActive`
- `StopMontageOnSlot_WithBlendOut_SlotCompletesNaturally`

**Existing tests fixed:**
- `Tick_DoesNotAdvanceInactiveSlots` -- updated to match new blend-out behavior

---

### OFX-005 -- FakeAnimationBackend BlendWeight always zero

**Fix:** `AdvanceSlots` now computes `BlendWeight` per tick: ramp-in from 0 to 1 over `BlendInTime`, hold at 1 during body, ramp-out from 1 to 0 over `BlendOutTime`.

**Files changed:**
- `Hrot\Subsystems\Hrot.MuscleCharacter.Animation.Fake\FakeAnimationBackend.cs`

**Tests added (Phase1BackendBehaviorTests.cs):**
- `BlendWeight_IsZero_BeforeBlendInCompletes`
- `BlendWeight_IsOne_DuringHoldPhase`
- `BlendWeight_DecreasesUnderOne_DuringBlendOut`

---

### OFX-006 -- ANIM008-011 validators missing / vacuous tests

**Fix:** Added `BlueprintGraphIr` (minimal in-memory graph IR with `HasNode<T>()`) and `BlueprintAnimationValidators` static class with four methods:
- `ValidateAnim008(graph)` -- warns if `EnqueueMontageNode` present without `PlayMontageChainNode`
- `ValidateAnim009(graph)` -- warns if `ReleaseLookNode` present without `LookAtPointNode`/`LookAtEntityNode`
- `ValidateAnim010(patternKind)` -- errors if patternKind is not "SpanCast" or "PointerCast"
- `ValidateAnim011(graph, entityClassName, entityAnimDef?)` -- errors if animation primitives used on entity without animation config

**Files changed:**
- `Hrot\Subsystems\Hrot.MuscleCharacter.Animation\Validation\AnimationValidators.cs`

**Tests rewritten (AnimationValidatorTests.cs):** Replaced 8 vacuous mock-based tests with 12 real tests using `BlueprintGraphIr` + `BlueprintAnimationValidators`:
- ANIM008: `ANIM008_EnqueueAloneWarns`, `ANIM008_EnqueueWithPlayChainDoesNotWarn`, `ANIM008_NoEnqueue_DoesNotWarn`
- ANIM009: `ANIM009_ReleaseLookWithoutLookAtNodeWarns`, `ANIM009_ReleaseLookWithLookAtPointNodeDoesNotWarn`, `ANIM009_ReleaseLookWithLookAtEntityNodeDoesNotWarn`
- ANIM010: `ANIM010_SpanCastPatternIsAccepted`, `ANIM010_PointerCastPatternIsAccepted`, `ANIM010_UnknownPatternIsError`
- ANIM011: `ANIM011_AnimPrimitiveWithNoAnimDef_IsError`, `ANIM011_AnimPrimitiveWithAnimDef_IsOk`, `ANIM011_MultipleAnimPrimitivesWithNoAnimDef_EachReported`
- Integration: `RealisticGraph_AllValidatorsPass`

---

### OFX-009 -- MontageQueueAdvanceSystem waits for silence instead of blend-out

**Fix:**
1. Added `IsAnySlotInBlendOut(handle)` and `CrossfadeMontageOnSlot(handle, params)` to `IAnimationBackend`.
2. Implemented in `FakeAnimationBackend`: `IsAnySlotInBlendOut` iterates slots; `CrossfadeMontageOnSlot` delegates to `PlayMontageOnSlot`.
3. Implemented in `StrideAnimationBackend`: `IsAnySlotInBlendOut` checks `slots[i].InBlendOut`; `CrossfadeMontageOnSlot` delegates to `PlayMontageOnSlot`.
4. `MontageQueueAdvanceSystem`: trigger condition changed from `slotInactive` only to `slotInBlendOut || slotInactive`. When in blend-out: calls `CrossfadeMontageOnSlot` directly. When fully silent: uses `StageQueueEntry` for bridge apply.

**Files changed:**
- `Hrot\Subsystems\Hrot.MuscleCharacter.Animation\Contracts\IAnimationBackend.cs`
- `Hrot\Subsystems\Hrot.MuscleCharacter.Animation.Fake\FakeAnimationBackend.cs`
- `Hrot\Subsystems\Hrot.MuscleCharacter.Animation.Stride\StrideAnimationBackend.cs`
- `Hrot\Subsystems\Hrot.MuscleCharacter.Animation\Systems\MontageQueueAdvanceSystem.cs`

**Tests added (Phase3SystemTests.cs):**
- `QueueAdvances_WhenSlotEntersBlendOutWindow_BeforeSilence`
- `CrossfadeMontageOnSlot_IsCalledForNextQueueEntry`

---

### OFX-012 -- AnimationChannelIntentEgressTranslator ignores ActionParams blob in dirty check

**Fix:** Added `_lastPublishedActionParams` dictionary keyed by Entity, storing the 4-ulong hash of the 32-byte Params blob. The publish gate now requires both `ActionInstanceId` AND Params hash to be unchanged before suppressing. Changed from `fixed (ulong* u = (ulong*)ch.Params)` (CS0213) to `ulong* u = (ulong*)ch.Params` (local value type, no `fixed` needed).

**Files changed:**
- `Hrot\Subsystems\Hrot.Animation.Replication\Translators\Channels\AnimationChannelIntentEgressTranslator.cs`

**Tests added (AnimationChannelTranslatorTests.cs):**
- `AnimChannelIntentEgress_PublishesOnActionParamsChange_WhenSameInstanceId` -- verifies re-publish when Params byte changes with same ActionInstanceId

---

### OFX-022 -- FakeAnimationBackend.AdvanceFootsteps uses `while` instead of `if`

**Fix:** Changed `while (state.DistanceSinceLastFootstep >= FootstepStrideMeters)` to `if (...)` so at most one footstep per tick (as per DD-Fake §5). Also resets `DistanceSinceLastFootstep = 0f` in the stationary guard when entity is not grounded or below min speed.

**Files changed:**
- `Hrot\Subsystems\Hrot.MuscleCharacter.Animation.Fake\FakeAnimationBackend.cs`

**Tests added (Phase1BackendBehaviorTests.cs):**
- `AdvanceFootsteps_StationaryEntity_ResetsDistanceAccumulation`

**Existing tests fixed:**
- `DrainNotifies_ReturnsUpToBufferSize` -- changed to 3 separate 0.46f ticks (not one 1.4f tick)
- `DrainNotifies_HandlesSmallerDestBuffer` -- changed to 5 separate 0.46f ticks (not one 2.3f tick)

---

### OFX-023 -- FakeAnimationBackend aim/stance state not queryable for tests

**Fix:** Added `QueryAimState(handle)` returning `FakeAimState` and `QueryStanceState(handle)` returning `FakeStanceState` to `FakeAnimationBackend` as non-interface test-support methods.

**Files changed:**
- `Hrot\Subsystems\Hrot.MuscleCharacter.Animation.Fake\FakeAnimationBackend.cs`

**Tests added (Phase1BackendBehaviorTests.cs):**
- `Tick_RampsAimBlendWeight` -- aim blend weight increases from 0 toward 1 over ticks
- `Tick_CompletesStanceTransition` -- stance transitions complete and final stance is set

---

## Test Count Delta

| Project | Before | After | Delta |
|---------|--------|-------|-------|
| Hrot.MuscleCharacter.Animation.Tests | 174 | 193 | +19 |
| Hrot.Animation.Replication.Tests | 42 | 43 | +1 |
| **Total** | **216** | **236** | **+20** |

---

## Issues Encountered and Resolved

1. **CS0535 MockAnimationBackend** -- `Phase0ContractsTests.cs` had `MockAnimationBackend : IAnimationBackend` missing the new `IsAnySlotInBlendOut` and `CrossfadeMontageOnSlot` methods added by OFX-009. Fixed by adding stub implementations.

2. **CS0213 fixed pointer** -- In `AnimationChannelIntentEgressTranslator`, using `fixed (ulong* u = (ulong*)ch.Params)` on a local value-type variable caused CS0213. Fixed by using `ulong* u = (ulong*)ch.Params` directly (no `fixed` needed for local stack-allocated structs).

3. **CS0117 LookAtPointParams** -- `Phase1BackendBehaviorTests.cs` used non-existent fields `TargetX/Y/Z` on `LookAtPointParams`. Fixed to use actual field names `WorldPointX/Y/Z`.

4. **OFX-004 breaks Tick_DoesNotAdvanceInactiveSlots** -- The test assumed hard-stop behavior but OFX-004 changed to blend-out window. Updated test to verify `InBlendOutWindow==1` after stop, then verify deactivation after completing blend-out duration.

5. **OFX-022 breaks DrainNotifies buffer tests** -- `DrainNotifies_ReturnsUpToBufferSize` and `DrainNotifies_HandlesSmallerDestBuffer` used single large ticks to trigger multiple footsteps; OFX-022 changed to at-most-one-per-tick. Fixed by splitting into multiple separate ticks.

6. **AnimationValidatorTests.cs duplicate content** -- File replacement left old test bodies appended after the new namespace closing brace. Fixed by truncating the file to line 198.

7. **Missing RegisterComponent<AnimationExecutorState>** -- OFX-002 manual test setup called `AddComponent<AnimationExecutorState>` without registering it first. Fixed by adding `repo.RegisterComponent<AnimationExecutorState>()`. Also removed the unnecessary `ActorCapabilityState` setup.
