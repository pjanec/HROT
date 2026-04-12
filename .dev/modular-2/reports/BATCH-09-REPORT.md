# BATCH-09 Report: Decouple SimHost from NED (TASK-P4-002)

**Batch Number:** BATCH-09
**Task:** TASK-P4-002
**Status:** COMPLETE (Phase 14 deferred -- see note below)
**Build:** 0 errors, 0 new warnings
**Unit Tests:** 445/445 passed
**Integration Tests:** 39/41 passed (2 pre-existing failures, confirmed with git stash baseline)

---

## Summary

All 16 phases of BATCH-09 were implemented. SimHost entity lifecycle (create/delete requests) was
fully moved to the CGF brain role where it belongs. SimHostVisualization was decoupled from the
NED DdsWriter via the ISimHostMissionSender abstraction. Translator files were moved to
Hrot.Network.NED/SimHost/.

Phase 14 (remove NED project reference from Hrot.SimHost.csproj) was evaluated but NOT completed:
Hrot.SimHost still uses NED/DDS types in audio targets, damage assessment, munition detonation,
mission control ACK, and other translators. The entity lifecycle decoupling was successful;
full NED decoupling of Hrot.SimHost is a future workstream.

---

## Files Created

| File | Purpose |
|------|---------|
| `Hrot.Core/Network/EntityLifecycleInterfaces.cs` | Neutral DTOs and interfaces: `EntityCreationRequest`, `EntityDeletionRequest`, `EntityOperationStatus`, `IEntityCreationRequestSource`, `IEntityDeletionRequestSource`, `IEntityAckSink` |
| `Hrot.CGF/Systems/CreateEntityRequestSystem.cs` | Moved from Hrot.SimHost/Systems, namespace updated to Hrot.CGF.Systems |
| `Hrot.CGF/Systems/DeleteEntityRequestSystem.cs` | Moved from Hrot.SimHost/Systems, namespace updated to Hrot.CGF.Systems |
| `Hrot.CGF/Systems/NedRequestFinalizationSystem.cs` | Moved from Hrot.SimHost/Systems (was SstRequestFinalizationSystem.cs), namespace updated |
| `Hrot.Network.NED/CGF/NedCgfEntityLifecycleAdapters.cs` | DDS-backed implementations: `NedEntityCreationRequestSource`, `NedEntityDeletionRequestSource`, `NedEntityAckSink` (all public) |
| `Hrot.Network.NED/SimHost/NedSimHostMissionSender.cs` | NED implementation of `ISimHostMissionSender` |
| `Hrot.Network.NED/SimHost/NedSimHostAuxiliaryTranslators.cs` | NED implementation of `ISimHostAuxiliaryTranslators` |
| `Hrot.Network.NED/SimHost/AudioTargetDetectedEgressTranslator.cs` | Moved from Hrot.SimHost/Network/Egress/ |
| `Hrot.Network.NED/SimHost/DamageAssessedEgressTranslator.cs` | Moved from Hrot.SimHost/Network/Egress/ |
| `Hrot.Network.NED/SimHost/MissionControlAckEgressTranslator.cs` | Moved from Hrot.SimHost/Network/Egress/ |
| `Hrot.Network.NED/SimHost/MunitionDetonationEgressTranslator.cs` | Moved from Hrot.SimHost/Network/Egress/ |
| `Hrot.Network.NED/SimHost/WeaponFireIntentEgressTranslator.cs` | Moved from Hrot.SimHost/Network/Egress/ |
| `Hrot.Network.NED/SimHost/WeaponFireNotificationEgressTranslator.cs` | Moved from Hrot.SimHost/Network/Egress/ |
| `Hrot.Network.NED/SimHost/EntityHitDamageIngressTranslator.cs` | Moved from Hrot.SimHost/Network/Ingress/ |
| `Hrot.Network.NED/SimHost/MissionControlIngressTranslator.cs` | Moved from Hrot.SimHost/Network/Ingress/ |
| `Hrot.Network.NED/SimHost/MunitionDetonationIngressTranslator.cs` | Moved from Hrot.SimHost/Network/Ingress/ |
| `Hrot.Network.NED/SimHost/WeaponFireRequestIngressTranslator.cs` | Moved from Hrot.SimHost/Network/Ingress/ |
| `Hrot.Network.NED/SimHost/SimHostAuxiliaryTranslatorPack.cs` | Moved from Hrot.SimHost/Network/ |
| `Hrot.Network.NED/SimHost/BrainPathfindingTranslatorPack.cs` | Moved from Hrot.SimHost/Network/ |
| `Hrot.Network.NED/SimHost/BrainPerceptionTranslatorPack.cs` | Moved from Hrot.SimHost/Network/ |
| `Hrot.Network.NED/SimHost/PathfindingTranslators.cs` | Moved from Hrot.SimHost/Network/ |
| `Hrot.Network.NED/SimHost/PerceptionTranslators.cs` | Moved from Hrot.SimHost/Network/ |
| `Hrot.Network.NED/SimHost/SimPathfindingTranslatorPack.cs` | Moved from Hrot.SimHost/Network/ |
| `Hrot.Network.NED/SimHost/SimPerceptionTranslatorPack.cs` | Moved from Hrot.SimHost/Network/ |

