# DTE-BATCH-14: Integration Troubleshooting Phase 3

**Batch Number:** DTE-BATCH-14  
**Tasks:** INTS-P3-011, INTS-P3-012, INTS-P3-013, INTS-P3-014  
**Phase:** Integration Troubleshooting P3  
**Estimated Effort:** 18–22 hours  
**Priority:** HIGH  
**Dependencies:** DTE-BATCH-13 approved

---

## ?? Onboarding & Workflow

### Developer Instructions
This batch adds trace logging and an end-to-end DDS lifecycle integration test to validate the full SimHost?IG?IOS flow.

### Required Reading (IN ORDER)
1. **Workflow Guide:** `.dev-workstream/README.md`
2. **Task Details:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md` (INTS-P3-011 ? INTS-P3-014)
3. **Design Document:** `docs/design/DESIGN-Integration-Troubleshooting.md`
4. **Previous Review:** `.dev-workstream/reviews/DTE-BATCH-13-REVIEW.md`

### Source Code Location
- **Primary Work Area:** `Bagira.SimHost/`, `Bagira.IG/`, `Bagira.IOS/`, `Bagira.Runner/`
- **Test Projects:** `Bagira.SimHost.Integration.Tests/`, `Bagira.IG.Tests/`, `Bagira.IOS.Tests/`

### Report Submission
**When done, submit your report to:**  
`.dev-workstream/reports/DTE-BATCH-14-REPORT.md`

**If you have questions, create:**  
`.dev-workstream/questions/DTE-BATCH-14-QUESTIONS.md`

---

## ?? MANDATORY WORKFLOW: Finish the Batch Completely

**CRITICAL:** Do the work end-to-end without stopping to ask for permission. Implement, write tests, run tests, fix root causes, and repeat until **all tests pass**. No partial handoffs, no asking to run tests, no asking whether to fix obvious failures.

---

## ? Tasks

### Task 1: INTS-P3-011 — Trace logging for SimHost entity spawn
**Files:** `Bagira.SimHost/UI/SimHostScenarioManager.cs`, `FDP.Toolkit.NetworkSpawning/Systems/NetworkSpawningSystem.cs`, `FDP.Toolkit.Lifecycle/*`, `Bagira.SimHost/Translators/*` (UPDATE)  
**Task Definition:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p3-011--trace-logging-simhost-entity-spawn-flow-1`

---

### Task 2: INTS-P3-012 — Trace logging for IG ingress & render
**Files:** `Bagira.IG/Translators/EntityMasterTranslator.cs`, `Bagira.IG/Translators/GeoSpatialTranslator.cs`, `Bagira.IG/Systems/StyleResolutionSystem.cs`, `Bagira.IG/Adapters/SstVisualizerAdapter.cs` (UPDATE)  
**Task Definition:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p3-012--trace-logging-ig-entity-ingress--render-flow-2`

---

### Task 3: INTS-P3-013 — Trace logging for IOS/IG interactions
**Files:** `Bagira.Map.Common/Commands/BdcCommandGateway.cs`, `Bagira.SimHost/Systems/CreateEntityRequestSystem.cs`, `Bagira.IOS/IosLogic.cs`, `Bagira.IOS/Services/RequestTransactionManager.cs`, `Bagira.IG/*` (UPDATE)  
**Task Definition:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p3-013--trace-logging-ig-map-drawings--ios-interactions-flows-36`

---

### Task 4: INTS-P3-014 — End-to-End entity lifecycle integration test
**File:** `Bagira.SimHost.Integration.Tests/*` (ADD/UPDATE)  
**Task Definition:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md#ints-p3-014--integration-test-end-to-end-entity-lifecycle`

---

## ?? Testing Requirements
- **Framework:** xUnit only. Do not add MSTest or NUnit tests.
- Run:
  - `dotnet test Bagira.SimHost.Integration.Tests/Bagira.SimHost.Integration.Tests.csproj`
  - `dotnet test Bagira.IG.Tests/Bagira.IG.Tests.csproj`
  - `dotnet test Bagira.IOS.Tests/Bagira.IOS.Tests.csproj`

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

**Q1:** What issues did you encounter adding trace logging across SimHost/IG/IOS? How did you resolve them?

**Q2:** Did you spot any weak points in the end-to-end lifecycle test? What would you improve?

**Q3:** What design decisions did you make beyond the instructions? What alternatives did you consider?

**Q4:** What edge cases did you discover that weren't mentioned in the spec?

**Q5:** Are there any performance concerns or optimization opportunities you noticed?

---

## ?? Success Criteria

This batch is DONE when:
- [ ] INTS-P3-011–P3-014 complete with xUnit tests
- [ ] All tests pass
- [ ] Report submitted to `.dev-workstream/reports/DTE-BATCH-14-REPORT.md`

---

## ?? Common Pitfalls to Avoid
- Ensure trace logging does not spam per-frame loops (guard render logs).
- Keep trace logs behind debug/trace gating where required.
- Dispose DDS participants in integration tests to avoid domain conflicts.

---

## ?? Reference Materials
- **Task Details:** `docs/design/TASK-DETAILS-Integration-Troubleshooting.md`
- **Design:** `docs/design/DESIGN-Integration-Troubleshooting.md`
