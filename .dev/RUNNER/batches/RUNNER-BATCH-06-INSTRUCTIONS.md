# RUNNER-BATCH-06 Instructions

**Phase R3.5 (Test Report Generator) & Phase R4 (Runner Integration Verification)**

The previous batch stabilized the Runner by correctly resolving the global component ID mapping (`Phase R0.2`) and deploying the core test action executors and metrics collection (`Phase R3.3` / `R3.4`).

This batch represents the final stretch of the Runner Application track. We will finalize the Headless framework by saving out test reports and establish complete architectural confidence via an integration testing sweep across the different deployment modes (embedded runner vs. standalone processes).

---

## Part 1: Phase R3.5 — Test Report Generator

**Objective:** Combine the results from the executed test script and the sampled metrics into a structured JSON report format upon completion.

### Task 1: Integrate Test Report Generation
1. In `Hrot.ClusterRunner/Services/HeadlessTestExecutor.cs`, locate the `SaveReport()` stub or functionality (which currently may just write `TestRunSummary.json`).
2. Implement the structured `TestReport` model defined in `TASK-DETAILS-RUNNER.md` (Section R3.5).
   - This includes mapping the overall `TestName`, `Status`, `DurationSeconds`, `Metrics` aggregations (Min/Max/Avg/P95), and `AssertionResults` (Total, Passed, Failed).
3. Ensure the executor saves this file locally as `test_report_{timestamp}.json` upon test completion or failure.

---

## Part 2: Phase R4 — Integration Testing

**Objective:** Prove that the aggregation of `IgSubsystem`, `IosSubsystem`, and `SimHostSubsystem` into a single process operates correctly alongside the traditional distributed multi-process mode.

### Task 2: Implement Runner Mode Integration Tests
1. Create a suite of tests in `Hrot.ClusterRunner.Tests` (or an appropriate integration test project) that validate the core value proposition of the `Hrot.ClusterRunner`: **Multi-Subsystem Embeddability**.
2. **Aggregated Mode Test:** 
   - Boot `SubsystemOrchestrator` internally with `RunMode.All`.
   - Verify that all three subsystems (`SimHost`, `IG`, `IOS`) initialize successfully inside the single process.
   - Assert that the `WaitingRoomCoordinator` successfully bypasses / completes the wait since all subsystems are locally present.
3. **Headless Execution Test:**
   - Execute a minimal `TestScript` (e.g., spawn an entity, tick 10 frames, assert position) programmatically through the `HeadlessTestExecutor`.
   - Assert that the test script completes with `PASSED` status and a valid report is generated.

### Task 3: Performance Benchmark & Triage
1. Review the flaky tests identified previously (`ComponentDirtyTracking_ConcurrentScanPerformance`, `EntityComplexityPerformanceTests`, `Performance_100Entities_Maintains60Hz`).
2. Provide a short written analysis (e.g., in `PERFORMANCE-TRIAGE.md`) evaluating whether these tests are genuinely flaky due to the test hardware constraints/contention or if the `BitMask256` array scanning needs optimization. If no code changes are necessary, formally document the skipping or ignoring of these tests for CI environments.

---

## Deliverables Checklist
- ✅ Fully implemented `TestReport` generator outputting structured JSON.
- ✅ Integration tests validating `RunMode.All` embedded execution.
- ✅ Integration tests validating end-to-end `HeadlessTestExecutor` flow.
- ✅ Triage documentation for the flaky performance tests.
- ✅ **REPORT:** `.dev-workstream/reports/RUNNER-BATCH-06-REPORT.md` confirming the completion of the Runner Application track.
