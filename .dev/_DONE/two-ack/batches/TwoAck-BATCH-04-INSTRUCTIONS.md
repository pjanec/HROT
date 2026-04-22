# TwoAck-BATCH-04: Debt Burndown (Test Infrastructure & Architecture)

**Batch Number:** TwoAck-BATCH-04
**Tasks:** DEBT-TEST-003, DEBT-ARCH-002
**Phase:** Technical Debt & Refactoring
**Estimated Effort:** ~2-4 hours
**Priority:** MEDIUM
**Dependencies:** TwoAck-BATCH-03

---

## 📋 Onboarding & Workflow

### Developer Instructions
The structural Two-ACK feature implementation successfully concluded in Batch 03, achieving total systemic stability across unit and integration boundaries. However, as is the case in large infrastructural refactors, some internal tech debt was generated along the way. This batch targets low-hanging code deduplication and test performance optimizations you identified in previous batches.

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/TwoAck-BATCH-04-REPORT.md`

---

## 🎯 Batch Objectives
Improve the throughput logic structure in the test assemblies: Extract replicated block behaviors to helper structures, and implement `IClassFixture` to pool ImGui allocations during UI validation sequences.

---

## ✅ Tasks

### Task 1: Deduplicate Runner Integration Helpers (DEBT-ARCH-002) P3
**Files:** `Hrot.ClusterRunner.Integration.Tests/*.cs`
**Action Required:**
- During BATCH-03 `TryTakeCreateAck` (which handles Two-ACK Phase matching) was discovered copy-pasted across `MiniIosIntegrationTests`, `MapPlacementIntegrationTests`, and `AreaAuthoringIntegrationTests`. 
- Extract identically purposed logic blocks into a unified `RunnerTestHelpers` static utility, or a Base Test Class Fixture implementation, unifying future state evaluation checks against a single source of truth.

### Task 2: Implement Shared ImGui Test Fixture (DEBT-TEST-003) P3
**Files:** `Hrot.ExCon.Tests`
**Action Required:**
- BATCH-02 successfully introduced headless `ImGui.CreateContext` tests via `[Collection("ImGui Sequential")]`, however font-atlas builds are executing redundantly per test frame costing roughly 50ms overhead per test iteration. 
- Refactor the tests generating native contexts to utilize an `IClassFixture<ImGuiTestFixture>` caching pattern mapping over the tests (mirroring `DerEntityInspectorPanelTests` methodology).

---

## 🧪 Testing Requirements
- Confirm all tests run cleanly and rapidly without crashes.
- Monitor `dotnet test` outputs to ensure no side-effects damage multi-threaded logic through fixture sharing.

---

## 📊 Report Requirements

**Developer Insights**
**Q1:** What issues did you encounter during implementation? How did you resolve them?
**Q2:** Did you spot any core functional weaknesses alongside the test-fixes? What would you improve?
**Q3:** What edge cases did you discover during `IClassFixture` implementations?
**Q4:** Are there any optimizations remaining in `ImGui` testing logic?
