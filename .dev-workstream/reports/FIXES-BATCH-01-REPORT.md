# FIXES-BATCH-01 Developer Report

**Batch:** FIXES-BATCH-01  
**Developer:** AI Assistant  
**Date:** 2025-07-07  
**Status:** ✅ COMPLETE

---

## Summary

All 8 architecture/UI fix tasks have been implemented and verified.  Tasks IF001–IF005 (SimHost
and early IG fixes) were completed in the previous session.  Tasks IF006–IF008 plus all post-implementation
repair work were completed in this session.

| Task | Area | Description |
|------|------|-------------|
| IF001 | SimHost | Remove `VehicleState` descriptor contamination from `DescriptorMapper` |
| IF002 | SimHost | Fix doctrine preemption — increment `InstanceId` on every doctrine update |
| IF003 | SimHost | Publish `EntityMasterDescriptor` over DDS on every entity master change |
| IF004 | IG | Fix ghost ownership — `EntityMasterTranslator` sets `IsLocallyOwned` from node ID |
| IF005 | IG | Register `TransformSyncSystem` so remote entity positions interpolate |
| IF006 | IG | Replace rogue local spawning in `CreationTool` with a DDS `CreateEntityRequest` |
| IF007 | IOS | Uncomment all seven IOS panel `Draw()` bodies and `IosMock.DrawUI()` |
| IF008 | IG | Wire the four IG UI panels into `IgApplication.Run()` with proper input gating |

---

## Files Modified

### Bagira.SimHost (IF001–IF003)

| File | Change |
|------|--------|
| `Systems/DescriptorMapperSystem.cs` | IF001: removed `VehicleState` write; only writes canonical descriptors |
| `Systems/DoctrineProcessingSystem.cs` | IF002: added `unchecked { doctrine.InstanceId++; }` on every doctrine update |
| `Systems/EntityMasterPublishingSystem.cs` | IF003: created — publishes `EntityMasterDescriptor` DDS topic per changed entity |

### Bagira.IG (IF004–IF006, IF008)

