# BATCH-05 Report

**Batch:** BATCH-05
**Tasks:** CS020, CS021, CS024, CS025
**Status:** COMPLETE

---

## Tasks Completed

### CS020 — EditorOrbatAdapter Full Implementation
- Implemented `RequestAssignSubordinate` in `Hrot/Subsystems/Hrot.Editor/Adapters/EditorOrbatAdapter.cs`
  - Publishes `CmdAssignSubordinate` directly to the ECS bus
- Implemented `RequestRemoveSubordinate` in the same file
  - Publishes `CmdRemoveSubordinate` directly to the ECS bus
- Tests: 4 new tests added to `Hrot/Subsystems/Hrot.Editor.Tests/Adapters/AdapterTests.cs`
  - CS020-T01: assign publishes `CmdAssignSubordinate` with correct fields
  - CS020-T02: remove publishes `CmdRemoveSubordinate` with correct subordinate
  - CS020-T03: assign unknown renderer ID logs warning, no event published
  - CS020-T04: `CanAcceptSubordinates` is true for composite entities, false for others

### CS021 — ExConOrbatAdapter Full Implementation
- Extended `ICommandGateway` (`Hrot/Engine/Hrot.Core/Network/ICommandGateway.cs`) with
  `Task SendUpdateAttributeAsync(UpdateEntityAttributeCommand cmd, CancellationToken ct = default)`
- Implemented `NedCommandGateway.SendUpdateAttributeAsync` writing an `UpdateEntityAttributeRequest` DDS message
- Added `SendUpdateAttributeAsync` stubs to all `NullCommandGateway` implementations:
  `NedNetworkFactory`, `BdcNetworkFactory`, `ExConSubsystem`, `OfflineNetworkFactory`,
  `MockNetworkFactory`, `NullIgCommandGateway`, `Hrot.IG.Tests` stubs
- Implemented `ExConOrbatAdapter.RequestAssignSubordinate` in `Hrot/Subsystems/Hrot.ExCon/Adapters/ExConOrbatAdapter.cs`
  - Builds `UpdateEntityAttributeCommand` with `AttributePatchJson = {"CommanderId": N}` and calls `_gateway.SendUpdateAttributeAsync`
- Implemented `ExConOrbatAdapter.RequestRemoveSubordinate`
  - Builds `UpdateEntityAttributeCommand` with `AttributePatchJson = {"CommanderId": 0}` and calls `_gateway.SendUpdateAttributeAsync`
- `CanAcceptSubordinates` uses `entity.TkbType` directly on `IDerEntity` (not via descriptor) for `IsCompositeType` check
- Tests: 3 new tests in `Hrot/Subsystems/Hrot.ExCon.Tests/Adapters/ExConOrbatAdapterTests.cs`
  - CS021-T01: assign sends patch `{"CommanderId": N}` with correct entity ID
  - CS021-T02: remove sends patch `{"CommanderId": 0}`
  - CS021-T03: `CanAcceptSubordinates` returns true for composite TkbType, false otherwise

### CS024 — UpdateEntityAttributeRequestSystem CommanderId Interception
- Modified `ProcessRequest` in `Hrot/Network/Hrot.Network.NED/Systems/UpdateEntityAttributeRequestSystem.cs`
- Step 3a (`InterceptCommanderId`) runs BEFORE the null-compiler guard (step 3)
- `InterceptCommanderId` private method:
  - Parses JSON, extracts `"CommanderId"` key
  - Non-zero: publishes `CmdAssignSubordinate` if commander found in `_entityMap`
  - Zero: publishes `CmdRemoveSubordinate` if target has `UnitSubordinate`
  - Returns JSON with `"CommanderId"` key removed via `RebuildJsonWithout`
- When `commanderIntercepted` and no JSON compiler: sends `WriteAck` (not `WriteErrorAck`)
- When `HasAppliedAny == false` and `commanderIntercepted`: sends `WriteAck` with empty mask
- Tests: `Hrot/Network/Hrot.Network.NED.Tests/UpdateEntityAttributeCommanderIdTests.cs` — 5 tests, all pass
  - T01: assign subordinate published when non-zero CommanderId and commander in map
  - T02: remove subordinate published when CommanderId=0 and target has UnitSubordinate
  - T03: no event when CommanderId=0 and target has no UnitSubordinate
  - T04: no event when non-zero CommanderId but commander not in map
  - T05: no exception when CommanderId references unknown entity (graceful no-op)

### CS025 — Integration Tests: Distributed Boundary Validation
- CS025-T02: `Hrot/Subsystems/Hrot.SimHost.Tests/Integration/HierarchyCapacityIntegrationTests.cs`
  - `Assign_17Subordinates_16AcceptedOneRejected`: 17 CmdAssignSubordinate events; UnitRoster.Count==16;
    17th entity has no UnitSubordinate; CmdAssignSubordinateRejected published exactly once
- CS025-T06: `Hrot/Subsystems/Hrot.SimHost.Tests/Integration/HierarchySerializationIntegrationTests.cs`
  - `Serialize_ThenDeserialize_ReconstitutesHierarchy`: full save/reload cycle; after deserialization
    subordinate has `InitialUnitSubordinateIntent` with correct `CommanderNetworkId`; after
    `GenesisMaterializationSystem.Execute` the `UnitSubordinate` is reconstituted and intent removed;
    `UnitRoster.Count == 1` on commander

---

## Test Results

| Assembly | Passed | Failed | Skipped | Notes |
|---|---|---|---|---|
| `Hrot.Editor.Tests.dll` | 94 | 0 | 0 | |
| `Hrot.ExCon.Tests.dll` | 322 | 0 | 0 | Post-run crash is pre-existing native cleanup issue |
| `Hrot.Network.NED.Tests.dll` | 70 | 0 | 0 | |
| `Hrot.SimHost.Tests.dll` | 502 | 2 | 3 | 2 failures are pre-existing `MissionPlanTranslatorTests` |

**New tests added this batch:** 4 (CS020) + 3 (CS021) + 5 (CS024) + 2 (CS025) = 14 tests

---

## Build

`Build succeeded. 0 Error(s)` — `dotnet build IOS-IG-SimHost.sln --no-restore -v quiet`

---

## Notes

- `MissionPlanTranslatorTests` (2 failures) are pre-existing failures confirmed before batch start.
- `BdcNetworkFactory` and `NedNetworkFactory` null stubs use fully-qualified
  `Fdp.Toolkit.Replication.Events.UpdateEntityAttributeCommand` to avoid adding a using to those files.
- The `InterceptCommanderId` step was placed BEFORE the null-compiler guard so tests with no compiler still reach the intercept path.
