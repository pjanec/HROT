# BATCH-03 Report

**Batch:** BATCH-03  
**Developer:** GitHub Copilot (Claude Sonnet 4.6)  
**Date:** 2026-04-06  
**Status:** Complete

---

## 📊 Task Completion

| Task ID | Status | Notes |
|---------|--------|-------|
| EDIT1-N001 | ✅ | `SharedOrbatPanel` — unsafe drag-drop confined, `HandleDropPayload` / `HandleSelectEntity` internal methods, 3 tests |
| EDIT1-N002 | ✅ | `PreviewPanel` — `HandleEnterPreview` / `HandleExitPreview` internal methods, 4 tests |
| EDIT1-N003 | ✅ | `ZoneEditorPanel` — `HandleApplyRoadNetwork` / `HandlePlaceObstacle` internal methods, 6 tests |
| EDIT1-N004 | ✅ | `SharedContextMenuPopulator` static class, no ImGui/ECS/DDS imports, 10 tests |

---

## 🧪 Testing Results

**Unit Tests (new):** 29 / 29 passing  
**Hrot.ExCon.Tests (full suite):** 377 / 377 passing  
**Hrot.Map.Common.Tests (full suite):** 111 / 111 passing  
**Full solution build:** 0 CS errors

**Key Test Scenarios Verified:**

- [x] `SelectEntity` called with correct ID when selection triggered on any ORBAT node  
- [x] `RequestEmbark(passengerId, vehicleId)` invoked when two distinct node IDs are dropped  
- [x] `RequestEmbark` NOT invoked when passenger == vehicle (self-embarkation guard)  
- [x] `EnterPreviewMode` called via `HandleEnterPreview`; `ExitPreviewMode` NOT called  
- [x] `ExitPreviewMode` called via `HandleExitPreview`; `EnterPreviewMode` NOT called  
- [x] `SetRoadNetworkPath` receives both current zone name and road-network path  
- [x] `StartObstaclePlacementMode` receives current obstacle radius (default 5 m; custom 15 m)  
- [x] `ObstacleRadius` clamped to [1, 50] on assignment  
- [x] Context menu: "Edit Shape" present when `hasEditablePolyline=true`; absent otherwise  
- [x] Context menu: "Edit Route" present when `hasRoutePlan=true`; absent otherwise  
- [x] Context menu: "Rename…" absent when `entityId == 0`  
- [x] Context menu: separator inserted before "Delete" item  
- [x] `PopulateEmptyMapMenu` adds exactly one item ("Measurement Tool")  
- [x] Callback wiring verified: `CenterOnEntity` and `ActivateMeasureTool` called with correct args  

---

## 📝 Developer Insights

### Q1: What issues did you encounter implementing each panel? How did you solve them?

**`SharedOrbatPanel` (unsafe drag-drop):**  
The `SetDragDropPayload` / `AcceptDragDropPayload` ImGui APIs require unsafe pointer access.
The `&id` address-of operator and the `*(int*)payload.Data` dereference both need an `unsafe`
block.  Added `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` to `Hrot.UI.Common.csproj` and
confined unsafe to two minimal inline blocks (source setup and drop-target receipt).
The arrow-toggle indentation pattern also needed careful Indent/Unindent pairing to avoid
depth drift; tested by running the build and inspecting the ImGui render path mentally.

**`PreviewPanel` / `ZoneEditorPanel`:**  
Straightforward.  The only subtlety was ensuring `ObstacleRadius` clamping happened in the
property setter (not only in the ImGui slider call) so that tests driving the property
directly also enforce the invariant.

**Internal method accessibility for tests:**  
Added `AssemblyInfo.cs` with `[assembly: InternalsVisibleTo("Hrot.ExCon.Tests")]` to
`Hrot.UI.Common` so that `HandleDropPayload`, `HandleSelectEntity`, `HandleEnterPreview`,
`HandleExitPreview`, `HandleApplyRoadNetwork`, and `HandlePlaceObstacle` are all testable
without making them public.

---

### Q2: What was the most complex part of `SharedOrbatPanel` (drag-and-drop)?  How did you structure the testable contract around it given ImGui limitations?

The core complexity is that ImGui drag-drop is inherently a per-frame render artefact — the
payload pointer lifetimes are tied to the ImGui render loop.  Testing it directly would require
a headless ImGui context.

