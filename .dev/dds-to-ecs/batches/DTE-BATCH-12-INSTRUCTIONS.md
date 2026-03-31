# DTE-BATCH-12: Integration Troubleshooting Phase 1

**Batch Number:** DTE-BATCH-12  
**Tasks:** INTS-P1-001, INTS-P1-002, INTS-P1-003, INTS-P1-004, INTS-P1-005  
**Phase:** Integration Troubleshooting P1  
**Estimated Effort:** 18�22 hours  
**Priority:** HIGH  
**Dependencies:** DTE-BATCH-11 approved

---

## ?? Onboarding & Workflow

### Developer Instructions
This batch addresses the Phase 1 integration fixes from `TASK-DETAILS-Integration-Troubleshooting.md`. Focus on TKB bootstrap consistency, SpawnEntityCommand usage, IOS DDS writers, ImGui passthrough, and IG-to-IOS event bridging.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Details:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md` (INTS-P1-001 ? INTS-P1-005)
3. **Design Document:** `docs/design/DESIGN-Integration-Troubleshooting.md`
4. **Previous Review:** `.dev-workstream/reviews/DTE-BATCH-11-REVIEW.md`

### Source Code Location
- **Primary Work Area:** `Hrot.SimHost/`, `Hrot.IG/`, `Hrot.ExCon/`, `Hrot.ClusterRunner/`, `Hrot.Map.Common/`
- **Test Projects:** `Hrot.SimHost.Tests/`, `Hrot.IG.Tests/`, `Hrot.ExCon.Tests/`, `Hrot.ClusterRunner.Integration.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/DTE-BATCH-12-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/DTE-BATCH-12-QUESTIONS.md`

---

## ?? MANDATORY WORKFLOW: Finish the Batch Completely

**CRITICAL:** Do the work end-to-end without stopping to ask for permission. Implement, write tests, run tests, fix root causes, and repeat until **all tests pass**. No partial handoffs, no asking to run tests, no asking whether to fix obvious failures.

---

## ? Tasks

### Task 1: INTS-P1-001 � Register TKB Catalog in SimHost and IG
**Files:** `Hrot.SimHost/SimHostApp.cs`, `Hrot.IG/IgApplication.cs` (UPDATE)  
**Task Definition:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-001--register-tkb-catalog-in-simhost-and-ig`

---

### Task 2: INTS-P1-002 � Fix SimHost Vehicle Spawning to Use `SpawnEntityCommand`
**File:** `Hrot.SimHost/UI/SimHostScenarioManager.cs` (UPDATE)  
**Task Definition:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-002--fix-simhost-vehicle-spawning-to-use-spawnentitycommand`

---

### Task 3: INTS-P1-003 � Replace `NullDdsWriter` with `DdsWriterAdapter`
**Files:**
- `Hrot.Map.Common/Dds/DdsWriterAdapter.cs` (NEW)
- `Hrot.ExCon/Program.cs` (UPDATE)
- `Hrot.ClusterRunner/Services/IosSubsystem.cs` (UPDATE)

**Task Definition:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-003--replace-nullddswriter-with-ddswriteradapter-in-ios`

---

### Task 4: INTS-P1-004 � ImGui DockSpace passthrough
**File:** `Hrot.ExCon/IosMock.cs` (UPDATE)  
**Task Definition:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-004--add-passthrucentralnode-to-imgui-dockspace`

---

### Task 5: INTS-P1-005 � Wire IG-to-IOS map event translators
**File:** `Hrot.IG/IgApplication.cs`, `Hrot.Map.Common/Commands/BdcCommandGateway.cs`, `Hrot.IG/UI/MiniIosPanelState.cs` (UPDATE)  
**Task Definition:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p1-005--wire-ig-to-ios-map-event-translators`

---

## ?? Testing Requirements
- **Framework:** xUnit only. Do not add MSTest or NUnit tests.
- Run:
  - `dotnet test Hrot.SimHost.Tests/Hrot.SimHost.Tests.csproj`
  - `dotnet test Hrot.IG.Tests/Hrot.IG.Tests.csproj`
  - `dotnet test Hrot.ExCon.Tests/Hrot.ExCon.Tests.csproj`
  - `dotnet test Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj`

---

## ?? Quality Standards

**? TEST QUALITY EXPECTATIONS**
- Tests must verify behavior (no string-only checks).
- Include all scenarios specified in `TASK-DETAILS-Integration-Troubleshooting.md` for these tasks.

**? REPORT QUALITY EXPECTATIONS**
- Include test output summary.
- List any design deviations and why they were required.

---

## ?? Report Requirements

## Developer Insights

**Q1:** What issues did you encounter with TKB registration and SpawnEntityCommand integration? How did you resolve them?

**Q2:** Did you spot any weak points in DDS writer integration? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or optimization opportunities you noticed?

---

## ?? Success Criteria

This batch is DONE when:
- [ ] INTS-P1-001�P1-005 complete with xUnit tests
- [ ] All tests pass
- [ ] Report submitted to `.dev-workstream/reports/DTE-BATCH-12-REPORT.md`

---

## ?? Common Pitfalls to Avoid
- Do not initialize a second DDS participant for IG event bridging.
- Do not bypass `SpawnEntityCommand` in SimHost scenario manager.
- Do not leave `NullDdsWriter` in production entry points.

---

## ?? Reference Materials
- **Task Details:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md`
- **Design:** `docs/design/DESIGN-Integration-Troubleshooting.md`
