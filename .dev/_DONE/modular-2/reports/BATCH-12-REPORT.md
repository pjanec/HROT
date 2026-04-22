# BATCH-12 Report: Move ISubsystem Adapters to Plugin Assemblies

**Batch:** BATCH-12  
**Tasks:** TASK-P4-004  
**Date:** 2025  
**Status:** DONE (all phases complete; 2 headless tests skipped with explanation)

---

## Phase Summary

| Phase | Status | Notes |
|-------|--------|-------|
| Phase 1.1: CgfDebugVisualizerAdapter → Hrot.CGF | DONE | Moved to `Hrot.CGF/CgfDebugVisualizerAdapter.cs`; namespace changed to `Hrot.CGF` |
| Phase 1.2: EyesAndMuscleModule → Hrot.SimHost | DONE | Moved to `Hrot.SimHost/Modules/EyesAndMuscleModule.cs`; namespace changed to `Hrot.SimHost.Modules` |
| Phase 1.3: ClusterScenarioPanel + ClusterUiCache → Hrot.Orchestrator | DONE | Moved to `Hrot.Orchestrator/Panels/`; namespace changed to `Hrot.Orchestrator.Panels` |
| Phase 1.4: FdpPanelWindows → Hrot.Presentation | DONE | Moved to `Hrot.Presentation/Windows/FdpPanelWindows.cs`; classes changed from `internal sealed` to `public sealed` |
| Phase 2: CiSubsystem → Hrot.ClusterRunner/Scenarios | DONE | Moved within ClusterRunner from `Services/` to `Scenarios/`; namespace changed to `Hrot.ClusterRunner.Scenarios` |
| Phase 3: EyesAndMuscleSubsystem update | DONE | Added `using Hrot.SimHost.Modules;`; added `using IMapCameraProvider = Fdp.Engine.Runner.IMapCameraProvider;` alias to resolve ambiguity |
| Phase 4: SimHostSubsystem + SimHostWindows → Hrot.SimHost | DONE | Moved to `Hrot.SimHost/`; window classes stay `internal` |
| Phase 5: IgSubsystem + IgWindows → Hrot.IG | DONE | Moved to `Hrot.IG/`; window classes stay `internal` |
| Phase 6: CgfSubsystem → Hrot.CGF | DONE | Moved to `Hrot.CGF/CgfSubsystem.cs`; using directives updated |
| Phase 7: ExConSubsystem + ExConWindows → Hrot.ExCon | DONE | Moved to `Hrot.ExCon/`; added `using Fdp.Engine.Runner;` (was missing); `ClusterControlWindow` accessed from `Hrot.Orchestrator.Windows` |
| Phase 8: OrchestratorSubsystem + OrchestratorWindow + ClusterControlWindow → Hrot.Orchestrator | DONE | Moved to `Hrot.Orchestrator/`; `ClusterControlWindow` changed from `internal` to `public sealed` |
| Phase 9: EditorSubsystem + EditorWindows → Hrot.Editor | DONE | Moved to `Hrot.Editor/`; window classes stay `internal` |
| Phase 10: Program.cs updated | DONE | Added `using` directives for all 7 new namespaces |
| Phase 11: Headless unit tests | DONE (partial) | 3 active (IG, ExCon, Orchestrator), 2 skipped (SimHost, CGF — blocked by DdsIdAllocator waiting for live Orchestrator) |

---

## Remaining Files in `Hrot.ClusterRunner/Services/`

After the move, exactly 2 files remain in `Hrot.ClusterRunner/Services/` as intended:

| File | Reason for staying |
|------|---------------------|
| `EyesAndMuscleSubsystem.cs` | ClusterRunner-specific async SoD PoC; no separate plugin assembly for it |
| `PerspectiveUpdateSubsystem.cs` | Runner infrastructure subsystem; no domain-specific plugin |