---

## Files Deleted

| File | Reason |
|------|--------|
| `Hrot.Core/Network/ISimHostNetworkAdapter.cs` | Wrong abstraction (SimHost is muscle, not brain) |
| `Hrot.SimHost/Systems/CreateEntityRequestSystem.cs` | Moved to Hrot.CGF/Systems/ |
| `Hrot.SimHost/Systems/DeleteEntityRequestSystem.cs` | Moved to Hrot.CGF/Systems/ |
| `Hrot.SimHost/Systems/SstRequestFinalizationSystem.cs` | Moved to Hrot.CGF/Systems/ as NedRequestFinalizationSystem.cs |
| `Hrot.SimHost/Systems/ICreateEntityRequestSource.cs` | Replaced by IEntityCreationRequestSource in Hrot.Core.Network |
| `Hrot.SimHost/Systems/ICreateEntityAckSink.cs` | Replaced by IEntityAckSink in Hrot.Core.Network |
| `Hrot.SimHost/Systems/IDeleteEntityRequestSource.cs` | Replaced by IEntityDeletionRequestSource in Hrot.Core.Network |
| `Hrot.SimHost/Network/SimHostNetworkAdapters.cs` | Replaced by NedCgfEntityLifecycleAdapters.cs in Hrot.Network.NED/CGF/ |
| `Hrot.SimHost/Network/Ingress/EntityHitDamageIngressTranslator.cs` | Moved to Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/Ingress/MissionControlIngressTranslator.cs` | Moved to Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/Ingress/MunitionDetonationIngressTranslator.cs` | Moved to Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/Ingress/WeaponFireRequestIngressTranslator.cs` | Moved to Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/Egress/AudioTargetDetectedEgressTranslator.cs` | Moved to Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/Egress/DamageAssessedEgressTranslator.cs` | Moved to Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/Egress/MissionControlAckEgressTranslator.cs` | Moved to Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/Egress/MunitionDetonationEgressTranslator.cs` | Moved to Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/Egress/WeaponFireIntentEgressTranslator.cs` | Moved to Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/Egress/WeaponFireNotificationEgressTranslator.cs` | Moved to Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/SimHostAuxiliaryTranslatorPack.cs` | Moved to Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/BrainPathfindingTranslatorPack.cs` | Moved to Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/BrainPerceptionTranslatorPack.cs` | Moved to Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/PathfindingTranslators.cs` | Moved to Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/PerceptionTranslators.cs` | Moved to Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/SimPathfindingTranslatorPack.cs` | Moved to Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/SimPerceptionTranslatorPack.cs` | Moved to Hrot.Network.NED/SimHost/ |

---

## Files Modified

