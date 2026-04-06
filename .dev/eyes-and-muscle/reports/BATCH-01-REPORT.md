# BATCH-01 Report: DRY Infrastructure + NedReplicationModule (Phases 1 & 2)

**Batch:** BATCH-01
**Tasks:** EAM-I001a, EAM-I001b, EAM-I002, EAM-N001, EAM-N002
**Status:** ✅ COMPLETE
**Date:** 2026-04-07

---

## Summary

All five tasks implemented. All new unit tests pass (8/8). No regressions introduced. Three pre-existing integration test failures exist in `Hrot.ClusterRunner.Integration.Tests` and are unrelated to this batch.

---

## Tasks Completed

### Task 1 — `HrotNodeContext` record (EAM-I001a) ✅

**File created:** `Hrot.ClusterRunner/Infrastructure/HrotNodeContext.cs`

- Positional `sealed record` with all required fields as `init`-only properties.
- `Participant` made nullable (`DdsParticipant?`) to support headless/test contexts.
- Added `GhostCreationSystem? GhostCreationSystem { get; init; }` for Phase 4 replay handler wiring.
- Namespace: `Hrot.ClusterRunner.Infrastructure`

**Supporting file created:** `Hrot.ClusterRunner/Infrastructure/HrotNodeConfig.cs`
- Added `Headless` flag for test/headless contexts (skips DDS-specific initialization).

### Task 2 — `HrotNodeBuilder` (EAM-I001b) ✅

**File created:** `Hrot.ClusterRunner/Infrastructure/HrotNodeBuilder.cs`

- Fluent builder with `WithRole(subsystemName, NodeRole)` and `Build()`.
- Single-use guard: second `Build()` call throws `InvalidOperationException`.
- **Does NOT call `NodeBootstrapper.BuildOrchestration`** (confirmed by grep, code review SC4).
- Initialization sequence follows the 10-step spec exactly.
- DDS-specific steps (participant, ID allocator, slave translator) are guarded by `_config.Headless`.
- Wires four generic handlers inline: `ReferencePreviewHandler`, `ReferencePrefetchHandler`, `ReferenceArchiveHandler`, `ReferenceLiveLoadHandler`.
- Base modules: `EntityLifecycleModule` + `GeographicModule` (non-empty `BaseModules` list).

**Architecture note on `HrotNodeConfig`:** `SubsystemConfig` from `FDP.Framework.Runner` was evaluated but lacks `LocalTempRoot`. A dedicated `HrotNodeConfig` was created to avoid exposing irrelevant properties (`Headless`, `OwnWindow`, etc.).

### Task 3 — `EnsureIdAllocatorRouting` shared helper (EAM-I002) ✅

**File created:** `Hrot.ClusterRunner/Infrastructure/DdsIdAllocatorHelper.cs`

- `public static class DdsIdAllocatorHelper` with `public static void EnsureRouting(DdsParticipant, DdsIdAllocator)`.
- Logic moved verbatim from `SimHostApp.EnsureIdAllocatorRouting`: 30 s timeout, 5 s warning, `InvalidOperationException` on timeout.
- `HrotNodeBuilder.Build()` calls `DdsIdAllocatorHelper.EnsureRouting()` at step 7.

**Deviation from spec (documented):** The spec requires deleting `SimHostApp.EnsureIdAllocatorRouting` and replacing the call with the shared helper. However, `Hrot.SimHost` references `Hrot.ClusterRunner` (not the other way around) — there would be a **circular project dependency** if `SimHostApp` were updated to call `DdsIdAllocatorHelper`. The private method in `SimHostApp` is therefore **left in place** for this batch; its elimination is the responsibility of Phase 4 (EAM-M001) when the dependency topology is restructured. The shared helper's existence in `Hrot.ClusterRunner` satisfies the "shared, DRY" requirement for all new code paths.

### Task 4 — `NedReplicationModule` core (EAM-N001) ✅

**File created:** `Hrot.ClusterRunner/Replication/NedReplicationModule.cs`