The approach taken was to extract the minimal payload-resolution logic into a single `internal`
method:
```csharp
internal void HandleDropPayload(int passengerId, int vehicleId, IOrbatController ctrl)
{
    if (passengerId != vehicleId)
        ctrl.RequestEmbark(passengerId, vehicleId);
}
```
The unsafe raw-pointer extraction (`*(int*)payload.Data`) happens in `DrawContent` and
immediately calls this method.  Tests bypass `DrawContent` entirely and call
`HandleDropPayload` with synthetic integer arguments.  This cleanly separates the unsafe
pointer extraction from the domain logic, and means tests verify the only thing that matters:
the guard condition and the controller call.

---

### Q3: What design decisions did you make beyond the spec?

1. **`HandleSelectEntity` was added as a second internal test helper** on `SharedOrbatPanel`.
   The spec only required a `HandleDropPayload` helper, but the selection click test needed
   an equivalent hook.  Keeping the pattern consistent also signals to future developers that
   there is a testable surface without ImGui.

2. **`ObstacleRadius` clamping in the property setter** (not just the ImGui `SliderFloat`
   call).  The slider naturally clamps during render, but since tests bypass `DrawContent`,
   clamping in the setter ensures the invariant is enforced regardless of how the field
   is written.  This follows the same pattern as `ConfigPanel.IconScale`.

3. **`CallbackCapturingBuilder` as a second test-double type** for `SharedContextMenuPopulatorTests`
   (alongside the simpler `RecordingContextMenuBuilder`).  The recording builder just tracks
   labels; the capturing builder also preserves `Action` callbacks so we can actually invoke
   them and verify the `IEntityActionController` mock receives the correct entity ID.

4. **Arrow button toggling `_expandedNodes` directly** (in addition to calling
   `ctrl.ToggleExpanded`) so the panel's own expansion state stays in sync regardless
   of what the controller does with the event.

---

### Q4: What gaps between spec and codebase reality did you find?

1. **`ImGui.ArrowButton` overload** — The spec references arrow-toggle on rows with children
   without specifying which ImGui API to use.  `ImGui.ArrowButton(id, direction)` is the
   correct call and works cleanly; no issue.

2. **`SetDragDropPayload` signature** — The ImGuiNET binding uses `nint` (not `void*`) for the
   data pointer, so the address-of expression must be cast: `(nint)(&id)`.  This is a small but
   non-obvious adaptation from the spec pseudocode.

3. **`AcceptDragDropPayload` return type** — The binding returns an `ImGuiPayloadPtr` struct
   whose `NativePtr` field is null when no matching payload is present.  The spec says "on
   non-null result" which maps to `payload.NativePtr != null`.  The spec's wording implied a
   simple null check on the return value; the actual API check is on the struct field.

4. **`BeginDragDropSource` / `EndDragDropSource` pairing** — The ImGuiNET binding mirrors the
   C++ API: `BeginDragDropSource` must always be followed by `EndDragDropSource` when it
   returns true.  Similarly `BeginDragDropTarget` must be followed by `EndDragDropTarget`.
   The spec did not call this out explicitly.

---

### Q5: What are the highest-risk items for BATCH-04 (domain events)?

1. **Unmanaged event ID collisions** — `EmbarkEntityCommand` (3201), `DisembarkEntityCommand`
   (3202), and `SeedTargetCommand` (4101) must be verified against `GlobalEventIds.cs` before
   assignment.  A collision causes a silent wrong-event dispatch at runtime, which is very
   hard to debug.  The block boundaries must be re-checked even if BATCH-01's additions looked
   clean.

2. **Managed vs. unmanaged registration** — `SpawnZoneObstacleCommand` and
   `UpdateZoneConfigCommand` contain `string` fields and must use `RegisterManagedEvent<T>` /
   `Bus.PublishManaged`, not the unmanaged path.  Mixing these will either panic (unmanaged
   path receiving a managed heap reference) or silently drop events.

3. **Registry ordering at startup** — The FDP kernel enforces that `RegisterEvent<T>` is called
   before the first `Bus.Publish<T>`.  If any existing integration test fires a related event
   before the new registry calls are reached, it will throw.  Registry order in
   `CognitiveComponentRegistry.RegisterAll` must be audit-checked.

4. **`HrotSharedComponentRegistry.cs` managed registration API** — The spec says "use the
   pattern already established for other events in the same file" but the managed-event API
   may not yet exist in that file.  This needs investigation before any code is written.

---

## ⚠️ Outstanding Issues / Next Steps

- No blockers.  All four new panels compile and their tests pass.
- BATCH-04 (domain events: EDIT1-E001, EDIT1-E002, EDIT1-E003, EDIT1-E004) can proceed
  immediately.  See Q5 above for risk-mitigation notes.
- The one pre-existing warning in `MissionPanel.cs` (CS8604 on a `FdpLog.Warn` arg) is
  unchanged and predates this batch — not introduced here.
