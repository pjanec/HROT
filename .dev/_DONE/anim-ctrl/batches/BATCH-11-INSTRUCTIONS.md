# BATCH-11 INSTRUCTIONS — Phase 7: Integration Foundation + Scenario 1

**Batch ID:** BATCH-11  
**Phase:** 7 (Integration tests, networkless stage-1)  
**Scope:** ANC-P7-01, ANC-P7-02, ANC-P7-03, ANC-P7-04  
**Duration Estimate:** 15–18 hours  
**Target Build:** IOS-IG-SimHost.sln (Debug)  
**Success Criteria:** All 4 tasks complete; foundation + first scenario passing tests.

---

## Context & Design References

**Phase 7 Goal:** Eight end-to-end integration scenarios over the full Muscle pipeline + fake backend (DD-Tests §6).

**Design Docs:**
- [DD-Tests §5–8](./DD-Tests_AnimationControl_v1_1.md): Integration test structure, fixtures, scenarios 1–8
- [DD-1 §15–16](./DD-1_MuscleCharacterRuntime_v1_2.md): Runtime systems overview for end-to-end verification
- [DD-Fake §11](./DD-Fake_FakeAnimationBackend_v1_1.md): Backend behavior + event delivery

**Phase 7 depends on:**
- ✅ Phases 0–5 complete (foundation, blueprint nodes, systems)
- ✅ BATCH-10 approved (AiPrimitive registration ready)
- ✅ BATCH-09 verified (PumpUntil infrastructure confirmed working; AnimationTestHelpers ready)

---

## Task Breakdown

### ANC-P7-01: `PumpUntil` + `IPumpableHarness` (shared infra)

**Objective:** Promote `PumpUntil`/`PumpFrames` + `IPumpableHarness` interface to the shared integration-test infrastructure project (Fdp.Core or similar). Frame-budgeted execution; throws `TimeoutException` with diagnostic dump when condition fails.

**Design Refs:** DD-Tests §5.2, §7.1, §11.3

**Current Status:** BATCH-09 verified that the infrastructure exists and works (ANC-P7-01 verified status). However, check if it's in the correct location (shared project) or if it needs to be promoted from a local test file.

**Success Condition:**
- `PumpUntil<T>(Func<T>, maxFrames, diagnosticConditionName)` compiles and passes unit tests
- Condition met returns early (no waste of frames)
- Never-true condition throws `TimeoutException` after `maxFrames` with condition name + diagnostic dump in message
- Interface `IPumpableHarness` exists with `Tick()` method
- Located in shared test infrastructure (not animation-specific)

**Implementation Notes:**
- If already in shared infra: verify functionality with 1–2 unit tests
- If in animation-test-local: promote to shared infra, update usages
- Diagnostic dump should include frame count, condition name, and a short stack trace or timestamp

**Files Involved:** `Fdp.Core` or equivalent shared infra; `Hrot.Animation.Integration.Tests` usage

---

### ANC-P7-02: Animation diagnostics + command helpers

**Objective:** Verify and finalize three helper functions for animation integration tests (already created in BATCH-09):
- `DumpAnimationDiagnostics(Entity, FakeAnimBackendState)`: Human-readable state dump for debugging
- `WriteParams<T>(T, byte[] blob)`: Generic struct serialization with 32-byte overflow detection
- `IssuePlayMontage(Entity, montageId, slotIndex)`: Dispatcher helper reducing boilerplate

**Design Refs:** DD-Tests §7.2, §7.3, §7.4

**Current Status:** Implemented in BATCH-09 (AnimationTestHelpers.cs, 260 lines, 10 tests + 1 skipped).

**Success Condition:**
- All three helpers compile and pass tests
- `WriteParams<T>` correctly raises when `sizeof(T) > 32`
- `IssuePlayMontage` correctly writes channel command and increments ActionInstanceId
- `DumpAnimationDiagnostics` produces readable output (used by P7-04+)
- All helpers used by P7-04+ scenarios

**Implementation Notes:**
- BATCH-09 deferred one test (`WriteParams` oversized check); verify it's in DEBT-TRACKER as acceptable
- No new functionality needed; confirm existing implementation meets criteria

**Files Involved:** `Hrot/Subsystems/Hrot.Animation.Integration.Tests/AnimationTestHelpers.cs`

---

### ANC-P7-03: Integration fixture + inline TKB test data

