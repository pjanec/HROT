# BATCH-16: Phase 8 Part 2 (ANC-P8-04 Networked Stage-2 Integration)

**Batch Number:** BATCH-16  
**Tasks:** ANC-P8-04  
**Phase:** Phase 8 - Networked stage-2 integration suite  
**Estimated Effort:** 12-18 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-15 approved

---

## Onboarding & Workflow

### Developer Instructions

Implement the remaining open tracker task: `ANC-P8-04`.
Build a networked Brain↔Muscle loopback integration suite reusing the eight animation scenarios under DDS round-trip conditions.

Do not stop and ask for permission to run tests, fix breakages, or complete obvious plumbing work. Finish implementation, verification, and report.

### Required Reading (IN ORDER)

1. `.dev/anim-ctrl/TASK-DETAIL.md` (`ANC-P8-04` section)
2. `.dev/anim-ctrl/DD-Tests_AnimationControl_v1_1.md` (section 10, section 11.4)
3. `.dev/anim-ctrl/DD-2_AnimationReplication_v1_1.md` (section 8 and topic table)
4. `.dev/anim-ctrl/reviews/BATCH-15-REVIEW.md`
5. Existing stage-1 integration tests for scenario reuse:
- `Hrot/Subsystems/Hrot.Animation.Integration.Tests/` (or equivalent existing phase-7 integration project)
6. Existing replication module and translators:
- `Hrot/Subsystems/Hrot.Animation.Replication/`

### Source Code Location

- New/updated network integration test project location (per task detail):
  - `Hrot/Subsystems/Hrot.Animation.Network.Integration.Tests/`
- Harness and node bootstrap references:
  - `HrotRunnerHarness` and related runner/bootstrap code in `Hrot/Runner/` and network modules.

### Report Submission

Write report to:
`.dev/anim-ctrl/reports/BATCH-16-REPORT.md`

If blocked by hard architectural contradiction, document in:
`.dev/anim-ctrl/questions/BATCH-16-QUESTIONS.md`

---

## Context

Phase 8 part 1 is approved. The tracker has one remaining implementation task: `ANC-P8-04`.
This batch must produce a networked stage-2 suite proving the existing scenario logic works across Brain↔Muscle DDS latency/round-trip.

Prefer referencing existing phase-7 fixtures/helpers over duplicating logic.

---

## Task: ANC-P8-04 Networked stage-2 integration suite

**Task Definition:** `.dev/anim-ctrl/TASK-DETAIL.md#anc-p8-04--networked-stage-2-integration-suite`

### Requirements

1. Create/extend `Hrot.Animation.Network.Integration.Tests` using loopback harness with two nodes (`simhost,cgf` / Brain+Muscle equivalent as defined in this repo).
2. Reuse/adapt the eight existing scenario assertions from phase-7 integration tests:
- happy-path montage completion
- notify keyframe
- stop interrupted
- stance transition
- queue chain
- enqueue mid-play
- footstep cadence
- look-at acquire/release
3. Add additional pump frames / waits to account for network round-trip delay.
4. Validate end-to-end behavior across replicated intent->status/event flow, not local single-node shortcuts.
5. Keep tests deterministic and CI-safe.

### Required Assertions

1. Each scenario must prove behavior after replication path traversal (not just local component mutation).
2. At least one assertion per scenario must check an observed Brain-side outcome that depends on Muscle-originated replicated status/event.
3. Validate expected approximate latency envelope (around 2 ticks per direction) without brittle exact-frame coupling.

### Test Quality Bar

- No fake/object-exists tests.
- No assertions that only check "did not throw".
- Each scenario test must assert concrete values (IDs, reasons, stance values, marker hashes, status transitions).

---

## Verification Requirements

Run and include summary output for:
1. `dotnet test Hrot/Subsystems/Hrot.Animation.Network.Integration.Tests/Hrot.Animation.Network.Integration.Tests.csproj -c Debug`
2. `dotnet test Hrot/Subsystems/Hrot.Animation.Replication.Tests/Hrot.Animation.Replication.Tests.csproj -c Debug --no-build`
3. `dotnet test Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Stride.Tests/Hrot.MuscleCharacter.Animation.Stride.Tests.csproj -c Debug --no-build`
4. `dotnet build IOS-IG-SimHost.sln -c Debug --no-restore -maxcpucount:4`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement -> Write tests -> **ALL tests pass** ✅
2. **Task 2:** Implement -> Write tests -> **ALL tests pass** ✅
3. **Task 3:** Implement -> Write tests -> **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

**Why:** Ensures each component is solid before building on top of it. Prevents cascading failures.

---

## Report Requirements

Include in `.dev/anim-ctrl/reports/BATCH-16-REPORT.md`:
1. Files changed.
2. Scenario matrix mapping each of 8 scenarios to test method names.
3. Key assertions proving replicated behavior.
4. Build/test command summaries.
5. Any unresolved risks and why they are non-blocking/blocking.

### Developer Insights (mandatory)

Answer explicitly:
1. What issues were encountered and how were they resolved?
2. What weak points were spotted in networked stage-2 harness/test architecture?
3. What design decisions were made beyond the spec?
4. What edge cases were discovered?
5. Suggested commit message.

---

## Success Criteria

This batch is done only when:
- [ ] `ANC-P8-04` implemented with networked stage-2 integration tests
- [ ] All eight scenarios covered across Brain↔Muscle replication path
- [ ] Required regression suites pass
- [ ] Solution build passes cleanly
- [ ] Report submitted
