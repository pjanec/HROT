# BATCH-09 Review

**Status:** APPROVED ✅  
**Reviewer:** Dev Lead  
**Tasks:** PACK2-C001 · PACK2-R003

---

## Results

| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| Hrot.Editor.Tests | 15 | 17 | +2 ✅ |
| Hrot.ClusterRunner.Integration.Tests | pre-existing | +5 smoke tests | (2 pass / 3 skip) |
| Hrot.ScenarioEditor.Tests | 14 | 14 | 0 (no regressions) |

---

## Files Changed

| File | Change |
|------|--------|
| `Hrot.Editor/Hrot.Editor.csproj` | Added `OutputType=Exe`, `Hrot.SimHost`, `Hrot.CGF`, `Hrot.Orchestrator` refs |
| `Hrot.Editor/Program.cs` | NEW — offline All-In-One composition root |
| `Hrot.Editor.Tests/Hrot.Editor.Tests.csproj` | Added explicit refs for transitive types |
| `Hrot.Editor.Tests/OfflineKernelBootTests.cs` | NEW — 2 smoke tests (init + 10-frame tick) |
| `Hrot.ClusterRunner.Integration.Tests/HrotRunnerHarness.cs` | Added `HrotRunnerHarness(RunMode mode, int domainId)` ctor |
| `Hrot.ClusterRunner.Integration.Tests/CgfHarness.cs` | NEW |
| `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` | NEW |
| `Hrot.ClusterRunner.Integration.Tests/HarnessSmokeTests.cs` | NEW — 5 smoke tests |

---

## Deviations

1. **`NetworkEntityMap` from `FDP.Toolkit.Replication.Services`**: The subagent found `NetworkEntityMap` in `FDP.Toolkit.Replication.Services` (not `ModuleHost.Network.Cyclone.Services` as in instructions). Accepted — used whatever resolves correctly in that project.

2. **`ModuleHostKernel.Update(float)` obsolete**: Post-subagent fix applied by dev lead: replaced `TimeControllerFactory.Create()` with `new SteppingTimeController(new GlobalTime { TimeScale = 1.0f })` and replaced `kernel.Update(dt)` calls with `stepping.Step(dt); kernel.Update()` pattern in all three affected files (Program.cs, OfflineKernelBootTests.cs, EditorHarness.cs). Build now produces 0 warnings for this.

3. **CgfHarness DDS smoke tests skipped**: 3 of 5 harness smoke tests are `[Fact(Skip = "Requires CycloneDDS")]` for CgfHarness tests. EditorHarness 2 smoke tests pass (offline, no DDS).

4. **`Hrot.Editor.Tests.csproj` needed explicit refs**: EXE project references don't expose transitive types for compilation in test projects; added direct refs to Hrot.SimHost, Hrot.CGF, Hrot.Orchestrator, ModuleHost.Core.

---

## Quality Assessment

- `EditorDependencyTests.HrotEditor_HasNoTransitiveNedDependency` ✅ PASSES — `Hrot.NED` still absent from `Hrot.Editor.dll`'s assembly refs.
- `OfflineKernelBootTests` confirms kernel initializes + ticks 10 frames without exception.  
- `EditorHarness` is fully offline; `PumpFrames` and `PumpUntil` use `SteppingTimeController.Step() + Update()` pattern.
- `CgfHarness` wraps `CgfSubsystem` with auto-increment / shared-domain ctors as required.
- `HrotRunnerHarness(RunMode, int)` constructor added — prerequisite for R006 (IT-4).

---

## Tasks Completed

- [x] PACK2-C001 — HROT Editor All-In-One composition root
- [x] PACK2-R003 — CgfHarness + EditorHarness scaffolding