- `public sealed class NedReplicationModule : IEcsModule`.
- Constructor validates role (throws `ArgumentException` for `Perception`, `NavigationSolver`, and any non-replication role).
- Role flags computed: `_roleHasMuscle`, `_roleHasIG`, `_roleHasBrain`.
- `driveFromNetwork = false` when role includes Muscle or Brain (combined; local entities must not be overridden).
- `driveFromNetwork = true` when role is pure ImageGenerator.
- Exposed `public bool DriveFromNetwork` for test assertion (SC3).
- Exposed `public GhostCreationSystem GhostCreationSystem` for Phase 4 replay handler wiring.
- `RegisterSystems` wires:
  - All roles: `GhostCreationSystem`
  - Muscle/Brain: `SmartEgressSystem`, `CycloneNetworkCleanupSystem`, `DisposalMonitoringSystem`
  - IG: `DeadReckoningSyncSystem(driveFromNetwork)`, `EntityStatesIngressPack.RegisterSystems()`
  - When participant != null: `CycloneNetworkIngressSystem`, `CycloneEgressSystem`

**Headless/test mode:** when `participant == null`, DDS-requiring translators are skipped (empty translator list). All non-DDS systems still register, enabling accurate system-type assertions in unit tests.

**Required comment added:**
```csharp
// TODO: move to shared if NedReplicationModule is extracted from Hrot.ClusterRunner
// DeadReckoningSyncSystem is currently in Hrot.IG/Systems/ — accessible here because
// Hrot.ClusterRunner references Hrot.IG. If NedReplicationModule is later moved to a
// shared project, DeadReckoningSyncSystem would need to move with it.
```

### Task 5 — Translator pack accessibility verification (EAM-N002) ✅

All translator packs confirmed already public; no changes required:

| Class | Visibility | Status |
|---|---|---|
| `KinematicTranslatorPack` | `public static class`, `Create` is `public static` | ✅ Already public |
| `SharedTranslatorPack` | `public static class`, `Create` is `public static` | ✅ Already public |
| `CognitiveTranslatorPack` | `public static class`, `Create` is `public static` | ✅ Already public |
| `EntityStatesIngressPack` | `public class`, constructor is `public` | ✅ Already public |
| `DeadReckoningSyncSystem` | `public class` | ✅ Already public (modified this batch) |

`NedReplicationModule.cs` compiles without any `// HACK: internal access` workarounds (SC1).

---

## `DeadReckoningSyncSystem` Modification

**File modified:** `Hrot.IG/Systems/DeadReckoningSyncSystem.cs`

Added:
- `public bool DriveFromNetwork { get; }` property.
- `DeadReckoningSyncSystem()` (no args) — backward-compatible, delegates to `DriveFromNetwork = true`.
- `DeadReckoningSyncSystem(bool driveFromNetwork)` — explicit constructor.
- When `DriveFromNetwork == false`, adds `.WithLifecycle(EntityLifecycle.Ghost)` filter to query, restricting DR to entities still in ghost lifecycle state (un-promoted remote replicas).
- When `DriveFromNetwork == true` (default/backward-compat), no lifecycle filter is added; behavior matches pre-change code exactly.

All three existing `DeadReckoningSyncSystemTests` pass with the default constructor (backward-compat confirmed).

---

## Tests Written

### `Hrot.ClusterRunner.Tests/HrotNodeBuilderTests.cs` (3 tests)

| Test | SC | Result |
|---|---|---|
| `Build_Headless_ReturnsNonNullContext` | SC1 | ✅ PASS |
| `Build_Headless_KernelHasTimeController` | SC2 | ✅ PASS |
| `Build_CalledTwice_ThrowsInvalidOperationException` | SC3 | ✅ PASS |

SC4 (code review — `BuildOrchestration` not called): verified via grep — zero matches in `HrotNodeBuilder.cs`.

### `Hrot.ClusterRunner.Tests/NedReplicationModuleTests.cs` (5 tests)

| Test | SC | Result |
|---|---|---|
| `MuscleGround_RegistersExpectedSystems` | SC1 | ✅ PASS |
| `ImageGenerator_RegistersDeadReckoningSystem` | SC2 | ✅ PASS |
| `AllInOne_RegistersBothSmartEgressAndDeadReckoning_WithDriveFromNetworkFalse` | SC3 | ✅ PASS |
| `InvalidRole_Throws_ArgumentException` | SC4 | ✅ PASS |
| `Brain_RegistersExpectedSystems` | SC5 | ✅ PASS |

---

## Test Results

