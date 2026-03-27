# TwoAck-BATCH-02: Quality Assurance & Debt Burndown

**Batch Number:** TwoAck-BATCH-02
**Tasks:** CORRECTIVE-001, DEBT-TEST-001, DEBT-TEST-002, DEBT-UX-001
**Phase:** Technical Debt & Corrections
**Estimated Effort:** ~4-6 hours
**Priority:** HIGH
**Dependencies:** TwoAck-BATCH-01

---

## 📋 Onboarding & Workflow

### Developer Instructions
This batch addresses critical failures generated during TwoAck-BATCH-01, focusing squarely on test suites. The previous phase left the CI broken due to outdated integers on modified Enums and completely side-stepped the mandated ImGui testing protocol. 

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Review Feedback:** `.dev-workstream/reviews/TwoAck-BATCH-01-REVIEW.md` - MANDATORY READING ON TEST QUALITY FAILURES.
3. **Debt Tracker:** `.dev-workstream/DEBT-TRACKER.md` - See `TwoAck-BATCH-01` rows.

### Source Code Location
- **Primary Work Area:** `Bagira.SimHost.Tests`, `Bagira.IOS.Tests`
- **FDP is READ-ONLY:** Do not modify the `EntityLifecycleModule`.

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/TwoAck-BATCH-02-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/TwoAck-BATCH-02-QUESTIONS.md`

---

## Context

The system must run successfully on CI. Due to `SstStatusCode.EntityNotFound` shifting from `2` to `3`, `Bagira.SimHost.Tests` broke. Furthermore, shallow tests were checked in for `MissionPanel` covering only a private state wrapper, circumventing the actual visual behavioral state. We need these rectified immediately.

---

## 🎯 Batch Objectives
Restore the CI test suite to 100% passing state by fixing `MissionControlRequestSystemTests`. Remediate the shallow test logic applied in IOS testing and replace them with structurally sound ImGui behavioral verification.

---

## ✅ Tasks

### Task 0: CI Regression Fix (CORRECTIVE-001) P1
**File:** `Bagira.SimHost.Tests/MissionControlRequestSystemTests.cs` (Line 259)
**Action Required:**
- `ProcessRequest_UnknownEntity_WritesNackAfterRetrying` currently asserts `errorCode: 2`. 
- Since `SstStatusCode.EntityNotFound` was shifted, update this literal value to properly index the `EntityNotFound` code (3). Ensure `dotnet test Bagira.SimHost.Tests` complies successfully.

### Task 1: Re-Implement ImGui MissionPanel Tests (DEBT-TEST-001) P2
**File:** `Bagira.IOS.Tests/TwoAckIosTests.cs`
**Action Required:**
- Remove the shallow tests covering just `IsPendingGuardActive`.
- Write a genuine visual test for `MissionPanel.Draw(IIosLogic logic)`.
- **Expected Assertion:** Setup a mock environment, pass an entity matching the `logic.IsEntityPending == true` condition, and assert `ImGui.BeginDisabled()` was executed sequentially through ImGui Mock validation outputs rendering processes.

### Task 2: Implement IosMock Global Alert UI test (DEBT-TEST-002) P2
**File:** `Bagira.IOS.Tests/IosMockTests.cs` (New or Extrapolated File)
**Action Required:**
- Setup an isolated test invoking `IosMock.DrawUI()`.
- Simulate `Logic.GlobalAlert` being non-null.
- **Expected Assertion:** Verify that `ImGui.OpenPopup("Entity Error")` executes.

### Task 3: UX Corrections (DEBT-UX-001) P3
**File:** `Bagira.IOS/Panels/MissionPanel.cs`
**Action Required:**
- Replace the pending visual text string `(awaiting entity confirmation...)` with the originally stated format: `[Constructing across network...]`. 

---

## 🧪 Testing Requirements
- **NO SHALLOW TESTS:** Assertions MUST prove the side-effect actions took place (ImGui executions) rather than only checking intermediate parameter variables mapped prior to rendering.
- Wait until `dotnet test` returns completely successful and cleanly.

---

## 📊 Report Requirements

**Developer Insights**

**Q1:** What issues did you encounter during implementation? How did you resolve them?
**Q2:** Did you spot any weak points in the existing codebase? What would you improve?
**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?
**Q4:** What edge cases did you discover that weren't mentioned in the spec?
**Q5:** Are there any performance concerns or optimization opportunities you noticed in the testing patterns here?

---

## 🎯 Success Criteria
- [ ] Task 0 (CI Build Fix) compiles and executes perfectly green over `dotnet test`.
- [ ] Tasks 1 & 2 assert literal framework method executions for `ImGui.BeginDisabled()` over the rendered UI components safely.
- [ ] Report submitted answering all insight queries explicitly.

---

## 📚 Reference Materials
- **Task Specifics:** `docs/two-ack/TWOACK-TASK-DETAIL.md` - Re-read `TWOACK-IOS003` logic bounds.
