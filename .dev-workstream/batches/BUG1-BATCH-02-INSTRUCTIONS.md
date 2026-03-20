# BUG1-BATCH-02: Tech Debt, Continuous Drag & Mission Fixes

**Batch Number:** BUG1-BATCH-02  
**Tasks:** BUG1-T001, BUG1-T002, BUG1-T003, BUG1-T004, BUG1-I001, BUG1-M001, BUG1-M002  
**Phase:** Phase 3, Phase 4 & Technical Debt Burndown  
**Estimated Effort:** ~11-14 hours  
**Priority:** HIGH  
**Dependencies:** BUG1-BATCH-01

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome to BUG1-BATCH-02. This batch tackles the accumulated technical debt from the previous batch (testability issues, NodeID pass-through to IOS, and pre-existing failing IG tests). Following the tech debt, you will implement Phase 3 (IG Continuous Drag mode) and Phase 4 (Mission system fixes). 

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md` - How to work with batches
2. **Task Definitions:** `docs/bugs-1/TASK-DETAIL.md` - See BUG1-I001, BUG1-M001, BUG1-M002 specifics
3. **Design Document:** `docs/bugs-1/DESIGN.md` - Architectural context
4. **Previous Review:** `.dev-workstream/reviews/BUG1-BATCH-01-REVIEW.md` - See previous batch results

### Source Code Location
- **Primary Work Area:** `Bagira.Map.Common/`, `Bagira.SimHost/`, `Bagira.IG/`, `Bagira.IOS/`
- **Test Project:** Respective `.Tests` packages

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BUG1-BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/BUG1-BATCH-02-QUESTIONS.md`

---

## 🔄 MANDATORY WORKFLOW: Test-Driven Task Progression

**CRITICAL: You MUST complete tasks in sequence with passing tests:**

1. **Task 1:** Implement → Write tests → **ALL tests pass** ✅
2. **Task 2:** Implement → Write tests → **ALL tests pass** ✅  
3. **Task 3:** Implement → Write tests → **ALL tests pass** ✅

**DO NOT** move to the next task until:
- ✅ Current task implementation complete
- ✅ Current task tests written
- ✅ **ALL tests passing** (including previous batch tests)

---

## Context

We discovered testability and architectural paper-cuts during BATCH-01, plus pre-existing bad tests in IG. We fix these tech debt items first. Secondly, we add a Continuous Drag capability for developers to observe network updates during map object dragging. Finally, we resolve mission creation issues (missing default triggers) and OCC version conflicts causing UI blocks.

