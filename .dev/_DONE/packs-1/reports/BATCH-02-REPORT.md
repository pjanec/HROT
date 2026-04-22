# BATCH-02 Report

## Status
COMPLETE

## Tasks Completed

- **PACK-I001:** `PersonalRouteAuthoringSystem` now writes `NavigationIntent { Mode=FollowRoute, TrajectoryId, IntentId++ }` as an ECS component instead of publishing `CmdFollowTrajectory` on the bus. Deferred-frame mechanism preserved. Three new tests added.

- **PACK-I002:** `SimHostVisualization.HandleRightClickForEntity` brain-dead non-shift path now writes `NavigationIntent { Mode=DirectPoint, FinalDestination, TargetSpeed=15f, ArrivalRadius=3.0f, IntentId++ }`. No `NavState` mutation remains. Tests updated to assert NavigationIntent is written and setDestination is NOT called.

- **PACK-I003:** Deleted `CmdNavigateToPoint`, `CmdFollowTrajectory`, `CmdNavigateViaRoad`, `CmdStop`, `CmdSetSpeed` structs from `CommandEvents.cs`. Removed all 5 processing methods from `VehicleCommandSystem`. Removed 5 API methods from `VehicleAPI.cs`. Cleaned up all callers (`SimHostScenarioManager`, `SimHostComponentRegistry`, `CarKinemApp`, `HeadlessCarKinemApp`, `ScenarioManager`). Deleted 5 legacy tests; updated `Command_IgnoresDeadEntity` to use `CmdLeaveFormation`. Created `NavigationIntentBridgeSystemTests.cs` with 3 tests.

- **PACK-P002:** Moved `DdsCreateEntityRequestSource`, `DdsCreateUpdateDeleteEntityAckSink`, `DdsDeleteEntityRequestSource` out of `SimHostModule.cs` into `Hrot.SimHost/Network/SimHostNetworkAdapters.cs` (public, same logic). `SimHostModule` constructor refactored to accept all DDS-coupled systems and translators as optional parameters; it no longer requires `DdsParticipant`. All creation of DDS adapters, `NedRequestFinalizationSystem`, `CreateEntityRequestSystem`, and `DeleteEntityRequestSystem` moved to `SimHostApp.cs`. Added 1 new test `SimHostModule_CanBeConstructed_WithoutDdsParticipant`.

- **PACK-P004:** `UpdateEntityDescriptorRequestSystem.cs` moved from `Hrot.Map.Common/Systems/` to `Hrot.Map.Common/Replication/Ingress/`. Namespace updated from `Hrot.Map.Common.Systems` to `Hrot.Map.Common.Replication.Ingress`. Removed unconditional registration from `SimHostApp._kernelGroup` core block; re-registered alongside other DDS-coupled spawning systems. Updated `using` directives in `SimHostAppTests.cs` and `UpdateEntityDescriptorRequestSystemTests.cs`.

## Test Results

```
Hrot.SimHost.Tests:           Failed: 0, Passed: 412, Skipped: 0  (1 pre-existing failure excluded: Dispose_AlsoCallsBaseDispose)
FDP.Toolkit.CarKinem.Tests:   Failed: 0, Passed: 127, Skipped: 0
FDP.Toolkit.Navigation.Tests: Failed: 0, Passed:  41, Skipped: 0
```

dotnet build IOS-IG-SimHost.sln: **Build succeeded. 0 Error(s)**

## Developer Insights

### Issues Encountered

1. **Namespace ambiguity in SimHostVisualization.cs** (PACK-I002): `Hrot.NED.Descriptors.NavigationIntent` (DDS wire struct) and `FDP.Toolkit.Navigation.NavigationIntent` (ECS component) have the same simple name. Resolved with explicit type aliases (`EcsNavigationIntent`, `EcsNavigationMode`). The same aliases were required in `SimHostVisualizationTests.cs`.

2. **Brace corruption in PersonalRouteAuthoringSystem.cs** (PACK-I001): The first `replace_string_in_file` call on the deferred-dispatch block left a misaligned brace structure. Fixed by rewriting the entire `OnUpdate` method body as a single replacement.

3. **Cascade callers of deleted Cmd types** (PACK-I003): `CommandEvents.cs` deletion triggered compile errors in `Fdp.Examples.CarKinem` (HeadlessCarKinemApp, CarKinemApp, ScenarioManager). All three used deleted types; replaced with direct `NavState` mutation (acceptable in the Examples project which is not production code).

4. **`IEcsModuleSystem` vs `ComponentSystem`** (PACK-P002): `_kernelGroup.AddSystem()` accepts only `Fdp.Kernel.ComponentSystem`, but `CreateEntityRequestSystem`, `DeleteEntityRequestSystem`, and `NedRequestFinalizationSystem` implement `IEcsModuleSystem`. Attempting to register them via `_kernelGroup.AddSystem()` caused CS1503 type-mismatch errors. Resolved by passing all three systems as optional constructor parameters to `SimHostModule` and registering them via the existing `registry.RegisterSystem()` path in `RegisterSystems()`.

### Weak Points Spotted

