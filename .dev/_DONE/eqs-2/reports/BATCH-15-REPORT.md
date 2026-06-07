# BATCH-15 Report — EQS-037 + EQS-038

**Status:** COMPLETE  
**Tests:** 55/55 Hrot integration Eqs + 53/53 FDP toolkit Eqs — all pass

---

## Tasks Completed

### EQS-037 — EqsSensorHandle wrapper struct

**New file:** `FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsSensorHandle.cs`

- `readonly struct EqsSensorHandle : IEquatable<EqsSensorHandle>`
- `[StructLayout(LayoutKind.Sequential, Pack = 4)]`
- Field: `readonly Entity ChildId`
- `IsValid => !ChildId.IsNull`
- `Equals`, `GetHashCode`, `==`, `!=`, `ToString` implemented

**New test file:** `FDP/Toolkits/Fdp.Toolkits.Tests/Eqs/EqsSensorHandleTests.cs`

| Test ID | Test name | Result |
|---------|-----------|--------|
| T-SH1 | `ChildId_RoundTrips` | PASS |
| T-SH2 | `SameEntity_EqualAndSameHash` | PASS |
| T-SH3 | `Default_IsNotValid` | PASS |
| T-SH4 | `DifferentEntities_NotEqual` | PASS |

---

### EQS-038 — Structural: compound key, solver relaxation, child ghost support

#### Files modified

**`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsResultPool.cs`**

- `EqsResultEvent` struct: replaced `long SensorNetworkId` with `long ParentNetworkId` + `int LocalChildIndex`
- New size: 28 bytes (ParentNetworkId:8 + LocalChildIndex:4 + Epoch:4 + RefreshTick:4 + ResultHandle:4 + EntryCount:4)

**`FDP/Toolkits/Fdp.Toolkits/Spatial/Eqs/EqsDdsTopics.cs`**

- `EqsSensorConfigTopic`: replaced single `[DdsKey] long EntityId` with `[DdsKey] long ParentNetworkId` + `[DdsKey] int LocalChildIndex`
- `EqsResultTopic`: replaced single `[DdsKey] long SensorNetworkId` with `[DdsKey] long ParentNetworkId` + `[DdsKey] int LocalChildIndex`

**`Hrot/Subsystems/Hrot.SimHost/Systems/EqsSolverSystem.cs`**

- Query: `.With<EqsSensor>().WithLifecycle(EntityLifecycle.All)` — no longer requires `NetworkIdentity`
- `EvaluateSensor`: 3-branch compound identity resolution:
  1. `PartMetadata` present → parent's `NetworkIdentity.Value` + `InstanceId`
  2. Direct `NetworkIdentity` → legacy (`localChildIndex = 0`)
  3. Neither → local-only (`parentNetworkId = 0`, `localChildIndex = entity.Index`)
- Both publish sites (stub fallback + full result) use compound key

**`Hrot/Network/Hrot.Network.NED/SimHost/EqsSensorConfigEgressTranslator.cs`**

- 3-branch identity resolution (mirrors solver)
- Skips local-only sensors (parentNetworkId == 0)
- DDS topic write uses compound key
- Dispose: writes `new EqsSensorConfigTopic { ParentNetworkId = netId.Value, LocalChildIndex = 0 }`

**`Hrot/Network/Hrot.Network.NED/SimHost/EqsSensorConfigIngressTranslator.cs`**

- `Dictionary<(long ParentNetId, int ChildIndex), Entity> _childGhostCache`
- `LocalChildIndex == 0`: legacy path (parent ghost via `IEntityMap`)
- `LocalChildIndex != 0`: child path — spawn carrier ghost with `PartMetadata` + `EqsSensor` + `EqsCognitiveBuffer`, no `NetworkIdentity`; or update existing via cache

**`Hrot/Network/Hrot.Network.NED/SimHost/EqsResultEventEgressTranslator.cs`**

- Skips local-only events: `if (evt.ParentNetworkId == 0) continue;`
- Topic write uses compound key

**`Hrot/Network/Hrot.Network.NED/CGF/EqsResultIngressTranslator.cs`**

- `Dictionary<(long ParentNetId, int ChildIndex), Entity> _childEntityCache`
- Skips `ParentNetworkId == 0`
- `LocalChildIndex == 0`: legacy entity lookup via `IEntityMap`
- `LocalChildIndex != 0`: `PartMetadata` scan with dictionary cache

**`Hrot/Subsystems/Hrot.SimHost/Systems/EqsResultUpdateSystem.cs`**

- Path B entity lookup refactored to 3 cases:
  - `ParentNetworkId == 0` → local-only (match by `entity.Index`)
  - Candidate has `PartMetadata` → child-entity match (`InstanceId == LocalChildIndex` + parent `NetworkIdentity`)
  - Candidate has no `PartMetadata` AND `LocalChildIndex == 0` → legacy match (direct `NetworkIdentity`)
- PartMetadata path checked BEFORE legacy path so `InstanceId=0` child entities are correctly routed

**`Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsLastUpdateTimeTests.cs`** (existing file, collateral fix)

- Line 68: `SensorNetworkId = netId` → `ParentNetworkId = netId, LocalChildIndex = 0`

#### New test file

**`Hrot/Runner/Hrot.ClusterRunner.Integration.Tests/Eqs/EqsMultiSensorTests.cs`**

| Test ID | Test name | Result |
|---------|-----------|--------|
| T-38-1 | `LocalOnlySensor_NoNetworkIdentity` | PASS |
| T-38-2 | `ChildSensor_WithPartMetadata_PathB` | PASS |
| T-38-3 | `LegacySensor_WithNetworkIdentity_SingleKey` | PASS |
| T-38-4 | `MultiChildSensors_ThreeChildren_AllBuffersPopulated` | PASS |
| T-38-5 | `DistributedChildSensor_MuscleReceivesCarrierGhost` | PASS |

---

## Test Results Summary

| Suite | Filter | Total | Passed | Failed |
|-------|--------|-------|--------|--------|
| FDP toolkit | `FullyQualifiedName~Eqs` | 53 | 53 | 0 |
| Hrot integration | `FullyQualifiedName~Eqs` | 55 | 55 | 0 |

---

## Deviations from Spec

1. **`Entity.Id` does not exist** — spec's `ChildId.Id != 0` adapted to `!ChildId.IsNull`; `Entity` struct exposes `IsNull` as the null check, not an `Id` property.

2. **T-38-5 domain number** — spec proposed domain ~210 for the distributed test. Domain 210 is already in use by `EqsFlagsMeaningfulTests` (`_domainCounter = 210`). T-38-5 uses domain 229 (counter starts at 228, `NextDomain()` yields 229), which is between `EqsScoreDeltaTests` (221) and `EqsContextSlotTests` (231–232) and within the CycloneDDS maximum of 232.

3. **`EqsResultUpdateSystem` routing order** — spec described `LocalChildIndex == 0` as the legacy branch trigger. Routing was changed to check `PartMetadata` BEFORE the `LocalChildIndex == 0` legacy branch. This is required because `InstanceId = 0` is a valid first child index; without this reorder, child[0] entities would be misrouted to the legacy path (T-38-4 verified the fix is necessary).

4. **`EqsLastUpdateTimeTests.cs` collateral fix** — not in spec scope, but the existing test used the old `SensorNetworkId` field which was renamed by EQS-038. Fixed as part of the build requirement.
