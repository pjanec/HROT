# BATCH-01 Review

**Batch:** BATCH-01  
**Reviewer:** Development Lead  
**Date:** 2026-03-17  
**Status:** ✅ APPROVED

---

## Summary

The determinist orchestration, scenario interface, and test harness are well-built with excellent test quality verifying actual behavior. Testing correctly compiles/verifies logic against offline instances. A few specific NLog formatting requirements were missed which will be addressed in BATCH-02's corrective tasks.

---

## Issues Found

### Issue 1: Missing NLog Configuration Details
**File:** `FDP/Examples/Fdp.Examples.Runner/Program.cs` 
**Problem:** The file target layout is missing `tick=${event-properties:tick}` and the filename relies on C# `DateTime.Now` string interpolation instead of the requested NLog `${cached}` format. `MappedDiagnosticsContext["scenario"]` was not set.
**Fix:** Update the NLog programmatic configuration to exactly match the format in DEM1-F004. This is logged as Corrective Task 1.

### Issue 2: Missing Per-Tick Trace Logging
**File:** `FDP/Examples/Fdp.Examples.Common/ScenarioSubsystem.cs`
**Problem:** The `Update` method does not invoke `FdpLog<ScenarioSubsystem>.Trace(...)` each tick as stipulated in the instructions.
**Fix:** Add `FdpLog<ScenarioSubsystem>.Trace("[{0}] tick={1}", _scenario.ScenarioName, _tick);` at the start of `Update`. This is logged as Corrective Task 2.

---

## Verdict

**Status:** APPROVED

**All requirements met. Ready to merge.**

---

## 📝 Commit Message

```
feat: demo runner framework foundation (BATCH-01)

Completes DEM1-F001, DEM1-F002, DEM1-F003, DEM1-F004, DEM1-F005

Implements deterministic evaluation mode via ScenarioSubsystem.
Creates fdp-demo-runner execution entry point with programmatic NLog targets.
Establishes xUnit scenario test harness for independent offline validation.

FDP.Framework.Runner:
- Added deterministic configuration to RunnerOptions and SubsystemOrchestrator

Fdp.Examples.Common:
- Added IScenario and ScenarioFailureException
- Implemented ScenarioSubsystem with time injection and EvaluateTick hooks
- Added DemoScenario ids and doctrine constants

Fdp.Examples.Runner:
- Implemented demo runner Program.cs using CommandLine
- Dynamic NLog target generation (file + console)

Testing:
- 14 tests covering logic, formatting, timeouts, and orchestrator overrides
- Disabled xUnit parallelization to protect global LogManager state

Related: DEM1-TASK-DETAIL.md, DEM1-DESIGN.md
```

---

**Next Batch:** BATCH-02