The `Hrot.ClusterRunner/Windows/` directory has been removed entirely (all 7 window files moved to their respective assemblies/directories).

---

## Files Moved

| Old location | New location | Notes |
|---|---|---|
| `ClusterRunner/Services/CgfDebugVisualizerAdapter.cs` | `Hrot.CGF/CgfDebugVisualizerAdapter.cs` | |
| `ClusterRunner/Services/EyesAndMuscleModule.cs` | `Hrot.SimHost/Modules/EyesAndMuscleModule.cs` | |
| `ClusterRunner/Services/ClusterScenarioPanel.cs` | `Hrot.Orchestrator/Panels/ClusterScenarioPanel.cs` | |
| `ClusterRunner/Services/ClusterUiCache.cs` | `Hrot.Orchestrator/Panels/ClusterUiCache.cs` | |
| `ClusterRunner/Services/CiSubsystem.cs` | `Hrot.ClusterRunner/Scenarios/CiSubsystem.cs` | Stays in ClusterRunner, different folder |
| `ClusterRunner/Services/SimHostSubsystem.cs` | `Hrot.SimHost/SimHostSubsystem.cs` | |
| `ClusterRunner/Services/IgSubsystem.cs` | `Hrot.IG/IgSubsystem.cs` | |
| `ClusterRunner/Services/CgfSubsystem.cs` | `Hrot.CGF/CgfSubsystem.cs` | |
| `ClusterRunner/Services/ExConSubsystem.cs` | `Hrot.ExCon/ExConSubsystem.cs` | |
| `ClusterRunner/Services/OrchestratorSubsystem.cs` | `Hrot.Orchestrator/OrchestratorSubsystem.cs` | |
| `ClusterRunner/Services/EditorSubsystem.cs` | `Hrot.Editor/EditorSubsystem.cs` | |
| `ClusterRunner/Windows/FdpPanelWindows.cs` | `Hrot.Presentation/Windows/FdpPanelWindows.cs` | Made `public sealed` |
| `ClusterRunner/Windows/SimHostWindows.cs` | `Hrot.SimHost/Windows/SimHostWindows.cs` | |
| `ClusterRunner/Windows/IgWindows.cs` | `Hrot.IG/Windows/IgWindows.cs` | |
| `ClusterRunner/Windows/ExConWindows.cs` | `Hrot.ExCon/Windows/ExConWindows.cs` | |
| `ClusterRunner/Windows/OrchestratorWindow.cs` | `Hrot.Orchestrator/Windows/OrchestratorWindow.cs` | |
| `ClusterRunner/Windows/ClusterControlWindow.cs` | `Hrot.Orchestrator/Windows/ClusterControlWindow.cs` | Made `public sealed` |
| `ClusterRunner/Windows/EditorWindows.cs` | `Hrot.Editor/Windows/EditorWindows.cs` | |

---

## csproj Changes

Six `*.csproj` files were updated with new `ProjectReference` entries and `InternalsVisibleTo` attributes for test access:

| Project | Changes |
|---|---|
| `Hrot.CGF.csproj` | Added `Fdp.Presentation`, `Hrot.SimHost`, `Hrot.Map.Common` references; `InternalsVisibleTo Hrot.CGF.Tests`, `Hrot.SimHost.Tests`, etc. |
| `Hrot.Orchestrator.csproj` | Added `Fdp.Engine`, `Fdp.Presentation` references; added `InternalsVisibleTo Hrot.Orchestrator.Tests`, `Hrot.Orchestrator.Integration.Tests`, etc. |
| `Hrot.ExCon.csproj` | Added `Fdp.Presentation`, `Hrot.Network.NED`, `Hrot.Network.Orchestration`, `Hrot.Orchestrator` references |
| `Hrot.SimHost.csproj` | No new references needed (already had Fdp.Presentation) |
| `Hrot.IG.csproj` | No new references needed |
| `Hrot.Editor.csproj` | Added `Hrot.Orchestrator` reference for `ClusterScenarioPanel` and `ClusterUiCache` |
| `Hrot.Presentation.csproj` | New directory `Windows/` added; no new csproj references |

