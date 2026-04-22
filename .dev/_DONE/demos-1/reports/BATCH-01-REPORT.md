# BATCH-01 Report

**Batch:** BATCH-01  
**Developer:** GitHub Copilot (Claude Sonnet 4.6)  
**Date:** 2026-03-17  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| DEM1-F001 | ✅ Complete | Deterministic mode in RunnerOptions, RunnerConfiguration, SubsystemConfig, SubsystemOrchestrator |
| DEM1-F002 | ✅ Complete | IScenario, ScenarioFailureException, ScenarioSubsystem in new Fdp.Examples.Common project |
| DEM1-F003 | ✅ Complete | fdp-demo-runner CLI with DemoRunnerOptions, ScenarioRegistry, Program.cs |
| DEM1-F004 | ✅ Complete | Programmatic NLog setup in Program.cs — file + console targets, log path printed to stdout |
| DEM1-F005 | ✅ Complete | ScenarioNames/DemoTemplateIds/DemoDoctrineIds constants, ScenarioTestHarness, Fdp.Examples.Scenarios.Tests project |

**Related projects created:**
- `FDP/Framework/FDP.Framework.Runner.Tests/` — DEM1-F001 deterministic orchestrator tests
- `FDP/Examples/Fdp.Examples.Common/` — IScenario, ScenarioSubsystem, constants
- `FDP/Examples/Fdp.Examples.Scenarios/` — stub (populated in future batches)
- `FDP/Examples/Fdp.Examples.Runner/` — fdp-demo-runner executable
- `FDP/Examples/Fdp.Examples.Scenarios.Tests/` — harness + scenario/NLog/runner integration tests

---

## 🧪 Testing Results

**Unit Tests Passed:** 14 / 14  
**Integration Tests Passed:** 14 / 14

**Test breakdown by task:**

| Test | Task | Status |
|------|------|--------|
| `DeterministicOrchestratorPassesFixedDt_ToSubsystemUpdate` | DEM1-F001 | ✅ |
| `NonDeterministicHeadlessOrchestratorPassesZeroDt` | DEM1-F001 | ✅ |
| `SubsystemConfigPropagatesDeterministicFlag` | DEM1-F001 | ✅ |
| `ScenarioSubsystem_ExitsZero_WhenScenarioSucceeds` | DEM1-F002 | ✅ |
| `ScenarioSubsystem_ExitsOne_WhenAssertionFails` | DEM1-F002 | ✅ |
| `ScenarioSubsystem_ExitsTwo_OnTimeout` | DEM1-F002 | ✅ |
| `ScenarioSubsystem_Deterministic_GlobalTimeHasCorrectDelta` | DEM1-F002 | ✅ |
| `Runner_WithUnknownScenario_ExitsNonZero` | DEM1-F003 | ✅ |
| `Runner_PrintsLogFilePath_ToStdout` | DEM1-F003 | ✅ |
| `AfterRun_LogFileExists_AndContainsExpectedLines` | DEM1-F004 | ✅ |
| `OnFailure_LogFileContains_DiagnosticValues` | DEM1-F004 | ✅ |
| `ScenarioTestHarness_WithSucceedingScenario_ReturnsZero` | DEM1-F005 | ✅ |
| `ScenarioTestHarness_WithFailingScenario_ReturnsOne` | DEM1-F005 | ✅ |
| `ScenarioTestHarness_WithTimingOutScenario_ReturnsTwo` | DEM1-F005 | ✅ |

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

**NLog global-state parallelism:** xUnit runs test classes in parallel by default, and multiple tests were calling `LogManager.Configuration = ...` simultaneously. One test's MemoryTarget would capture logs intended for another test, causing intermittent false failures. Resolved by adding `[assembly: CollectionBehavior(DisableTestParallelization = true)]` to the test assembly.

**NLog file locking:** `KeepFileOpen = true` in the NLog file target held the file handle open while the test tried to `File.ReadAllText()`. Resolved by using `KeepFileOpen = false` in test file targets and calling `LogManager.Configuration = null` before reading, which releases all NLog file handles.

**Log format mismatch:** The batch spec requires tests to check for `"[CI SUCCESS]"` and `"[CI FAILURE]"` as bracketed literals. My first implementation logged `"=== CI SUCCESS"` for the success path (matching the onboarding guide visual format). After seeing the test failures I aligned the log format to `"[CI SUCCESS]"` to match the spec, making it consistent with `"[CI FAILURE]"` and `"[CI TIMEOUT]"`.

**MapCamera namespace:** `IMapCameraProvider.GetMapCamera()` returns `FDP.Toolkit.Vis2D.Components.MapCamera`, which is in a different sub-namespace than `FDP.Toolkit.Vis2D.MapCanvas`. Adding `using FDP.Toolkit.Vis2D.Components;` resolved the compile error.

