# BATCH-05 Continuation — Final Three Tasks (D-11, ANC-P3-10, ANC-P3-11)

**Delegate to:** Claude Sonnet 4.6 (Coder Sub-agent)  
**Reference:** [BATCH-05-INSTRUCTIONS.md](./BATCH-05-INSTRUCTIONS.md), [DD-1](../DD-1_MuscleCharacterRuntime_v1_2.md), [TASK-DETAIL.md](../TASK-DETAIL.md)  
**Previous completion:** 4 of 6 Part 2 systems done; 92 tests passing; build clean.

---

## Current Status

**Completed by Part 1:**
- ✅ NotifyEventEmitterSystem (ANC-P3-06) — fully implemented
- ✅ AnimationStateReporterSystem (ANC-P3-07) — fully implemented
- ✅ AnimationCapabilityChangeReactorSystem (ANC-P3-09) — fully implemented
- ✅ AnimationBackendCleanupSystem (ANC-P3-08) — placeholder (awaits PendingDestroy)

**Build:** Clean (0 errors, 0 warnings)  
**Tests:** 92 passing, no regressions

---

## Remaining Scope (3 Tasks, ~10 hours)

### Task 1: Complete PlayMontageQueueExecutor (D-11) — **CRITICAL**

**File:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Executors/AnimationExecutors.cs`

The stub `PlayMontageQueueExecutor` at line 152 needs to be replaced with **three separate executors** that operate on the `AnimationMontageQueue` component (via Span-cast mutation pattern, as demonstrated in `PlayMontageExecutor.cs`).

#### 1a. PlayMontageChainExecutor

- **Purpose:** Write multiple queue entries in one command (batch-chain play).
- **Input:** `PlayMontageChainParams` — array of montage IDs + playback settings, max length 8 (ANIM012).
- **Action:**
  1. Parse `PlayMontageChainParams` from `channel.Params`.
  2. Validate chain length ≤8. If > 8, set `Status=Failure` and return.
  3. For each montage ID in the chain:
     - Validate ID exists in baked data (use `_cache` + `BackendHandle` like `PlayMontageExecutor`).
     - If any invalid, set `Status=Failure` and return (fail fast).
  4. Write all entries to `AnimationMontageQueue` via **Span-cast mutation**:
     ```csharp
     ref var queue = ref repo.GetComponentRW<AnimationMontageQueue>(entity);
     queue.Count = (byte)chainLength;
     // Fill queue.Entries[0..chainLength-1] with entries from params
     ```
  5. Bump `AnimationMontageQueueState.QueueVersion` (if component present) to signal update to bridge.
  6. Set `Status=Running`.

- **Tests to pass:**
  ```
  PlayMontageChain_WritesMultipleEntries
  PlayMontageChain_ValidatesChainLength_FailsIf_LengthGt8
  PlayMontageChain_FailsIfAnyMontageInvalid
  ```

#### 1b. EnqueueExecutor

- **Purpose:** Append a single entry to the queue (no `DispatchedInstanceId` bump).
- **Input:** `EnqueueParams` — montage ID + playback settings.
- **Action:**
  1. Parse `EnqueueParams` from `channel.Params`.
  2. Validate montage ID exists in baked data.
  3. Get queue: `ref var queue = ref repo.GetComponentRW<AnimationMontageQueue>(entity)`.
  4. **Guard:** If queue is at capacity (`queue.Count == 8`):
     - Do NOT add entry (silent no-op).
     - Log a debug message (e.g., "Queue full, dropping enqueue").
     - Set `Status=Running` (command accepted but not acted upon).
     - Return.
  5. Otherwise:
     - Append entry: `queue.Entries[queue.Count++] = newEntry`.
     - Bump `QueueVersion` (if component present).
     - Set `Status=Success`.

- **Tests to pass:**
  ```
  Enqueue_AppendsToQueueAndBumpsVersion
  Enqueue_AtCapacity_FailsSilently
  Enqueue_ValidatesMontageId
  ```

#### 1c. ClearQueueExecutor

- **Purpose:** Truncate the queue and signal update to bridge.
- **Input:** No params needed (just a command).
- **Action:**
  1. Get queue: `ref var queue = ref repo.GetComponentRW<AnimationMontageQueue>(entity)`.
  2. Set `queue.Count = 0` (truncate).
  3. Bump `AnimationMontageQueueState.QueueVersion` to trigger re-read by bridge.
  4. Set `Status=Success`.

- **Tests to pass:**
  ```
  ClearQueue_TruncatesQueueAndBumpsVersion
  ClearQueue_IsIdempotent
  ```

---

### Task 2: Register All Systems in Phase Order (ANC-P3-10)

**File:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/AnimationModule.cs` (or where systems are registered)

**Requirement:** Register all 8 + 1 systems/reactor in the documented order per DD-1 §17:

