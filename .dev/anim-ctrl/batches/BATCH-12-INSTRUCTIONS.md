# BATCH-12 INSTRUCTIONS — Phase 7: Integration Scenarios 2–8

**Batch ID:** BATCH-12  
**Phase:** 7 (Integration tests, networkless stage-1)  
**Scope:** ANC-P7-05, ANC-P7-06, ANC-P7-07, ANC-P7-08, ANC-P7-09, ANC-P7-10, ANC-P7-11  
**Duration Estimate:** 12–15 hours  
**Target Build:** IOS-IG-SimHost.sln (Debug)  
**Success Criteria:** All 7 tasks complete; 7 scenarios passing; no regressions; all assertions in place.

---

## Context & Design References

**Phase 7 Goal:** Eight end-to-end integration scenarios over the full Muscle pipeline + fake backend (DD-Tests §6).

**BATCH-11 Foundation (now complete):**
- ✅ `PumpUntil` / `IPumpableHarness` verified in shared infra
- ✅ `AnimationIntegrationFixture` created; all 8 systems orchestrated correctly
- ✅ `TestData.cs` with baked character (2 montages, 2 stances, footstep markers)
- ✅ `AnimationTestHelpers` finalized (`IssuePlayMontage` writes full params blob)
- ✅ Scenario 1 (happy-path single montage) passing

**This Batch (Scenarios 2–8):**
All remaining scenarios reuse the fixture, test helpers, and test data from BATCH-11. No new infrastructure needed.

