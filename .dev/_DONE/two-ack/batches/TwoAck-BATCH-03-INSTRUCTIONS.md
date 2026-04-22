# TwoAck-BATCH-03: Integration Stability & Debt Burndown

**Batch Number:** TwoAck-BATCH-03
**Tasks:** CORRECTIVE-002, CORRECTIVE-003, DEBT-ARCH-001
**Phase:** Technical Debt & Integration Fixes
**Estimated Effort:** ~6-8 hours
**Priority:** HIGH
**Dependencies:** TwoAck-BATCH-02

---

## 📋 Onboarding & Workflow

### Developer Instructions
Your structural work and ImGui context manipulation in `TwoAck-BATCH-02` were excellent, however substituting the single-ACK creation entity workflow for the Two-ACK architecture produced systemic ripples. When we introduced Phase-1 `InProgress` ACKs into the data pipeline, the downstream Integration Tests broke violently. Due to an oversight, 18 total integration tests fail against `StatusCode=0` assertions globally. This batch serves to adapt the `MockIOSClient` and Runner integrations to successfully deserialize Two-ACK sequences correctly.

### Required Reading (IN ORDER)
1. **Current Review:** `.dev-workstream/reviews/TwoAck-BATCH-02-REVIEW.md`
2. **Design Document Recap:** `docs/two-ack/TWOACK-DESIGN.md` (Notice the Phase-1 vs Phase-2 terminal boundaries).

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/TwoAck-BATCH-03-REPORT.md`

---

## 🎯 Batch Objectives
Ensure the entire `IOS-IG-SimHost-FDP-2` solution runs clean on the CI via `dotnet test` without ignoring Project-level validations. Unify `createEntityAckQueue` injection across factory paths acting on P3 architectural debt logged earlier.

---

## ✅ Tasks

### Task 0: Restore SimHost Integration Tests (CORRECTIVE-002) P1
**File:** `Hrot.SimHost.Integration.Tests/Infrastructure/MockIOSClient.cs`
**Action Required:**
- Change `WaitForAckAsync` logic in the Mock Client. Currently, it retrieves the very first ACK published. Under Two-ACK, this is `InProgress=1`. 
- Modify the method to evaluate and loop past `InProgress`, only returning when hitting terminal states (e.g., `StatusCode != (int)SstStatusCode.InProgress`), OR augment `TryGetAck` behavior to match against expected variables securely.
- Ensure the 17 execution failures in `Hrot.SimHost.Integration.Tests.dll` are fully restored.

### Task 1: Runner Integration Adjustments (CORRECTIVE-003) P1
**Files:** `Hrot.ClusterRunner.Integration.Tests/MiniIosIntegrationTests.cs` (or equivalent test structures failing).
**Action Required:**
- `FirstSpawn_DoesNotExhaustIdPool` fails on the same phase logic. Trace its consumption chain and instruct its mock-layer to expect Two-ACKs properly (`InProgress` then `Success`). Ensure `Hrot.ClusterRunner.Integration.Tests.dll` runs completely green.

### Task 2: Mandatory Ack Queue Constructor Injection (DEBT-ARCH-001) P3
**Files:** `Hrot.ExCon/IosLogic.cs` and all Factory references.
**Action Required:**
- The report from BATCH-02 wisely noted that `_createEntityAckQueue` was optional for backward compatibility, presenting a silent failure surface if phase-processing goes ignored by legacy callers. 
- Refactor `IosLogic` to require the parameter mandatorily.
- Sweep through both the Production apps and the Testing suite factory setups (`IosMock`, `IosApplication`, internal tests, etc.) appending the exact collection queue logic where necessary. Make it explicit.

---

## 🧪 Testing Requirements
- Confirm all modifications by running the full solution suite.
- Run `dotnet test` uniformly from the solution root. Zero failures are tolerated in `SimHost.Integration.Tests` or `Runner.Integration.Tests`.

---

## 📊 Report Requirements

**Developer Insights**
**Q1:** What issues did you encounter during implementation? How did you resolve them?
**Q2:** Did you spot any weak points in the existing codebase? What would you improve?
**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?
**Q4:** What edge cases did you discover that weren't mentioned in the spec?
**Q5:** Are there any performance concerns or optimization opportunities?