**InternalsVisibleTo for Exe project:** The `Program` class is `internal` in the `fdp-demo-runner` Exe project. To call `Program.RunMain` from tests, I added an `<AssemblyAttribute>` element in the `.csproj` to emit `[assembly: InternalsVisibleTo("Fdp.Examples.Scenarios.Tests")]`.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `ModuleHostKernel.Update(float deltaTime)` (the legacy manual-dt overload) warns internally that it "might desync the _timeController state." Since `ScenarioSubsystem` calls `_timeController.Step(dt)` before `_kernel.Update()`, it routes through the standard `Update()` path which reads from the controller — but if someone accidentally calls `_kernel.Update(someDt)` instead, the time singleton gets overwritten with an inconsistent state. I'd add a `[Obsolete]` attribute or remove the legacy overload.

- The `FDP.Framework.Runner.SubsystemOrchestrator.RunFrames(int)` previously always passed `0f` as delta regardless of options. This was a silent bug that would affect any headless test using `RunFrames` with a deterministic scenario — all deltas would be zero. Now it correctly uses `_fixedDeltaSeconds` when deterministic.

**Q3: What design decisions did you make beyond the instructions? What alternatives did you consider?**

- **Constructor dt vs. config dt for ScenarioSubsystem:** The spec says both the constructor `fixedDeltaSeconds` parameter and `config.FixedDeltaSeconds` (from the orchestrator) apply. I designed it so that the constructor parameter is the fallback (for direct test usage), while `Initialize(config)` overwrites with `config.FixedDeltaSeconds` when `config.Deterministic == true`. This means `ScenarioTestHarness.Run(dt: 0.025f)` works correctly because both sources agree.

- **PlaceholderScenario in the Runner project:** Rather than adding a placeholder to `Fdp.Examples.Scenarios` (which the runner references), I added `PlaceholderScenario` directly inside `Fdp.Examples.Runner` as an `internal sealed` class. This keeps the scenarios assembly clean and avoids introducing any "test only" entry into the production scenarios library before Phase 2.

- **`AttachOrchestrator` instead of passing orchestrator in constructor:** The subsystem needs the orchestrator reference to call `Stop()` in the exit path, but the subsystem is created before the orchestrator. Using `AttachOrchestrator(orch)` as a separate step (called before `orch.Run()`) is cleaner than a circular constructor dependency.

- **`[assembly: CollectionBehavior(DisableTestParallelization = true)]` scope:** Applied to the entire Fdp.Examples.Scenarios.Tests assembly rather than individual `[Collection]` groups. Since all tests in this assembly touch NLog's global configuration, disabling parallelism entirely is safer and simpler than attempting to segregate by Collection.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- **EvaluateTick ordering with GlobalTime:** The spec calls for `Step(dt)` before `EvaluateTick`, and then `kernel.Update()` afterward. I also explicitly `SetSingletonUnmanaged(stepped)` on the world between Step and EvaluateTick. This ensures that when a scenario reads `world.GetSingletonUnmanaged<GlobalTime>()` inside `EvaluateTick`, it sees this frame's delta — not the delta written by the previous tick's `kernel.Update()`.

- **kernel.Update() calls _timeController.Update() again:** After I call `_timeController.Step()`, the kernel also calls `_timeController.Update()` internally during `UpdateInternal`. With `SteppingTimeController`, `Update()` simply returns the last stepped state without advancing — so this is idempotent and produces the correct DeltaTime in `SetSingletonUnmanaged` the second time too.

- **Tick 0 vs tick 1 semantics:** `_tick` starts at 0 and is incremented to 1 before the first `EvaluateTick` call. So scenarios should treat tick 1 as "first frame." The spec tests confirm this — `MockSucceedAtTickScenario(5)` succeeds on tick 5.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `FdpLog<T>.Info(format, arg0)` uses `_logger.Info(format, arg0)` which avoids params-array boxing — this is already well designed and suitable for the hot update path.  

- In the NLog file tests, `KeepFileOpen = false` creates a new OS file handle on every log write. For production use in `Program.cs` the NLog configuration uses the default (KeepFileOpen = true) which is correct. This distinction is intentional and documented with a comment.

- The `Program.RunMain(args, stdout, exitCallback)` pattern adds a thin allocation layer (closures capturing `capturedCode`) but this is only hit once at startup — no hot-path concern.

---

## ⚠️ Outstanding Issues / Next Steps

- `Fdp.Examples.Scenarios` is a stub — no actual scenarios are implemented yet. Future batches will populate `ScenarioRegistry` (`AutoDriveScenario`, `ComponentDamageScenario`, etc.) and add the corresponding xUnit tests in `Fdp.Examples.Scenarios.Tests`.
- The `--attach-vis2d` flag in `DemoRunnerOptions` wires through to `headless = false` but the Vis2D Raylib path has no scenario implementations to render yet.
- `DemoScenarioTracker` component and `MockBlackboardState` (from DEM1-I002) are not implemented — they are part of Phase 1 infrastructure tasks, not Phase 0.
