# BATCH-07 Report — TKB Phase 8: ScenarioHeaderDto, Consensus Check, Save Pipeline

**Tasks:** TKB-016, TKB-021, TKB-018  
**Status:** COMPLETE

---

## Summary

All three tasks in BATCH-07 were implemented. Additionally, a namespace bug was fixed
in `ScenarioFileService.cs` (wrong `using Fdp.Toolkit.Tkb;` replaced with `using Fdp.Interfaces;`
since `ITkbDatabase` lives in `Fdp.Interfaces`, not `Fdp.Toolkit.Tkb`).

---

## TKB-016 — Extend `ScenarioHeaderDto` with `TkbName`

**Files modified:**
- `Hrot/Engine/Hrot.Core/Scenario/Map/ScenarioHeaderDto.cs` — added `public string? TkbName { get; set; }` with doc comment

**Files created:**
- `Hrot/Engine/Hrot.Core.Tests/ScenarioHeaderDtoTests.cs` — 3 tests

**Tests:**
1. `ScenarioHeaderDto_WithTkbName_Deserializes` — verifies TkbName round-trips from JSON
2. `ScenarioHeaderDto_WithoutTkbName_IsNull` — verifies null when absent from JSON
3. `ScenarioHeaderDto_TkbNameNull_InJson_IsNull` — verifies null when explicitly null in JSON

---

## TKB-021 — Wire `ActiveTkbName` into scenario save pipeline

### FDP part

**Files modified:**
- `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioHeader.cs` — added `string? TkbName = null` optional param
- `FDP/Toolkits/Fdp.Toolkits/Scenario/ScenarioSerializer.cs` — conditional TkbName write to headerNode

**Files created:**
- `FDP/Toolkits/Fdp.Toolkits.Tests/Scenario/ScenarioSerializerTkbNameTests.cs` — 2 tests

**Tests:**
1. `Serialize_WithTkbName_IncludesTkbNameInHeader` — verifies TkbName appears in serialized JSON
2. `Serialize_WithoutTkbName_OmitsTkbNameFromHeader` — verifies TkbName absent when null

### HROT part

**Files modified:**
- `Hrot/Engine/Hrot.Presentation/ScenarioEditor/Services/ScenarioFileService.cs`:
  - Added `private readonly ITkbDatabase? _tkbDb;` field
  - Added `ITkbDatabase? tkbDb = null` optional constructor parameter
  - Updated `ScenarioHeaderDto` initializer to include `TkbName = _tkbDb?.ActiveTkbName`
  - Updated `ScenarioHeader` constructor call to pass `TkbName: _tkbDb?.ActiveTkbName`
  - Fixed namespace: replaced `using Fdp.Toolkit.Tkb;` with `using Fdp.Interfaces;`

**Files created:**
- `Hrot/Engine/Hrot.Presentation.Tests/ScenarioFileServiceTkbTests.cs` — 3 tests

**Tests:**
1. `SaveScenario_WithActiveTkbName_StampsTkbNameInHeader` — active TkbName appears in saved file
2. `SaveScenario_WithNullActiveTkbName_OmitsOrNullsTkbName` — null ActiveTkbName produces null TkbName
3. `SaveScenario_WithoutTkbDatabase_OmitsOrNullsTkbName` — no ITkbDatabase produces null TkbName

---

## TKB-018 — Orchestrator `TkbName` consensus check

**Files modified:**
- `Hrot/Subsystems/Hrot.Orchestrator/StorageGatewayModule.cs`:
  - Added `CheckTkbNameConsensus(string[] files)` static method
  - Added `PeekTkbNameFromFile(string filePath)` static helper (forward-only Utf8JsonReader)
  - Called `CheckTkbNameConsensus(files)` in `PrefetchScenarioAsync` after empty-dir guard
- `Hrot/Subsystems/Hrot.Orchestrator.Tests/StorageGatewayTests.cs`:
  - Added `StorageGatewayTkbConsensusTests` class with 5 tests

**Tests:**
1. `PrefetchScenario_SameTkbName_AllFiles_Succeeds` — same TkbName in all files passes
2. `PrefetchScenario_ConflictingTkbNames_ThrowsInvalidOperationException` — conflict throws
3. `PrefetchScenario_NullTkbNames_AllFiles_Succeeds` — all null TkbNames passes
4. `PrefetchScenario_MixedNullAndNonNull_SameName_Succeeds` — null + name passes
5. `PrefetchScenario_NonJsonFiles_AreIgnoredByConsensusCheck` — .bin files ignored

---

## Test Counts

| Project | Filter | Passed | Failed |
|---|---|---|---|
| Fdp.Toolkits.Tests | FullyQualifiedName~Tkb | 111 | 0 |
| Hrot.Core.Tests | FullyQualifiedName~ScenarioHeaderDto | 3 | 0 |
| Hrot.Presentation.Tests | FullyQualifiedName~ScenarioFileServiceTkb | 3 | 0 |
| Hrot.Orchestrator.Tests | FullyQualifiedName~TkbConsensus | 5 | 0 |
| Hrot.SimHost.Tests | FullyQualifiedName~Tkb | 29 | 0 |

Pre-existing failures unrelated to TKB:
- `Hrot.Core.Tests`: 5 `LogArchiveExtractionServiceTests` failures (pre-existing)
- `Hrot.Orchestrator.Tests`: 4 pre-existing failures (ClusterMasterContext, PrefetchScenario, ReferenceArchive, StorageProcess)
- `Hrot.Presentation.Tests`: 2 `EntityDragGizmoTests` failures (floating-point precision, pre-existing)

---

## Issues Encountered

1. **Namespace bug**: `ScenarioFileService.cs` used `using Fdp.Toolkit.Tkb;` but `ITkbDatabase`
   is in `Fdp.Interfaces`. Fixed during implementation.

2. **Subagent unavailable**: Both subagent delegation attempts for BATCH-07 returned
   "Agent error: Sorry, no response was returned." — implemented directly by dev lead.
