# BATCH-13 Report

**Batch:** BATCH-13
**Tasks:** TASK-P4-005, TASK-P5-001
**Date:** 2026-04-12
**Status:** COMPLETE

---

## TASK-P4-005: OfflineNetworkFactory for Hrot.Editor

**Status:** DONE

### What was done

- Created `Hrot.Editor/OfflineNetworkFactory.cs` implementing `INetworkFactory` with
  all 9 methods returning no-op null stubs:
  - `CreateReplicationModule()` → `NullReplicationModule` (implements `IReplicationModule` + `IEcsModule`)
  - `CreateCommandGateway()` → `NullCommandGateway`
  - `CreateExConEgressWriters()` → `NullExConEgressWriters`
  - `CreateTimeControlGateway()` → `NullTimeControlGateway`
  - `CreateSimHostMissionSender()` → `NullSimHostMissionSender`
  - `CreateSimHostAuxiliaryTranslators()` → `NullSimHostAuxiliaryTranslators`
  - `CreateSimHostPathfindingTranslators()` → `NullSimHostPathfindingTranslators`
  - `CreateSimHostPerceptionTranslators()` → `NullSimHostPerceptionTranslators`
  - `CreateIgTranslators()` → `NullIgTranslators` (the public class already in `Hrot.Core.Network`)

- Added `using Hrot.Core.Network;` to `EditorSubsystem.cs`.
- Added `private readonly INetworkFactory _networkFactory = new OfflineNetworkFactory();`
  field to `EditorSubsystem` (between the class doc comment and the `_world` field group).
- `Hrot.Core` is already a transitive reference via `Hrot.SimHost` — no csproj changes needed.

### Verification

```
dotnet list Hrot.Editor/Hrot.Editor.csproj reference
```

Output confirms: no `Hrot.Network.NED` or `Hrot.Network.BDC` direct references.

---

## TASK-P5-001: Delete RunMode Enum

**Status:** DONE

### What was done

1. **Deleted** `Hrot.ClusterRunner/Configuration/RunMode.cs`.

2. **Rewrote** `Hrot.ClusterRunner/Configuration/HrotRunnerConfiguration.cs`:
   - Removed `ParsedMode: RunMode` property and `ParseModeString()` helper.
   - Added `RequestedSubsystems: HashSet<string>(StringComparer.OrdinalIgnoreCase)`.
   - `Validate()` now expands "all"/"demo" to comma-separated names, splits, normalizes
     "ios"→"excon", validates each token, and populates `RequestedSubsystems`.
   - Editor + distributed-flags validation updated to use `Contains`.
   - `--wait-for` validation uses `isAll`/`isOrchestratorOnly` bool helpers.

3. **Updated** `Hrot.ClusterRunner/Program.cs`:
   - All `config.ParsedMode.HasFlag(RunMode.X)` → `config.RequestedSubsystems.Contains("x")`.
   - Log line updated: `mode={string.Join(",", config.RequestedSubsystems)}`.
   - CI check: `if (config.RequestedSubsystems.Contains("ci"))`.

4. **Updated test files** (all `RunMode`/`ParsedMode` references replaced):
   - `Hrot.ClusterRunner.Tests/RunnerConfigurationTests.cs` — all 28 tests rewritten.
   - `Hrot.ClusterRunner.Tests/RunnerIntegrationTests.cs` — comment + assertion updated.
   - `Hrot.ClusterRunner.Tests/Configuration/RunModeTests.cs` — replaced with
     `RequestedSubsystems`-based assertions; 3 static enum tests replaced with
     equivalent set-based tests.

5. **Updated integration test harness** and callers:
   - `HrotRunnerHarness.cs` — `HrotRunnerHarness(RunMode, int)` constructor replaced
     with `HrotRunnerHarness(string modes, int)` (comma-separated subsystem names).
   - `AllSubsystemsSpawnMovingVehicleTests.cs`, `DistributedBrainMuscleIntegrationTests.cs`,
     `CgfSubsystemHeadlessTests.cs`, `AclBackdoorEliminationTests.cs`,
     `HarnessSmokeTests.cs`, `UrbanCombatFileLifecycleTests.cs`,
     `SplitAuthoritySpawnTests.cs`, `NetworkGatewayIntegrationTests.cs` — updated
     to use string modes like `"simhost,cgf"`.

---

## Build Result

```
dotnet build IOS-IG-SimHost.sln -v quiet
0 Error(s)
2 Warning(s)  (pre-existing duplicate using in EditorSubsystemBootTests.cs)
```

---

## Test Results

```
Hrot.ClusterRunner.Tests       — 208 passed, 0 failed
Hrot.Editor.Tests              —  53 passed, 0 failed
Hrot.SimHost.Tests             — 433 passed, 2 skipped, 0 failed
Hrot.IG.Tests                  — 404 passed, 0 failed
Hrot.ExCon.Tests               —  40+ passed, 0 failed
Hrot.Orchestrator.Tests        —  89 passed, 0 failed
Hrot.Core.Tests                —  86 passed, 0 failed
Hrot.Network.NED.Tests         —  53 passed, 0 failed
Hrot.Network.BDC.Tests         —   8 passed, 0 failed
(all other unit test assemblies — 0 failed)

Pre-existing failure (unrelated):
  Fdp.Examples.CarKinem.Tests.BugVerificationTests.Test_Issue1_Stepping_Moves_Simulation
  (net9.0 target, unchanged by this batch)
```

---

## Deferred Items

None. Both tasks are fully complete.

---

## Commit

```
8efab12 BATCH-13: OfflineNetworkFactory + CLI RunMode refactoring (TASK-P4-005, TASK-P5-001)
```