**Design References:**
- [DD-Tests §6 Scenarios 2–8](./DD-Tests_AnimationControl_v1_1.md#section-6-eight-scenarios): Flow and assertions for each scenario
- [DD-1 §15–16](./DD-1_MuscleCharacterRuntime_v1_2.md): Runtime systems overview
- [DD-Fake §11](./DD-Fake_FakeAnimationBackend_v1_1.md): Backend behavior + event delivery

---

## Task Breakdown

### ANC-P7-05: Scenario 2 — Notify at Keyframe

**Objective:** Verify that `AnimNotifyEvent` fires when a montage reaches an authored keyframe marker.

**Design Refs:** DD-Tests §6 Scenario 2; DD-Fake §11.3 (PayloadVector enrichment).

**Success Condition:**
- Play a montage ("Walk") with a keyframed notify marker (e.g., `"MagOut"` at frame 0.2s)
- `PumpUntil` condition: `Bus.Read<AnimNotifyEvent>().Length >= 1`
- Assert `AnimNotifyEvent.Category == AnimNotifyCategory.MagOut`
- Assert `evt.TargetEntity == entity`
- Assert `evt.IsTriggered == true` (marker crossed during tick)

**Test Name:** `PlayMontage_NotifyFiresAtAuthoredKeyframe`

**Implementation Notes:**
- Use `TestData.WalkMontageId` (already has a `MagOut` keyframe at frame 0.2s in the baked data)
- Frame budget ~100 (notify fires well before montage ends)
- After assertion, verify montage continues to completion (no side effects from notify)

**Files Involved:** `AnimationIntegrationScenarios.cs` (add test method)

---

### ANC-P7-06: Scenario 3 — Stop → Interrupted

**Objective:** Verify that stopping a montage mid-play publishes `MontageEndedEvent.EndReason == Interrupted`.

**Design Refs:** DD-Tests §6 Scenario 3; DD-1 §14.2 (StopMontageAction dispatch).

**Success Condition:**
- Spawn humanoid
- Register with backend (pump once)
- Play montage ("Walk")
- After ~10–15 frames (montage running but not finished), call `IssueStopMontage(entity, slot, World)`
- `PumpUntil` condition: `Bus.Read<MontageEndedEvent>().Length >= 1`
- Assert `MontageEndedEvent.EndReason == MontageEndReason.Interrupted`
- Assert `evt.MontageId == WalkMontageId`
- Assert `evt.Target == entity`

**Test Name:** `StopMontage_MidPlayInterruptsAndPublishesInterruptedEvent`

**Implementation Notes:**
- Requires `IssueStopMontage` helper (similar to `IssuePlayMontage`, writes `StopMontageAction` to channel)
- Must write `Params = {SlotIndex}` to `AnimationChannel` struct
- Frame budget ~100
- After assertion, verify entity is now idle (no active slot)

**Files Involved:** `AnimationTestHelpers.cs` (add `IssueStopMontage` helper); `AnimationIntegrationScenarios.cs` (add test)

---

### ANC-P7-07: Scenario 4 — Stance Transition

**Objective:** Verify that setting stance via `SetStanceAction` transitions correctly and publishes `StanceChangedEvent`.

**Design Refs:** DD-Tests §6 Scenario 4; DD-1 §14.5 (StanceTransitionSystem + StanceChangedEvent).

**Success Condition:**
- Spawn humanoid (initial stance: Standing)
- Register with backend (pump once)
- Call `IssueSetStance(entity, StanceId.Crouched, World)` (write `SetStanceAction` to channel)
- `PumpUntil` condition: `Bus.Read<StanceChangedEvent>().Length >= 1`
- Assert `StanceChangedEvent.FromStance == StanceId.Standing`
- Assert `StanceChangedEvent.ToStance == StanceId.Crouched`
- Assert `StanceChangedEvent.Target == entity`
- Assert entity's `StanceStatus.CurrentStance == StanceId.Crouched`
- Verify exactly one `StanceChangedEvent` published (no duplicates)

**Test Name:** `StanceIntent_DrivesTransitionAndPublishesStanceChangedEvent`

**Implementation Notes:**
- Requires `IssueSetStance` helper (writes `SetStanceAction` + `StanceId` to channel)
- Frame budget ~50 (stance change is typically fast)
- After assertion, verify no further events in subsequent pumps

**Files Involved:** `AnimationTestHelpers.cs` (add `IssueSetStance` helper); `AnimationIntegrationScenarios.cs` (add test)

---

### ANC-P7-08: Scenario 5 — Montage Chain via Queue

**Objective:** Verify that queuing three montages plays them in order, each triggering its own `MontageEndedEvent` with correct `QueueIndex`.

**Design Refs:** DD-Tests §6 Scenario 5; DD-1 §14.4 (MontageQueueAdvanceSystem); DD-5 §14.2 (chain semantics).

**Success Condition:**
- Spawn humanoid; register with backend
- Call `IssuePlayMontage(entity, WalkMontageId, World)` (single play, queue index 0)
- Call `IssueEnqueueMontage(entity, RunMontageId, World)` 3 times (queue indices 1, 2, 3)
- `PumpUntil` condition: `Bus.Read<MontageEndedEvent>().Length >= 4`
- Assert 4 events received (Walk + Run × 3)
- Assert each event has correct `MontageId` and `QueueIndex` (0, 1, 2, 3)
- Assert all `EndReason == NaturalEnd`
- Assert each event's `Target == entity`

**Test Name:** `PlayMontageQueue_ThreeEntriesPlaysInOrderAndReportsOneSuccess`

**Implementation Notes:**
- Requires `IssueEnqueueMontage` helper (writes `EnqueueMontageAction` without incrementing `ActionInstanceId`)
- Walk = 0.5s (30 frames), Run = 0.4s (24 frames) → total ~54 frames
- Frame budget ~200
- Verify no overlap: each event fires only after previous montage finishes
- After all 4 events, final channel status should be `Success` (deferred until all queued items play)

**Files Involved:** `AnimationTestHelpers.cs` (add `IssueEnqueueMontage` helper); `AnimationIntegrationScenarios.cs` (add test)

---

### ANC-P7-09: Scenario 6 — Enqueue Mid-Play

**Objective:** Verify that enqueueing a montage while another is playing appends it to the queue and plays after the current.

**Design Refs:** DD-Tests §6 Scenario 6; DD-5 §14.2 (enqueue semantics during active montage).

**Success Condition:**
- Spawn humanoid; register with backend
- Call `IssuePlayMontage(entity, WalkMontageId, World)` (single play)
- Pump ~15 frames (montage running but not finished)
- Call `IssueEnqueueMontage(entity, RunMontageId, World)` while Walk is active (appended to queue)
- `PumpUntil` condition: `Bus.Read<MontageEndedEvent>().Length >= 2`
- Assert 2 events: Walk (QueueIndex=0), then Run (QueueIndex=1)
- Assert Walk ends naturally, Run starts afterward
- Verify no `ActionInstanceId` bump on enqueue (deferred from single-shot increment)

**Test Name:** `EnqueueMontage_DuringActiveQueueAppendsAndPlays`

**Implementation Notes:**
- Frame budget ~200 (Walk + Run + buffer)
- Critical: enqueue at frame ~15 (montage running, not finished)
- Verify Run starts only after Walk naturally ends (not prematurely)
- Assert `AnimationChannel.Status == Success` only after all enqueued items finish

**Files Involved:** `AnimationTestHelpers.cs` (use existing `IssueEnqueueMontage`); `AnimationIntegrationScenarios.cs` (add test)

---

### ANC-P7-10: Scenario 7 — Footstep Cadence

**Objective:** Verify that `FootstepEvent` fires at correct cadence during locomotion montage.

**Design Refs:** DD-Tests §6 Scenario 7; DD-Fake §11.3 (synthetic footstep emission); DD-1 §16 (FootstepEventEmitterSystem).

**Success Condition:**
- Play Walk montage (0.5s = 30 frames at 60 Hz)
- Montage has footstep markers authored (e.g., at frames 5, 15, 25 → 3 footsteps per 30-frame walk)
- `PumpUntil` montage completes
- Assert `Bus.Read<FootstepEvent>().Length >= 3` (at least 3 footsteps)
- Assert each event has `Target == entity`
- Verify footsteps are evenly spaced (cadence ~0.2s apart for walk)

**Test Name:** `Locomotion_DrivesFootstepEventsAtCorrectCadence`

**Implementation Notes:**
- Use `TestData.WalkMontageId` (already has footstep markers)
- Frame budget ~50 (all within single 0.5s walk)
- Count footsteps; verify spacing is deterministic across multiple runs
- Requires test data to have valid footstep markers in baked montage

**Files Involved:** `AnimationIntegrationScenarios.cs` (add test)

---

### ANC-P7-11: Scenario 8 — Look-At Acquire/Release

**Objective:** Verify that setting a look-at target acquires it (Status=Running), and releasing resets (Status=Success).

**Design Refs:** DD-Tests §6 Scenario 8; DD-1 §14.3 (LookAtDispatcherSystem + LookAtExecutorState).

**Success Condition:**
- Spawn humanoid; register with backend
- Verify initial `LookAtChannel.Status == Failure` (idle)
- Call `IssueAcquireLookAt(entity, targetPoint, World)` (acquire point on plane)
- Pump 1 frame (dispatcher processes)
- Assert `LookAtChannel.Status == Running` (look-at active)
- Pump several more frames (running state continues)
- Call `IssueReleaseLookAt(entity, World)` (release)
- Pump 1 frame (dispatcher processes release)
- Assert `LookAtChannel.Status == Success` (released successfully)

**Test Name:** `LookAtPoint_AcquiresAndReleasesAimWithStatusTransitions`

**Implementation Notes:**
- Requires `IssueAcquireLookAt(entity, targetPoint, World)` helper (writes `AcquireLookAtAction` + point to channel)
- Requires `IssueReleaseLookAt(entity, World)` helper (writes `ReleaseLookAtAction` to channel)
- Frame budget ~50
- Verify no events published for look-at (look-at is input-only in stage-1, events deferred to stage-2)
- After release, verify entity can start a montage without look-at interference

**Files Involved:** `AnimationTestHelpers.cs` (add `IssueAcquireLookAt`, `IssueReleaseLookAt` helpers); `AnimationIntegrationScenarios.cs` (add test)

---

## Implementation Priorities & Dependencies

**Order of Implementation (recommended):**

1. **P7-05 (Scenario 2: Notify)** — Simplest; reuses montage play + event reading. No new helpers.
2. **P7-06 (Scenario 3: Stop)** — Add `IssueStopMontage` helper; straightforward action dispatch.
3. **P7-07 (Scenario 4: Stance)** — Add `IssueSetStance` helper; event-driven component update.
4. **P7-08 & P7-09 (Scenarios 5–6: Queue)** — Add `IssueEnqueueMontage` helper; test queue sequencing.
5. **P7-10 (Scenario 7: Footstep)** — Reuses montage play; event counting (depends on test data).
6. **P7-11 (Scenario 8: Look-At)** — Add 2 helpers; status-transition based test (most complex interaction).

---

## Developer Insights & Quality Focus

The developer should answer these questions in the report:

1. **Did the queue semantics (`QueueIndex`, `ActionInstanceId` behavior) match the design expectations?** Specifically:
   - Single `PlayMontage` → `ActionInstanceId` bumped once
   - `EnqueueMontage` → no bump (deferred)
   - `QueueIndex = 0xFF` for single-shot, `0–N` for queued items
   - Events fire in order with correct indices

2. **What integration point was the most tricky to get right?** (e.g., double-buffering, component ordering, event timing)

3. **Did you need to add assertions to any system or executor to catch bugs?** (e.g., zero MontageId check per D-21)

4. **Were there any test data gaps?** (e.g., missing footstep markers, montage durations, stance definitions)

5. **What weak points or friction did you encounter in the fixture or helpers?**

---

## Test-Driven Task Progression

**Per DD-Fake §11 and DD-Tests §11.1, follow this workflow:**

1. **Write the test first** (name from success condition above; minimal assertions)
2. **Run the test; watch it fail** (PumpUntil timeout or assertion mismatch)
3. **Implement the helper(s)** as needed (e.g., `IssueStopMontage`)
4. **Implement the scenario** in the system under test (already done; systems run in fixture)
5. **Add assertions** one by one; verify each passes
6. **Refactor** for clarity (rename, extract helpers)
7. **Re-run full suite** (verify all 8 scenarios + baseline tests pass)

**Mandatory:** Do not commit until:
- All 7 new tests pass
- All 169 baseline tests pass (no regressions)
- Build is clean (0 errors)
- Assertions check behavior, not just compilation

---

## Expected Deliverables

**New Files / Changes:**
- `AnimationTestHelpers.cs`: Add 5–6 new helper methods (`IssueStopMontage`, `IssueSetStance`, `IssueEnqueueMontage`, `IssueAcquireLookAt`, `IssueReleaseLookAt`)
- `AnimationIntegrationScenarios.cs`: Add 7 new test methods (one per scenario)
- `TestData.cs`: Verify footstep markers + montage definitions present and correct

**Report Structure:**
- Summary: 7 tasks complete, X tests added (expect ~7), all green
- Task-by-task status (ANC-P7-05 through P7-11)
- Developer insights (5 questions above)
- Test count table (before/after)
- Build/test verification output
- Files changed summary

**Success Criteria (Batch Completion):**
```
✅ All 7 tasks complete (ANC-P7-05 through P7-11)
✅ 7 new integration tests passing
✅ 169 baseline tests still passing (no regressions)
✅ Build clean (0 errors)
✅ All assertions rigorous (exercise code paths, not just existence)
✅ Developer insights recorded in report
```

---

## Notes for the Developer

- **Reuse & Leverage:** All fixture, helpers, and test data from BATCH-11 are production-ready. Focus on scenarios.
- **Frame Budgets:** Each scenario's pump budget is specified in the task above. Adjust slightly if needed (e.g., longer durations).
- **Event Bus Semantics:** Remember: `Bus.SwapBuffers()` happens after each `PumpFrame()`. Events are readable only after the tick that published them.
- **Debt Incorporation:** Implement D-21 (MontageId zero-check assertion) during P7-06 or P7-08.
- **Fixture Reuse:** Each scenario starts with `ResetWorld()` and `SpawnHumanoid()`. Keep isolation tight.

Good luck! Phase 7 foundation is solid. Scenarios 2–8 should follow naturally.