**Related Tasks:**
- [DEBT] BUG1-T001: Inject ACK writer into `UpdateEntityDescriptorRequestSystem`.
- [DEBT] BUG1-T002: Separate egress translators from ingress in `SimHostApp`.
- [DEBT] BUG1-T003: Pass NodeId into IOS Subsystem.
- [DEBT] BUG1-T004: Fix pre-existing IG test failures.
- [BUG1-I001](docs/bugs-1/TASK-DETAIL.md#bug1-i001-add-continuous-drag-update-toggle-to-ig): Add Continuous Drag Update Toggle to IG.
- [BUG1-M001](docs/bugs-1/TASK-DETAIL.md#bug1-m001-default-doctrinefinished-trigger-on-task-creation): Default DoctrineFinished Trigger.
- [BUG1-M002](docs/bugs-1/TASK-DETAIL.md#bug1-m002-track-control-commands-for-occ-version-sync): Track Control Commands for OCC Version Sync.

---

## ✅ Tasks

### Task 1: Refactor ACK Writer Injection (BUG1-T001)

**File:** `Bagira.Map.Common/Systems/UpdateEntityDescriptorRequestSystem.cs` (UPDATE)  

**Description:**
The system is `sealed` and creates DDS objects in its constructor, slowing down unit tests. 

**Requirements:**
- Refactor the constructor to allow injecting an interface (e.g., `IUpdateEntityAckSink` or `IDdsWriter<UpdateEntityDescriptorAck>`).
- Preserve the default constructor for production to automatically initialize DDS resources if not provided.
- Ensure all existing tests pass correctly.

**Tests Required:**
- ✅ Verify unit tests can mock the ACK writer without spinning up full DDS layer.

---

### Task 2: Separate Egress Translators (BUG1-T002)

**File:** `Bagira.SimHost/SimHostApp.cs` (UPDATE)  

**Description:**
`SimHostApp.OnLoad()` passes an all-encompassing `translators` list to `CycloneNetworkCleanupSystem`, combining both egress and ingress translators.

**Requirements:**
- Maintain a separate list for Egress translators.
- Only pass the Egress translators into the `CycloneNetworkCleanupSystem` constructor.
- This prevents "Dispose" being needlessly called on ingress translators.

**Tests Required:**
- ✅ Existing initialization tests continue to pass.

---

### Task 3: Node-ID Pass-through to IOS (BUG1-T003)

**File:** `Bagira.Runner/Services/IosSubsystem.cs` (UPDATE)  

**Description:**
BUG1-F002 passed the offset-resolved `NodeId` into `IgSubsystem` and `SimHostSubsystem`, but `IosSubsystem` may have been missed.

**Requirements:**
- Update `IosSubsystem.cs` to pass `config.NodeId` down to the embedded `IosApplication` analogously to how the IG and SimHost work.
- If `IosApplication` does not yet accept it, update its `InitializeEmbedded` signature.

**Tests Required:**
- ✅ Subsystem startup correctly wires `config.NodeId` to the underlying app.

---

### Task 4: Fix IG Tests (BUG1-T004)

**File:** `Bagira.IG.Tests` (UPDATE)  

**Description:**
There are ~6 pre-existing failures under `EditToolTests` and `TraceLoggingTests`.

**Requirements:**
- Investigate these 6 tests and correct their assertions or setup logic. They were broken before the BUG1 work stream.
- No hacky `[Skip]` attributes allowed; genuinely fix them.

**Tests Required:**
- ✅ All `Bagira.IG.Tests` must yield 100% green.

---

### Task 5: Continuous Drag Toggle (BUG1-I001)

**File:** `Bagira.IG/IgApplication.cs`, `Bagira.IG/Systems/MapUserConfig.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md §BUG1-I001](docs/bugs-1/TASK-DETAIL.md#bug1-i001-add-continuous-drag-update-toggle-to-ig)

**Requirements:**
- Implement `ContinuousDragUpdates` property and logic described in the design.
- Throttle interval = 0.1s (10Hz).
- Reset continuous drag timer on drag end.
- Must fall back identically to existing behavior when `false`.

**Tests Required:**
- ✅ Validated that continuous mode throttles to ~10 Hz.
- ✅ Validated `false` produces absolutely NO network calls until drop.

---

### Task 6: Default DoctrineFinished Trigger (BUG1-M001)

**File:** `Bagira.IOS/Panels/MissionPanel.cs` (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md §BUG1-M001](docs/bugs-1/TASK-DETAIL.md#bug1-m001-default-doctrinefinished-trigger-on-task-creation)

**Requirements:**
- Add default trigger `Type = "DoctrineFinished"` in `HandleAddTask()`.

**Tests Required:**
- ✅ Single and multiple task creation must yield precisely one trigger correctly named.

---

### Task 7: Track Control Commands for OCC Version (BUG1-M002)

**File:** `Bagira.IOS/Services/MissionEditorService.cs` (and Interface/Caller) (UPDATE)  
**Task Definition:** See [TASK-DETAIL.md §BUG1-M002](docs/bugs-1/TASK-DETAIL.md#bug1-m002-track-control-commands-for-occ-version-sync)

**Requirements:**
- Replace synchronous `SendControlCommand` with `SendControlCommandAsync` tracking against the TCS `_pendingCommits` dictionary.
- UI must update `_draftBaseVersion` and correctly lock during flight.

**Tests Required:**
- ✅ Abort updates `_draftBaseVersion` and locks UI cleanly.
- ✅ Jump updates `_draftBaseVersion`.

---

## ⚠️ Quality Standards

**❗ TEST QUALITY EXPECTATIONS**
- **REQUIRED:** Tests that verify actual behavior and edge cases explicitly. Do NOT trust strings.
- **FOR BUG1-T004:** Write down root causes in the Developer Insight questions.

---

## 📊 Report Requirements

**Focus on Developer Insights, Not Understanding Checks**

Please capture your valuable insights and experience:

**Q1:** What issues did you encounter during implementation? How did you resolve them?

**Q2:** Did you spot any weak points in the existing codebase? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** What were the exact root causes of the 6 failing IG tests?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] BUG1-T001 to T004 completed
- [ ] BUG1-I001 completed
- [ ] BUG1-M001 to M002 completed
- [ ] All tests passing
- [ ] Report submitted answering the Developer Insight questions