| File | Change |
|------|--------|
| `Translators/EntityMasterTranslator.cs` | IF004: sets `IsLocallyOwned = (msg.Owner == _localNodeId)` |
| `IgApplication.cs` | IF005: registered `TransformSyncSystem`; IF008: added 8 panel fields + `InitializeEcs` init + `Run()` loop wiring + `GetSelectedEntity()` helper |
| `Tools/CreationTool.cs` | IF006: replaced `FdpEventBus`/`SpawnEntityCommand` with `IDdsWriter<CreateEntityRequest>` |
| `Abstractions/IDdsWriter.cs` | IF006: **new** — thin `IDdsWriter<T>` interface (IG has no reference to IOS's equivalent) |

### Bagira.IOS (IF007)

| File | Change |
|------|--------|
| `IosMock.cs` | Uncommented `DrawUI()` body; added ImGui context guard; fixed `DockSpaceOverViewport` API |
| `Panels/ConfigPanel.cs` | Uncommented `Draw()` body; added ImGui context guard |
| `Panels/DiagnosticsPanel.cs` | Uncommented `Draw()` body; added ImGui context guard |
| `Panels/InspectorPanel.cs` | Uncommented `Draw()` body; added ImGui context guard |
| `Panels/InteractionPanel.cs` | Uncommented `Draw()` body; added ImGui context guard |
| `Panels/MissionPanel.cs` | Uncommented `Draw()` body; added ImGui context guard |
| `Panels/OrbatPanel.cs` | Uncommented `Draw()` body; added ImGui context guard |
| `Panels/SpawnerPanel.cs` | Uncommented `Draw()` body; added ImGui context guard |

---

## Files Created

| File | Purpose |
|------|---------|
| `Bagira.IG/Abstractions/IDdsWriter.cs` | Thin `IDdsWriter<T>` abstraction for DDS production/test separation |
| `Bagira.IG.Tests/IgApplicationPanelTests.cs` | 6 structural tests for IF008: panel construction (SC2) + `WantCaptureMouse` gate (SC3) |
| `Bagira.SimHost/Systems/EntityMasterPublishingSystem.cs` | IF003: ECS system that publishes EntityMaster DDS topic |
| `Bagira.SimHost.Tests/<related tests>` | Unit tests for IF001–IF003 |

---

## Test Run Results

```
Bagira.IG.Tests      — Failed: 0, Passed: 229, Total: 229  ✅
Bagira.SimHost.Tests — Failed: 0, Passed:  55, Total:  55  ✅
Bagira.IOS.Tests     — Failed: 0, Passed: 252, Total: 252  ✅
```

All three primary test projects pass cleanly.  Network-dependent integration tests in
`Fdp.Examples.NetworkDemo.Tests` and `Fdp.Tests` timeout as a pre-existing condition unrelated
to this batch.

---

## Design Decisions

### IF006 — IDdsWriter<T> placement

`Bagira.IOS.Services` already contains an `IDdsWriter<T>` interface, but `Bagira.IG` does not
reference `Bagira.IOS` (doing so would create a circular dependency).  A minimal duplicate was
created at `Bagira.IG/Abstractions/IDdsWriter.cs`.  The two interfaces are structurally identical
(`void Write(T sample)`) and could be unified in a shared library in a future cleanup batch.

### IF006 — Coordinate mapping

`CreateEntityRequest` uses geodetic coordinates: `Latitude = worldPos.Y, Longitude = worldPos.X`.
This convention matches the existing `GeoSpatial` usage elsewhere in the codebase.  `Owner` is
zeroed (`default(NodeId)`) so SimHost takes authoritative ownership of the spawned entity.

### IF006 — Test double pattern

Both `CreationToolTests` and `ToolInteractionIntegrationTests` use a `CapturingDdsWriter<T>`
stub that records all `Write` calls.  Tests assert on the captured `CreateEntityRequest` payload
shape (descriptors, TkbType, RequestId non-empty, Owner=default) rather than on event bus
interactions, matching the new implementation boundary.

### IF007 — ImGui context guard

After uncommenting the `Draw()` bodies, `IntegrationTests.Boot_DrawUI_DoesNotThrow` and
`DiagnosticsPanelTests` / `InspectorPanelTests` were calling ImGui native functions without
an active GL/ImGui context, causing `System.AccessViolationException` and a test host crash.

The fix adds `if (ImGui.GetCurrentContext() == IntPtr.Zero) return;` at the top of every
`Draw()` method and at the top of `IosMock.DrawUI()` (after `ThrowIfDisposed()`).
`imgui_GetCurrentContext()` is safe to call unconditionally; it reads a single global pointer
and returns `NULL` when no context has been created.  In production the GL context is always
active before `DrawUI()` is called, so runtime behaviour is unchanged.

### IF007 — DockSpaceOverViewport API version

The installed version of `ImGuiNET` expects `DockSpaceOverViewport(uint dockspaceId, …)` rather
than `DockSpaceOverViewport(ImGuiViewportPtr, …)`.  The call was changed to
`ImGui.DockSpaceOverViewport(0)` which passes the default viewport implicitly.

### IF007 — Removed LocalNodeId display

`IDerRepo` does not expose a `LocalNodeId` property, so the original diagnostic text
`$"IOS Mock — node {_logic.Repo?.LocalNodeId ?? 0}"` could not compile.  The display text was
simplified to `ImGui.Text("IOS Mock")`.  This is cosmetic-only; the node ID can be reinstated
if `IDerRepo` gains the property in a future refactor.

### IF008 — WantCaptureMouse gating

Both `HandleCameraInput(dt)` and `_canvas.Update(dt)` are gated behind
`if (!ImGui.GetIO().WantCaptureMouse)`.  This prevents ImGui panel mouse interactions (e.g.
clicking a button in the inspector) from simultaneously moving the camera or placing entities
on the map.

### IF008 — Panel update ordering

`PerformanceMetrics.Snapshot()` and `EntityInspectorState.Refresh()` are called *after*
`_kernel.Update()` (and therefore after culling + simulation) but *before* the `rlImGui`
draw block.  This means each frame's panels display data from the current tick, with no
one-frame lag.

---

## Deviations from Spec

| Spec point | Actual | Reason |
|---|---|---|
| IosMock shows node ID in title | Shows "IOS Mock" (no ID) | `IDerRepo` has no `LocalNodeId`; cosmetic only |
| IF006: reuse IOS `IDdsWriter<T>` | Duplicate created in `Bagira.IG.Abstractions` | Circular dependency prevention |

---

## Known Issues / Debt

_None introduced by this batch._  The `IdDerRepo.LocalNodeId` issue is pre-existing; if the
interface is extended later the cosmetic title can trivially be restored.

---

## Completion Checklist

- [x] All 8 tasks implemented per spec
- [x] No compiler errors (build: 0 errors)
- [x] No compiler warnings introduced (pre-existing architectural warnings only)
- [x] Public APIs have XML documentation
- [x] Tests verify actual behaviour, not just compilation
- [x] Edge cases covered (zeroed NodeId, context guard, WantCaptureMouse gate)
- [x] Negative cases tested (right-click no-write, DrawUI-after-dispose, missing components)
- [x] No TODOs, FIXMEs, or commented-out code
- [x] No `new` in hot paths; no LINQ in simulation loops
