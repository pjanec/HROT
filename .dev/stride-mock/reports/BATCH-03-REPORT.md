# BATCH-03 Report

**Workstream:** stride-mock  
**Batch:** BATCH-03  
**Status:** COMPLETE  
**Date:** 2025-07-25

---

## Summary

Implemented SM-006 (`StrideMockSubsystem`) and SM-007 (ClusterRunner wiring) completely.
All required tests pass; no regressions introduced.

---

## Tasks Completed

### SM-006: StrideMockSubsystem Implementation

**Files modified/created:**

| File | Action | Description |
|------|--------|-------------|
| `Hrot/Subsystems/Hrot.StrideMock/StrideMockSubsystem.cs` | Created | Thin ISubsystem + IMapCameraProvider adapter |
| `Hrot/Subsystems/Hrot.StrideMock/Hrot.StrideMock.csproj` | Modified | Added project references |
| `Hrot/Subsystems/Hrot.StrideMock/Hrot.StrideMock.Tests/StrideMockSubsystemTests.cs` | Created | 11 tests covering SC_SM006_1 through SC_SM006_9 |

**Key design decisions:**

- `StrideMockSubsystem` is a pure thin adapter: no business logic, only lifecycle delegation to `StrideNodeBootstrapper` and `SyncFdpToStrideScript`
- `TitleBarColor = new Vector4(0.8f, 0.4f, 0.1f, 1f)` — orange, distinct from all existing subsystem colors
- Headless guards on `Update` (skip `HandleInput`), `DrawWorld` (`if (_headless || _core == null || _script == null) return`), and `DrawUI` — matching `SimHostSubsystem` pattern
- `SkipAllocatorRouting = config.Headless` prevents test hangs when `OfflineNetworkFactory` is used
- TKB population: `DemoTkbSetup.RegisterAll` was intentionally **omitted** — the Hrot bootstrapper's `HrotEnvironment.CreateTkb()` already registers TkbType 100 (`Tank_M1Abrams`) via `NedTkbCatalog.RegisterAll`; calling `DemoTkbSetup.RegisterAll` again would throw `InvalidOperationException: Template with TkbType '100' already exists`. Only `UrbanCombatNewScenario.RegisterUrbanCombatTkbTemplates` (IDs 1001-2003, non-overlapping) is called.
- `Fdp.Examples.Common` project reference removed (no longer needed after removing `DemoTkbSetup` call)
- `Raylib-cs` and `rlImGui-cs` packages added to `Hrot.StrideMock.csproj` for `DrawWorld`/`DrawUI`

### SM-007: ClusterRunner Wiring

**Files modified:**

| File | Action | Description |
|------|--------|-------------|
| `Hrot/Runner/Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs` | Modified | Added `"stridemock"` to `validNames` HashSet |
| `Hrot/Runner/Hrot.ClusterRunner/Program.cs` | Modified | Added `"STRIDEMOCK" => 700` to `ResolveAppNodeId` switch |
| `Hrot/Runner/Hrot.ClusterRunner.Tests/Configuration/RunModeTests.cs` | Modified | Added 5 SM-007 tests |

**Important:** `"stridemock"` was NOT added to the `"all"`/`"demo"` expansion — StrideMock is a standalone node, not part of the default cluster configuration.

The `Hrot.ClusterRunner.csproj` already contained the `Hrot.StrideMock` project reference (added in BATCH-01), so no change was needed there.

---

## Test Results

### SM-006 Tests (StrideMockSubsystemTests)

| Test ID | Test Name | Result |
|---------|-----------|--------|
| SC_SM006_1 | `Name_ReturnsStrideMock` | PASS |
| SC_SM006_2 | `TitleBarColor_IsOrange` | PASS |
| SC_SM006_3 | `Constructor_NullFactory_ThrowsArgumentNullException` | PASS |
| SC_SM006_3 (Init) | `Initialize_HeadlessConfig_DoesNotThrow` | PASS |
| SC_SM006_4 | `GetCameraView_AfterInitialize_ReturnsNonNull` | PASS |
| SC_SM006_5 | `ApplyCameraView_SetsTargetAndZoom` | PASS |
| SC_SM006_6 | `Update_HeadlessAfterInitialize_DoesNotThrow` | PASS |
| SC_SM006_7 | `DrawWorld_HeadlessAfterInitialize_DoesNotThrow` | PASS |
| SC_SM006_8 | `DrawUI_HeadlessAfterInitialize_DoesNotThrow` | PASS |
| SC_SM006_9 | `Shutdown_AfterInitialize_DoesNotThrow` | PASS |
| — | `Shutdown_BeforeInitialize_DoesNotThrow` | PASS |

**Total:** 11/11 PASS  
**Full suite:** `Hrot.StrideMock.Tests` — 41/41 PASS

### SM-007 Tests (RunModeTests additions)

| Test ID | Test Name | Result |
|---------|-----------|--------|
| SC_SM007_1 | `Validate_StrideMockMode_DoesNotThrow` | PASS |
| SC_SM007_2 | `Validate_OrchestratorCgfStrideMock_DoesNotThrow` | PASS |
| SC_SM007_3 | `Validate_ExistingModes_StillParseWithoutError` | PASS |
| SC_SM007_4/5 | `StrideMockSubsystem_ImplementsISubsystem` | PASS |
| SC_SM007_5 (ext) | `StrideMockSubsystem_ImplementsIMapCameraProvider` | PASS |
| — | `Validate_AllMode_DoesNotContainStrideMock` | PASS |

**Total new SM-007 tests:** 6/6 PASS  
**Full `RunModeTests` suite:** 15/15 PASS  
**Full `Hrot.ClusterRunner.Tests`:** 237/239 PASS (2 pre-existing failures in `DataDrivenGizmoPredicateTests` — `InvalidCastException` in `DataDrivenGizmoSystem.Execute`, unrelated to this batch)

---

## Build Verification

| Project | Result |
|---------|--------|
| `Hrot.StrideMock` | SUCCESS |
| `Hrot.StrideMock.Tests` | SUCCESS |
| `Hrot.ClusterRunner` | SUCCESS |
| `Hrot.ClusterRunner.Tests` | SUCCESS |

---

## Pre-existing Failures (not introduced by this batch)

- `DataDrivenGizmoPredicateTests.D003_Predicate_True_AllowsUpdateAndDraw` — `InvalidCastException: Unable to cast object of type 'D003NoOpDrawBuilder' to type 'DebugPrimitiveBuffer'`
- `DataDrivenGizmoPredicateTests` (second test, same class) — same root cause

These failures are in `DataDrivenGizmoSystem.cs:line 309` and are unrelated to any file changed in this batch.

---

## Deviations from BATCH-03-INSTRUCTIONS

| Deviation | Reason |
|-----------|--------|
| `DemoTkbSetup.RegisterAll(tkb)` removed | `HrotEnvironment.CreateTkb()` already registers TkbType 100 (`Tank_M1Abrams`) via `NedTkbCatalog.RegisterAll`; double-registration would throw `InvalidOperationException` |
| `Fdp.Examples.Common` project reference removed | No longer needed after removing `DemoTkbSetup.RegisterAll` call |
| `Raylib-cs` and `rlImGui-cs` packages added to csproj | Required for `DrawWorld`/`DrawUI` rendering calls that reference `Raylib_cs.Raylib` and `ImGuiNET.ImGui` |
