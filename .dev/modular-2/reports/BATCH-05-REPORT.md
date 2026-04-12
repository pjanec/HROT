# BATCH-05 Report

**Batch:** BATCH-05
**Date:** 2026-04-12
**Build Result:** 0 errors, 0 warnings

---

## Files Created

### A1 — Neutral mission types in Hrot.Core

- `Hrot.Core/Mission/MissionTypes.cs` — `eForceIdentifier`, `eTaskState`, `eMissionCommandType`, `MissionTrigger` (class), `MissionTask` (class), `MissionPlan` (class)
- `Hrot.Core/Mission/GeoPoint.cs` — `GeoPoint` struct

### Part B — INetworkFactory interfaces in Hrot.Core

- `Hrot.Core/Network/INetworkFactory.cs`
- `Hrot.Core/Network/ICommandGateway.cs`
- `Hrot.Core/Network/IExConEgressWriters.cs`
- `Hrot.Core/Network/Commands.cs` — `CreateEntityCommand`, `UpdateEntityDescriptorCommand`, `MissionControlCommand`, `MapConfigDto`, `MapCommandDto`

### A5 — New projects

- `Hrot.Presentation/Hrot.Presentation.csproj`
- `Hrot.Presentation.Tests/Hrot.Presentation.Tests.csproj`

---

## Files Moved (A6)

All `.cs` from `Hrot.UI.Common/` copied to `Hrot.Presentation/` (same subfolder structure, namespaces preserved).
All `.cs` from `Hrot.ScenarioEditor/` copied to `Hrot.Presentation/ScenarioEditor/` (namespaces preserved).
All `.cs` from `Hrot.ScenarioEditor.Tests/` copied to `Hrot.Presentation.Tests/` (namespaces preserved).

Source directories (`Hrot.UI.Common`, `Hrot.ScenarioEditor`, `Hrot.ScenarioEditor.Tests`) remain on disk but their projects are removed from the solution.

---

## Files Updated

### A2 — Hrot.UI.Common source files

- `Hrot.UI.Common/Facades/IMapPickService.cs` — Removed `using Hrot.NED.Common;`, added `using Hrot.Core.Mission;`, changed `Task<GeoPoint>` → `Task<Hrot.Core.Mission.GeoPoint>`
- `Hrot.UI.Common/Facades/IMissionEditorService.cs` — Removed NED usings, added `using Hrot.Core.Mission;`, updated all method signatures to use neutral types
- `Hrot.UI.Common/Panels/MissionPanel.cs` — Removed NED usings, added `using Hrot.Core.Mission;`, updated all `MissionPlan`/`MissionTask`/`MissionTrigger`/`eTaskState`/`eMissionCommandType`/`GeoPoint` references; changed struct-based null checks (`.HasValue`, `.Value`) to class-based null checks; rewrote `ClonePlan` for class semantics
- `Hrot.UI.Common/Panels/SpawnerPanel.cs` — Replaced `using Hrot.NED.Descriptors;` with `using Hrot.Core.Mission;`; `eForceIdentifier` now resolves from `Hrot.Core.Mission`
- `Hrot.UI.Common/Hrot.UI.Common.csproj` — Removed `Hrot.NED` reference, added `Hrot.Core` reference

### A3 — Hrot.ExCon adapters

- `Hrot.ExCon/ExConPanelAdapters.cs` — Added `using Hrot.Core.Mission;`; updated `ExConMissionShim` method signatures to use neutral types with NED↔neutral mapping helpers (`MapToNeutral`, `MapToNed`); updated `ExConMapPickShim.PickLocationAsync` to convert NED→neutral GeoPoint

### A4 — Hrot.Editor adapters

- `Hrot.Editor/Adapters/EditorMapPickAdapter.cs` — Changed `PickLocationAsync` return type to `Task<Hrot.Core.Mission.GeoPoint>`; converts NED GeoPoint from `LocationPickerTool` callback to neutral GeoPoint
- `Hrot.Editor/Adapters/EditorMissionService.cs` — Updated `GetMissionSnapshot`, `CommitMissionAsync`, `SendControlCommandAsync` signatures to use neutral types; added `MapDomainPlanToNeutral` and `MapNeutralPlanToNed` helpers; removed `MapDomainPlanToNed`

