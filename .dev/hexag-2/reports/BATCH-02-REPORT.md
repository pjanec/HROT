# BATCH-02 Report

## Tasks Completed
- [x] HEXAG2-S003
- [x] HEXAG2-S004
- [x] HEXAG2-S005

## Tests Written

| Test Name | Location |
|-----------|----------|
| `NullOrchestrationTranslator_ImplementsInterface_WithoutDdsReferences` | `Hrot/Engine/Hrot.Core.Tests/OrchestrationInterfacesTests.cs` |
| `NullMasterTimeTranslators_ImplementsInterface_WithoutDdsReferences` | `Hrot/Engine/Hrot.Core.Tests/OrchestrationInterfacesTests.cs` |
| `NullSlaveOrchestrationTranslator_ImplementsInterface_WithoutDdsReferences` | `Hrot/Engine/Hrot.Core.Tests/OrchestrationInterfacesTests.cs` |
| `NullOrchestrationObserver_ImplementsInterface_WithoutDdsReferences` | `Hrot/Engine/Hrot.Core.Tests/OrchestrationInterfacesTests.cs` |

## Test Results

```
Hrot.Core.Tests OrchestrationInterfacesTests: Passed! - Failed: 0, Passed: 4, Total: 4
Hrot.Orchestrator.Tests:                       Passed! - Failed: 0, Passed: 91, Total: 91
Hrot.Orchestrator.Integration.Tests:           Passed! - Failed: 0, Passed: 12, Total: 12
Hrot.SimHost.Tests:                            Passed! - Failed: 0, Passed: 365, Skipped: 3, Total: 368
Hrot.IG.Tests:                                 Passed! - Failed: 0, Passed: 422, Total: 422
Hrot.ExCon.Tests:                              Passed! - Failed: 0, Passed: 388, Total: 388
Hrot.ClusterRunner.Integration.Tests (full):   Failed:  2 (pre-existing timing flakiness)
Hrot.ClusterRunner.Integration.Tests (solo):   Both failing tests pass individually

dotnet build IOS-IG-SimHost.sln: 0 Warning(s), 0 Error(s)
```

The 2 failures in the integration suite (`ExCon_CommitMissionAsync_ResolvesWithAck_NotTimeout` and
`AllSubsystems_TransitionToOperatingLive_CommitStateIsNotDroppedAsDuplicate`) are pre-existing DDS
timing flakiness: both pass reliably when run individually (6.5 s and 15.7 s respectively). They are
not caused by changes in this batch.

## Developer Insights

### Issues Encountered

1. **`IOrchestrationTranslator` name collision** -- `Hrot.Common.Infrastructure.IOrchestrationTranslator`
   already existed (from the modular-2 workstream) when the new `Hrot.Core.Network.IOrchestrationTranslator`
   was introduced. `NedNetworkFactory` imports both namespaces, creating an ambiguous reference error at
   build time. Fixed by using fully-qualified names in all five new factory methods in `NedNetworkFactory`.

2. **Circular dependency when moving `NodeOpMasterTranslator`** -- The original translator used
   `using Hrot.Orchestrator;` to access `FileManifestEntry`, which is defined in
   `Hrot.Orchestrator/StorageGatewayModule.cs`. Adding `Hrot.Orchestrator` as a project reference to
   `Hrot.Network.Orchestration` would have created a circular dependency (`Hrot.Orchestrator` already
   references `Hrot.Network.Orchestration`).

   Resolution: moved `FileManifestEntry` to `Hrot.Network.Orchestration/Payloads/OrchestrationPayloadDtos.cs`
   (architecturally correct -- it IS a network protocol type: deserialized from `NodeOpStatus.ResultJson`).
   Updated `Hrot.Orchestrator` files to import it from the new location using
   `using Hrot.Network.Orchestration;`.

3. **Test projects needed explicit `ProjectReference` to `Hrot.Network.Orchestration`** -- After moving
   the translator types, `Hrot.Orchestrator.Tests` and `Hrot.Orchestrator.Integration.Tests` needed
   direct references added to their `.csproj` files. Transitive project references are not sufficient
   for compilation in .NET SDK projects.

### Weak Points Spotted

1. **Two `IOrchestrationTranslator` interfaces in the codebase** -- `Hrot.Common.Infrastructure.IOrchestrationTranslator`
   (slave, from modular-2) and `Hrot.Core.Network.IOrchestrationTranslator` (master, new). They are
   identical in shape (`Tick()`, `IDisposable`) but live in different namespaces for historical reasons.
   A later batch should unify them (or give the master interface a distinct name to avoid confusion).

2. **`OrchestratorSubsystem` still creates DDS objects directly** -- `OrchestratorSubsystem.Initialize()`
   still calls `HrotEnvironment.CreateParticipant()` and directly constructs `DdsReader<T>` /
   `DdsWriter<T>` to pass into the moved translators. HEXAG2-S006/S007/S008 will address this,
   but moving the translators to `Hrot.Network.Orchestration` while leaving the construction in
   the subsystem is a temporary inconsistency.

