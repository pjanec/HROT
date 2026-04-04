# BATCH-06 Report

**Batch:** BATCH-06  
**Status:** ✅ All tasks implemented and verified  
**Date:** 2025-07-22

---

## Task Summary

| Task | Status | Notes |
|------|--------|-------|
| PACK-D001 | ✅ Done | `DamageAssessedEvent.HitEntityId` → `Entity HitEntity`; NetworkEntityMap moved to translator boundary |
| PACK-M003 | ✅ Done | `EntityMissionHolder` and `IgMissionHolder` deleted; `ActiveMissionPlan` POCO introduced |
| PACK-A001 | ✅ Done | `AudioPerceptionSystem` purified; `TargetHeardEvent` defined; `ThreatEvaluationSystem` extended; translators added |

---

## Test Results

| Test Project | Pass | Fail | Total | Notes |
|---|---|---|---|---|
| `FDP.Toolkit.Combat.Tests` | 52 | 0 | 52 | All pass |
| `FDP.Toolkit.Perception.Tests` | 35 | 0 | 35 | Includes 4 new PACK-A001 tests |
| `Hrot.SimHost.Tests` | 425 | 1 | 426 | 1 pre-existing `GeoSpatialEgressTranslatorTests.Dispose_AlsoCallsBaseDispose` failure (confirmed pre-existing in BATCH-01 baseline) |
| `Hrot.SimHost.Integration.Tests` | 36 | 2 | 38 | 2 pre-existing failures: `TraceLoggingTests.SpawnVehicle_EmitsTraceSequence` and `EntityLifecycleIntegrationTests.DomainIsolation_Domain0Spawn_DoesNotAffectDomain10` (confirmed pre-existing in BATCH-01 report) |

**New tests added (PACK-A001):**
- `AudioPerceptionSystemTests.AudioPerception_PublishesTargetHeardEvent_WhenListenerWithinHearingRange`
- `AudioPerceptionSystemTests.AudioPerception_DoesNotPublish_WhenListenerOutsideHearingRange`
- `AudioPerceptionSystemTests.AudioPerception_OnlyPublishesForNearbyListener_WhenTwoListenersExist`
- `ThreatEvaluationSystemTests.ThreatEvaluation_BoostsScore_OnTargetHeardEvent`
- `AudioTargetDetectedEgressTranslatorTests.ScanAndPublish_WritesAudioTargetDetected_ForSingleEvent`
- `AudioTargetDetectedEgressTranslatorTests.ScanAndPublish_DoesNotWrite_WhenNoEvents`
- `AudioTargetDetectedEgressTranslatorTests.ScanAndPublish_SkipsEvent_WhenListenerNotMapped`

---

## Files Modified / Created

### PACK-D001 — Replace `long HitEntityId` with `Entity HitEntity`

