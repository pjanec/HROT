# BATCH-23 Report

**Batch:** BATCH-23
**Tasks:** P2-03 (DangerAreaSensor + DangerAreaCognitiveBuffer + DangerAreaRefreshSystem), P2-04 (Phase-2 integration test)
**Status:** APPROVED

---

## Files Created / Modified

| File | Action | Description |
|------|--------|-------------|
| `FDP/Engine/Fdp.Core/GlobalComponentIds.cs` | Modified | Added DangerAreaSensor=257 and DangerAreaCognitiveBuffer=258 in the squad block (256-299) |
| `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/DangerAreaSensorComponent.cs` | Created | 16-byte sequential component: BlueprintId, Epoch, RefreshIntervalSeconds, LastRefreshSimTime |
| `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/DangerAreaCognitiveBuffer.cs` | Created | 552-byte component: Count(4) + _pad(4) + DangerAreaDescriptorArray(8*68=544); GetSpanRW/GetSpanRO use InlineArray defensive-copy pattern |
| `FDP/Toolkits/Fdp.Toolkits/Squad/Systems/DangerAreaRefreshSystem.cs` | Created | Refresh system: interval-gated, stackalloc Span<DangerAreaDescriptor>[8], writes via GetSpanRW, increments Epoch |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Systems/DangerAreaRefreshSystemTests.cs` | Created | 4 tests: SC-P2-03-1 through SC-P2-03-4 |
| `FDP/Toolkits/Fdp.Toolkits.Tests/Squad/Phase2IntegrationTests.cs` | Created | 4 tests: SC-P2-04-1 through SC-P2-04-4 |
| `FDP/Toolkits/Fdp.Toolkits/Squad/DangerArea/Fake/FakeDangerAreaProvider.cs` | Not modified | Already had full Add() overload with zFloor/zCeiling parameters |

---

## Build Output

```
dotnet build FDP/Toolkits/Fdp.Toolkits/Fdp.Toolkits.csproj
  Build succeeded.
    0 Warning(s)
    0 Error(s)

dotnet build FDP/Toolkits/Fdp.Toolkits.Tests/Fdp.Toolkits.Tests.csproj
  Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Test Results

### New Tests (DangerAreaRefresh + Phase2Integration)

```
dotnet test ... --filter "FullyQualifiedName~DangerAreaRefresh|FullyQualifiedName~Phase2Integration"

Passed!  - Failed: 0, Passed: 8, Skipped: 0, Total: 8, Duration: 111 ms
```

All 8 new tests pass:
- SC-P2-03-1: `SingleChild_WritesDescriptorsAndSetsCount` - PASS
- SC-P2-03-2: `EpochIncrements` - PASS
- SC-P2-03-3: `TwoSensorChildren_RefreshedIndependently` - PASS
- SC-P2-03-4: `ZPreserved` - PASS
- SC-P2-04-1: `Contacts_MergeCorrectly` - PASS
- SC-P2-04-2: `DangerAreaBuffer_HasStreetCrossing` - PASS
- SC-P2-04-3: `MemberAdded_SourceMaskGrows` - PASS
- SC-P2-04-4: `ZeroAlloc_Over100Ticks` - PASS

### Regression Tests (Squad + ThreatMatrix + StarterPack)

```
dotnet test ... --filter "FullyQualifiedName~Squad|FullyQualifiedName~ThreatMatrix|FullyQualifiedName~StarterPack"

Passed!  - Failed: 0, Passed: 66, Skipped: 0, Total: 66, Duration: 616 ms
```

66 total = 58 prior baseline + 8 new tests. Zero regressions.

---

## Deviations from Instructions

1. **FakeDangerAreaProvider.Add overload**: The instructions noted it might need a new overload for ZFloor/ZCeiling. Inspection found the existing `Add()` already accepted all parameters (center, extentsXY, angleRad, zFloor, zCeiling, nearSide, farSide) with defaults. No overload was needed.

2. **_pad in DangerAreaCognitiveBuffer**: The instructions said to verify whether `_pad` was necessary. `DangerAreaDescriptor` is 4-byte aligned (starts with `uint`). A 4-byte Count + 4-byte _pad = 8 bytes before Slots gives clean 8-byte offset. Kept `_pad` as specified (total struct size 552 bytes).

3. **AddComponent API**: The `EntityRepository.AddComponent<T>(Entity)` generic overload does not exist; the API requires passing a struct value `AddComponent(entity, new T())`. Fixed in both test files.