**Objective:** Build the shared test fixture for all eight scenarios:
- `AnimationIntegrationFixture : IPumpableHarness, IDisposable`
- Bootstrap via `SimHostNodeBootstrapper(networkFactory: null)` (no networking for stage-1)
- Methods: `SpawnHumanoid()`, `ResetWorld()`, `Tick()`
- Inline TKB test data: `TestData.MinimalCharacterDef()` via `BakeForTest`
- Support for xUnit `IClassFixture<AnimationIntegrationFixture>` pattern

**Design Refs:** DD-Tests §5.1, §8

**Success Condition:**
- Fixture bootstraps once per test class; reusable across scenarios
- `ResetWorld()` destroys test entities + drains notification bus + resets montage queues
- `SpawnHumanoid()` returns Entity with AnimationChannel + LookAtChannel + MontageQueue initialized
- `Tick()` implements `IPumpableHarness` interface; advances one frame
- `Dispose()` cleans up SimHostNodeBootstrapper
- Smoke test: spawn entity + tick N times without error
- Inline TKB data: character with at least 2 montages (e.g., "Walk", "Run") + 2 stances + footstep markers

**Implementation Notes:**
- `SimHostNodeBootstrapper(networkFactory: null)` means no DDS; FakeAnimationBackend is used
- TKB data can be JSON-based or inline structs; see BATCH-09 `BakeForTest` pattern
- Fixture lifecycle: Create once, `ResetWorld()` between scenarios

**Files Involved:** `Hrot/Subsystems/Hrot.Animation.Integration.Tests/AnimationIntegrationFixture.cs`, `TestData.cs` (or similar)

---

### ANC-P7-04: Scenario 1 — happy-path single montage

**Objective:** End-to-end test: play a montage, observe dispatch ack → Running → Success; verify `MontageEndedEvent{NaturalEnd, OriginalReloadId}` is published.

**Design Refs:** DD-Tests §6 Scenario 1

**Success Condition:**
- Test name: `PlayMontage_RunsToCompletionAndReportsSuccess`
- Setup: Spawn humanoid, issue `PlayMontage(montageId: "Walk", slotIndex: 0)`
- Execution: Tick until montage completes (budget: ~100 frames)
- Assertions:
  - AnimationChannel.Status transitions: Idle → Running → Success
  - Exactly 1 `MontageEndedEvent` published
  - `MontageEndedEvent.EndReason == MontageEndReason.NaturalEnd`
  - `MontageEndedEvent.MontageId == originalMontageId`
  - No spurious events in between
- Use `PumpUntil()` to enforce frame budget + timeout

**Implementation Notes:**
- Humanoid montage length should be known (e.g., "Walk" = 30 frames at 30 FPS = 1.0 second)
- `FakeAnimationBackend` emits `MontageEndedEvent` when montage expires (see BATCH-01/P1-05 implementation)
- Verify notification bus drains correctly (AnimationTestHelpers + FakeAnimBackendState)

**Files Involved:** `Hrot.Animation.Integration.Tests/AnimationIntegrationScenarios.cs` or similar test class

---

## Onboarding Notes

- **BATCH-09 Review:** Confirmed PumpUntil infrastructure exists and works; AnimationTestHelpers ready
- **BATCH-10 Approved:** Phase 5 complete; all 11 animation nodes registered (foundation for scenarios)
- **DD-Tests §5–8:** Detailed scenario walkthroughs; use as reference during implementation
- **Test Layer 3 (L3):** These are **integration tests**, not unit tests. They exercise the full Muscle pipeline: dispatcher → executor systems → fake backend → notification bus

---

## Code Quality Standards

1. **No Smoke Tests:** Every assertion verifies actual behavior (state transitions, event content, counts), not just "did it run without error"
2. **Early Failure:** Use hard-asserts for invariants; throw descriptive exceptions, not silent failures
3. **Diagnostic Output:** Use `DumpAnimationDiagnostics()` on timeout; include frame count, current state, last event
4. **Known Values:** Where applicable, use constants (e.g., montage IDs, frame budgets) and document reasoning
5. **Isolation:** Each scenario uses `ResetWorld()` to clean state between tests

---

## Developer Insights Questions

You must answer these in your BATCH-11-REPORT:

1. **Did you discover any gaps between the DD-Tests design and the actual runtime behavior?** (e.g., timing differences, missing event fields, state transition variations)

2. **What was the most challenging aspect of building the integration fixture?** (e.g., bootstrapper configuration, entity cleanup, test data format)

3. **Did you encounter any type-safety or marshaling issues in the animation pipeline?** (e.g., struct packing, enum bit-width mismatches)

4. **What integration points required the most careful synchronization?** (e.g., event bus draining, frame budgeting, dispatcher ack timing)

