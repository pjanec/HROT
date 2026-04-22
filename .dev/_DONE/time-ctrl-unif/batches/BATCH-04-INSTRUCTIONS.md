# BATCH-04: Role-Split Lockstep Translators and Factory Updates

**Batch Number:** BATCH-04  
**Tasks:** TCU-TR001, TCU-TR002, TCU-TR003, TCU-T003, TCU-T004  
**Phase:** Phase 4 — Role-Split Lockstep Translators  
**Estimated Effort:** 5–7 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (domain message types), BATCH-02 (MasterSyncController), BATCH-03 (SlaveSyncController)

---

## 📋 Onboarding & Workflow

### Developer Instructions

This batch replaces the echo-prone symmetric `FrameLockstepDescriptorTranslator` with two strictly asymmetric translators. It also updates `TimeNetworkModule` factory methods and `TimeControllerFactory`. No application wiring yet (that's Phase 5).

Key design goal: translators are **stateless pipes** — no echo-prevention tracking variables. Echo is structurally impossible because each translator only has access to *one side* of the DDS topics.

### Required Reading (IN ORDER)

1. **Design Document:** `.dev/time-ctrl-unif/docs/DESIGN.md` — §4.4 Role-Split Lockstep Translators
2. **Task Definitions:** `.dev/time-ctrl-unif/docs/TASK-DETAIL.md` — read TCU-TR001, TCU-TR002, TCU-TR003, TCU-T003, TCU-T004 in full
3. **Previous Reviews:** `.dev/time-ctrl-unif/reviews/BATCH-03-REVIEW.md`  
4. **Existing code to study (DO NOT MODIFY):**
   - `FDP/Toolkits/FDP.Toolkit.Time/FrameLockstepDescriptorTranslator.cs` — the symmetric translator being replaced (study its structure carefully: IDescriptorTranslator, ScanAndPublish, PollIngress)
   - `FDP/Toolkits/FDP.Toolkit.Time/SwitchTimeModeDescriptorTranslator.cs` — reference for translator pattern
   - `FDP/Toolkits/FDP.Toolkit.Time/Translators/TimePulseEgressTranslator.cs` — reference for translator pattern
   - `FDP/Toolkits/FDP.Toolkit.Time/TimeNetworkModule.cs` — factory to update
   - `FDP/Toolkits/FDP.Toolkit.Time/Controllers/TimeControllerFactory.cs` — factory to update
   - `FDP/Toolkits/FDP.Toolkit.Time/Controllers/MasterSyncController.cs` — to use as Master return type
   - `FDP/Toolkits/FDP.Toolkit.Time/Controllers/SlaveSyncController.cs` — to use as Slave return type

### Source Code Location

- **New master translator:** `FDP/Toolkits/FDP.Toolkit.Time/Translators/MasterLockstepTranslator.cs` (NEW)
- **New slave translator:** `FDP/Toolkits/FDP.Toolkit.Time/Translators/SlaveLockstepTranslator.cs` (NEW)
- **Updated factory:** `FDP/Toolkits/FDP.Toolkit.Time/TimeNetworkModule.cs` (UPDATE)
- **Updated factory:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/TimeControllerFactory.cs` (UPDATE)
- **New tests:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/LockstepTranslatorTests.cs` (NEW)
- **Update tests:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/TimeControllerFactoryTests.cs` (UPDATE — add new tests)
- **FDP solution:** `FDP/FDP.sln`
- **Time tests csproj:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj`

### Report Submission

**When done, submit your report to:**  
`.dev/time-ctrl-unif/reports/BATCH-04-REPORT.md`

---

## Context

The current `FrameLockstepDescriptorTranslator` wires both FrameOrder and FrameAck on every node. This creates echo loops that required stateful tracking variables (`_lastSentOrderFrameId`, `_lastSentAckFrameId`). The new translators are role-specific: master only writes FrameOrder and reads FrameAck; slave only reads FrameOrder and writes FrameAck. Echoes are structurally impossible.

**Related Tasks:**
- [TCU-TR001](../docs/TASK-DETAIL.md#tcu-tr001--masterlocksteptranslator)
- [TCU-TR002](../docs/TASK-DETAIL.md#tcu-tr002--slavelocksteptranslator)
- [TCU-TR003](../docs/TASK-DETAIL.md#tcu-tr003--update-timenetworkmodule-factory-methods)
- [TCU-T003](../docs/TASK-DETAIL.md#tcu-t003--unit-tests-lockstep-translators)
- [TCU-T004](../docs/TASK-DETAIL.md#tcu-t004--unit-tests-timecontrollerfactory-updated)

---

## 🎯 Batch Objectives

1. `MasterLockstepTranslator.cs` — reads FrameAck (ingress → FrameStepCompletedEvent), writes FrameOrder (AdvanceFrameIntent → egress). No FrameOrder reader, no FrameAck writer. No tracking state.
2. `SlaveLockstepTranslator.cs` — reads FrameOrder (ingress → AdvanceFrameIntent), writes FrameAck (FrameStepCompletedEvent → egress). No echo prevention needed.
3. `TimeNetworkModule` updated with `CreateMasterLockstepTranslator` + `CreateSlaveLockstepTranslator`; existing `CreateLockstepTranslator` marked `[Obsolete]`.
4. `TimeControllerFactory` updated: `Master+Continuous` returns `MasterSyncController`; `Slave+Any` returns `SlaveSyncController`.
5. Tests file for translators and factory updates.

---

## ✅ Tasks

### Task 1: MasterLockstepTranslator (TCU-TR001)

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Translators/MasterLockstepTranslator.cs` (NEW FILE)  
**Task Definition:** See [TCU-TR001 in TASK-DETAIL.md](../docs/TASK-DETAIL.md#tcu-tr001--masterlocksteptranslator)

**Summary:**
- Implements `IDescriptorTranslator`; `TopicName = "FrameOrder"`; `DescriptorOrdinal = 202`
- Egress: drain `AdvanceFrameIntent` from bus → map to `FrameOrderDescriptor` → write to DDS
- Ingress: read `FrameAckDescriptor` from DDS → map to `FrameStepCompletedEvent` → PublishManaged to bus
- **DDS resources created:** `DdsWriter<FrameOrderDescriptor>`, `DdsReader<FrameAckDescriptor>`
- **DDS resources NOT created:** `DdsReader<FrameOrderDescriptor>`, `DdsWriter<FrameAckDescriptor>`
- **No `_lastSentOrderFrameId` or any tracking state** — just stateless pipes
- `participant == null` → both sides are no-ops (test environment safety)

**FrameOrderDescriptor field mapping from AdvanceFrameIntent:**
- `FrameOrderDescriptor.FrameID = intent.FrameID`
- `FrameOrderDescriptor.FixedDelta = intent.FixedDelta`
- `FrameOrderDescriptor.TargetSimTime = intent.TargetSimTime`
- `FrameOrderDescriptor.TimeScale = 0` (or the master's current TimeScale if available)

**FrameAckDescriptor field mapping to FrameStepCompletedEvent:**
- `FrameStepCompletedEvent.FrameID = ack.FrameID`
- `FrameStepCompletedEvent.NodeID = ack.NodeID`

---

### Task 2: SlaveLockstepTranslator (TCU-TR002)

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Translators/SlaveLockstepTranslator.cs` (NEW FILE)  
**Task Definition:** See [TCU-TR002 in TASK-DETAIL.md](../docs/TASK-DETAIL.md#tcu-tr002--slavelocksteptranslator)

**Summary:**
- Implements `IDescriptorTranslator`; `TopicName = "FrameOrder"`; `DescriptorOrdinal = 203`
- Ingress: read `FrameOrderDescriptor` from DDS → map to `AdvanceFrameIntent` → PublishManaged to bus
- Egress: drain `FrameStepCompletedEvent` from bus → map to `FrameAckDescriptor` → write to DDS
- **DDS resources created:** `DdsReader<FrameOrderDescriptor>`, `DdsWriter<FrameAckDescriptor>`
- **DDS resources NOT created:** `DdsWriter<FrameOrderDescriptor>`, `DdsReader<FrameAckDescriptor>`
- `participant == null` → no-ops
- `_localNodeId` passed at construction; used when creating `FrameAckDescriptor.NodeID`

**FrameOrderDescriptor → AdvanceFrameIntent mapping:**
- `AdvanceFrameIntent.FrameID = order.FrameID`
- `AdvanceFrameIntent.FixedDelta = order.FixedDelta`
- `AdvanceFrameIntent.TargetSimTime = order.TargetSimTime`

**FrameStepCompletedEvent → FrameAckDescriptor mapping:**
- `FrameAckDescriptor.FrameID = evt.FrameID`
- `FrameAckDescriptor.NodeID = _localNodeId`

---

### Task 3: Update TimeNetworkModule Factory Methods (TCU-TR003)

**File:** `FDP/Toolkits/FDP.Toolkit.Time/TimeNetworkModule.cs` (UPDATE)  
**Task Definition:** See [TCU-TR003 in TASK-DETAIL.md](../docs/TASK-DETAIL.md#tcu-tr003--update-timenetworkmodule-factory-methods)

**Changes:**
1. Add `CreateMasterLockstepTranslator(participant, eventBus)` → returns `MasterLockstepTranslator`
2. Add `CreateSlaveLockstepTranslator(participant, eventBus, localNodeId)` → returns `SlaveLockstepTranslator`
3. Mark `CreateLockstepTranslator(participant, eventBus, localNodeId)` with `[Obsolete("Use CreateMasterLockstepTranslator or CreateSlaveLockstepTranslator")]`
4. Keep the old method functional — do NOT remove it (Phase 5 will migrate call sites)

---

### Task 4: Update TimeControllerFactory (TCU-W005)

**File:** `FDP/Toolkits/FDP.Toolkit.Time/Controllers/TimeControllerFactory.cs` (UPDATE)  
**Task Definition:** See [TCU-W005 in TASK-DETAIL.md](../docs/TASK-DETAIL.md#tcu-w005--update-timecontrollerfactory)

**Changes:**
1. `TimeRole.Master` + `TimeMode.Continuous` → returns `MasterSyncController` (not `MasterTimeController`)
2. `TimeRole.Slave` + either mode → returns `SlaveSyncController` (not `SlaveTimeController`)
3. `TimeRole.Standalone` → **unchanged** (must still return the existing standalone controller)

**CRITICAL:** Standalone path must remain unchanged. Many unit tests and tools rely on it.

---

### Task 5: Unit Tests for Translators and Factory (TCU-T003 + TCU-T004)

**New file:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/LockstepTranslatorTests.cs`  
**Updated file:** `FDP/Toolkits/FDP.Toolkit.Time.Tests/TimeControllerFactoryTests.cs`

**Translator tests (TCU-T003):**
From TASK-DETAIL.md §TCU-TR001 success conditions:
- `MasterLockstepTranslator_NullParticipant_DoesNotThrow` — construct with null; call `ScanAndPublish` and `PollIngress`; assert no exception
- `MasterLockstepTranslator_Egress_PublishesFrameOrderFromAdvanceFrameIntent` — publish `AdvanceFrameIntent { FrameID=7, FixedDelta=0.016f }` to bus; swap; call `ScanAndPublish` (null DDS = no-op DDS write); swap; assert no stray events remain
- `MasterLockstepTranslator_Ingress_PublishesFrameStepCompletedEvent` — null DDS ingress is no-op; documents that contract
- `MasterLockstepTranslator_TopicName_IsFrameOrder`
- `SlaveLockstepTranslator_NullParticipant_DoesNotThrow`
- `SlaveLockstepTranslator_Ingress_PublishesAdvanceFrameIntent` — null DDS = no-op
- `SlaveLockstepTranslator_Egress_DrainFrameStepCompletedEvent` — publish `FrameStepCompletedEvent { FrameID=3, NodeID=10 }` to bus; call `ScanAndPublish`; swap; assert event was drained from bus
- `SlaveLockstepTranslator_DescriptorOrdinal_Is203`

**Factory tests (TCU-T004, add to existing TimeControllerFactoryTests.cs):**
- `TimeControllerFactory_Master_Continuous_ReturnsMasterSyncController`
- `TimeControllerFactory_Slave_Continuous_ReturnsSlaveSyncController`
- `TimeControllerFactory_Slave_Deterministic_ReturnsSlaveSyncController`
- `TimeControllerFactory_Standalone_ReturnsUnchangedType` — existing test must still pass

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: Complete in sequence with passing tests:**

1. **Tasks 1+2 (TCU-TR001/TR002):** Implement translators → `dotnet build FDP/FDP.sln` — zero errors ✅  
2. **Task 3 (TCU-TR003):** Update TimeNetworkModule → build clean ✅  
3. **Task 4 (TCU-W005):** Update TimeControllerFactory → build clean ✅  
4. **Task 5 (TCU-T003/T004):** Write all tests → `dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj` — all pass ✅

**DO NOT** skip steps. Fix all errors before proceeding. Write report only when everything is green. No asking for permission.

---

## 🧪 Testing Requirements

- **Minimum:** 8 translator tests (TCU-T003) + 4 factory tests (TCU-T004) = **12 tests minimum**  
- **Null-participant safety:** All translators must work when participant is null  
- **Existing factory tests must not break:** Run full test suite to verify

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] `MasterLockstepTranslator.cs` and `SlaveLockstepTranslator.cs` compile
- [ ] `TimeNetworkModule` has `CreateMasterLockstepTranslator` and `CreateSlaveLockstepTranslator`
- [ ] `CreateLockstepTranslator` is marked `[Obsolete]` but still compiles
- [ ] `TimeControllerFactory` returns `MasterSyncController` for Master+Continuous and `SlaveSyncController` for Slave+Any
- [ ] `dotnet build FDP/FDP.sln` — zero errors  
- [ ] 12+ tests pass — all new translator and factory tests
- [ ] `dotnet test FDP/Toolkits/FDP.Toolkit.Time.Tests/FDP.Toolkit.Time.Tests.csproj` — all 111 pre-existing + new tests pass
- [ ] `BATCH-04-REPORT.md` submitted

---

## 📊 Report Requirements

Submit to `.dev/time-ctrl-unif/reports/BATCH-04-REPORT.md`.

```markdown
# BATCH-04 Report
## Completion Status
## Test Results
## Developer Insights
**Q1:** Challenges with the translator abstraction vs. existing IDescriptorTranslator interface?
**Q2:** Weak points spotted in the DDS translation layer?
**Q3:** Design decisions made beyond the spec?
**Q4:** Anything about the TimeControllerFactory update worth noting?
**Q5:** Suggested commit message
```

---

## ⚠️ Common Pitfalls

- Ordinal numbers matter: `MasterLockstepTranslator` is `202`, `SlaveLockstepTranslator` is `203`.
- `participant == null` must be a safe no-op for ALL code paths in both translators.
- Do NOT rename or remove the old `CreateLockstepTranslator` (Phase 5 call sites still use it).
- Standalone factory path (`TimeRole.Standalone`) must be **completely unchanged** — many tests depend on it.
- Domain types (`AdvanceFrameIntent`, `FrameStepCompletedEvent`) have no `[EventId]` — use `PublishManaged`/`ConsumeManaged`.
- Study `FrameLockstepDescriptorTranslator.cs` to understand `ScanAndPublish` / `PollIngress` pattern before writing.

---

## 📚 Reference Materials

- **Task Definitions:** `.dev/time-ctrl-unif/docs/TASK-DETAIL.md` — §TCU-TR001, §TCU-TR002, §TCU-TR003, §TCU-T003, §TCU-T004
- **Design:** `.dev/time-ctrl-unif/docs/DESIGN.md` — §4.4 Role-Split Lockstep Translators
- **Developer Skill Guide:** `.github/skills/developer/SKILL.md`