- **`NedRequestFinalizationSystem` naming**: The class is in `Hrot.SimHost.Systems` and the file is called `SstRequestFinalizationSystem.cs` — file name doesn't match class name. This is a latent maintenance hazard.
- **`SimHostModule` multi-param constructor**: With 9 optional parameters, the constructor is growing wide. A builder or options-object pattern would improve readability for callers that only need subsets.
- **`NetworkSpawningSystem` requires DDS**: The `INetworkIdAllocator` injected into `NetworkSpawningSystem` is always a `DdsIdAllocator` in practice. Creating a truly offline-safe spawner without DDS infrastructure is not currently possible without a mock allocator. The "offline instantiation" test still spins up a DDS participant for the spawner — only the SimHostModule itself is truly DDS-free.
- **`SimHostAppTests.RegisteredSystemTypes_ContainsNoDuplicates`**: This test manually replicates the `_kernelGroup` construction logic and will drift if the real SimHostApp changes. Long-term, it would be better to use the real `SimHostApp` with headless mode and assert system counts on the actual running kernel group.

### Design Decisions Beyond Spec

- **Systems passed via SimHostModule constructor** rather than through a separate registration method: passing them as optional constructor params fit naturally with the existing `RegisterSystems` pattern. This avoids introducing a new `RegisterNetworkSystems()` overload or a separate "network boundary module" type.
- **`UpdateEntityDescriptorRequestSystem` registered in `_kernelGroup` after the DDS block**: The spec says "conditionally in the same network-boundary module". Since `SimHostApp` has no explicit `if (withDds)` guard, the system is registered in the same code section as `requestSystem`/`deleteSystem`/`finalizationSystem`, immediately after their creation — making the DDS coupling visually obvious even without an explicit conditional.

### Unexpected Findings from Tests

- The `SimHostModule_CanBeConstructed_WithoutDdsParticipant` test confirmed that SimHostModule's constructor is now clean: passing only a `NetworkSpawningSystem` with no systems and no translators constructs the module successfully — the DDS dependency is fully eliminated from the module boundary.
- The existing `SimHostAppTests.RegisteredSystemTypes_ContainsNoDuplicates` test correctly resolved `UpdateEntityDescriptorRequestSystem` from the new `Hrot.Map.Common.Replication.Ingress` namespace after the using directive was updated — confirming PACK-P004 with no logic change.

## Files Changed

**Created:**
- `Hrot.SimHost/Network/SimHostNetworkAdapters.cs` (DDS adapter classes moved from SimHostModule)
- `Hrot.Map.Common/Replication/Ingress/UpdateEntityDescriptorRequestSystem.cs` (relocated)
- `FDP/Toolkits/FDP.Toolkit.Navigation.Tests/NavigationIntentBridgeSystemTests.cs` (PACK-I003 SC3)

**Deleted:**
- `Hrot.Map.Common/Systems/UpdateEntityDescriptorRequestSystem.cs` (relocated to above)

**Modified:**
- `Hrot.SimHost/Systems/Routing/PersonalRouteAuthoringSystem.cs` — PACK-I001
- `Hrot.SimHost.Tests/PersonalRouteAuthoringSystemTests.cs` — PACK-I001 tests
- `Hrot.SimHost/SimHostVisualization.cs` — PACK-I002
- `Hrot.SimHost.Tests/SimHostVisualizationTests.cs` — PACK-I002 tests
- `FDP/Toolkits/FDP.Toolkit.CarKinem/Commands/CommandEvents.cs` — PACK-I003 (5 structs deleted)
- `FDP/Toolkits/FDP.Toolkit.CarKinem/Systems/VehicleCommandSystem.cs` — PACK-I003 (5 methods deleted)
- `FDP/Toolkits/FDP.Toolkit.CarKinem/Commands/VehicleAPI.cs` — PACK-I003 (5 methods deleted)
- `FDP/Toolkits/FDP.Toolkit.CarKinem.Tests/Commands/VehicleCommandSystemTests.cs` — PACK-I003 (5 tests deleted, 1 updated)
- `Hrot.SimHost/SimHostComponentRegistry.cs` — PACK-I003 (5 RegisterEvent calls removed)
- `Hrot.SimHost/UI/SimHostScenarioManager.cs` — PACK-I003 (CmdFollowTrajectory → NavigationIntent)
- `FDP/Examples/Fdp.Examples.CarKinem/Headless/HeadlessCarKinemApp.cs` — PACK-I003 cascade caller
- `FDP/Examples/Fdp.Examples.CarKinem/CarKinemApp.cs` — PACK-I003 cascade caller
- `FDP/Examples/Fdp.Examples.CarKinem/Core/ScenarioManager.cs` — PACK-I003 cascade caller
- `Hrot.SimHost/Modules/SimHostModule.cs` — PACK-P002 (DDS inner classes removed, constructor refactored)
- `Hrot.SimHost/SimHostApp.cs` — PACK-P002 + PACK-P004 (DDS adapters/systems/translators moved here)
- `Hrot.SimHost.Tests/EntityMissionTranslatorTests.cs` — PACK-P002 (test updated + new offline test)
- `Hrot.SimHost.Tests/SimHostAppTests.cs` — PACK-P004 (using directive updated)
- `Hrot.SimHost.Tests/UpdateEntityDescriptorRequestSystemTests.cs` — PACK-P004 (using directive updated)
