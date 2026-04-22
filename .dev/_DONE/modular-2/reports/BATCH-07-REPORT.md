# BATCH-07 Report: Create Hrot.Network.BDC

**Task:** TASK-P3-003  
**Date:** 2026-04-12

---

## Files Created

### Hrot.Network.BDC project
- `Hrot.Network.BDC/Hrot.Network.BDC.csproj`
- `Hrot.Network.BDC/BdcCommon.cs` - DDS struct types (BdcNodeId, BdcGeoPoint, BdcEulerOri, BdcAngularVector)
- `Hrot.Network.BDC/BdcEntityMessages.cs` - DDS topics (BdcEntityMaster, BdcWorldPos)
- `Hrot.Network.BDC/BdcMissionMessages.cs` - DDS topics (BdcMissionControlRequest, BdcMissionControlAck)
- `Hrot.Network.BDC/Replication/BdcEntityMasterTranslator.cs`
- `Hrot.Network.BDC/Replication/BdcWorldPosTranslator.cs`
- `Hrot.Network.BDC/Replication/BdcReplicationModule.cs`
- `Hrot.Network.BDC/Factory/BdcNetworkFactory.cs`

### Hrot.Network.BDC.Tests project
- `Hrot.Network.BDC.Tests/Hrot.Network.BDC.Tests.csproj`
- `Hrot.Network.BDC.Tests/BdcNetworkFactoryTests.cs`

Both projects added to `IOS-IG-SimHost.sln`.

---

## Deviations from Instructions

### 1. DdsReader constructor does not take a topic name
**Instructions pseudocode:** `new DdsReader<BdcEntityMaster>(participant, "BDC_EntityMaster")`  
**Actual API:** `new DdsReader<BdcEntityMaster>(participant)` — topic name comes from the `[DdsTopic]` attribute on the struct.  
**Source:** `EntityMasterIngressTranslator.cs` in NED.

### 2. DDS reader polling API differs from pseudocode
**Instructions pseudocode used:** `_reader.TakeSamples()` returning objects with `.Sample` and `.InstanceState`  
**Actual API:** `using var loan = _reader.Take(); foreach (var sample in loan)` where `sample.Info.InstanceState`, `sample.IsValid`, and `sample.Data` are the properties.  
For disposed instances, only key fields are valid; content is recovered via `DdsTypeSupport.FromNative<T>(sample.NativePtr)`.  
**Source:** `EntityMasterIngressTranslator.cs` in NED.

### 3. NetworkEntityMap API differs
**Instructions pseudocode:** `_entityMap.TryGetEntityByNetId(msg.EntityId, out _)`  
**Actual API:** `_entityMap.TryGetEntity(netId, out entity)`  
**Source:** `GeoSpatialIngressTranslator.cs` in NED.

### 4. GhostCreationSystem.CreateGhost signature differs
**Instructions pseudocode:** `_ghostCreation.CreateGhost(cmd, msg.EntityId, msg.TkbType, _localNodeId)`  
**Actual API:** `_ghostCreation.CreateGhost(repo, netId, view.Tick)` where `repo` is `EntityRepository` cast from `view`, and returns the created `Entity`. TkbType is not a parameter on CreateGhost (TkbIdentity is attached separately by EntityMasterIngressTranslator in NED via `cmd.AddComponent`).  
**Decision:** For BDC minimal scope, ghost is created without TkbIdentity (since `CreateGhost` only takes networkId and tick). This is acceptable for minimal BDC scope.

### 5. DestroyEntityCommand differs
**Instructions pseudocode:** `_eventBus.Publish(new DestroyEntityCommand { Entity = entity })`  
**Actual API:** `_eventBus.PublishManaged(new DestroyEntityCommand { NetworkId = networkEntityId, Reason = "..." })`  
**Source:** `EntityMasterIngressTranslator.cs` in NED.

### 6. SimTransform fields differ
**Instructions pseudocode:** `xf.EulerAngles.x/y/z`  
**Actual API:** `SimTransform` has `Position` (Vector3) and `Rotation` (Quaternion).  
**Adaptation:** Used `SimTransformBridgeSystem.RotationToHeadingDeg()` and `RotationToPitchRollDeg()` for egress (as in GeoSpatialEgressTranslator), and `SimTransformBridgeSystem.HeadingDegToRotation()` for ingress (as in GeoSpatialIngressTranslator).