| File | Change |
|------|--------|
| `FDP/Toolkits/FDP.Toolkit.Combat/Events/DetonationEvents.cs` | `HitEntityId` field replaced with `Entity HitEntity`; `Pack = 1` removed |
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/DamageCalculationSystem.cs` | Removed `NetworkEntityMap` constructor param; publishes `HitEntity = targetEntity` directly |
| `FDP/Toolkits/FDP.Toolkit.Combat/Systems/HealthApplicationSystem.cs` | Removed `NetworkEntityMap` param; uses `evt.HitEntity` directly |
| `FDP/Toolkits/FDP.Toolkit.Combat/Modules/DamageAssessmentModule.cs` | Removed `NetworkEntityMap entityMap` parameter from `RegisterSystems` |
| `Hrot.SimHost/Network/Egress/DamageAssessedEgressTranslator.cs` | Added `NetworkEntityMap _entityMap`; `ScanAndPublish` maps Entity → network ID before writing |
| `Hrot.SimHost/Network/Ingress/EntityHitDamageIngressTranslator.cs` | `ProcessSample` resolves `long → Entity` via map; publishes `HitEntity` |
| `Hrot.SimHost/SimHostApp.cs` | Passes `entityMap` to `DamageAssessedEgressTranslator` constructor |
| `Hrot.SimHost/Modules/SimulationLogicModule.cs` | Removed `_entityMap` argument from `RegisterSystems` call |
| `FDP/Toolkits/FDP.Toolkit.Combat.Tests/DamageCalculationSystemTests.cs` | Removed NetworkEntityMap; assertions use `HitEntity` |
| `FDP/Toolkits/FDP.Toolkit.Combat.Tests/HealthApplicationSystemTests.cs` | Rewritten to use Entity-based publish; no NetworkEntityMap |
| `Hrot.SimHost.Tests/DamageAssessedEgressTranslatorTests.cs` | Added `NetworkEntityMap`; tests use Entity publish; added SC-3 unmapped entity test |
| `Hrot.SimHost.Tests/EntityHitDamageIngressTranslatorTests.cs` | Assertion updated from `HitEntityId` to `HitEntity` |

### PACK-M003 — Introduce `ActiveMissionPlan` POCO

| File | Change |
|------|--------|
| `FDP/Toolkits/FDP.Toolkit.Behavior/Components/DomainMissionPlan.cs` | **Created** — `DomainMissionTask`, `DomainMissionPlan`, `ActiveMissionPlan` classes |
| `FDP/Toolkits/FDP.Toolkit.Behavior/Components/BehaviorApplicationComponentIds.cs` | **Created** — `ActiveMissionPlan = 162` constant for `[ComponentId]` attribute |
| `Hrot.SimHost/Components/EntityMissionHolder.cs` | **Deleted** |
| `Hrot.IG/Components/IgMissionHolder.cs` | **Deleted** |
| `Hrot.SimHost/Systems/MissionControlExecutionSystem.cs` | Uses `ActiveMissionPlan`; conversion from `MissionPlan` → `DomainMissionPlan` |
| `Hrot.SimHost/Systems/MissionAdapterSystem.cs` | Reads `ActiveMissionPlan.Plan.Tasks` |
| `Hrot.IG/Translators/IgMissionIngressTranslator.cs` | Uses `ActiveMissionPlan`; added `MapToPlan()` helper |
| `Hrot.IG/Systems/MissionRenderLayer.cs` | Queries and renders from `ActiveMissionPlan.Plan.Tasks` |
| `Hrot.SimHost/SimHostComponentRegistry.cs` | `RegisterManagedComponent<ActiveMissionPlan>()` |
| `Hrot.IG/IgApplication.cs` | `RegisterManagedComponent<ActiveMissionPlan>()` |
| `Hrot.Map.Definitions/HrotComponentIds.cs` | `ActiveMissionPlan = 162` (was `EntityMissionHolder = 162`) |
| `Hrot.SimHost.Integration.Tests/Infrastructure/SimHostInstance.cs` | Updated to use `ActiveMissionPlan`; added `MapToActiveMissionPlan()` helper |
| `Hrot.SimHost.Tests/Systems/MissionControlExecutionSystemTests.cs` | `EntityMissionHolder` → `ActiveMissionPlan` |
| `Hrot.SimHost.Tests/Systems/MissionControlRequestSystemFollowRouteTests.cs` | `EntityMissionHolder` → `ActiveMissionPlan`; assertion paths updated |
| `Hrot.SimHost.Tests/MissionControlRequestSystemTests.cs` | `EntityMissionHolder` → `ActiveMissionPlan` |

**Collateral fix:** Added missing `using Hrot.Common.Events;` to `MissionControlExecutionSystemTests.cs` and `MissionControlRequestSystemTests.cs` (pre-existing build errors).

### PACK-A001 — Decouple AudioPerceptionSystem from TargetMemory

| File | Change |
|------|--------|
| `FDP/Toolkits/FDP.Toolkit.Perception/PerceptionConstants.cs` | Added `TargetHeardEventId = 4004` |
| `FDP/Toolkits/FDP.Toolkit.Perception/Events/PerceptionEvents.cs` | Added `TargetHeardEvent` struct |
| `FDP/Toolkits/FDP.Toolkit.Perception/Systems/AudioPerceptionSystem.cs` | Removed `TargetMemory` guard and mutation; publishes `TargetHeardEvent` |
| `FDP/Toolkits/FDP.Toolkit.Perception/Systems/ThreatEvaluationSystem.cs` | Added Step 3: consumes `TargetHeardEvent` and boosts `TargetMemory` via ECB |
| `Hrot.NED/SimDescriptors.cs` | Added `AudioTargetDetected` partial struct |
| `Hrot.SimHost/Network/Egress/AudioTargetDetectedEgressTranslator.cs` | **Created** — consumes `TargetHeardEvent`; writes `AudioTargetDetected` DDS |
| `Hrot.IG/Translators/AudioTargetDetectedIngressTranslator.cs` | **Created** — reads `AudioTargetDetected` DDS; publishes `TargetHeardEvent` |
| `Hrot.SimHost/SimHostApp.cs` | Registers `AudioTargetDetectedEgressTranslator` |
| `Hrot.IG/IgApplication.cs` | Registers `AudioTargetDetectedIngressTranslator` |
| `Hrot.IG/Hrot.IG.csproj` | Added `FDP.Toolkit.Perception` project reference |
| `FDP/Toolkits/FDP.Toolkit.Perception.Tests/PerceptionTestWorldFactory.cs` | Registered `TargetHeardEvent` |
| `FDP/Toolkits/FDP.Toolkit.Perception.Tests/AudioPerceptionSystemTests.cs` | Rewrote all 3 tests: assert `TargetHeardEvent` published; assert `TargetMemory` NOT mutated |
| `FDP/Toolkits/FDP.Toolkit.Perception.Tests/ThreatEvaluationSystemTests.cs` | Added Test 8: `TargetHeardEvent` → `TargetMemory` boost |
| `Hrot.SimHost.Tests/AudioTargetDetectedEgressTranslatorTests.cs` | **Created** — SC-1 (single event), SC-2 (no events), SC-3 (unmapped listener) |

---

## Deviations from Instructions

None. All steps followed as specified.

---

## Pre-existing Issues Documented

| Issue | Tests Affected |
|-------|---------------|
| `GeoSpatialEgressTranslatorTests.Dispose_AlsoCallsBaseDispose` | 1 unit test (pre-existing from before BATCH-01) |
| `TraceLoggingTests.SpawnVehicle_EmitsTraceSequence` | Integration test (confirmed pre-existing in BATCH-01 report) |
| `EntityLifecycleIntegrationTests.DomainIsolation_Domain0Spawn_DoesNotAffectDomain10` | Integration test (confirmed pre-existing in BATCH-01 report) |
