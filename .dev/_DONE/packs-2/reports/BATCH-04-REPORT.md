# BATCH-04 Report

**Batch:** BATCH-04  
**Tasks:** PACK2-E002, PACK2-E003  
**Date completed:** 2026-04-04  
**Developer:** GitHub Copilot (Claude Sonnet 4.6)

---

## Task Completion

| Task | Status | Notes |
|------|--------|-------|
| Prerequisite: Move `SelectionState` to `Hrot.Map.Common` | ✅ Complete | Keeps `Hrot.IG.Components` namespace |
| Prerequisite: Move `CullingState`, `CullingStateConstants`, `ResolvedStyle`, `ResolvedStyleConstants` to `Hrot.Map.Common` | ✅ Complete | Required by `NedVisualizerAdapter` — see Q4 |
| PACK2-E002: Move 10 tool files to `Hrot.ScenarioEditor/Tools/` | ✅ Complete | Namespace updated to `Hrot.ScenarioEditor.Tools` |
| PACK2-E002: Update `Hrot.ScenarioEditor.csproj` (NuGet + project refs) | ✅ Complete | Added Raylib-cs, rlImgui-cs, NLog, FDP.Toolkit.ImGui, FDP.Toolkit.Replication, FDP.Toolkit.Behavior |
| PACK2-E002: Add `Hrot.ScenarioEditor` ref to `Hrot.IG.csproj` | ✅ Complete | |
| PACK2-E002: Update using directives in `Hrot.IG` consumers | ✅ Complete | 3 files in Hrot.IG + 10 test files |
| PACK2-E002: Add `InternalsVisibleTo` to `Hrot.ScenarioEditor.csproj` | ✅ Complete | Required for `TestHook_*` members in MeasureTool/StandardInteractionTool |
| PACK2-E002: Write `ToolPresenceTests.cs` | ✅ Complete | 2 new tests |
| PACK2-E003: Move 5 rendering files to `Hrot.ScenarioEditor/Rendering/` | ✅ Complete | Namespace updated to `Hrot.ScenarioEditor.Rendering` |
| PACK2-E003: Move 4 adapter files to `Hrot.ScenarioEditor/Adapters/` | ✅ Complete | Namespace updated to `Hrot.ScenarioEditor.Adapters` |
| PACK2-E003: Update `using Hrot.IG.Adapters` in `SelectionRenderSystem.cs` | ✅ Complete | |
| PACK2-E003: Update consumers in `Hrot.IG` and `Hrot.IG.Tests` | ✅ Complete | IgApplication.cs + 6 test files |
| PACK2-E003: Write `RenderLayerPresenceTests.cs` | ✅ Complete | 3 new tests |
| Build `IOS-IG-SimHost.sln --no-incremental` → 0 errors | ✅ Pass | |

---

## Test Counts

| Suite | Before BATCH-04 | After BATCH-04 | Delta |
|-------|-----------------|----------------|-------|
| `Hrot.IG.Tests` | 408 passed, 7 failed | 408 passed, 7 failed | None (pre-existing failures unchanged) |
| `Hrot.Map.Common.Tests` | 99 passed, 0 failed | 99 passed, 0 failed | None |
| `Hrot.ScenarioEditor.Tests` | 2 passed, 0 failed | 7 passed, 0 failed | +5 new tests (2 ToolPresence + 3 RenderLayerPresence) |

Pre-existing `Hrot.IG.Tests` failures (untouched by BATCH-04):
- 6× `UniqueNameGeneratorTests` (all subtests of `GetMaxIndex_*` and `CreateSessionGenerator_*`)
- 1× `TraceLoggingTests.IngressAndRender_EmitsTraceLines`

---

## Q1: Which files in `Hrot.IG` referenced the old `Hrot.IG.Tools` namespace? How many `using` directives needed updating?

**Files in `Hrot.IG`:**
- `IgApplication.cs` — `using Hrot.IG.Tools;` replaced with `using Hrot.ScenarioEditor.Tools;` + alias `StandardInteractionTool` updated + `Hrot.IG.Tools.MeasureTool` fully-qualified reference updated
- `Systems/MapCommandController.cs` — `using Hrot.IG.Tools;` → `using Hrot.ScenarioEditor.Tools;`
- `UI/WaypointEditorPanel.cs` — `using Hrot.IG.Tools;` → `using Hrot.ScenarioEditor.Tools;`

**Files in `Hrot.IG.Tests`:**
- `AdvancedFeaturesIntegrationTests.cs`, `CreationToolTests.cs`, `EditToolTests.cs`, `IgApplicationTests.cs`, `MapCommandControllerTests.cs`, `MapEventTranslatorTests.cs`, `MeasureToolTests.cs`, `RouteEditToolTests.cs`, `ToolInteractionIntegrationTests.cs`, `WaypointEditorPanelTests.cs`

**Total: 3 `using` directives in `Hrot.IG` + 10 in `Hrot.IG.Tests` = 13 using directive updates for Tools alone.**