```
[Simulation] (early → late)
  1. AnimationDispatcherSystem                      (P3-01)
  2. LookAtDispatcherSystem                         (P3-02)
  3. AnimationCapabilityChangeReactorSystem         (P3-09) [BEFORE dispatchers catch capability state]
  4. MontageQueueAdvanceSystem                      (P3-04)
  5. AnimationRuntimeBridgeSystem                   (P3-05)

[PostSimulation] (early → late)
  6. NotifyEventEmitterSystem                       (P3-06) [early, after bridge.Tick() but before state report]
  7. AnimationStateReporterSystem                   (P3-07) [mid, after notify drain]
  8. AnimationBackendCleanupSystem                  (P3-08) [late, before chunk reaper]
```

**Implementation:**
- Add `[UpdateInPhase(SystemPhase.Simulation)]` to dispatcher/reactor/bridge systems (if not already there).
- Add `[UpdateAfter(typeof(AnimationRuntimeBridgeSystem))]` to NotifyEventEmitterSystem.
- Add `[UpdateAfter(typeof(NotifyEventEmitterSystem))]` to AnimationStateReporterSystem.
- Add `[UpdateAfter(typeof(AnimationStateReporterSystem))]` to AnimationBackendCleanupSystem.
- **Verify in test:** (see Task 3 tests, specifically `PhaseOrdering_Verification_Test`).

**Note:** Dependency ordering is critical:
- Capability reactor MUST run before dispatchers see capability state.
- Notify emitter MUST run after bridge applies montage state changes.
- Cleanup MUST run before chunk reaper (external system).

---

### Task 3: Write 15–20 Layer-2 System Tests (ANC-P3-11)

**File:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Tests/Phase3SystemTests.cs` (extend existing)

Write tests in the same fixture pattern as existing tests (use `CreateFixture()`, `CreateAnimatedEntity()`, etc.).

#### 3a. PlayMontageQueueExecutor Tests (5 tests)
```
[Test] PlayMontageChain_WritesAllEntriesToQueue
[Test] PlayMontageChain_ValidatesLength_FailsIf_LengthGt8
[Test] PlayMontageChain_FailsIfAnyMontageIdInvalid
[Test] Enqueue_AppendsEntry_AndBumpsVersion
[Test] Enqueue_AtCapacity_FailsSilently
[Test] ClearQueue_TruncatesAndBumpsVersion
```

#### 3b. NotifyEventEmitterSystem Tests (2 tests)
```
[Test] DrainNotifies_ConsumesRawEventsFromBackend_DoesNotThrow
[Test] DrainNotifies_EmptyBackend_ReturnsEarly
```

#### 3c. AnimationStateReporterSystem Tests (3 tests)
```
[Test] QueueCompletion_SetsMontageStatusSuccess
[Test] AimCompletion_SetsMontageStatusSuccess
[Test] MontageRunning_RemainsRunning
```

#### 3d. AnimationCapabilityChangeReactorSystem Tests (3 tests)
```
[Test] CanPlayAnimations_Loss_SetChannelToFailure_AndClearsQueue
[Test] CanPlayAnimations_Loss_BumpsDispatchedInstanceId
[Test] CanAim_Loss_SetLookAtChannelToFailure
```

#### 3e. System Registration & Phase Ordering (1 test)
```
[Test] PhaseOrdering_AllSystemsRegistered_InCorrectOrder
  - Verify dispatcher runs before reactor
  - Verify reactor runs before bridge
  - Verify notify emitter runs before state reporter
  - Verify state reporter runs before cleanup
```

#### 3f. Integration Tests (2–3 tests)
```
[Test] FullPipeline_PlayMontage_CompleteSuccessfully_EmitNotifiesAndReport
  - Play montage → bridge advances → notifies emitted → state reporter writes Success

[Test] SimultaneousCapabilityLoss_AndMontagePlay_IsRobust
  - Capability loss mid-play → reactor force-stops → channel fails
  - No double-failures or state corruption

[Test] QueueChain_PlayMultipleMontages_SequentiallyAdvance
  - Chain 3 montages → bridge advances queue → verify each plays
```

#### Test Implementation Notes
- Use `Phase3SystemTests` fixture with `CreateFixture()`.
- Create test entity with: `CharacterAnimationDefRuntime`, `AnimationChannel`, `AnimationMontageQueue`, `LookAtChannel`, etc.
- Manually call system `Execute()` to drive state.
- Assert on component state (Status, queue.Count, DispatchedInstanceId) using direct component access.
- Keep tests <1 second total (fake backend is instant, no real time).

---

## Report Format

When all three tasks are complete, write `.dev/anim-ctrl/reports/BATCH-05-REPORT.md`:

```markdown
# BATCH-05 Report — Phase 3 Part 2 Implementation (Continuation)

## Summary
- [x] PlayMontageQueueExecutor fully implemented (D-11 resolved)
- [x] All 8 + 1 systems + reactor registered in correct phase order (ANC-P3-10)
- [x] 15–20 new Layer-2 system tests written and passing (ANC-P3-11)
- [x] Zero new warnings or errors
- [x] Full Phase 3 complete (ANC-P3-01 through ANC-P3-11)

## Tasks Completed

### D-11: PlayMontageQueueExecutor Implementation
- **PlayMontageChainExecutor:** Validates chain length ≤8, writes all entries to queue, bumps QueueVersion
- **EnqueueExecutor:** Appends entry to queue (no-op if full), validates montage ID
- **ClearQueueExecutor:** Truncates queue, bumps QueueVersion
- **Tests:** 5 tests, all passing