---

## Build Result

**0 errors.** Build confirmed clean after the following fix-up passes:

1. `OrchestratorSubsystem.cs` — re-added `using Hrot.Map.Common;` (removed by mistake; `HrotEnvironment.CreateParticipant` lives in that namespace)
2. `ExConSubsystem.cs` — added missing `using Fdp.Engine.Runner;` (required for `ISubsystem`, `IWindowRegistrar`, `SubsystemConfig`)
3. `EyesAndMuscleSubsystem.cs` — added `using IMapCameraProvider = Fdp.Engine.Runner.IMapCameraProvider;` alias to resolve ambiguity between `Hrot.SimHost.Modules.IMapCameraProvider` and `Fdp.Engine.Runner.IMapCameraProvider`
4. `SimTimeSyncIntegrationTests.cs` — added missing `using Hrot.IG;` directive
5. `SubsystemOrchestratorTests.cs` — updated 3 FQN references from `Hrot.ClusterRunner.Services.*` to `Hrot.SimHost.*`, `Hrot.IG.*`, `Hrot.ExCon.*`
6. `ExConSubsystemClusterTests.cs` — updated source file path from deleted location to `Hrot.ExCon/ExConSubsystem.cs`; relaxed `Hrot.Orchestrator` containment check (ExCon legitimately imports `Hrot.Orchestrator.Panels/Windows`)

---

## Test Results

All unit tests pass. The one pre-existing failure (`Fdp.Examples.CarKinem.Tests`, net9.0 target) is unrelated to this batch.

| Test Assembly | Passed | Failed | Skipped | Notes |
|---|---|---|---|---|
| `Hrot.Orchestrator.Tests` | 89 | 0 | 0 | Includes `OrchestratorSubsystem_InitializeHeadless_DoesNotThrow` |
| `Hrot.IG.Tests` | 404 | 0 | 0 | Includes `IgSubsystem_InitializeHeadless_DoesNotThrow` |
| `Hrot.ExCon.Tests` | 311 | 0 | 0 | Includes `ExConSubsystem_InitializeHeadless_DoesNotThrow` |
| `Hrot.SimHost.Tests` | 433 | 0 | 2 | SimHostSubsystem + CgfSubsystem headless tests skipped (see below) |
| `Hrot.ClusterRunner.Tests` | 208 | 0 | 0 | Includes updated `ExConSubsystemClusterTests` |
| All other assemblies | (passing) | 0 | — | No regressions |

### Skipped Tests (Phase 11)

Two headless tests were added but marked `Skip` because `Initialize()` synchronously blocks on the `DdsIdAllocator` which waits up to 30 seconds for a live `Hrot.Orchestrator` process:

- `Hrot.SimHost.Tests.SubsystemHeadlessTests.SimHostSubsystem_InitializeHeadless_DoesNotThrow`
- `Hrot.SimHost.Tests.CgfSubsystemHeadlessTests.CgfSubsystem_InitializeHeadless_DoesNotThrow`

These tests exist in the codebase and can be run manually against a live cluster. The graphics-only headless concern (no `DllNotFoundException` for Raylib) is covered by the existing `IgApplicationTests` suite which uses similar patterns.

---

## Deferred Items

| Item | Reason | Proposed Batch |
|---|---|---|
| SimHostSubsystem / CgfSubsystem headless test automation | `DdsIdAllocator` requires live Orchestrator; not suitable as pure unit test without mocking or a `NoDds` constructor path | BATCH-13 or separate DDS-mock infrastructure batch |
| Possible `Hrot.ClusterRunner.Services/` directory removal | Only 2 files remain; could flatten to `Services/` or rename to `Infrastructure/` | Low priority cosmetic cleanup |