5. **What weak points in the animation subsystem infrastructure did you identify for future work?**

---

## Test-Driven Task Progression

**For each task, follow this workflow:**

1. **Write the Test First** (or Specification)
   - Define success criteria as xUnit `[Fact]` tests
   - For fixtures, write a smoke test that exercises the bootstrap path
   - For scenarios, write the full assertion set before implementing

2. **Verify the Test Fails**
   - Run the test; confirm it fails with a clear error
   - Error should point to the missing implementation, not type errors

3. **Implement Minimally**
   - Add only the code needed for the test to pass
   - Avoid speculative features or over-engineering

4. **Verify All Tests Pass**
   - Run the full animation test suite (Hrot.MuscleCharacter.Animation.Tests + Hrot.Animation.Integration.Tests)
   - Ensure no regressions in baseline tests (169 baseline + 11 BATCH-09 + 22 BATCH-10)

5. **Refactor for Clarity** (if needed)
   - Clean up temporary code
   - Extract helpers if pattern repeats
   - Document non-obvious design choices

6. **Commit Per Task**
   - Each task should have a clean, standalone commit
   - Include task ID in commit message (e.g., "ANC-P7-01: PumpUntil infra...")

---

## Batch Completion Checklist

- [ ] ANC-P7-01: PumpUntil + IPumpableHarness compiles, tests pass, correct location
- [ ] ANC-P7-02: AnimationTestHelpers verified; all three helpers working, tests pass
- [ ] ANC-P7-03: AnimationIntegrationFixture scaffolded; smoke test passes; TKB test data inline
- [ ] ANC-P7-04: Scenario 1 (happy-path montage) implemented; test passes; frame budget verified
- [ ] Full test suite clean: 169 baseline + 11 BATCH-09 + 22 BATCH-10 + NEW tests all green
- [ ] Build clean: `dotnet build IOS-IG-SimHost.sln -c Debug` → 0 errors, 0 warnings
- [ ] No regressions detected
- [ ] All developer insights questions answered in BATCH-11-REPORT.md
- [ ] Code ready for review

---

## Files to Create/Modify

**Expected Outputs:**
- `Hrot/Subsystems/Hrot.Animation.Integration.Tests/AnimationIntegrationFixture.cs` (new)
- `Hrot/Subsystems/Hrot.Animation.Integration.Tests/AnimationIntegrationScenarios.cs` (new, or extend existing)
- `Hrot/Subsystems/Hrot.Animation.Integration.Tests/TestData.cs` (new, or extend)
- Possibly promote `PumpUntil` + `IPumpableHarness` to shared infra (Fdp.Core or similar)
- `.dev/anim-ctrl/reports/BATCH-11-REPORT.md` (completion report)

---

## Build & Test Commands

**Build:**
```powershell
dotnet build "IOS-IG-SimHost.sln" -c Debug --no-restore -maxcpucount:4
```

**Run all animation tests:**
```powershell
dotnet test "Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Tests/Hrot.MuscleCharacter.Animation.Tests.csproj" --no-build --logger "console;verbosity=minimal"
dotnet test "Hrot/Subsystems/Hrot.Animation.Integration.Tests/Hrot.Animation.Integration.Tests.csproj" --no-build --logger "console;verbosity=minimal"
```

**Run BATCH-11 tests only:**
```powershell
dotnet test "Hrot/Subsystems/Hrot.Animation.Integration.Tests/Hrot.Animation.Integration.Tests.csproj" --filter "FullyQualifiedName~AnimationIntegrationScenarios" --no-build --logger "console;verbosity=minimal"
```

---

## Success Criteria

**BATCH-11 is COMPLETE when:**

1. ✅ All 4 tasks implemented (P7-01 through P7-04)
2. ✅ All new tests passing (exact count TBD, estimated 12–15 tests across 4 tasks)
3. ✅ 169 baseline animation tests all green (no regressions)
4. ✅ Build clean: 0 errors, 0 warnings
5. ✅ Developer insights: All 5 questions answered with substantive responses
6. ✅ BATCH-11-REPORT.md written and ready for review

---

## Next Batch Preparation

After BATCH-11 approval:
- BATCH-12 will cover ANC-P7-05 through ANC-P7-08 (Scenarios 2–5)
- BATCH-13 will cover ANC-P7-09 through ANC-P7-11 (Scenarios 6–8)
- Phase 6 (Replication) may proceed in parallel after Phase 7 foundation is solid

---

**Ready to delegate.** Implement all 4 tasks. Report completion to BATCH-11-REPORT.md. Good luck!
