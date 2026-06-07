# BATCH-15: Phase 8 Part 1 (Stride Backend Core + Smoke)

**Batch Number:** BATCH-15  
**Tasks:** ANC-P8-01, ANC-P8-02, ANC-P8-03  
**Phase:** Phase 8 - Stride backend + networked stage-2 extension  
**Estimated Effort:** 12-18 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-14 approved (Phase 6 complete)

---

## Onboarding & Workflow

### Developer Instructions

This batch starts Phase 8 with Stride backend integration and smoke validation. Build from DD-1 backend seam and DD-Tests smoke expectations. Do not attempt to complete networked stage-2 scenarios in this batch (that is ANC-P8-04 and will be batched next).

Do not stop and ask for permission to run tests, fix compilation issues, or complete obvious integration work. Finish all tasks, run verification, and write the report.

### Required Reading (IN ORDER)

1. `.dev/anim-ctrl/TASK-DETAIL.md` (ANC-P8-01..P8-03 sections)
2. `.dev/anim-ctrl/DD-1_MuscleCharacterRuntime_v1_2.md` (Stride backend sections, engine seam, no leakage)
3. `.dev/anim-ctrl/DD-Tests_AnimationControl_v1_1.md` (section 11.4 smoke expectations)
4. `.dev/anim-ctrl/reviews/BATCH-14-REVIEW.md`
5. Existing fake/contract implementation:
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/Contracts/IAnimationBackend.cs`
- `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Fake/`

### Source Code Location

- Primary work area:
  - `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Stride/` (create if not present)
- Tests:
  - `Hrot/Subsystems/Hrot.MuscleCharacter.Animation.Stride.Tests/` (or existing suitable smoke test project)
- Existing runtime integration points:
  - `Hrot/Subsystems/Hrot.MuscleCharacter.Animation/`

### Report Submission

Write report to:
`.dev/anim-ctrl/reports/BATCH-15-REPORT.md`

If blocked by architectural mismatch, document in:
`.dev/anim-ctrl/questions/BATCH-15-QUESTIONS.md`

---

## Context

Phase 6 is now complete and approved. Remaining tracker work is Phase 8 and deferred editor-only ANC-P5-08. This batch focuses on landing a functioning Stride backend seam and validating boot/tick/notify smoke behavior.

Prefer referencing DD and task detail instead of re-encoding design in code comments.

Related tasks:
- `ANC-P8-01` Stride backend skeleton
- `ANC-P8-02` Stride scene/transform + notify mapping
- `ANC-P8-03` Stride smoke test suite

---

## Tasks

### Task 1: ANC-P8-01 `StrideAnimationBackend` skeleton

**Task Definition:** `.dev/anim-ctrl/TASK-DETAIL.md#anc-p8-01--strideanimationbackend-skeleton`

**Requirements:**
1. Implement `IAnimationBackend` in Stride-specific assembly/namespace.
2. Keep engine-specific types inside `.Stride` boundary (no leakage into core contracts).
3. Implement per-entity registration lifecycle (`RegisterEntity`, `UnregisterEntity`, handle validation).
4. Implement non-crashing stubs for all required operations with deterministic behavior.
5. Provide clear fail-fast behavior for unsupported operations (no silent no-op unless explicitly designed).

**Tests Required:**
- Registration/unregistration handle lifecycle test.
- Basic slot play/query progression test using backend tick.
- Invalid/stale handle behavior test.

### Task 2: ANC-P8-02 Stride scene/transform + notify mapping

**Task Definition:** `.dev/anim-ctrl/TASK-DETAIL.md#anc-p8-02--stride-scenetransform--notify-mapping`

**Requirements:**
1. Wire backend entity mapping to Stride scene/transform representation used in this repo.
2. Implement marker/notify callback path into `RawNotifyEvent` drain path.
3. Ensure notify categories and payload fields are preserved according to existing contracts.
4. Keep this as smoke-level integration (do not overbuild full production animation graph parity in this batch).

**Tests Required:**
- Smoke test: authored marker callback produces a drained `RawNotifyEvent` with expected key fields.
- Transform/target resolution smoke assertion (backend does not crash when processing mapped entity).

### Task 3: ANC-P8-03 `StrideBackendSmokeTest` suite

**Task Definition:** `.dev/anim-ctrl/TASK-DETAIL.md#anc-p8-03--stridebackendsmoketest-suite`

**Requirements:**
1. Add dedicated smoke tests for Stride backend boot and tick through the same `IAnimationBackend` seam.
2. Scope is smoke/stability, not full scenario parity.
3. Verify backend can run minimal play+tick+notify path without crash.
4. Keep tests deterministic and CI-friendly.

**Tests Required:**
- Backend construction + initialization smoke.
- Register entity + play montage + tick + query state smoke.
- Marker/notify smoke through `DrainNotifies`.

---

## Testing Requirements

At minimum:
1. New Stride smoke tests for ANC-P8-01..03.
2. Existing animation replication tests remain green (`Hrot.Animation.Replication.Tests`).
3. Solution build remains clean.

Run and include summary output for:
- `dotnet build IOS-IG-SimHost.sln -c Debug --no-restore`
- relevant Stride backend test project command(s)
- `dotnet test Hrot/Subsystems/Hrot.Animation.Replication.Tests/Hrot.Animation.Replication.Tests.csproj -c Debug --no-build`

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

Include in `.dev/anim-ctrl/reports/BATCH-15-REPORT.md`:
1. Files changed grouped by task.
2. Exact tests added/updated and what behavior they validate.
3. Build/test command summaries.
4. Any architectural decisions or constraints discovered.

### Developer Insights (mandatory)

Answer explicitly:
1. What issues were encountered and how were they resolved?
2. What weak points were spotted in Stride integration boundaries?
3. What design decisions were made beyond the spec?
4. What edge cases were discovered?
5. Suggested commit message.

---

## Success Criteria

This batch is DONE when:
- [ ] ANC-P8-01 implemented with test coverage
- [ ] ANC-P8-02 notify/transform mapping implemented with smoke coverage
- [ ] ANC-P8-03 smoke suite added and passing
- [ ] No build regressions
- [ ] Replication tests remain green
- [ ] Report submitted