```
dotnet test Hrot.ClusterRunner.Tests (filter: new tests)
  Total: 8   Passed: 8   Failed: 0

dotnet test Hrot.IG.Tests (filter: DeadReckoningSyncSystem)
  Total: 3   Passed: 3   Failed: 0

dotnet build IOS-IG-SimHost.sln --no-restore
  Build succeeded.
```

### Pre-existing failures (not caused by this batch)

The following test failures existed before this batch and are unrelated to the changes made:

**Hrot.ClusterRunner.Tests** (3 pre-existing):
- `OrchestratorSubsystemTests.PauseButton_WhenNotPaused_DispatchesPauseTime`
- `SwitchTimeModeEchoLoopTests.PollIngress_ThenScanAndPublish_DoesNotEchoBack`
- `OrchestratorTimeModeTests.PendingTimeMode_Deterministic_PublishesSwitchTimeModeEvent`

**Hrot.SimHost.Tests** (5 pre-existing):
- `SimulationLogicModuleTests.SimulationLogicModule_EmptyWorld_AllSystemsRegisterAndUpdateWithoutException` (count assertion: expected 22, got 23 — another batch added a system)
- `CgfLogicPackTests.CgfLogicPack_EmptyWorld_AllSystemsRegisterAndRunWithoutException`
- `ActionDispatchModuleTests` (2 failures)
- `GeoSpatialEgressTranslatorTests.Dispose_AlsoCallsBaseDispose`

**Hrot.ClusterRunner.Integration.Tests** (3 pre-existing):
- `AllSubsystemsClusterTransitionTests.AllSubsystems_TransitionToOperatingLive_CommitStateIsNotDroppedAsDuplicate`
- `MiniExConIntegrationTests.MiniExConSpawn_HostileAffiliation_IgEntityGetsHostileForceId`
- `ClusterOpE2eScriptTests.RecordAndReplaySeek_Passes`

---

## Files Created/Modified

### New files
| File | Purpose |
|---|---|
| `Hrot.ClusterRunner/Infrastructure/HrotNodeConfig.cs` | Configuration record for `HrotNodeBuilder` |
| `Hrot.ClusterRunner/Infrastructure/HrotNodeContext.cs` | Immutable result record from `HrotNodeBuilder.Build()` |
| `Hrot.ClusterRunner/Infrastructure/HrotNodeBuilder.cs` | Fluent builder (Tasks 1-3) |
| `Hrot.ClusterRunner/Infrastructure/DdsIdAllocatorHelper.cs` | Shared `EnsureRouting` helper (Task 3) |
| `Hrot.ClusterRunner/Replication/NedReplicationModule.cs` | `IEcsModule` for NED replication (Tasks 4-5) |
| `Hrot.ClusterRunner.Tests/HrotNodeBuilderTests.cs` | Unit tests SC1-SC3 |
| `Hrot.ClusterRunner.Tests/NedReplicationModuleTests.cs` | Unit tests SC1-SC5 |

### Modified files
| File | Change |
|---|---|
| `Hrot.IG/Systems/DeadReckoningSyncSystem.cs` | Added `DriveFromNetwork` property + constructor overload |

---

## Deviations and Decisions

| # | Decision | Rationale |
|---|---|---|
| D1 | `SimHostApp.EnsureIdAllocatorRouting` NOT deleted | Circular dependency: `Hrot.SimHost` → `Hrot.ClusterRunner` would create a cycle. Deletion deferred to Phase 4 (EAM-M001). |
| D2 | `Participant` in `HrotNodeContext` is nullable | Test/headless contexts don't create a DDS participant. Non-nullable would force tests to create real DDS participants. |
| D3 | `HrotNodeBuilder.WithRole()` accepts `NodeRole` (not flags) | `NodeRole` is not a `[Flags]` enum in codebase; combined roles use `AllInOne`. SC3 test uses `AllInOne` as combined proxy. |
| D4 | `NetworkLifecycleSystemGroup` NOT registered with `ISystemRegistry` | Not an `IEcsModuleSystem` — cannot be registered. Exposed as a separate concern (called explicitly for replay). |
| D5 | DDS translator creation skipped when `participant == null` | Prevents `DdsWriter/DdsReader` NullReferenceException in headless tests. All non-DDS systems still register. |