### ANC-P3-10: System Registration & Phase Ordering
- Registered 8 systems + capability reactor
- Verified order: Dispatcher → LookAtDispatcher → CapabilityReactor → MontageQueueAdvance → Bridge → NotifyEmitter → StateReporter → Cleanup
- Dependency attributes: `[UpdateAfter]` added where needed
- No ordering violations detected

### ANC-P3-11: Layer-2 System Test Suite
- **New tests written:** 15–20 (specify exact count)
- **Coverage:** PlayMontageQueueExecutor (5), NotifyEventEmitter (2), StateReporter (3), CapabilityReactor (3), PhaseOrdering (1), Integration (2–3)
- **Total execution time:** <1 second
- **All passing:** Yes

## Build & Test Validation
- [ ] `dotnet build Hrot.MuscleCharacter.Animation.csproj -c Debug` succeeds
- [ ] `dotnet test Hrot.MuscleCharacter.Animation.Tests -c Debug --logger "console;verbosity=minimal"` — all green
  - **Old tests:** 92 passing
  - **New tests:** XX passing
  - **Total:** YY passing
- [ ] No warnings, no errors
- [ ] Full solution `IOS-IG-SimHost.sln` builds clean

## Developer Insights

### 1. PlayMontageQueueExecutor implementation
**Q:** How did you validate chain length and handle enqueue-at-capacity?  
**A:** [Your answer — describe validation logic, silent no-op strategy, QueueVersion bump pattern]

### 2. System phase ordering
**Q:** Did you encounter any ordering violations or race conditions during testing?  
**A:** [Your answer — confirm order correctness, document any surprises]

### 3. NotifyEventEmitterSystem integration
**Q:** How is the system hooked into the draining pipeline? Any thread-safety concerns?  
**A:** [Your answer — confirm single-writer principle, no double-drains]

### 4. Capability reactor timing
**Q:** Why does the reactor run early in Simulation (before dispatchers)? What would break if it ran late?  
**A:** [Your answer — explain dispatcher dependency, consequence of late ordering]

### 5. System test coverage
**Q:** What edge cases did you test that weren't in the spec?  
**A:** [Your answer — list unexpected scenarios, e.g., rapid capability toggles, queue overflow, concurrent cleanup]

### 6. Phase 3 completeness
**Q:** Are all 11 Phase 3 tasks now green and interoperating correctly?  
**A:** [Your answer — confirm integration, any remaining tech debt]

### 7. Design decisions beyond the spec
**Q:** Did you refactor or optimize any Part 1 systems?  
**A:** [Your answer — describe any structural changes, trade-offs, or improvements]

## Known Issues & Next Steps

### Resolved
- ✅ D-11 (PlayMontageQueueExecutor stub) — fully implemented
- ✅ Phase ordering confirmed

### Deferred (BATCH-06+)
- ( ) AnimationBackendCleanupSystem full implementation — awaits PendingDestroy component from core
- ( ) NotifyEventEmitterSystem event synthesis — deferred to Phase 4 (event types not yet defined)
- ( ) StanceTransitionSystem real backend polling — deferred to Stride backend integration (BATCH-08)

## Next Phase: BATCH-06 (Phase 4 — Events & Catalog)

Phase 3 is now complete and ready for Phase 4 (ANC-P4-01 through ANC-P4-04). Phase 4 will:
- Define 8 event types with mandatory attributes
- Register picker attributes and drawers
- Create catalog entries (with FootstepEvent exclusion)
- Add validators BP2016 / BP2017

**Dependency:** Phase 4 events depend on Phase 3's `Status` + `EndReason` output from AnimationStateReporterSystem.

---

**Report written:** [Date/Time]  
**Submitted by:** [Coder Sub-agent]  
**Reviewed by:** [Dev Lead]
```

---

## Test-Driven Workflow

1. **Read task specs** in TASK-DETAIL.md and DD-1 §XX.
2. **Write tests first** for each executor and system.
3. **Implement** to satisfy tests.
4. **Run full test suite** locally: `dotnet test Hrot.MuscleCharacter.Animation.Tests -c Debug`
5. **Verify build:** `dotnet build Hrot.MuscleCharacter.Animation.csproj -c Debug` — 0 errors, 0 warnings.
6. **Check full solution:** `dotnet build IOS-IG-SimHost.sln -c Debug` — no regressions.
7. **Commit each task** (use meaningful commit messages per git log style).

---

## Success Criteria

- ✅ PlayMontageQueueExecutor fully implemented (3 executors, all tests passing)
- ✅ All 8 + 1 systems registered in correct phase order
- ✅ 15–20 new Layer-2 tests, all passing
- ✅ Total tests: 100+ (92 old + 15+ new)
- ✅ Zero new warnings or errors
- ✅ Full solution builds clean
- ✅ BATCH-05-REPORT.md complete with all 7 developer insights
- ✅ D-11 marked resolved in report

**Expected completion:** ~10 hours  
**Expected pull start:** End of day or next morning
