# BATCH-05 Report

**Batch:** BATCH-05  
**Developer:** GitHub Copilot  
**Tasks:** PACK2-A001 · PACK2-E004 · PACK2-R002  
**Date:** 2026-04-04

---

## Task Completion Table

| Sub-task | Description | Status |
|----------|-------------|--------|
| A.1 | Create `WorldResetEvent.cs` | ✅ Done |
| A.2 | Add `FlushForWorldReset()` to `StandardInteractionTool` | ✅ Done |
| A.3 | Write `WorldResetTests.cs` (2 tests) | ✅ Done |
| B.1 | Add `FDP.Toolkit.Scenario` reference to `Hrot.ScenarioEditor.csproj` | ✅ Done |
| B.2 | Create `ScenarioFileService.cs` | ✅ Done |
| B.3 | Modify `ScenarioEditorModule.cs` to wire `ScenarioFileService` | ✅ Done |
| B.4 | Write `ScenarioFileServiceTests.cs` (5 tests) | ✅ Done |
| C.1 | Create `PackRole.cs` enum in `Hrot.Map.Common` | ✅ Done |
| C.2 | Add `PackRole` parameter to `EntityStatesIngressPack` | ✅ Done |
| C.3 | Add `PackRole` parameter to `ActuatorIntentsEgressPack` | ✅ Done |
| C.4 | Extend `CgfApplication` with sim kernel and `Install()` API | ✅ Done |
| C.5 | Extend `CgfSubsystem.Initialize` to install 3 packs | ✅ Done |
| C.6 | Write `CgfSubsystemTests.cs` (1 test) | ✅ Done |

---

## Q&A

**Q1: Which test used for round-trip in E004? Was `SimTransform` auto-serializable?**

A custom `SaveablePosition` struct with `[ComponentId(220)]` was used for the round-trip test. `SimTransform` was NOT used — it was not explored, as a simpler dedicated test component was more appropriate for isolation. The `SaveablePosition` struct (float X, Y, Z fields) was directly auto-serializable by `FdpAutoSerializer`.

**Q2: Was `ScenarioHeader` a class or record? What were its required properties?**

`ScenarioHeader` is a **record** (positional record) defined as:
```csharp
public record ScenarioHeader(string SubsystemType, int SchemaVersion = 1);
```
It has one required positional parameter: `SubsystemType` (string). `SchemaVersion` defaults to 1. The batch instructions showed object-initializer syntax (`new ScenarioHeader { SubsystemType = "..." }`) which is incorrect for a record with positional parameters — the correct form is `new ScenarioHeader("Hrot.Scenario")`, which is what was implemented.

**Q3: Did `CgfApplication.Install()` need any additional `using` directives beyond those listed?**

Yes. The instructions listed `using ModuleHost.Core.Abstractions;` (for `IEcsModule`) and `using System.Collections.Generic;` (for `IReadOnlyList`). Additionally:
- `using ModuleHost.Core;` was already present in the file (for existing `ModuleHostKernel` usage) — no change needed.
- `using Fdp.Kernel;` was already present — no change needed.
- `System.Collections.Generic` was added for `IReadOnlyList<string>`.
- `ModuleHost.Core.Abstractions` was added for `IEcsModule` in `Install()`.

An additional deviation: `Participant` and `EventBus` were added as `internal` properties, but `Hrot.ClusterRunner` is a different assembly that was not in `Hrot.CGF`'s `InternalsVisibleTo` list. `Hrot.ClusterRunner` was added to `Hrot.CGF.csproj`'s `InternalsVisibleTo`.

**Q4: Were there any unexpected callers of `EntityStatesIngressPack` or `ActuatorIntentsEgressPack` constructors?**

No unexpected callers. Search results found:
- `EntityStatesIngressPack`: 2 callers, both in `Hrot.Map.Common.Tests/Replication/Egress/EntityStatesIngressPackTests.cs` — both updated.
- `ActuatorIntentsEgressPack`: 2 callers, both in `Hrot.SimHost.Tests/ActuatorIntentsEgressPackTests.cs` — both updated. `using Hrot.Map.Common;` was added to that test file for the `PackRole` enum.

---

## Test Counts

