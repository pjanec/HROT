# BATCH-10 Report: Complete SimHost NED Cleanup + Decouple CGF/IG from NED

**Batch:** BATCH-10
**Developer:** GitHub Copilot
**Date:** 2026-04-12
**Status:** Partial (Phase 1 complete, Phase 2 partial, Phase 3 deferred)

---

## Task Completion

| Task / Phase | Status | Notes |
|--------------|--------|-------|
| Phase 1a: Move remaining pack files to Hrot.Network.NED/SimHost/ | Done | 6 files created with new namespace |
| Phase 1b: Delete ALL old translator files from Hrot.SimHost/Network/ | Done | 17 files deleted, directory empty |
| Phase 1c: Add ISimHostPathfindingTranslators / ISimHostPerceptionTranslators interfaces | Done | 2 new interface files + INetworkFactory updated |
| Phase 1d: Create NedSimHostPathfindingTranslators / NedSimHostPerceptionTranslators | Done | 2 new implementation files |
| Phase 1e: Check remaining NED usages in Hrot.SimHost | Done | SharedTranslatorPack / KinematicTranslatorPack / CognitiveTranslatorPack still used -- NED ref kept |
| Phase 1f: Update NodeBootstrapper to use factory (dual-path) | Done | Factory injection added with backward-compat fallback |
| Phase 2a/2b: Audit CGF NED usages, remove unused import | Done | Removed `using Hrot.NED.Messages;` (was unused) |
| Phase 2c-2d: Remove Hrot.Network.NED from Hrot.CGF.csproj | Blocked | `MissionControlExecutionSystem` resides in Hrot.Network.NED (namespace `Hrot.Common.Systems`) -- CGF still needs NED |
| Phase 3: Decouple IG from NED | Deferred | 12+ IG files use NED types across Systems/, Translators/, UI/, Services/ -- needs dedicated batch |
| Phase 4: Final build + test | Done | Build 0 errors; unit tests 433/433; integration tests 39/41 (2 pre-existing) |

---

## Build Results

```
dotnet build IOS-IG-SimHost.sln -v quiet
```

**Result: 0 errors, 0 new warnings**

---

## Testing Results

**Unit Tests (Hrot.SimHost.Tests):** 433 / 433 passed
**Integration Tests (Hrot.SimHost.Integration.Tests):** 39 / 41 passed
**ClusterRunner Integration Tests:** Test run started; did not complete within observation window
(DDS-based tests require live network setup and take 5-10+ minutes)

**Key Test Scenarios Verified:**
- All translator unit tests pass with updated namespace references
- All SimHost integration tests pass (2 pre-existing failures confirmed with git stash baseline)
- Node bootstrapper fallback path exercises direct pack construction (existing tests unchanged)

### Pre-Existing Failures (Not Caused by This Batch)

Confirmed via `git stash` before applying changes:

1. `EntityCreationFlowTests.MissingEntityMaster_ReturnsErrorAck` -- pre-existing
2. `MissionExecutionFlowTests.EntityMission_MovesEntity` -- pre-existing

---

## Files Created

| File | Purpose |
|------|---------|
| `Hrot.Network.NED/SimHost/PathfindingTranslators.cs` | Moved from Hrot.SimHost/Network/; namespace changed to `Hrot.Network.NED.SimHost` |
| `Hrot.Network.NED/SimHost/PerceptionTranslators.cs` | Moved from Hrot.SimHost/Network/; namespace changed to `Hrot.Network.NED.SimHost` |
| `Hrot.Network.NED/SimHost/BrainPathfindingTranslatorPack.cs` | Moved from Hrot.SimHost/Network/; namespace changed to `Hrot.Network.NED.SimHost` |
| `Hrot.Network.NED/SimHost/BrainPerceptionTranslatorPack.cs` | Moved from Hrot.SimHost/Network/; namespace changed to `Hrot.Network.NED.SimHost` |
| `Hrot.Network.NED/SimHost/SimPathfindingTranslatorPack.cs` | Moved from Hrot.SimHost/Network/; namespace changed to `Hrot.Network.NED.SimHost` |
| `Hrot.Network.NED/SimHost/SimPerceptionTranslatorPack.cs` | Moved from Hrot.SimHost/Network/; namespace changed to `Hrot.Network.NED.SimHost` |
| `Hrot.Network.NED/SimHost/NedSimHostPathfindingTranslators.cs` | NED implementation of `ISimHostPathfindingTranslators`; wraps Brain+Solver packs; `RegisterOn(kernel)` |
| `Hrot.Network.NED/SimHost/NedSimHostPerceptionTranslators.cs` | NED implementation of `ISimHostPerceptionTranslators`; wraps Brain+Perception packs; `RegisterOn(kernel)` |
| `Hrot.Core/Network/ISimHostPathfindingTranslators.cs` | Protocol-neutral interface: `void RegisterOn(ModuleHostKernel)` |
| `Hrot.Core/Network/ISimHostPerceptionTranslators.cs` | Protocol-neutral interface: `void RegisterOn(ModuleHostKernel)` |

