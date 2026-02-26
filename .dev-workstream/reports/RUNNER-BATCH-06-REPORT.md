# RUNNER-BATCH-06 Report

**Batch:** RUNNER-BATCH-06  
**Phases:** R3.5 (Test Report Generator) + R4 (Runner Integration Verification)  
**Status:** ✅ Complete — Runner Application track finished

---

## Part 1: Phase R3.5 — Test Report Generator

### Summary

The `HeadlessTestExecutor` now emits a fully-structured JSON report upon test completion
(pass or fail). The report captures the test name, overall status, wall-clock duration,
per-metric statistical summaries (min / max / avg / p95), aggregated assertion counts,
and all failure messages. A human-readable console summary is also printed.

### Changes Made

#### `Bagira.Runner/Models/TestReport.cs` *(new file)*

Created the public model classes `TestReport` and `AssertionResults` as specified in
R3.5 SC-1. `TestReport.Metrics` reuses the existing `MetricSummary` class from
`Bagira.Runner.Services` (a superset of the spec's Min/Max/Avg/P95 fields), avoiding
duplication.

#### `Bagira.Runner/Services/HeadlessTestExecutor.cs`

- **Added `_runStopwatch` field** (`Stopwatch`): started via `Restart()` at the very
  beginning of `RunAsync()` so duration includes orchestrator init time.
- **Added `_totalAssertionChecks` field** (`int`): incremented once per assertion rule
  check inside `ValidateAssertions` to provide accurate totals for the report.
- **Replaced `GenerateReport()`**: now returns `Models.TestReport` populated with
  `TestName`, `Status`, `DurationSeconds`, per-metric summaries from
  `TestMetricsCollector`, and assertion totals (Total / Passed / Failed).
- **Replaced `SaveReport()`**: writes the JSON to
  `test_report_{yyyyMMdd_HHmmss}.json` (timestamped, prevents overwrites between runs),
  then prints the structured console summary specified in R3.5 SC-3.
- **Removed the private inner `TestReport` class**: superseded by the public model.

### Design Decisions

- **Single report file per run (timestamped):** The previous implementation wrote both
  a named report and `TestRunSummary.json`. Using only a timestamped file keeps the
  working directory clean and avoids silent overwrites when multiple scripts run in the
  same directory. Callers looking for the report can glob `test_report_*.json`.
- **`TestReport.Metrics` uses existing `MetricSummary`:** The existing class already
  covers all spec fields plus `Name` and `Count`, which are useful context in the JSON
  output. Creating a second model would be pure duplication.

---

## Part 2: Phase R4 — Integration Testing

### Summary

Two new test classes (`RunnerAggregatedModeTests`, `HeadlessExecutorIntegrationTests`)
in `Bagira.Runner.Tests` provide systematic coverage of the Runner's core value
proposition: multi-subsystem embeddability and end-to-end headless test execution.

### New File: `Bagira.Runner.Tests/RunnerIntegrationTests.cs`

#### `RunnerAggregatedModeTests` (5 tests)

| Test | Validates |
|------|-----------|
| `AggregatedMode_AllThreeSubsystems_InitializeSuccessfully` | All three mock subsystems receive `Initialize(cfg)` when `RunMode.All` is used |
| `AggregatedMode_ParsedMode_ContainsAllSubsystemFlags` | `ParsedMode` carries `SimHost \| IG \| IOS` flags |
| `AggregatedMode_NoWait_WaitingRoomBypassed` | `WaitForPeers` is empty when `--no-wait` is set (no DDS blocking) |
| `AggregatedMode_HeadlessFlag_PropagatedToAllSubsystems` | Each subsystem's `SubsystemConfig.Headless == true` |
| `AggregatedMode_RunFrames_UpdatesAllSubsystems` | `RunFrames(10)` drives exactly 10 update ticks on all three subsystems |

#### `HeadlessExecutorIntegrationTests` (3 tests)

| Test | Validates |
|------|-----------|
| `HeadlessExecution_TickAndAssert_PassesWithExitCode0` | Tick 10 frames + assert `frames_run==10` exits with code 0 and creates a `test_report_*.json` |
| `HeadlessExecution_SpawnAndAssertPosition_PassesWithExitCode0` | Spawn entity at (10,20,30), tick 5 frames, assert `x==10 z==30` — uses live `EntityRepository` |
| `HeadlessExecution_FailingAssertion_ExitCode1AndFailStatus` | Deliberate assertion failure (`frames_run==99`) exits code 1 and report contains `"FAIL"` |

All three `HeadlessExecutorIntegrationTests` run in real-time (≤ 400 ms each) using
a headless `SubsystemOrchestrator` with a `MockSubsystem`. A `NullTestLogger`
(implemented inline) avoids adding a hard dependency on
`Microsoft.Extensions.Logging.Abstractions` to the test project.

---

## Part 3: Performance Test Triage

### Summary

The three previously-identified flaky tests were analysed in
`.dev-workstream/PERFORMANCE-TRIAGE.md`. All three fail exclusively due to CI
environment resource constraints — not algorithmic regressions.

### Findings

| Test | Root Cause | Algorithm Deficiency? |
|------|-----------|----------------------|
| `ComponentDirtyTracking_ConcurrentScanPerformance` | 10-writer thread saturation overwhelms 2–4 vCPU CI agent | **No** — `HasChanges()` uses `Volatile.Read` on version counters (~5–20 ns); no `BitMask256` involvement |
| `EntityComplexityPerformanceTests.Lightweight_PlainUnmanaged_BestPerformance` | Slow/network-backed CI temp storage dominates I/O in `blocking: true` capture | **No** — achieves 400–800 FPS on local SSD; >90% of failure time is in `FileStream.Write` |
| `Performance_100Entities_Maintains60Hz` | CPU load from concurrent CI jobs steals 2–5 ms/tick on 2-core agents | **No** — passes in isolation; `BitMask256` scan contributes ≤ 2 µs of the 17 ms budget |

### Action Taken

Added `[Trait("Category", "Performance")]` to all three tests:
- `FDP/Kernel/Fdp.Kernel.Tests/ComponentDirtyTrackingTests.cs`
- `FDP/Kernel/Fdp.Kernel.Tests/EntityComplexityPerformanceTests.cs`
- `Bagira.SimHost.Integration.Tests/PerformanceTests.cs`

CI pipelines can now exclude these with `--filter "Category!=Performance"`.
See `PERFORMANCE-TRIAGE.md` for the full analysis and the recommended scheduled-run
strategy on a dedicated performance agent.

---

## Test Results

```
dotnet test Bagira.Runner.Tests/Bagira.Runner.Tests.csproj --no-build -c Debug
```

| Assembly | Passed | Failed | Notes |
|----------|--------|--------|-------|
| Bagira.Runner.Tests | **90** | 0 | +8 new integration tests vs. Batch 05 |

All pre-existing test assemblies build clean. The `[Trait("Category","Performance")]`
annotations compile without error in both `Fdp.Tests` and
`Bagira.SimHost.Integration.Tests`.

---

## Deliverables Checklist

- ✅ `Models/TestReport.cs` — structured `TestReport` + `AssertionResults` model
- ✅ `HeadlessTestExecutor` — `GenerateReport()` populates full structured report; `SaveReport()` writes timestamped JSON + console summary
- ✅ `RunnerAggregatedModeTests` — 5 tests validating `RunMode.All` embedded execution
- ✅ `HeadlessExecutorIntegrationTests` — 3 tests covering pass, entity-spawn, and deliberate-fail paths
- ✅ `PERFORMANCE-TRIAGE.md` — documented root cause and CI filter recommendation for all three flaky tests
- ✅ `[Trait("Category","Performance")]` applied to all three flaky tests
- ✅ 90/90 tests passing in `Bagira.Runner.Tests`
- ✅ Zero new compiler warnings introduced