| Project | Before | After | Delta |
|---------|--------|-------|-------|
| `Hrot.ScenarioEditor.Tests` | 7 | 14 | +7 |
| `Hrot.SimHost.Tests` | 440 (1 fail) | 440 (1 fail) | 0 |
| `Hrot.Map.Common.Tests` | 99 | 99 | 0 |
| `Hrot.ClusterRunner.Tests` | 191 (3 fail) | 192 (3 fail) | +1 |

Notes:
- `Hrot.SimHost.Tests`: pre-existing failure `GeoSpatialEgressTranslatorTests.Dispose_AlsoCallsBaseDispose` was present before this batch.
- `Hrot.ClusterRunner.Tests`: 3 pre-existing failures (`OrchestratorSubsystemTests.PauseButton_WhenNotPaused_DispatchesPauseTime`, `SwitchTimeModeEchoLoopTests.PollIngress_ThenScanAndPublish_DoesNotEchoBack`, `OrchestratorTimeModeTests.PendingTimeMode_Deterministic_PublishesSwitchTimeModeEvent`) were present before this batch.

---

## Build Result

**Result:** `Build succeeded` — **0 errors**, warnings only (all pre-existing in third-party or framework files).

---

## Deviations from Instructions

| Deviation | Reason |
|-----------|--------|
| `repo.Clear()` → `repo.SoftClear()` in `ScenarioFileService` | `EntityRepository.Clear()` is `internal`, not public. `SoftClear()` is the public equivalent that resets entity slots and clears the event bus. |
| Added `Hrot.ClusterRunner` to `InternalsVisibleTo` in `Hrot.CGF.csproj` | Internal `Participant` and `EventBus` accessors on `CgfApplication` are not accessible from `Hrot.ClusterRunner` without this. The instructions said "internal" but the assembly boundary required explicit opt-in. |
| `ScenarioHeader` uses positional record constructor, not object initializer | Instructions showed `new ScenarioHeader { SubsystemType = "Hrot.Scenario" }` but the actual type is a positional record; used `new ScenarioHeader("Hrot.Scenario")` instead. |
| Used `[ComponentId(220)]` on test `SaveablePosition` | The `EntityRepository.RegisterComponent<T>()` requires `[ComponentId]` on all component types (validated at registration). Component ID 220 is above the test range 210–219 used by `FDP.Toolkit.Scenario.Tests`, avoiding collisions. |

---

## Files Created / Modified

### New files
- `Hrot.ScenarioEditor/Events/WorldResetEvent.cs`
- `Hrot.ScenarioEditor/Services/ScenarioFileService.cs`
- `Hrot.Map.Common/PackRole.cs`
- `Hrot.ScenarioEditor.Tests/WorldResetTests.cs`
- `Hrot.ScenarioEditor.Tests/ScenarioFileServiceTests.cs`
- `Hrot.ClusterRunner.Tests/CgfSubsystemTests.cs`

### Modified files
- `Hrot.ScenarioEditor/Tools/StandardInteractionTool.cs` — added `FlushForWorldReset()` method
- `Hrot.ScenarioEditor/ScenarioEditorModule.cs` — added `ScenarioFileService?` constructor and `FileService` property
- `Hrot.ScenarioEditor/Hrot.ScenarioEditor.csproj` — added `FDP.Toolkit.Scenario` project reference
- `Hrot.Map.Common/Translators/EntityStatesIngressPack.cs` — added `PackRole` first parameter + validation guard
- `Hrot.SimHost/Translators/ActuatorIntentsEgressPack.cs` — added `PackRole` first parameter + validation guard
- `Hrot.Map.Common.Tests/Replication/Egress/EntityStatesIngressPackTests.cs` — updated 2 callers to pass `PackRole.Ingress`
- `Hrot.SimHost.Tests/ActuatorIntentsEgressPackTests.cs` — updated 2 callers to pass `PackRole.Egress`, added `using Hrot.Map.Common;`
- `Hrot.CGF/CgfApplication.cs` — added sim kernel fields, `Install()`, `InstalledModuleNames`, `Participant`, `EventBus`, lazy-init in `Tick()`, disposal in `Dispose()`
- `Hrot.CGF/Hrot.CGF.csproj` — added `InternalsVisibleTo` for `Hrot.ClusterRunner`
- `Hrot.ClusterRunner/Services/CgfSubsystem.cs` — rewrote `Initialize()` to install 3 packs
