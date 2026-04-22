# BATCH-09 Report

**Batch:** BATCH-09
**Tasks:** PACK2-C001 · PACK2-R003
**Date:** 2026-04-04
**Developer:** GitHub Copilot
**Status:** COMPLETE

---

## 1. Files Modified / Created

### Modified
| File | Change |
|------|--------|
| `Hrot.Editor/Hrot.Editor.csproj` | Added `<OutputType>Exe</OutputType>`; added ProjectReferences to `Hrot.SimHost`, `Hrot.CGF`, `Hrot.Orchestrator` |
| `Hrot.Editor.Tests/Hrot.Editor.Tests.csproj` | Added direct ProjectReferences to `Hrot.SimHost`, `Hrot.CGF`, `Hrot.Orchestrator`, `ModuleHost.Core` (required because EXE project refs don't expose transitive deps at compile time) |
| `Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj` | Added ProjectReference to `Hrot.ScenarioEditor` |
| `Hrot.ClusterRunner.Integration.Tests/HrotRunnerHarness.cs` | Added new shared-domain constructor `HrotRunnerHarness(RunMode mode, int domainId)` |

### Created
| File | Description |
|------|-------------|
| `Hrot.Editor/Program.cs` | Offline All-In-One composition root (PACK2-C001) |
| `Hrot.Editor.Tests/OfflineKernelBootTests.cs` | Smoke tests for composition root (2 tests) |
| `Hrot.ClusterRunner.Integration.Tests/CgfHarness.cs` | DDS-backed CGF harness (PACK2-R003) |
| `Hrot.ClusterRunner.Integration.Tests/EditorHarness.cs` | Offline editor kernel harness (PACK2-R003) |
| `Hrot.ClusterRunner.Integration.Tests/HarnessSmokeTests.cs` | Harness smoke tests (5 tests) |

---

## 2. Build Results

| Project | Result | Errors |
|---------|--------|--------|
| `Hrot.Editor` | **PASSED** | 0 |
| `Hrot.Editor.Tests` | **PASSED** | 0 |
| `Hrot.ClusterRunner.Integration.Tests` | **PASSED** | 0 |

---

## 3. EditorDependencyTests

`HrotEditor_HasNoTransitiveNedDependency` — **PASSED**

`Hrot.NED` does NOT appear in `GetReferencedAssemblies()` on `Hrot.Editor.dll`. `Program.cs` contains no direct usages of any `Hrot.NED.*` types.

---

## 4. Test Counts

### Hrot.Editor.Tests
| Suite | Before | After | Delta |
|-------|--------|-------|-------|
| Total | 15 | **17** | +2 |
| `OfflineKernelBootTests` | 0 | 2 | +2 |
| All other tests | 15 | 15 | 0 |

Result: **Passed: 17, Failed: 0, Skipped: 0**

### Hrot.ClusterRunner.Integration.Tests — HarnessSmokeTests (new)
| Test | Result |
|------|--------|
| `EditorHarness_Initializes_WithoutException` | PASSED |
| `EditorHarness_PumpFrames_WithoutException` | PASSED |
| `CgfHarness_TwoInstances_HaveDifferentDomainIds` | SKIPPED (Requires CycloneDDS) |
| `CgfHarness_SharedDomainCtor_UsesSuppledDomainId` | SKIPPED (Requires CycloneDDS) |
| `HrotRunnerHarness_SharedDomainCtor_UsesSuppledDomainId` | SKIPPED (Requires CycloneDDS) |

Result: **Passed: 2, Skipped: 3, Failed: 0**

---

## 5. Deviations from Instructions

| # | Deviation | Reason |
|---|-----------|--------|
| 1 | `NetworkEntityMap` namespace: used `FDP.Toolkit.Replication.Services` instead of `ModuleHost.Network.Cyclone.Services` | Both namespaces define a `NetworkEntityMap` class, but `SimHostCoreLogicPack` and `CgfLogicPack` use `FDP.Toolkit.Replication.Services.NetworkEntityMap`. Using the `ModuleHost.Network.Cyclone.Services` version would cause type mismatch compile errors. |
| 2 | `ClusterSlave` ctor: used positional args `new ClusterSlave(0, "Editor", world.Bus)` instead of named `new ClusterSlave(nodeId: 0, subsystemName: "Editor", eventBus: world.Bus)` | Two overloads exist — `ClusterSlave(int, string, FdpEventBus?)` and `ClusterSlave(FdpEventBus?, int, string)` — causing CS0121 ambiguity when named args are used. Positional call resolves unambiguously to `(int, string, FdpEventBus?)`. |
| 3 | `Hrot.Editor.Tests.csproj`: added direct refs to `Hrot.SimHost`, `Hrot.CGF`, `Hrot.Orchestrator`, `ModuleHost.Core` | After changing `Hrot.Editor` to `OutputType=Exe`, its project references no longer transitively expose compile-time types to `Hrot.Editor.Tests`. Direct references required for `OfflineKernelBootTests.cs` to resolve these types. |
| 4 | Added `using ModuleHost.Core;` to `Program.cs` and `OfflineKernelBootTests.cs` | `ModuleHostKernel` is in `ModuleHost.Core` namespace, which was not in the default imports. |
| 5 | Added `using Hrot.Editor.UI;` to `Program.cs` | `ScenarioBrowserPanel` and `EditorToolbarPanel` are in `Hrot.Editor.UI` namespace, not `Hrot.Editor`. |
| 6 | CgfHarness smoke tests marked `[Fact(Skip = "Requires CycloneDDS")]` | Per batch instructions: CgfHarness starts a real DDS participant; test environment does not have CycloneDDS available. |
