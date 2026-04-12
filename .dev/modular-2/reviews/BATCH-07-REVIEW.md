# BATCH-07 Review: Create Hrot.Network.BDC

**Date:** 2026-04-12  
**Batch:** BATCH-07  
**Tasks:** TASK-P3-003  
**Status:** APPROVED

---

## Summary

TASK-P3-003 (Create Hrot.Network.BDC) is complete. The codebase builds cleanly with 0 errors.
All 8 new BDC tests pass. Pre-existing failures in Hrot.SimHost.Tests (24), Hrot.IG.Tests (7),
Hrot.ClusterRunner.Tests (5), and Fdp.Engine.Tests (3) remain unchanged from pre-batch state
— they are tracked in DEBT-001 and DEBT-005 and are the target of Phase 4 work.

---

## Verification

### Build
- `dotnet build IOS-IG-SimHost.sln --no-incremental` - Build succeeded (0 errors, 4 warnings all pre-existing).

### Files Created
- `Hrot.Network.BDC/Hrot.Network.BDC.csproj` - correct project references, DDS code-gen import
- `Hrot.Network.BDC/BdcCommon.cs` - BdcNodeId, BdcGeoPoint, BdcEulerOri, BdcAngularVector
- `Hrot.Network.BDC/BdcEntityMessages.cs` - BdcEntityMaster, BdcWorldPos DDS topics
- `Hrot.Network.BDC/BdcMissionMessages.cs` - BdcMissionControlRequest, BdcMissionControlAck
- `Hrot.Network.BDC/Replication/BdcEntityMasterTranslator.cs`
- `Hrot.Network.BDC/Replication/BdcWorldPosTranslator.cs`
- `Hrot.Network.BDC/Replication/BdcReplicationModule.cs`
- `Hrot.Network.BDC/Factory/BdcNetworkFactory.cs`
- `Hrot.Network.BDC.Tests/Hrot.Network.BDC.Tests.csproj`
- `Hrot.Network.BDC.Tests/BdcNetworkFactoryTests.cs`
- Both projects added to `IOS-IG-SimHost.sln`

### Test Results
| Project | Passed | Failed | Notes |
|---------|--------|--------|-------|
| Hrot.Network.BDC.Tests | 8 | 0 | All new tests pass |
| Hrot.Core.Tests | 86 | 0 | |
| Hrot.ExCon.Tests | 391 | 0 | |
| Hrot.Editor.Tests | 60 | 0 | |
| Hrot.Orchestrator.Tests | 88 | 0 | |
| Hrot.Presentation.Tests | 16 | 0 | |
| Hrot.SimHost.Tests | 427 | 24 | Pre-existing (DEBT-001) |
| Hrot.IG.Tests | 414 | 7 | Pre-existing (DEBT-001) |
| Hrot.ClusterRunner.Tests | 206 | 5 | Pre-existing (DEBT-001) |
| Fdp.Engine.Tests | 726 | 3 | Pre-existing (DEBT-005) |

### API Deviations
The developer correctly adapted pseudocode to the actual API by following the NED
implementation patterns. All 11 deviations documented in the report are valid and
produce correct behavior. Key ones:
- DDS topic names from `[DdsTopic]` attribute, not constructor parameter — correct.
- `using var loan = _reader.Take()` pattern — correct DDS API usage.
- `NodeRole.AllInOne` doesn't exist — replaced with `Brain | MuscleGround | ImageGenerator` — acceptable.
- `[DdsManaged]` on `BdcMissionControlRequest.PayloadJson` — required per DDS codegen constraints.

---

## Issues Found

None new (only pre-existing debt items remain).

---

## Debt Tracker Updates

No new debt items. Existing debt unchanged:
- DEBT-001: Still open — target is Phase 4/5 batches
- DEBT-005: Still open — 3 failures remain in Fdp.Engine.Tests

---

## Suggested Git Commit Message (already committed)

```
feat(hrot-network-bdc): create Hrot.Network.BDC minimal BDC protocol adapter (BATCH-07)
```

---

## Next Action

Proceed to BATCH-08 (Phase 4: Decouple subsystems from NED + move subsystem adapters).
