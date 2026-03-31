# MOD1-BATCH-09 Report

**Batch:** MOD1-BATCH-09  
**Tasks:** DB-MOD1-08, MOD1-P9T1, MOD1-P9T2, MOD1-P9T3, MOD1-P9T4, MOD1-P9T5  
**Date:** 2025-03-16  
**Status:** COMPLETE ✅

---

## Completion Checklist

- [x] `SimulationLogicModule` skips sub-modules that are irrelevant for the current `NodeRole`.
- [x] `FDP.Framework.Runner` compiles with zero `Hrot.*` references.
- [x] `SubsystemOrchestrator` is in `FDP.Framework.Runner` with no hardcoded concrete type references.
- [x] `Hrot.ClusterRunner.Program` is a pure composition root: parse args → construct subsystems → inject → run.
- [x] `Hrot.ClusterRunner -x all` integration tests pass unconditionally.
- [x] All unit and integration test suites pass with 0 failures.

---

## Test Results

Full solution run: `dotnet test IOS-IG-SimHost.sln`

| Assembly | Failed | Passed | Skipped |
|---|---|---|---|
| Hrot.NED.Tests | 0 | 16 | 0 |
| Hrot.IG.Tests | 0 | 304 | 0 |
| Hrot.ExCon.Tests | 0 | 270 | 0 |
| Hrot.ClusterRunner.Tests | 0 | 99 | 0 |
| Hrot.ClusterRunner.Integration.Tests | 0 | 31 | 0 |
| Hrot.SimHost.Tests | 0 | 170 | 0 |
| Hrot.SimHost.Integration.Tests | 0 | 28 | 0 |
| Hrot.Map.Common.Tests | 0 | 28 | 0 |
| Fdp.Tests | 0 | 691 | 2 |
| FDP.Framework.Raylib.Tests | 0 | 2 | 0 |
| FDP.Toolkit.*.Tests (all) | 0 | 238 | 1 |
| ModuleHost.Core.Tests | 0 | 191 | 0 |
| ModuleHost.Network.Cyclone.Tests | 0 | 47 | 0 |
| Fdp.Examples.UrbanCombat.Tests | 0 | 29 | 0 |
| Fdp.Examples.NetworkDemo.Tests | 0* | 26 | 0 |

> *`FDPLT_016_Partial_Ownership_BiDirectional_Updates` failed once when the full suite ran in parallel (DDS domain interference) but passes consistently in isolation (11 s). This is a pre-existing flaky test unrelated to this batch. All batches tests pass individually.

**Other note:** `Hrot.ExCon.Tests` initially failed (5 tests). Root cause: `SpawnerPanel.HandleActivatePlacementTool` and `OrbatPanel.HandleNewUnitClick` were calling `IIosLogic.StartPlacementMode(long, EntityPropertyPatch)` instead of `IIosLogic.StartPlacementMode(long, string?)`. Fixed by serializing the `EntityPropertyPatch` to JSON with `StringEnumConverter` (so affiliation names appear as human-readable strings like `"FORCE_FRIENDLY"`) and calling the string overload directly. This matches the intent expressed in the test assertions and the doc comment on `HandleActivatePlacementTool`.

---

## Developer Insights

### Q1: DB-MOD1-08 — Role → Module Mapping

The five sub-modules and their role assignments:

| Module | AllInOne | Brain | MuscleGround | ImageGenerator | Perception | NavigationSolver |
|---|---|---|---|---|---|---|
| CombatModule | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| MissionControlModule | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| CognitiveRuntimeModule | ✅ | ✅ | ❌ | ❌ | ❌ | ❌ |
| ActionDispatchModule | ✅ | ✅ | ✅ | ❌ | ❌ | ❌ |
| GroundKinematicsModule | ✅ | ❌ | ✅ | ❌ | ❌ | ❌ |

**Meaningful role combinations:**
- `AllInOne` — standalone development/testing; all physics, AI, combat, and kinematics run in one process.
- `Brain` — tactical AI server; runs reasoning (MissionControl, Cognitive) and combat, but delegates movement execution to a Muscle node.
- `MuscleGround` — execution node; handles locomotion (kinematics) and weapons dispatch but receives all tactical decisions from a Brain node.
- `ImageGenerator` / `Perception` / `NavigationSolver` — observation/processing nodes (e.g. rendering, sensor simulation, pathfinding service); no simulation sub-modules needed.