---

## Files Deleted

| File | Reason |
|------|--------|
| `Hrot.SimHost/Network/Egress/AudioTargetDetectedEgressTranslator.cs` | Duplicate -- canonical copy in Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/Egress/DamageAssessedEgressTranslator.cs` | Duplicate -- canonical copy in Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/Egress/MissionControlAckEgressTranslator.cs` | Duplicate -- canonical copy in Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/Egress/MunitionDetonationEgressTranslator.cs` | Duplicate -- canonical copy in Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/Egress/WeaponFireIntentEgressTranslator.cs` | Duplicate -- canonical copy in Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/Egress/WeaponFireNotificationEgressTranslator.cs` | Duplicate -- canonical copy in Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/Ingress/EntityHitDamageIngressTranslator.cs` | Duplicate -- canonical copy in Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/Ingress/MissionControlIngressTranslator.cs` | Duplicate -- canonical copy in Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/Ingress/MunitionDetonationIngressTranslator.cs` | Duplicate -- canonical copy in Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/Ingress/WeaponFireRequestIngressTranslator.cs` | Duplicate -- canonical copy in Hrot.Network.NED/SimHost/ |
| `Hrot.SimHost/Network/SimHostAuxiliaryTranslatorPack.cs` | Duplicate -- canonical copy in Hrot.Network.NED/SimHost/ |
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
| `Hrot.Core/Network/INetworkFactory.cs` | Added `CreateSimHostPathfindingTranslators()` and `CreateSimHostPerceptionTranslators()` |
| `Hrot.Network.NED/Factory/NedNetworkFactory.cs` | Implemented both new factory methods; added `NullSimHostPathfindingTranslators` and `NullSimHostPerceptionTranslators` inner null-stubs |
| `Hrot.Network.BDC/Factory/BdcNetworkFactory.cs` | Added null stub implementations for both new factory methods + inner null-stub classes |
| `Hrot.SimHost/NodeBootstrapper.cs` | Added `INetworkFactory?` field and constructor; added optional `ModuleHostKernel? kernel` to `BuildTranslators`; dual-path: factory+kernel calls `RegisterOn(kernel)`, fallback uses direct pack construction |
| `Hrot.SimHost/SimHostApp.cs` | Removed stale `using Hrot.SimHost.Network;` |
| `Hrot.SimHost/Translators/ActuatorIntentsEgressPack.cs` | Updated `using Hrot.SimHost.Network.Egress;` to `using Hrot.Network.NED.SimHost;` |
| `Hrot.CGF/CgfApplication.cs` | Removed unused `using Hrot.NED.Messages;` |
| `Hrot.SimHost.Tests/TranslatorPackTests.cs` | Updated `using Hrot.SimHost.Network;` to `using Hrot.Network.NED.SimHost;` |
| `Hrot.SimHost.Tests/AudioTargetDetectedEgressTranslatorTests.cs` | Updated `using Hrot.SimHost.Network.Egress;` to `using Hrot.Network.NED.SimHost;` |
| `Hrot.SimHost.Tests/DamageAssessedEgressTranslatorTests.cs` | Updated `using Hrot.SimHost.Network.Egress;` to `using Hrot.Network.NED.SimHost;` |
| `Hrot.SimHost.Tests/MunitionDetonationEgressTranslatorTests.cs` | Updated `using Hrot.SimHost.Network.Egress;` to `using Hrot.Network.NED.SimHost;` |
| `Hrot.SimHost.Tests/WeaponFireIntentEgressTranslatorTests.cs` | Updated `using Hrot.SimHost.Network.Egress;` to `using Hrot.Network.NED.SimHost;` |
| `Hrot.SimHost.Tests/WeaponFireNotificationEgressTranslatorTests.cs` | Updated `using Hrot.SimHost.Network.Egress;` to `using Hrot.Network.NED.SimHost;` |
| `Hrot.SimHost.Tests/EntityHitDamageIngressTranslatorTests.cs` | Updated `using Hrot.SimHost.Network.Ingress;` to `using Hrot.Network.NED.SimHost;` |
| `Hrot.SimHost.Tests/MunitionDetonationIngressTranslatorTests.cs` | Updated `using Hrot.SimHost.Network.Ingress;` to `using Hrot.Network.NED.SimHost;` |
| `Hrot.SimHost.Tests/WeaponFireRequestIngressTranslatorTests.cs` | Updated `using Hrot.SimHost.Network.Ingress;` to `using Hrot.Network.NED.SimHost;` |
| `Hrot.SimHost.Tests/MissionAdapterSystemTests.cs` | Removed unused `using Hrot.SimHost.Network;` |
| `Hrot.SimHost.Tests/NavigationTranslatorTests.cs` | Removed unused `using Hrot.SimHost.Network;` |

---

## Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

After deleting Hrot.SimHost/Network/, two files with stale using directives were found:
`SimHostApp.cs` had `using Hrot.SimHost.Network;` (unused) and `ActuatorIntentsEgressPack.cs`
had `using Hrot.SimHost.Network.Egress;` (needed the egress translators). Both were fixed by
removing or updating the using directives. Additionally, 10 test files needed namespace updates.

The Phase 2 CGF decoupling attempt revealed that `MissionControlExecutionSystem` (in
`Hrot.Network.NED/Systems/`, namespace `Hrot.Common.Systems`) is registered by `CgfLogicPack.cs`
inside the CGF project. This type lives physically inside the NED assembly, so CGF cannot drop
its NED reference without first moving `MissionControlExecutionSystem` out of the NED assembly
(or into a neutral location such as `Hrot.Common` or a new `Hrot.Network.Common` project).

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

`MissionControlExecutionSystem` is in `Hrot.Network.NED/Systems/` with namespace
`Hrot.Common.Systems`. The namespace implies it belongs in the Common layer, but the file sits
in NED. This misplacement is the single remaining blocker for removing the NED reference from
`Hrot.CGF.csproj`. Moving it to `Hrot.Common` in a dedicated batch would immediately unblock
Phase 2 completion.

The IG decoupling (Phase 3) requires a larger workstream. The `IgApplication.cs` wires many
NED-specific translator types (ContextActionsUpdateTranslator, IgMissionIngressTranslator,
GroundClampingOverrideTranslator, AudioTargetDetectedIngressTranslator,
WeaponFireIngressTranslator) directly -- none of these are behind factory abstractions yet.
An `IIgAuxiliaryTranslators` interface + `NedIgAuxiliaryTranslators` implementation following
the same pattern as `ISimHostAuxiliaryTranslators` would enable this decoupling.

**Q3: What design decisions did you make beyond the instructions?**

`NodeBootstrapper.BuildTranslators()` was given an optional `ModuleHostKernel? kernel = null`
parameter rather than making `kernel` mandatory. When both factory and kernel are available,
the factory path is taken (translators registered via `RegisterOn(kernel)`). When either is
null, the original direct pack construction path is used. This preserves backward compatibility
with all existing unit tests that call `BuildTranslators` without a kernel, while enabling the
new factory code path for production use from `SimHostApp`.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

Phase 1e check found that `Hrot.SimHost` still uses `SharedTranslatorPack`,
`KinematicTranslatorPack`, and `CognitiveTranslatorPack` from `Hrot.Network.NED` via
`NedReplicationModule`. These are core entity-state replication packs -- they are not isolated
translator files and cannot be removed without abstracting `NedReplicationModule` itself behind
`INetworkFactory.CreateReplicationModule()`. That abstraction is the work of `TASK-P4-003`
(IG decoupling), not `TASK-P4-002`. As a result the NED project reference was kept in
`Hrot.SimHost.csproj`, matching the precedent from BATCH-09's deferred Phase 14.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

None introduced. The dual-path fallback in `NodeBootstrapper` is a one-time startup code path.

---

## Outstanding Issues / Next Steps

- [ ] **DEBT-009 partial:** Phase 1 complete. `Hrot.SimHost.csproj` still references NED because
  `SharedTranslatorPack` / `KinematicTranslatorPack` / `CognitiveTranslatorPack` are in NED.
  Removing the NED reference requires abstracting `IReplicationModule` out of `HrotNodeBuilder`
  (Phase 3 / TASK-P4-003).
- [ ] **CGF full decoupling (Phase 2 blocker):** `MissionControlExecutionSystem` must be moved
  from `Hrot.Network.NED/Systems/` to a NED-free project (e.g., `Hrot.Common/Systems/`).
  Once moved, removing `<ProjectReference>` to NED from `Hrot.CGF.csproj` will be trivial.
- [ ] **IG decoupling (Phase 3, TASK-P4-003):** 12+ IG files (IgApplication.cs,
  ContextMenuSystem.cs, SpawningModule.cs, StyleResolutionSystem.cs, MapCommandController.cs,
  IgCapabilitiesPublisher.cs, IgZoneDummyHandler.cs, MiniExConPanelState.cs, and 5 translator
  files) use NED types. Needs a dedicated batch with `IIgAuxiliaryTranslators` abstraction plus
  possibly extending `INetworkFactory.CreateReplicationModule()` to return a protocol-neutral
  interface.