### A7 — Project reference updates

Replaced `Hrot.UI.Common` / `Hrot.ScenarioEditor` references with `Hrot.Presentation` in:
- `Hrot.ClusterRunner/Hrot.ClusterRunner.csproj`
- `Hrot.ClusterRunner.Integration.Tests/Hrot.ClusterRunner.Integration.Tests.csproj`
- `Hrot.Editor/Hrot.Editor.csproj` (both old refs consolidated into one)
- `Hrot.ExCon/Hrot.ExCon.csproj`
- `Hrot.ExCon.Tests/Hrot.ExCon.Tests.csproj`
- `Hrot.IG/Hrot.IG.csproj`
- `Hrot.SimHost/Hrot.SimHost.csproj`
- `Hrot.ScenarioEditor.Tests/Hrot.ScenarioEditor.Tests.csproj` (kept for backward compat but removed from solution)

### A9 — Solution file

- `IOS-IG-SimHost.sln` — Removed `Hrot.ScenarioEditor`, `Hrot.ScenarioEditor.Tests`, `Hrot.UI.Common` project entries and configuration lines; added `Hrot.Presentation` (GUID `{D4E5F6A7-B8C9-0123-DEF0-123456789003}`) and `Hrot.Presentation.Tests` (GUID `{E5F6A7B8-C9D0-1234-EF01-234567890104}`) with full configuration entries

### Test files fixed (not in original instructions but required for build)

- `Hrot.ExCon.Tests/MissionPanelTests.cs` — Updated usings to `Hrot.Core.Mission`, fixed `.Value`/`.HasValue` accesses, replaced `with` expressions with direct property assignment
- `Hrot.ExCon.Tests/SpawnerPanelTests.cs` — Updated usings to `Hrot.Core.Mission`
- `Hrot.ExCon.Tests/TwoAckIosTests.cs` — Added `using Hrot.Core.Mission;`, updated one `MissionPlan?` reference
- `Hrot.Editor.Tests/Adapters/AdapterTests.cs` — Added `using Hrot.Core.Mission;`, updated `Task<GeoPoint>` types, updated `MissionPlan` construction, fixed `GeoPoint` disambiguation

---

## Build Result

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

---

## Test Results

| Project | Passed | Failed | Total |
|---|---|---|---|
| Hrot.Presentation.Tests | 16 | 0 | 16 |
| Hrot.ExCon.Tests | 325 | 0 | 325 |
| Hrot.Editor.Tests | 60 | 0 | 60 |
| Hrot.Core.Tests | 86 | 0 | 86 |

---

## Constraint Verification

`Hrot.Presentation` references verified (no Hrot.NED):
- `Hrot.Core`
- `Hrot.Map.Common`
- `Fdp.Core`
- `Fdp.Engine`
- `Fdp.Presentation`

---

## Deviations from Instructions

1. **Test files also updated** — `MissionPanelTests.cs`, `SpawnerPanelTests.cs`, `TwoAckIosTests.cs`, and `AdapterTests.cs` used NED types directly and had to be updated to use neutral types to compile. The `MissionPanelTests.cs` also used `with` record expressions and `.HasValue`/`.Value` on `MissionPlan` (which was formerly a struct but is now a class). These were rewritten using direct property assignment and null checks respectively.

2. **Source directories not deleted** — `Hrot.UI.Common/`, `Hrot.ScenarioEditor/`, and `Hrot.ScenarioEditor.Tests/` directories were not deleted (only removed from the solution). This is safe since they are no longer built by the solution. Deletion can be done in a cleanup batch to avoid accidental data loss.

3. **`Hrot.ScenarioEditor.Tests.csproj` updated** — Its `Hrot.ScenarioEditor` reference was replaced with `Hrot.Presentation` even though this project is not in the solution, so that any future re-addition would work correctly.
