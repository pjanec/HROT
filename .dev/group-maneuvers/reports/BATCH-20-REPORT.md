# BATCH-20 Report — Squad Phase 0 Prerequisites

**Batch:** BATCH-20  
**Date:** 2025-07-25  
**Status:** COMPLETED — all 5 tasks implemented, 14/14 new tests passing

---

## 1. Tasks Completed

| Task | Title | Status |
|------|-------|--------|
| P0-01 | Shrink AssignmentSlot 64→16 bytes + migrate call sites | Done |
| P0-02 | Create SquadCognitiveState blackboard projection | Done |
| P0-03 | Add ManeuverSelect=3 to DecisionKind + UT0151 analyzer | Done |
| P0-04 | DangerAreaDescriptor + IDangerAreaProvider + FakeDangerAreaProvider | Done |
| P0-05 | Integration and layout tests | Done |

---

## 2. Files Created

| File | Purpose |
|------|---------|
| `FDP/Toolkits/Fdp.Toolkits/Squad/State/SquadCognitiveState.cs` | 1024-byte blackboard projection for squad working state |
| `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/DangerAreaDescriptor.cs` | 68-byte sequential struct + DangerAreaKind enum |
| `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/IDangerAreaProvider.cs` | Sensor interface |
| `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/Fake/FakeDangerAreaProvider.cs` | Fluent test fake, zero-alloc Refresh, FNV-1a-32 helper |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/AssignmentSlotLayoutTests.cs` | 3 layout tests (P0-01) |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/SquadCognitiveStateLayoutTests.cs` | 4 layout + alias tests (P0-02) |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/DangerAreaProviderTests.cs` | 4 provider tests (P0-04) |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/SquadPhase0IntegrationTests.cs` | 3 integration tests (P0-05) |

---

## 3. Files Modified

| File | Change |
|------|--------|
| `FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentState.cs` | AssignmentSlot shrunk to 16B; ThreatMatrixAssignmentState deleted; AssignmentSlotArray gained instance helpers (GetSlot / GetAssignedTarget / SetAssignment) |
| `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` | Added SquadStateMarker=256 constant + doc block for 256-299 range |
| `FDP/Toolkits/Fdp.Toolkits/Utility/Core/UtilityCore.cs` | DecisionKind.ManeuverSelect = 3 added |
| `FDP/Toolkits/Fdp.Toolkits.Analyzers/SharedUtilityDiagnostics.cs` | UT0151_ManeuverSelectInvalidContext added |
| `FDP/Toolkits/Fdp.Toolkits.Analyzers/UtilityAuthoringAnalyzer.cs` | UT0151 in SupportedDiagnostics; CheckManeuverSelectContextBinding call + helpers added |
| `FDP/Toolkits/Fdp.Toolkits/Utility/Group/ThreatMatrixAssignmentSystem.cs` | Migrated to SquadCognitiveState.Project(ref bb).Assignment |
| `FDP/Toolkits/Fdp.Toolkits/Utility/Inputs/StandardInputs.cs` | Migrated to SquadCognitiveState.Project(ref bb).Assignment; added using |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/UtilityTestWorld.cs` | Migrated AssignmentFor to SquadCognitiveState.Project; added using |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Utility/StandardInputReaderTests.cs` | 3 call sites migrated to SquadCognitiveState.Project; added using |

---

## 4. Measured Sizes

| Type | Expected | Actual (Unsafe.SizeOf) | Verified by test |
|------|----------|------------------------|-----------------|
| `AssignmentSlot` | 16 B | **16 B** | AssignmentSlot_SizeIs16Bytes |
| `AssignmentSlotArray` | 256 B | **256 B** | AssignmentSlotArray_SizeIs256Bytes |
| `SquadCognitiveState` | 1024 B | **1024 B** | SquadCognitiveState_TotalSizeIs1024 |
| `DangerAreaDescriptor` | 68 B | **68 B** | DangerAreaDescriptor_PinnedSize_MatchesActual |

---

## 5. Hash Pinning

`FNV-1a-32("street-east-01")` = **0x720E1D2C** (1913527596)

Algorithm: basis=2166136261, prime=16777619, input=UTF-8 bytes.  
Verified by `FakeDangerAreaProvider_FeatureId_PinsForStreetEast01`.

---

## 6. Issues and Resolutions

| Issue | Resolution |
|-------|-----------|
| Batch instructions referenced `DataPolicyKind.NoSave` but enum is `DataPolicy.NoSave` | Used `[DataPolicy(DataPolicy.NoSave)]` per existing codebase pattern |
| `SquadContactPool` math: 10 ulongs would give 600B not 592B | Used 9 ulongs (72B) + Count(4)+LastMergeTick(4)+Contacts(512) = 592B |
| `AssignmentSlotArray` `SetAssignment` takes `ulong` but `AssignedTargetHandle` is `long` | Test uses `unchecked((long)0xDEADBEEF_CAFEBABE)` for the literal |
| Zero-alloc test was reported as flaky failure when run with parallel unrelated failing tests | Runs clean in isolation and in Squad-filtered run; pre-existing parallel test pollution from RecordingExportServiceTests |
| PowerShell uint32 overflow during FNV-1a-32 computation | Computed via minimal dotnet project |

---

## 7. Test Results

```
dotnet test --filter "FullyQualifiedName~Fdp.Toolkit.Squad"
Passed!  - Failed: 0, Passed: 14, Skipped: 0, Total: 14
```

Full suite (pre-existing failures unchanged):
```
dotnet test FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj
Failed: 65, Passed: 1677, Skipped: 0, Total: 1742
```

Pre-existing failures (not introduced by BATCH-20):
- `RecordingExportServiceTests` (EX_T*) — replay export series, unrelated
- `BicycleModelTests` — car kinematics, unrelated
- `SimTransformBridgeSystemTests` — geographic transforms, unrelated
- `FdpAutoSerializerFixedBufferTests` — serializer, unrelated
- `IdAllocationTests` — replication, unrelated
- `S6_CrowdAvoidanceTests` — navigation, unrelated
- `CombatComponentTests` — combat, unrelated
- `LocalDiskStorageProvider` — orchestration, unrelated

No new failures introduced by BATCH-20.

---

## 8. Suggested Commit Message

```
feat(squad): BATCH-20 Phase 0 prerequisites

- P0-01: Shrink AssignmentSlot 64->16 B; migrate all call sites from
  ThreatMatrixAssignmentState to SquadCognitiveState.Assignment
- P0-02: Add SquadCognitiveState (1024 B blackboard projection) with
  ElementPartition, SlotAssignmentArray, RoleAssignmentArray,
  AssignmentSlotArray, and SquadContactPool sub-regions
- P0-03: Add DecisionKind.ManeuverSelect=3; add UT0151 Roslyn analyzer
  rule blocking per-candidate/target context in ManeuverSelect decisions
- P0-04: Add DangerAreaDescriptor (68 B), IDangerAreaProvider interface,
  and FakeDangerAreaProvider (fluent builder, FNV-1a-32 feature ids,
  zero-alloc Refresh)
- P0-05: 14 new tests — layout, offset, projection aliasing, provider
  builder, feature-id pinning, zero-alloc, and integration tests

squad/AssignmentSlot=16B, SquadCognitiveState=1024B,
DangerAreaDescriptor=68B, FNV(street-east-01)=0x720E1D2C
```
