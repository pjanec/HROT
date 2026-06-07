# BATCH-14: Phase 6 Corrective Completion (ANC-P6-01..06)

**Batch Number:** BATCH-14  
**Tasks:** Corrective Task 0 + ANC-P6-01, ANC-P6-02, ANC-P6-04, ANC-P6-06 completion hardening  
**Phase:** Phase 6 - Replication (DD-2)  
**Estimated Effort:** 10-14 hours  
**Priority:** HIGH (blocking)  
**Dependencies:** BATCH-13 reviewed with CHANGES REQUIRED

---

## Context

BATCH-13 introduced the replication project and broad test coverage, but review found blocking gaps against DD-2:
- Side-buffer partial serialization contract for `AnimationMontageQueue` is not proven/implemented to spec.
- `LookAtEntity` intent ingress does not resolve target entity IDs through `NetworkEntityMap`.
- Test project has an invalid project-reference path causing warning `MSB9008`.
- ANC-P6-06 QoS requirements are not verified by tests.

Read and follow:
1. `.dev/anim-ctrl/TASK-DETAIL.md` (Phase 6 tasks)
2. `.dev/anim-ctrl/DD-2_AnimationReplication_v1_1.md` (especially sections 2, 4, 6, 9)
3. `.dev/anim-ctrl/reviews/BATCH-13-REVIEW.md`
4. `.dev/anim-ctrl/reports/BATCH-13-REPORT.md`

Do not duplicate DD content in code comments. Implement and test directly against DD behavior.

---

## Onboarding & Workflow

### Source Code Locations

- Primary implementation: `Hrot/Subsystems/Hrot.Animation.Replication/`
- Tests: `Hrot/Subsystems/Hrot.Animation.Replication.Tests/`
- Shared translator base touched by BATCH-13: `FDP/Network/Fdp.Network.Cyclone/Translators/`

### Required Quality Standard

Do not stop and ask for permission to run tests, fix failures, or complete obvious remediation steps. Finish all required fixes, run verification, and submit report when green.

---

## Corrective Task 0 (P1): Warning Hygiene and Build Baseline

**File:** `Hrot/Subsystems/Hrot.Animation.Replication.Tests/Hrot.Animation.Replication.Tests.csproj` (UPDATE)

### Requirements

1. Fix invalid `ProjectReference` path to `Fdp.Core.csproj` so the path resolves correctly.
2. Ensure `dotnet test Hrot/Subsystems/Hrot.Animation.Replication.Tests/Hrot.Animation.Replication.Tests.csproj -c Debug` has zero warnings.
3. Ensure `dotnet build IOS-IG-SimHost.sln -c Debug --no-restore` stays clean.

### Tests Required

- Include command outputs (key summary lines only) in report.

---

## Task 1: ANC-P6-04 Partial Queue Serialization Compliance

**Files:**
- `Hrot/Subsystems/Hrot.Animation.Replication/Translators/SideBuffers/AnimationMontageQueueEgressTranslator.cs` (UPDATE)
- `Hrot/Subsystems/Hrot.Animation.Replication/Translators/SideBuffers/AnimationMontageQueueIngressTranslator.cs` (UPDATE)
- `Hrot/Subsystems/Hrot.Animation.Replication/AnimationDdsMessages.cs` (UPDATE if needed)
- `Hrot/Subsystems/Hrot.Animation.Replication.Tests/MontageQueueTranslatorTests.cs` (UPDATE)

**Design Reference:** DD-2 section 4.2 (`QueueWirePayload`, serialize only `Count` live entries)

### Requirements

1. Implement serializer behavior that encodes only live entries (`Count * 16`) and proves payload length semantics in tests.
2. Ingress must still zero tail entries 3..7 when `Count=3` (existing behavior must remain).
3. If DDS transport type constraints force fixed-size topic samples, then:
- keep wire DTO deterministic,
- add explicit serializer utility test proving `payloadBytes == 12 + 16 * Count`,
- clearly document where fixed-size DDS framing differs from logical payload bytes.
4. Do not regress dirty trigger (`QueueVersion` only).

### Tests Required

1. Replace weak size assertion with strict assertion for logical payload byte count at `Count=3`.
2. Keep round-trip + tail-zero tests.
3. Add test that verifies serialized logical payload excludes tail entries.

---

## Task 2: ANC-P6-02 LookAtEntity Ingress Remap

**Files:**
- `Hrot/Subsystems/Hrot.Animation.Replication/Translators/Channels/LookAtChannelIntentIngressTranslator.cs` (UPDATE)
- `Hrot/Subsystems/Hrot.Animation.Replication.Tests/AnimationChannelTranslatorTests.cs` (UPDATE)

**Design Reference:** DD-2 section 2.3 (LookAt entity refs resolved via `NetworkEntityMap`)

### Requirements

1. On ingress, when action is `LookAtActionIds.LookAtEntity`, decode params and remap `TargetEntityId` from network ID to local entity ID using `NetworkEntityMap`.
2. Preserve existing behavior for non-entity look-at actions (point/release).
3. Keep read-modify-write semantics preserving local-only fields.
4. Fail early on invalid mapping behavior (no silent corruption). If remap target missing, keep channel unchanged and document chosen fail behavior in report.

### Tests Required

1. Add test proving network target ID is remapped to local target entity ID for `LookAtEntity` action.
2. Add negative test for unknown target mapping (explicit expected behavior).

---

## Task 3: ANC-P6-06 QoS Verification Hardening

**Files:**
- `Hrot/Subsystems/Hrot.Animation.Replication/AnimationReplicationModule.cs` (UPDATE if needed)
- `Hrot/Subsystems/Hrot.Animation.Replication.Tests/AnimationReplicationModuleTests.cs` (UPDATE)

**Design Reference:** DD-2 section 6 topic/QoS table; section 9 observability expectations.

### Requirements

1. Ensure translator/module surface exposes enough metadata to verify QoS policy per topic in tests, or provide a deterministic mapping table test in module if QoS API is not directly queryable from `DdsReader`/`DdsWriter` wrappers.
2. Tests must assert:
- State-bearing topics (channel/descriptor/side-buffer) are `Reliable + TransientLocal`.
- Event topics are `Reliable + Volatile`.
3. Keep existing 15-topic count/direction assertions.

### Tests Required

1. Add/expand module tests for QoS mapping checks for all 15 topics.
2. Verify no `FootstepEvent` topic/translator is introduced.

---

## Test-Driven Task Progression

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

Write completion report to:
`.dev/anim-ctrl/reports/BATCH-14-REPORT.md`

Include:
1. Files changed grouped by task.
2. Test list added/updated and what behavior each test proves.
3. Build/test command results (key pass/fail summary lines).
4. Any design trade-offs or unavoidable constraints.

## Developer Insights (mandatory)

Answer explicitly:
1. What issues were encountered and how were they resolved?
2. What weak points were spotted in current replication/network code?
3. What design decisions were made beyond the written spec, and why?
4. What edge cases were discovered while hardening tests?
5. Suggested commit message.

---

## Success Criteria

This batch is done only when:
- [ ] Corrective Task 0 complete (no warning from test project path issue)
- [ ] ANC-P6-04 partial-serialization behavior is implemented and strongly tested
- [ ] ANC-P6-02 LookAtEntity ingress remap implemented and tested (positive + negative)
- [ ] ANC-P6-06 QoS policy verification added and passing
- [ ] Replication tests pass
- [ ] `dotnet build IOS-IG-SimHost.sln -c Debug --no-restore` passes
- [ ] Report submitted to `.dev/anim-ctrl/reports/BATCH-14-REPORT.md`
