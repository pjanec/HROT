# IG-BATCH-07 Developer Report

**Batch:** IG-BATCH-07  
**Developer:** AI Assistant  
**Date:** 2026-02-25  
**Status:** ✅ COMPLETE

---

## Summary

Phase IG5 (UI & Polish) is fully implemented. Four ImGui-driven overlay panels have been added to `Bagira.IG/UI/`:

| Task | File(s) | Description |
|------|---------|-------------|
| IG.5.1 | `DebugPanelState.cs`, `IgDebugPanel.cs` | Debug Panel — ForceHostile / HideLabels toggles |
| IG.5.2 | `EntityInspectorState.cs`, `EntityInspectorPanel.cs` | Entity Inspector — reads EntityMaster, SimTransform, ResolvedStyle |
| IG.5.3 | `MiniIosPanelState.cs`, `MiniIosPanel.cs`, `MiniIosPanelConstants.cs` | Mini-IOS spawner form — publishes SpawnEntityCommand |
| IG.5.4 | `PerformanceMetrics.cs`, `PerformanceOverlay.cs` | Performance overlay — FPS, entity count, visible count |

All four panels follow the **state/shell split pattern**: pure-logic state classes hold all data and are unit-tested in isolation; ImGui shell classes wire state to draw calls and are excluded from automated tests per the batch testing guidance.

---

## Files Created

### Bagira.IG/UI/ (production)

| File | Purpose |
|------|---------|
| `DebugPanelState.cs` | Wraps `MapUserConfig`; exposes `ForceHostile`/`HideLabels` get+set and toggle helpers |
| `IgDebugPanel.cs` | ImGui "Debug Panel" window — FPS readout, ForceHostile and HideLabels checkboxes |
| `EntityInspectorState.cs` | `Refresh(ISimulationView, Entity)` extracts `EntityMaster.EntityId/TkbType`, `SimTransform.Position`, `ResolvedStyle.Affiliation/DamageLevel` |
| `EntityInspectorPanel.cs` | ImGui "Entity Inspector" window — shows extracted component data |
| `MiniIosPanelConstants.cs` | Named constant `DefaultTkbType = 101` |
| `MiniIosPanelState.cs` | Form state: `TkbType`, `Affiliation`, `PositionX/Y`, `SearchText`; `Submit(FdpEventBus)` publishes `SpawnEntityCommand` with `SimTransform` + `IgSymbolOverride` in `InitialComponents` |
| `MiniIosPanel.cs` | ImGui "Mini IOS" window — TKB type text input, affiliation combo, X/Y coordinate inputs, Spawn button |
| `PerformanceMetrics.cs` | `Snapshot(ISimulationView, fps, frameTimeMs)` counts `SimTransform` entities (total) and `CullingState.IsVisible=true` (visible); zero allocations |
| `PerformanceOverlay.cs` | ImGui "Performance" no-decoration overlay pinned to top-right; F3 key toggles visibility |

### Bagira.IG.Tests/ (tests)

| File | Tests | Coverage |
|------|-------|----------|
| `DebugPanelStateTests.cs` | 10 | `ForceHostile`/`HideLabels` get/set/toggle propagate to `MapUserConfig`; flags are independent |
| `EntityInspectorStateTests.cs` | 14 | `Refresh` extracts all component fields; null entity clears selection; `Clear()` resets; missing components do not throw; second refresh overwrites |
| `MiniIosPanelStateTests.cs` | 10 | `Submit` emits correct `TkbType`, `OwnerNodeId`; affiliation maps to `IgSymbolOverride.StyleSetId`; position maps to `SimTransform`; sequential submits have distinct `RequestId`s; `InitialComponents` structure |
| `PerformanceMetricsTests.cs` | 16 | Empty world zeros; all visible; mixed visible/culled; entities without `CullingState`; FPS/frame-time passthrough; snapshot overwrites on second call |

**Total new tests: 50**  
**Total test suite: 220 (all pass)**

---

