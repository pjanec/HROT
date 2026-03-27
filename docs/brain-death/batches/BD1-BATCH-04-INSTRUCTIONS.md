# BD1-BATCH-04: Tech Debt Burndown

**Batch Number:** BD1-BATCH-04  
**Tasks:** DEBT-1, DEBT-2, DEBT-3, DEBT-4  
**Phase:** Maintenance / Tech Debt  
**Estimated Effort:** ~4-6 hours  
**Priority:** MEDIUM  
**Dependencies:** BD1-BATCH-03

---

## 📋 Onboarding & Workflow

### Developer Instructions
Congratulations on completing the BD1 specification! All functional phases mapped out in the Task Tracker have been resolved.

This final batch acts as a dedicated tech-debt burndown block. You will be addressing the remaining architectural and test suite inconsistencies that built up during BD1, ensuring we leave the repository in a pristine state.

### Required Reading
1. **Debt Tracker:** `.dev-workstream/DEBT-TRACKER.md` (Review pending P2 and P3 tasks targeting BD1-BATCH-04)

### Source Code Location
- **Primary Work Areas:**
  - `FDP/Toolkits/FDP.Toolkit.Behavior/`
  - `FDP/Toolkits/FDP.Toolkit.ImGui/`
  - `Bagira.SimHost.Integration.Tests/`
  - `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/BD1-BATCH-04-REPORT.md`

---

## 🎯 Batch Objectives
- Fix broken integration tests by wiring up missing mission execution tiers.
- Eliminate native heap `Marshal.AllocHGlobal` churn in the ImGui reflector.
- Enforce test parallelization safety for native ImGui binaries.
- Unify/document the 1-frame propagation delay on `MissionDirectorSystem`.

---

## ✅ Tasks

### Task 1: Fix EntityMission_MovesEntity Integration Test (DEBT-1)
**File:** `Bagira.SimHost.Integration.Tests/EntityMission_MovesEntity.cs` (or equivalent target)
**Description:** The integration harness (likely `SimHostInstance`) lacks the proper pipeline mapping (e.g., `NavigationIntent` → `CarKinematicsSystem`) for missions to actually move the entity. Wire the necessary module updates in the test context.

### Task 2: ComponentReflector Native Allocations (DEBT-2)
**File:** `FDP/Toolkits/FDP.Toolkit.ImGui/Utils/ComponentReflector.cs`
**Description:** Replace `Marshal.AllocHGlobal` with `stackalloc` or a pooled `NativeArray<byte>` to check bytes per frame with absolutely zero native heap churn.

### Task 3: MissionDirectorSystem Frame Delay Documentation (DEBT-3)
**File:** `FDP/Toolkits/FDP.Toolkit.Behavior/Systems/MissionDirectorSystem.cs`
**Description:** `AssignDoctrineHashEvent` currently suffers a one-tick delay before taking effect. Detail the exact timing consequence via xml-docs, or adjust the Event bus firing group to execute this synchronously before `InputSystemGroup` takes over.

### Task 4: ImGui Tests Isolation Config (DEBT-4)
**File:** `FDP/Toolkits/FDP.Toolkit.ImGui.Tests/xunit.runner.json` (or `.csproj`)
**Description:** Add necessary xUnit runner configuration to ensure that `FDP.Toolkit.ImGui.Tests` run in isolation from other DLLs in the MSBuild run, solving native ImGui context collisions.

---

## 🎯 Success Criteria
- [ ] Task DEBT-1 completed (Integration tests pass)
- [ ] Task DEBT-2 completed
- [ ] Task DEBT-3 completed
- [ ] Task DEBT-4 completed (`dotnet test` runs reliably spanning the solution without ImGui aborts)
- [ ] ALL tests passing.
- [ ] Report submitted.