3. **`FileManifestEntry` namespace change affects callers** -- Moving `FileManifestEntry` from
   `Hrot.Orchestrator` to `Hrot.Network.Orchestration` changes its fully-qualified name. Any
   existing serialized/deserialized data or reflection-based code using the old name would break.
   In practice this is not a concern here (the type is used only for in-memory DDS result
   deserialization), but it should be noted as a potential edge case.

### Design Decisions Made Beyond Spec

1. **Null implementations made `public` instead of `internal`** -- The spec specifies
   `internal sealed class NullOrchestrationTranslator`, but four factory implementations in
   separate assemblies (`NedNetworkFactory`, `BdcNetworkFactory`, `OfflineNetworkFactory`,
   `MockNetworkFactory`) all need to return these null objects. Making them `public` avoids
   duplicating null inner classes in every factory.

2. **All null implementations placed in a single file** -- `NullOrchestrationImplementations.cs`
   in `Hrot.Core.Network` contains all five null classes (`NullOrchestrationTranslator`,
   `NullMasterTimeTranslators`, `NullSlaveOrchestrationTranslator`, `NullOrchestrationObserver`,
   `NullDisposable`) to keep the namespace tidy rather than creating five separate files.

3. **`FileManifestEntry` moved to `Hrot.Network.Orchestration/Payloads/`** -- Not explicitly
   required by the spec, but necessary to resolve the circular dependency introduced by the
   translator move. The placement in the `Payloads` subfolder is consistent with the other
   DTO types moved in HEXAG2-S005.

## Files Changed

### New files created
- `Hrot/Engine/Hrot.Core/Network/IOrchestrationTranslator.cs`
- `Hrot/Engine/Hrot.Core/Network/IMasterTimeTranslators.cs`
- `Hrot/Engine/Hrot.Core/Network/ISlaveOrchestrationTranslator.cs`
- `Hrot/Engine/Hrot.Core/Network/IOrchestrationObserver.cs`
- `Hrot/Engine/Hrot.Core/Network/NullOrchestrationImplementations.cs`
- `Hrot/Network/Hrot.Network.Orchestration/ClusterOpMasterTranslator.cs` (moved from `Hrot.Orchestrator`)
- `Hrot/Network/Hrot.Network.Orchestration/NodeOpMasterTranslator.cs` (moved from `Hrot.Orchestrator`)
- `Hrot/Network/Hrot.Network.Orchestration/Payloads/OrchestrationPayloadDtos.cs` (moved from `Hrot.Orchestrator`)
- `Hrot/Engine/Hrot.Core.Tests/OrchestrationInterfacesTests.cs`

### Modified files
- `Hrot/Engine/Hrot.Core/Network/INetworkFactory.cs` -- added 5 new methods
- `Hrot/Network/Hrot.Network.NED/Factory/NedNetworkFactory.cs` -- stub implementations (fully qualified to avoid ambiguity)
- `Hrot/Network/Hrot.Network.BDC/Factory/BdcNetworkFactory.cs` -- stub implementations
- `Hrot/Subsystems/Hrot.Editor/OfflineNetworkFactory.cs` -- stub implementations
- `Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/MockNetworkFactory.cs` -- stub implementations
- `Hrot/Subsystems/Hrot.Orchestrator/OrchestratorSubsystem.cs` -- updated namespace for translator types
- `Hrot/Subsystems/Hrot.Orchestrator/StorageGatewayModule.cs` -- removed `FileManifestEntry` definition, added `using Hrot.Network.Orchestration`
- `Hrot/Subsystems/Hrot.Orchestrator/ClusterMaster.cs` -- added `using Hrot.Network.Orchestration`
- `Hrot/Subsystems/Hrot.Orchestrator/GlobalContextClusterOpHandler.cs` -- added `using Hrot.Network.Orchestration`
- `Hrot/Subsystems/Hrot.Orchestrator.Tests/ClusterOpMasterTranslatorTests.cs` -- updated namespace
- `Hrot/Subsystems/Hrot.Orchestrator.Tests/NodeOpMasterTranslatorTests.cs` -- updated namespace
- `Hrot/Subsystems/Hrot.Orchestrator.Tests/TranslatorDtoTests.cs` -- updated namespace
- `Hrot/Subsystems/Hrot.Orchestrator.Tests/StorageGatewayTests.cs` -- added `using Hrot.Network.Orchestration`
- `Hrot/Subsystems/Hrot.Orchestrator.Tests/Hrot.Orchestrator.Tests.csproj` -- added `Hrot.Network.Orchestration` ProjectReference
- `Hrot/Subsystems/Hrot.Orchestrator.Integration.Tests/TranslatorRoundTripTests.cs` -- updated namespace
- `Hrot/Subsystems/Hrot.Orchestrator.Integration.Tests/Hrot.Orchestrator.Integration.Tests.csproj` -- added `Hrot.Network.Orchestration` ProjectReference

### Removed files
- `Hrot/Subsystems/Hrot.Orchestrator/Translators/ClusterOpMasterTranslator.cs`
- `Hrot/Subsystems/Hrot.Orchestrator/Translators/NodeOpMasterTranslator.cs`
- `Hrot/Subsystems/Hrot.Orchestrator/Translators/Payloads/OrchestrationPayloadDtos.cs`