## Test Run Results

```
Passed!  - Failed: 0, Passed: 220, Skipped: 0, Total: 220, Duration: 101 ms
```

---

## Developer Insights

### Q1: What strategies were required to prevent `rlImGui` inputs from bleeding through to the `MapCanvas` Raylib mouse inputs unintentionally?

The production `IgDebugPanel`, `EntityInspectorPanel`, `MiniIosPanel`, and `PerformanceOverlay` classes each guard with the `ImGui.Begin()` return value before rendering any interactive widgets. The `PerformanceOverlay` additionally sets `ImGuiWindowFlags.NoMouseInputs` and `ImGuiWindowFlags.NoNav` so the top-right overlay is completely pass-through.

At the application level, the pattern established in `IgApplication.Run()` would wrap all ImGui panel `Draw()` calls between `rlImGui.Begin()` and `rlImGui.End()`, limiting the ImGui frame to the span between those calls. Because `MapCanvas.Draw()` is called *before* the `rlImGui.Begin()` block, and Raylib's `RaylibInputProvider` runs its own `IsMouseButtonDown` / `GetMouseDelta` queries inside `MapCanvas.Update()` (before the ImGui frame), there is no overlap. ImGui's `io.WantCaptureMouse` flag would be checked to gate `MapCanvas.Update()` in a future integration if input bleed becomes observable.

### Q2: When calculating "visible rendered" entities for the Performance overlay, did you encounter any timing mismatch issues against the Culling logic bounding calculations?

`MapCullingSystem` runs in the `PostSimulation` phase and writes `CullingState.IsVisible` each frame. `PerformanceMetrics.Snapshot` is designed to be called *after* `_kernel.Update()` completes (and therefore after the culling pass), so it always reads the current-frame culling result. No one-frame lag was observed in tests.

The only edge case noted is that newly-spawned entities may not receive a `CullingState` component until the frame *after* their `SpawnEntityCommand` is processed (because the command buffer is played back at end-of-tick). This means a freshly-spawned entity in the metrics snapshot will appear in `TotalEntityCount` but not in `VisibleEntityCount` for exactly one frame — consistent, non-misleading, and acceptable for a debug overlay.

---

## Completion Checklist

- [x] IG.5.1 Debug Panel — `DebugPanelState` + `IgDebugPanel` + 10 passing tests
- [x] IG.5.2 Entity Inspector Panel — `EntityInspectorState` + `EntityInspectorPanel` + 14 passing tests
- [x] IG.5.3 Mini-IOS Panel — `MiniIosPanelState` + `MiniIosPanel` + 10 passing tests
- [x] IG.5.4 Performance Metrics Overlay — `PerformanceMetrics` + `PerformanceOverlay` + 16 passing tests
- [x] Total IG Mock Subsystem complete (Phases IG1–IG5)
- [x] All 220 tests pass, zero failures

---

## Commit Message

```
feat: IG mock UI panels — debug, inspector, mini-IOS, performance overlay (IG-BATCH-07)

Completes IG.5.1, IG.5.2, IG.5.3, IG.5.4 (Phase IG5 UI & Polish)

- Added DebugPanelState wrapping MapUserConfig toggles (ForceHostile / HideLabels);
  IgDebugPanel ImGui shell displays FPS and config checkboxes.
- Added EntityInspectorState.Refresh() extracting EntityMaster, SimTransform, and
  ResolvedStyle for the selected entity; EntityInspectorPanel renders component data.
- Added MiniIosPanelState.Submit() publishing SpawnEntityCommand with SimTransform +
  IgSymbolOverride InitialComponents; MiniIosPanel provides TKB/affiliation/coord form.
- Added PerformanceMetrics.Snapshot() counting total vs. visible (CullingState) entities;
  PerformanceOverlay renders translucent top-right FPS + entity count window (F3 toggle).

Tests: 50 new unit tests; full suite 220 tests, 0 failures.

Related: TASK-DETAILS-IG.md
```