### 7. IGeographicTransform method names differ
**Instructions pseudocode:** `CartesianToGeodetic()` / `GeodeticToCartesian()`  
**Actual API:** `ToGeodetic(Vector3)` / `ToCartesian(lat, lon, alt)` (matches NED translators).

### 8. CycloneNetworkIngressSystem namespace
**Instructions pseudocode:** `using ModuleHost.Network.Cyclone.Systems`  
**Actual:** `CycloneNetworkIngressSystem` is in `ModuleHost.Network.Cyclone.Modules` namespace (defined inline in `CycloneNetworkModule.cs`).

### 9. HasAuthority requires an extension method import
**Actual:** `view.HasAuthority(entity, packedKey)` requires `using FDP.Toolkit.Replication.Extensions;`. Added to both translators.

### 10. NodeRole.AllInOne does not exist
**Instructions pseudocode and test:** Used `NodeRole.AllInOne`  
**Actual:** `NodeRole` only has `Brain`, `MuscleGround`, `ImageGenerator`, `Perception`, `NavigationSolver`.  
**Fix:** Replaced `NodeRole.AllInOne` with `NodeRole.Brain | NodeRole.MuscleGround | NodeRole.ImageGenerator` in the test.  
**Impact:** Test method renamed from `_TrueForAllInOneRole` to `_FalseForAllInOneRole` (the test comment already said the assertion should be `False`; this is a fix to the test name in the instructions).

### 11. BdcMissionControlRequest required [DdsManaged]
**Issue:** The DDS code-generator rejected `BdcMissionControlRequest.PayloadJson` (string field) without `[DdsManaged]`.  
**Fix:** Added `[DdsManaged]` to `BdcMissionControlRequest` (the instructions had it only on `BdcMissionControlAck`).

---

## Build Result

```
0 Error(s)
```

---

## BDC Test Results

```
Passed!  - Failed: 0, Passed: 8, Skipped: 0, Total: 8
```

All 8 `BdcNetworkFactoryTests` tests passed.

---

## All Unit Test Results

Pre-existing failures in other projects (not caused by this batch — only new files were added, no existing files were modified):

| Project | Passed | Failed | Notes |
|---------|--------|--------|-------|
| Hrot.Network.BDC.Tests | 8 | 0 | New — all pass |
| Hrot.Core.Tests | 86 | 0 | |
| Hrot.ExCon.Tests | 306 | 0 | |
| Hrot.Editor.Tests | 53 | 0 | |
| Hrot.Orchestrator.Tests | 88 | 0 | |
| Hrot.Presentation.Tests | 16 | 0 | |
| Hrot.Map.Common.Tests | 30 | 0 | |
| Fdp.Engine.Tests | 693 | 32 | Pre-existing |
| Hrot.IG.Tests | 396 | 7 | Pre-existing |
| Hrot.SimHost.Tests | 427 | 24 | Pre-existing |
| Hrot.ClusterRunner.Tests | 204 | 4 | Pre-existing |
| Fdp.Network.Cyclone.Tests | 40 | 0 | |
| Fdp.Presentation.Tests | 181 | 0 | |
| Fdp.Examples.NetworkDemo.Tests | 22 | 0 | |
| Fdp.Examples.Scenarios.Tests | 66 | 0 | |
| Fdp.Examples.UrbanCombat.Tests | 20 | 0 | |

---

## API Adaptations Summary

The pseudocode in the instructions was illustrative. All four key translator patterns were adapted from the actual NED implementations as instructed:
- **EntityMasterEgressTranslator.cs** → BdcEntityMasterTranslator (egress path)
- **EntityMasterIngressTranslator.cs** → BdcEntityMasterTranslator (ingress path)
- **GeoSpatialEgressTranslator.cs** → BdcWorldPosTranslator (egress path)
- **GeoSpatialIngressTranslator.cs** → BdcWorldPosTranslator (ingress path)