For adapters (`Hrot.IG.Adapters` → `Hrot.ScenarioEditor.Adapters`):
- `IgApplication.cs` in `Hrot.IG`
- 6 test files in `Hrot.IG.Tests`: `MapEventTranslatorTests.cs`, `NedVisualizerAdapterTests.cs`, `StandardInteractionToolTests.cs`, `StubVisualizerAdapterTests.cs`, `ToolInteractionIntegrationTests.cs`, `TraceLoggingTests.cs`

For rendering (`Hrot.IG.Systems` → `Hrot.ScenarioEditor.Rendering`):
- `IgApplication.cs` (added `using Hrot.ScenarioEditor.Rendering;`, kept `using Hrot.IG.Systems;` for non-moved types)
- `Hrot.IG.Tests/RouteRenderLayerTests.cs` (replaced)
- `Hrot.IG.Tests/SelectionRenderSystemTests.cs` (replaced)

---

## Q2: Were any tools or render layers importing `Hrot.NED` or `CycloneDDS` types?

No. Scanning all 19 moved files (10 tools + 5 rendering + 4 adapters):
- None had `using Hrot.NED.*` or `using CycloneDDS.*` directives.
- The `NedVisualizerAdapter` (in `SstVisualizerAdapter.cs`) references DDS-aware types only through injected `ISimulationView` abstractions — the adapter does not depend on CycloneDDS or Hrot.NED directly.
- Confirmed: `Hrot.ScenarioEditor.csproj` has **no** direct `Hrot.NED` reference.

---

## Q3: Did `FDP.Toolkit.Behavior.Components` need to be added to `Hrot.ScenarioEditor.csproj`? Did this introduce transitive `Hrot.NED` references?

**Yes**, `FDP.Toolkit.Behavior.Components` was needed — `MissionRenderLayer.cs` uses `using FDP.Toolkit.Behavior.Components;` for mission-state components.

`FDP.Toolkit.Behavior.csproj` was added to `Hrot.ScenarioEditor.csproj`:
```xml
<ProjectReference Include="..\FDP\Toolkits\FDP.Toolkit.Behavior\FDP.Toolkit.Behavior.csproj" />
```

A scan of `FDP.Toolkit.Behavior.csproj` references confirms it does **not** reference `Hrot.NED` or `CycloneDDS.Runtime` directly. No transitive `Hrot.NED` dependency was introduced.

Additional references added (needed by `RouteRenderLayer.cs` and `StubVisualizerAdapter.cs`):
- `FDP.Toolkit.ImGui` (for `FDP.Toolkit.ImGui.Abstractions` used by `RouteRenderLayer`)
- `FDP.Toolkit.Replication` (for `FDP.Toolkit.Replication.Components` used by `RouteRenderLayer` and `StubVisualizerAdapter`)

---

## Q4: Were there any other IG-specific component types (beyond `SelectionState`) that needed moving to `Hrot.Map.Common`?

**Yes — 4 additional components were moved:**

| Component | Reason |
|-----------|--------|
| `CullingState.cs` | Used by `NedVisualizerAdapter` (`SstVisualizerAdapter.cs`) |
| `CullingStateConstants.cs` | Used by `NedVisualizerAdapter` |
| `ResolvedStyle.cs` | Used by `NedVisualizerAdapter` for visual rendering state |
| `ResolvedStyleConstants.cs` | Used by `NedVisualizerAdapter` |

All 4 were copied to `Hrot.Map.Common/Components/` keeping the `Hrot.IG.Components` namespace (same pattern as `SelectionState`). The originals in `Hrot.IG/Components/` were replaced with comment stubs.

Two additional fixes were required:
1. **`IgCameraConstants.InitialZoom`** referenced in `SstVisualizerAdapterConstants.cs` and `StubVisualizerConstants.cs`. Since `IgCameraConstants` lives in the `Hrot.IG` assembly (which ScenarioEditor cannot reference without a circular dependency), the references were replaced with the literal value `0.5f` with an explanatory comment.
2. **`Components.SelectionState`** in `SstVisualizerAdapter.cs` was a namespace-qualified reference that only works within the `Hrot.IG` assembly scope. Updated to plain `SelectionState` (accessible via `using Hrot.IG.Components;`).

`Hrot.Map.Common.csproj` required `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` to host `ResolvedStyle`, which contains `fixed byte` buffers.

---

## Files Created / Modified / Deleted