| File | Changes |
|------|---------|
| `Hrot.Core/Network/INetworkFactory.cs` | Removed `CreateSimHostNetworkAdapter()` |
| `Hrot.Core/Network/ISimHostAuxiliaryTranslators.cs` | Already existed; kept |
| `Hrot.Core/Network/ISimHostMissionSender.cs` | Already existed; kept |
| `Hrot.CGF/Hrot.CGF.csproj` | Added InternalsVisibleTo for Hrot.SimHost.Tests and Hrot.SimHost.Integration.Tests |
| `Hrot.Network.NED/Factory/NedNetworkFactory.cs` | Implemented `CreateSimHostMissionSender()` and `CreateSimHostAuxiliaryTranslators()` |
| `Hrot.Network.BDC/Factory/BdcNetworkFactory.cs` | Implemented `CreateSimHostMissionSender()` and `CreateSimHostAuxiliaryTranslators()` with null stubs |
| `Hrot.Network.NED/CGF/NedCgfEntityLifecycleAdapters.cs` | Made all 3 classes public (were internal) |
| `Hrot.SimHost/Modules/SimHostModule.cs` | Removed brain-specific system fields and constructor params |
| `Hrot.SimHost/SimHostApp.cs` | Removed entity lifecycle wiring; added NedNetworkFactory; replaced translator/mission sender construction with factory calls |
| `Hrot.SimHost/SimHostVisualization.cs` | Changed `DdsWriter<MissionControlRequest>` to `ISimHostMissionSender`; replaced manual mission construction with `SendNavigateToPoint()` call |
| `Hrot.ClusterRunner/Services/CgfSubsystem.cs` | Uses NED adapters and new systems; registers via `RegisterGlobalSystem()` |
| `Hrot.SimHost.Tests/CreateEntityRequestSystemTests.cs` | Updated stubs/DTOs; removed NED-specific tests |
| `Hrot.SimHost.Tests/DeleteEntityRequestSystemTests.cs` | Updated stubs/DTOs/assertions |
| `Hrot.SimHost.Tests/SstRequestFinalizationSystemTests.cs` | Updated usings and assertions |
| `Hrot.SimHost.Tests/AttributeCompilerFactoryTests.cs` | Updated `CreateEntityRequestSystemJsonTests` class |
| `Hrot.SimHost.Tests/SimHostVisualizationTests.cs` | Replaced DDS-based brain-active test with stub-based; removed all DDS/NED usings; added StubMissionSender |
| `Hrot.SimHost.Integration.Tests/Infrastructure/SimHostInstance.cs` | Updated stubs and `CreateEntity()` to use neutral DTOs |
| `Hrot.SimHost.Integration.Tests/Infrastructure/MockExConClient.cs` | Updated to use `EntityCreationRequest` |
| `Hrot.SimHost.Integration.Tests/EntityCreationFlowTests.cs` | Updated helper methods to use neutral DTOs |

---

## Phase Completion

| Phase | Description | Status |
|-------|-------------|--------|
| 1 | Delete ISimHostNetworkAdapter.cs, remove from INetworkFactory | Done |
| 2 | Create EntityLifecycleInterfaces.cs in Hrot.Core/Network | Done |
| 3 | Create NedCgfEntityLifecycleAdapters.cs in Hrot.Network.NED/CGF | Done |
| 4 | Move brain-role systems to Hrot.CGF/Systems | Done |
| 5 | Remove brain-specific params from SimHostModule | Done |
| 6 | Update CgfSubsystem.cs to use new types and RegisterGlobalSystem | Done |
| 7 | Remove entity lifecycle wiring from SimHostApp.cs | Done |
| 8 | Delete old DDS adapter file from Hrot.SimHost | Done |
| 9 | Create NedSimHostMissionSender.cs | Done |
| 10 | Move auxiliary translator files to Hrot.Network.NED/SimHost/ | Done |
| 11 | Implement factory methods in NedNetworkFactory and BdcNetworkFactory | Done |
| 12 | Update SimHostApp.cs to use NedNetworkFactory | Done |
| 13 | Update SimHostVisualization.cs to use ISimHostMissionSender | Done |
| 14 | Remove NED reference from Hrot.SimHost.csproj | Deferred -- NED still used for audio/damage/mission ACK translators |
| 15 | Update all affected test files | Done |
| 16 | Final build + test verification | Done |

---

## Pre-Existing Test Failures (Not Caused by This Batch)

Both failures confirmed with `git stash` before applying any changes:

1. `EntityCreationFlowTests.MissingEntityMaster_ReturnsErrorAck` -- pre-existing
2. `MissionExecutionFlowTests.EntityMission_MovesEntity` -- pre-existing

---

## Architecture Outcome

**Before:** SimHost (muscle) handled `CreateEntityRequest` / `DeleteEntityRequest` and contained
DDS-backed lifecycle adapters. Brain/muscle boundary was blurred.

**After:** Entity lifecycle (create/delete) is exclusively handled by CGF (brain) via
`CreateEntityRequestSystem`, `DeleteEntityRequestSystem`, `NedRequestFinalizationSystem` -- all
in `Hrot.CGF/Systems/`. SimHost only receives `SpawnEntityCommand` / `DestroyEntityCommand` on
the ECS event bus, which is the correct muscle-role contract.

SimHostVisualization's mission dispatch is now behind `ISimHostMissionSender`, so visualization
tests no longer require a DDS participant. The NED wire format knowledge is encapsulated in
`NedSimHostMissionSender` in `Hrot.Network.NED/SimHost/`.