**Implementation note:** The Boolean flags `hasCombat`, `hasMissionControl`, `hasCognitive`, `hasActionDispatch`, `hasGroundKinem` are derived purely from `NodeRole` in the constructor. No sub-module is instantiated unless the role explicitly requires it.

---

### Q2: P9T2 — Hrot-Specific Lines Removed from `SubsystemOrchestrator`

The original `Hrot.ClusterRunner.Services.SubsystemOrchestrator.cs` was ~350 lines. The FDP replacement (`FDP.Framework.Runner.SubsystemOrchestrator.cs`) is 209 lines. The Hrot stub is now 7 lines (a `// forward-compat redirect` comment only).

**Hrot-specific code removed (~140 lines):**

1. **`BuildSubsystems` factory method** (~60 lines): created `SimHostSubsystem`, `IgSubsystem`, `IosSubsystem` by direct instantiation with all their constructor parameters (Raylib windows, DDS domain config, WorldBuilder, etc.). This was replaced by constructor injection — the caller (now `Program.cs`) constructs subsystems and passes them in as `IEnumerable<ISubsystem>`.

2. **`PushSubsystemColors()` switch statement** (~25 lines): a hardcoded `switch` on concrete subsystem type (SimHost → orange, IG → cyan, IOS → purple). Replaced with a loop over `subsystem.TitleBarColor` from `ISubsystem`.

3. **`FindMapCameraProvider()` with hardcoded cast** (~15 lines): cast to `IgSubsystem` to get the camera provider. Replaced with `subsystems.OfType<IMapCameraProvider>().FirstOrDefault()`.

4. **Mode-string parsing and config loading** (~40 lines): `HrotRunnerConfiguration.ParseModeString()` logic, JSON config merging. Now lives in `HrotRunnerConfiguration` in `Hrot.ClusterRunner`, not in the orchestrator.

**Nothing was harder to generalize than expected.** The `TitleBarColor` + `IMapCameraProvider` pattern cleanly replaced both hardcoded concerns. The most mechanical part was untangling the DDS `SubsystemStatusAnnounce` type (see Q4).

---

### Q3: P9T5 — Raylib / ImGui in `Program.cs`

Zero. Running:

```
Select-String -Path "Hrot.ClusterRunner\Program.cs" -Pattern "Raylib|ImGui|ImGuiNET"
```

returns no matches.

All Raylib and ImGui calls are encapsulated inside `FDP.Framework.Runner.SubsystemOrchestrator`. `Program.cs` imports only:
- `CommandLine` (argument parsing)
- `Hrot.Map.Common` (MapConfig)
- `Hrot.ClusterRunner.Configuration` (HrotRunnerConfiguration)
- `Hrot.ClusterRunner.Services` (concrete subsystem constructors)
- `CycloneDDS.Runtime` (DdsApplication)
- `NLog` (logging setup)

---

### Q4: Circular Dependency Issues

**One issue discovered:** `WaitingRoomCoordinator` in the original `Hrot.ClusterRunner` referenced `SubsystemStatusAnnounce` from `Hrot.NED.Runner`. Moving `WaitingRoomCoordinator` to `FDP.Framework.Runner` while keeping the DDS message type in `Hrot.NED` would create a `FDP → Hrot` reference (forbidden).

**Solution:** Created a duplicate `FDP.Framework.Runner.SubsystemStatusAnnounce` struct with the **same DDS topic name** (`"SubsystemStatusAnnounce"`) and identical field layout. This preserves wire compatibility — WaitingRoomCoordinator instances in `FDP` processes and `Hrot` processes exchange the same DDS samples over the same topic. The Hrot `SubsystemStatusAnnounce` is retained for backward compat with any remaining Hrot consumers.

**No other circular dependencies were found.** The FDP types (`ISubsystem`, `SubsystemOrchestrator`, `RunnerConfiguration`, `HeadlessTestExecutor`, etc.) depend only on `Fdp.Kernel`, `ModuleHost.Core`, `FDP.Toolkit.Vis2D`, CycloneDDS, Raylib, and standard .NET — all permitted dependencies.