### New files (moved destinations)
| File | Change |
|------|--------|
| `Hrot.Map.Common/Components/SelectionState.cs` | New — moved from IG, keeps `Hrot.IG.Components` namespace |
| `Hrot.Map.Common/Components/CullingState.cs` | New — moved from IG (Q4) |
| `Hrot.Map.Common/Components/CullingStateConstants.cs` | New — moved from IG (Q4) |
| `Hrot.Map.Common/Components/ResolvedStyle.cs` | New — moved from IG (Q4) |
| `Hrot.Map.Common/Components/ResolvedStyleConstants.cs` | New — moved from IG (Q4) |
| `Hrot.ScenarioEditor/Tools/CreationTool.cs` | New — moved from IG |
| `Hrot.ScenarioEditor/Tools/CreationToolConstants.cs` | New |
| `Hrot.ScenarioEditor/Tools/EditTool.cs` | New |
| `Hrot.ScenarioEditor/Tools/EditToolConstants.cs` | New |
| `Hrot.ScenarioEditor/Tools/RouteEditTool.cs` | New |
| `Hrot.ScenarioEditor/Tools/RouteEditToolConstants.cs` | New |
| `Hrot.ScenarioEditor/Tools/MeasureTool.cs` | New |
| `Hrot.ScenarioEditor/Tools/MeasureToolConstants.cs` | New |
| `Hrot.ScenarioEditor/Tools/StandardInteractionTool.cs` | New |
| `Hrot.ScenarioEditor/Tools/StandardInteractionToolConstants.cs` | New |
| `Hrot.ScenarioEditor/Rendering/MapOverlayRenderLayer.cs` | New — moved from IG |
| `Hrot.ScenarioEditor/Rendering/RouteRenderLayer.cs` | New |
| `Hrot.ScenarioEditor/Rendering/MissionRenderLayer.cs` | New |
| `Hrot.ScenarioEditor/Rendering/SelectionRenderSystem.cs` | New |
| `Hrot.ScenarioEditor/Rendering/SelectionRenderConstants.cs` | New |
| `Hrot.ScenarioEditor/Adapters/SstVisualizerAdapter.cs` | New — moved from IG |
| `Hrot.ScenarioEditor/Adapters/SstVisualizerAdapterConstants.cs` | New |
| `Hrot.ScenarioEditor/Adapters/StubVisualizerAdapter.cs` | New |
| `Hrot.ScenarioEditor/Adapters/StubVisualizerConstants.cs` | New |
| `Hrot.ScenarioEditor.Tests/ToolPresenceTests.cs` | New — 2 reflection tests |
| `Hrot.ScenarioEditor.Tests/RenderLayerPresenceTests.cs` | New — 3 reflection tests |

### Modified files (stubs / using updates / csproj)
| File | Change |
|------|--------|
| `Hrot.IG/Components/SelectionState.cs` | Replace with comment stub |
| `Hrot.IG/Components/CullingState.cs` | Replace with comment stub |
| `Hrot.IG/Components/CullingStateConstants.cs` | Replace with comment stub |
| `Hrot.IG/Components/ResolvedStyle.cs` | Replace with comment stub |
| `Hrot.IG/Components/ResolvedStyleConstants.cs` | Replace with comment stub |
| `Hrot.IG/Hrot.IG.csproj` | Add `Hrot.ScenarioEditor` ProjectReference |
| `Hrot.IG/IgApplication.cs` | Update using directives (Tools, Adapters, Rendering); fix fully-qualified refs |
| `Hrot.IG/Systems/MapCommandController.cs` | `using Hrot.IG.Tools` → `using Hrot.ScenarioEditor.Tools` |
| `Hrot.IG/UI/WaypointEditorPanel.cs` | Same using update |
| `Hrot.Map.Common/Hrot.Map.Common.csproj` | Add `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` |
| `Hrot.ScenarioEditor/Hrot.ScenarioEditor.csproj` | Add NuGet packages + project refs + InternalsVisibleTo |
| `Hrot.ScenarioEditor.Tests/Hrot.ScenarioEditor.Tests.csproj` | Add Hrot.IG ProjectReference for reflection tests |
| `Hrot.IG.Tests/*.cs` (17 files) | Update using directives for moved namespaces |

### Deleted (source moved)
All 10 `Hrot.IG/Tools/*.cs` files deleted (sources now in `Hrot.ScenarioEditor/Tools/`).  
5 `Hrot.IG/Systems/` rendering files deleted.  
4 `Hrot.IG/Adapters/` files deleted.

---

## Suggested Commit Message

```
feat(packs-2): PACK2-E002+E003 — migrate tools and render layers to Hrot.ScenarioEditor

Move core interaction tools, rendering layers, and visualizer adapters from Hrot.IG
to Hrot.ScenarioEditor, making Hrot.IG a host-specific composition root.

Changes:
- Move SelectionState, CullingState, ResolvedStyle (+Constants) to Hrot.Map.Common
  (Hrot.IG.Components namespace preserved; comment stubs left in Hrot.IG)
- Move 10 tool files to Hrot.ScenarioEditor/Tools/ (namespace: Hrot.ScenarioEditor.Tools)
- Move 5 render layer files to Hrot.ScenarioEditor/Rendering/
- Move 4 adapter files to Hrot.ScenarioEditor/Adapters/
- Add Raylib-cs, rlImgui-cs, NLog, FDP.Toolkit.ImGui/Replication/Behavior to ScenarioEditor.csproj
- Add InternalsVisibleTo Hrot.IG.Tests/ScenarioEditor.Tests to ScenarioEditor.csproj
- Update all using directives in Hrot.IG and Hrot.IG.Tests
- Add ToolPresenceTests and RenderLayerPresenceTests reflection tests

Build: 0 errors. Hrot.IG.Tests: 408/415 pass (7 pre-existing failures unchanged).
Hrot.ScenarioEditor.Tests: 7/7 pass. Hrot.Map.Common.Tests: 99/99 pass.
```
