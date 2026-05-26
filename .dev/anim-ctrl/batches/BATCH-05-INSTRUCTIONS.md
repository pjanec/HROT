# BATCH-05: Phase 3 Part 2 — Remaining ECS Systems, Capability Reactor, Phase Ordering

**Delegate to:** Claude Sonnet 4.6 (dev-lead-agent)  
**Reference:** [TASK-DETAIL.md](../TASK-DETAIL.md#phase-3--muscle-ecs-systems-dd-1), [DD-1_MuscleCharacterRuntime_v1_2.md](../DD-1_MuscleCharacterRuntime_v1_2.md), [BATCH-04 Review](../reviews/BATCH-04-REVIEW.md)

---

## Scope

Implement the remaining 6 systems (NotifyEventEmitterSystem, AnimationStateReporterSystem, AnimationBackendCleanupSystem, + 3 stubs from Part 1), extend the existing capability-change reactor, register all systems in the correct phase order, and complete the Layer-2 system test suite.

**Tasks:** ANC-P3-06 through ANC-P3-11 (6 tasks, 5 of which are full implementations)

**Estimated effort:** 30–35 hours (Phase 3 Part 2 completion).

---

## Context & Onboarding

### Prerequisites (✅ BATCH-04 shipped)
- **ANC-P3-01 through P3-05:** All five Part 1 systems implemented and tested.
- **92 tests passing**, build clean.
- **D-04 resolved:** Field names confirmed as `Params`/`State`.
- **New debt items:** D-09–D-13 recorded in DEBT-TRACKER; P2-C (PlayMontageQueueExecutor stub) is the main blocker for this batch.

### Key Design References
- **DD-1 §11:** `NotifyEventEmitterSystem` — drain RawNotifyEvent, map to typed events, enrich `FootstepEvent.WorldPosition`.
- **DD-1 §18–11.1:** `AnimationStateReporterSystem` — emit `MontageStarted`/`Ended`/`SectionAdvanced`, `StanceChanged`; classify EndReason (Natural/Interrupted/BlendedOutByNext/Failed).
- **DD-1 §14, §20.5:** `AnimationBackendCleanupSystem` — watch `PendingDestroy`, call `backend.UnregisterEntity`, before chunk reaper.
- **DD-1 §13, §20.6–20.7:** Capability-change reactor extension — extend existing reactor to handle animation capability loss (force-stop, fail channel, clear queue).
- **DD-1 §17:** Phase ordering invariants (backend tick before drains, reporter after bridge, cleanup before reaper, status writes before egress).
- **DD-Tests §4:** Layer-2 system test suite (~10–12 tests, possibly now ~20–25 tests with Part 2).
- **BATCH-04 Review §5:** Known issues to address:
  - **D-11 (P2-C):** PlayMontageQueueExecutor is a stub — needs queue entry sequencing (chain length validation, per-entry play calls).
  - **D-12:** StanceTransitionSystem doesn't poll for backend completion (may be acceptable for fake backend).
  - **D-10, D-09:** Type-unsafe patterns (bit-cast for entity ID, staging buffer reuse) work but should be documented.

---

## Developer Insights (Report Requirements)

In your batch report (`.dev/anim-ctrl/reports/BATCH-05-REPORT.md`), explicitly answer:

1. **Reactor integration:** How does the capability-change reactor hook into the existing capability-reactor extension point? What's the lifecycle (when does it run relative to dispatch/bridge/cleanup)?

2. **Cleanup before reaper:** The design says cleanup must run before the chunk reaper. Confirm the system registration order and that unregistered handles are cleared before ECS destroys chunks. Did you encounter any race conditions?

3. **NotifyEventEmitterSystem enrichment:** The system must enrich `FootstepEvent.WorldPosition` from `SimTransform`. Did you encounter issues resolving the transform? Is the enrichment thread-safe (single-writer principle)?

4. **PlayMontageQueueExecutor implementation:** Complete the queue-entry sequencing. Document:
   - How do you validate chain length ≤8?
   - How do you prevent enqueue-at-capacity silent no-ops from silently failing?
   - How do you handle crossfade between queue entries (using `PlayMontageOnSlot` / `CrossfadeMontageOnSlot`)?

5. **Phase ordering validation:** Confirm the registered system order matches DD-1 §17:
   - PreSimulation: Dispatcher, LookAtDispatcher, StanceTransition (if any)
   - Simulation: (early) MontageQueueAdvance, (mid) Bridge, Reactor extension
   - PostSimulation: (early) NotifyEventEmitter, (mid) StateReporter, (late) Cleanup
   - Cleanup runs before chunk reaper (external guarantee)

6. **Test coverage:** How many new tests did you add? Do they cover:
   - Capability loss mid-montage (force-stop, fail status)?
   - Queue sequencing and crossfade?
   - Notify enrichment and event emission?
   - Entity cleanup and handle release?

7. **Design decisions beyond the spec:** Did you refactor any Part 1 systems (e.g., to complete PlayMontageQueueExecutor)? Any optimizations or structural changes?

---

## Test-Driven Task Progression

**Mandatory workflow (same as BATCH-04):**

1. **Read task spec** in TASK-DETAIL.md + corresponding DD sections.
2. **Write tests first** (Layer-2 system tests, xUnit).
3. **Implement** to satisfy tests.
4. **Verify all tests pass** locally before moving to next task.
5. **Document blocking issues** or design clarifications.

**Layer-2 test expectations:**
- `Hrot.MuscleCharacter.Animation.Tests` — system integration tests.
- Reuse the fixture from BATCH-04 (`CreateFixture()`, `CreateAnimatedEntity()`).
- Test each system in isolation (using fake backend).
- Add end-to-end scenarios that exercise multiple systems (e.g., play → notify → cleanup).

---

## Report Format

When finished, write `.dev/anim-ctrl/reports/BATCH-05-REPORT.md` with:

```markdown
# BATCH-05 Report — Phase 3 Part 2 Implementation

## Summary
- [ ] All 6 tasks (ANC-P3-06–11) complete and green.
- [ ] XX–YY new system tests passing (estimate).
- [ ] No breaking changes to Phase 0–3 contracts.
- [ ] PlayMontageQueueExecutor completed (D-11 resolved).

## Scope Completed
- **Implemented systems:** [list with brief status]
- **Test coverage:** [# of tests per system, total execution time]
- **Reactor integration:** [brief summary]
- **Phase ordering:** [confirmed or issues]
- **Blocking issues:** [if any]

## Developer Insights
### 1. Reactor integration
[Your answer]

### 2. Cleanup before reaper
[Your answer]

### 3. NotifyEventEmitterSystem enrichment
[Your answer]

### 4. PlayMontageQueueExecutor implementation
[Your answer]

### 5. Phase ordering validation
[Your answer]

### 6. Test coverage
[Your answer]

### 7. Design decisions beyond the spec
[Your answer]

## Validation
- [ ] `dotnet build Hrot.MuscleCharacter.Animation.csproj -c Debug` succeeds.
- [ ] `dotnet test Hrot.MuscleCharacter.Animation.Tests -c Debug` all green.
- [ ] No new warnings.
- [ ] Full solution builds clean.
```

---

## Tasks Checklist

### Main Tasks (ANC-P3-06 through ANC-P3-11)

- [ ] **ANC-P3-06** `NotifyEventEmitterSystem` (PostSimulation, early) — Drain `RawNotifyEvent`s from backend, map to typed events, enrich `FootstepEvent`, discard lifecycle events.
- [ ] **ANC-P3-07** `AnimationStateReporterSystem` (PostSimulation, mid) — Emit `MontageStarted`/`Ended`/`SectionAdvanced`, `StanceChanged`, write `Status=Success`, classify `EndReason`.
- [ ] **ANC-P3-08** `AnimationBackendCleanupSystem` (PostSimulation, late) — Watch `PendingDestroy`, call `backend.UnregisterEntity`, clear handle, run before reaper.
- [ ] **ANC-P3-09** Capability-change reactor extension — Extend existing reactor: on `CanPlayAnimations` loss force-stop slots + fail channel; on `CanAim` loss release aim; on `CanChangeStance` loss let transition finish.
- [ ] **ANC-P3-10** Phase-ordering registration — Register all 8 systems + reactor in the documented phase order. Verify invariants (backend tick before drains, reporter after bridge, cleanup before reaper).
- [ ] **ANC-P3-11** Layer-2 system test suite — ~10–12 base tests from Part 1 + new tests for Part 2 systems (~15–20 new tests), all <1 s total.

### Completions from Part 1

- [ ] **ANC-P3-04 (PlayMontageQueueAdvanceSystem) — Complete** the stub queue-mutation handling:
  - Validate montage IDs in queue entries.
  - Implement crossfade logic when advancing to next entry.
  - (Note: Not a separate task; integrate with Part 1 system if needed.)

- [ ] **ANC-P3-04 (PlayMontageQueueExecutor) — Implement full sequencing:**
  - PlayMontageChain: validate chain length ≤8, write all entries to queue.
  - Enqueue: append to queue (no DispatchedInstanceId bump).
  - ClearQueue: truncate queue, bump QueueVersion.
  - Prevent enqueue-at-capacity (log as no-op, set Status=Running but don't add).

---

## Known Issues to Resolve

### D-11 (P2-C) — PlayMontageQueueExecutor is a stub ✅ **TARGET THIS BATCH**
**File:** `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Executors/AnimationExecutors.cs`  
**Action:** Implement PlayMontageChain/Enqueue/ClearQueue executors with full entry validation and queue mutation via Span-cast pattern.  
**Test:** `PlayMontageQueue_ValidatesChainLength`, `PlayMontageQueue_EnqueueAtCapacity_NoOps`, `PlayMontageQueue_CrossfadesOnAdvance`.

### D-10 (P2-B) — LookAtExecutorState.TargetPointX type-cast (documentation)
**Note:** Not blocking. Bridge needs to resolve entity-mode look-at targets via `SimTransform` query. If you find this breaks in testing, escalate.

### D-12 (P2) — StanceTransitionSystem polling (acceptable for Part 2)
**Note:** Stance transitions in fake backend are instant. Real backend may need polling. For Part 2, document as "deferred to Stride backend integration (BATCH-08)."

---

## Next Steps (for Dev Lead post-review)

After BATCH-05 is reviewed and committed:
1. **Verify** that ANC-P3-06 through P3-11 are marked `[x]` in TASK-TRACKER.md.
2. **Verify** that D-11 is marked resolved, and new debt items (if any) are recorded in DEBT-TRACKER.md.
3. **Full Phase 3 complete:** All 11 tasks done. Ready for Phase 4 (Events & Catalog).
4. **Proceed to BATCH-06** (Phase 4: Eight event types, picker attributes, catalog entries, validators) if no critical issues.

---

## Communication

**Key dependencies for Phase 4:**
- Phase 3 must be 100% green (all 11 systems + tests) before Phase 4 begins.
- Phase 4 introduces event types that depend on Phase 3 `Status`/`EndReason` outputs.
- Phase 5 depends on Phase 4's `IEngineEventCatalog` and picker attributes.

**Unblocking note:** This batch completes the core animation runtime. Quality of Phase 3 system integration tests will directly impact confidence in Phases 4–7. Invest in edge-case scenarios (simultaneous capability loss + queue mutation, mid-animation entity destruction, notify cadence under varying frame rates).

---

**Expected completion:** ~30–35 hours of focused work.  
**Success condition:** All 11 Phase 3 tasks green, all ~25 Layer-2 tests passing, reactor integrated, phase ordering verified, D-11 resolved.
