# BUG1-BATCH-03: Critical Bug Fixes & Ongoing Debt Burndown

**Batch Number:** BUG1-BATCH-03
**Tasks:** BUG1-T005, BUG1-T006, BUG1-T007, BUG1-M001-A
**Phase:** Technical Debt & Bug Trailing
**Estimated Effort:** ~10-12 hours  
**Priority:** CRITICAL  
**Dependencies:** BUG1-BATCH-02

---

## 📋 Onboarding & Workflow

### Developer Instructions
Welcome back to BUG1-BATCH-03. After User testing, we found a critical bug in how SimHost parses newly minted `DoctrineFinished` triggers coming from the network (BUG1-M001-A). Your top priority is addressing this P1 failure. Once resolved, proceed with the outstanding debt items highlighted during BATCH-02 review.
Ensure strict adherence to testing for all paths implemented.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Design Document:** `docs/bugs-1/DESIGN.md`
3. **Previous Review:** `.dev-workstream/reviews/BUG1-BATCH-02-REVIEW.md`

### Source Code Location
- **Primary Work Area:** `Hrot.SimHost/`, `Hrot.ExCon/`
- **Test Project:** Respective `.Tests` packages

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BUG1-BATCH-03-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/BUG1-BATCH-03-QUESTIONS.md`

---

## ✅ Tasks

### Task 1: Fix DoctrineFinished String Parsing Bug (BUG1-M001-A)  [P1 Critical]

**File:** `Hrot.SimHost/Systems/MissionControlRequestSystem.cs` (UPDATE)  

**Description:**
User feedback indicates that vehicles don't move when assigned a task. The root cause is `MissionControlRequestSystem.ResolveTrigger()` does not contain a handler for the `"DoctrineFinished"` string pattern coming from the `MissionTrigger.Type` field over DDS. It falls through to the default of `(TimerElapsed, 0f)` which instantly completes the movement phase!

**Requirements:**
- Add string pattern `"DoctrineFinished"` to the switch statement mapping to `EcsMissionTrigger.DoctrineFinished`.
- Maintain the current `0f` default parameter mapping for it.

**Tests Required:**
- ✅ Unit tests checking `ResolveTrigger` string-to-EcsComponent conversions covering "DoctrineFinished" and validating it actually yields `EcsMissionTrigger.DoctrineFinished`.

---

### Task 2: Translator Separation Test Coverage (BUG1-T005)

**File:** `Hrot.SimHost.Tests` (CREATE/UPDATE)  

**Description:**
We separated egress translators in BATCH-02's `SimHostApp.OnLoad()`, but there's no automated test preventing regression on this specific behavior.

**Requirements:**
- Write an initialization test or utilize a mock translator array confirming that `CycloneNetworkCleanupSystem` only accepts intended egress instances.

**Tests Required:**
- ✅ Valid injection counting to verify ingress translators are completely excluded from `CycloneNetworkCleanupSystem`.

---

### Task 3: Plumb NodeId into IosMock (BUG1-T006)

**File:** `Hrot.ClusterRunner/Services/IosSubsystem.cs`, `Hrot.ExCon/IosApplication.cs` (UPDATE)  

**Description:**
The sub-system class saves the `_nodeIdOverride`, but it hasn't actually been passed down into `IosMock.InitializeEmbedded` yet.

**Requirements:**
- Modify `InitializeEmbedded` parameter lists as needed.
- Forward `_nodeIdOverride` so the base applications themselves understand what NodeId they are.

**Tests Required:**
- ✅ Add tests verifying the deeper wiring into `IosApplication`/`IosMock` instances.

---

### Task 4: Command Async Round-trip Test (BUG1-T007)

**File:** `Hrot.ExCon.Tests` (UPDATE)  

**Description:**
During BATCH-02, `HandleAbort` to `SendControlCommandAsync` updating `CommitInFlight` worked nicely, but there's no integration test validating the full TCS completion callback sets `CommitInFlight = false` and processes the version upcast correctly.

**Requirements:**
- Complete the validation flow testing from initiating abort through to completing the asynchronous TCS task mimicking the real network ACK.

**Tests Required:**
- ✅ Full `OnAckReceived` simulating success mapping back to the TCS and cleanly unlocking the UI controls.

---

## 📈 Quality Standards

**❗ TEST QUALITY EXPECTATIONS**
- Do NOT just write test stubs.
- Task 4 acts as your asynchronous integration proof; write robust logic utilizing tasks appropriately to simulate standard DDS round trip event cycles.

---

## 📊 Report Requirements

**Q1:** How did you structure the asynchronous `TaskCompletionSource` callback testing for Task 4 without leaking memory or thread locks?

**Q2:** Are there any further missing string mappings inside `ResolveTrigger` in `MissionControlRequestSystem` that we should note in debt tracker?

---

## 🎯 Success Criteria

This batch is DONE when:
- [ ] BUG1-M001-A completed and fixing unit tests.
- [ ] BUG1-T005 to T007 completed
- [ ] All tests passing
- [ ] Report submitted answering Developer Insight questions
