# BATCH-03 Report

**Batch:** BATCH-03  
**Developer:** GitHub Copilot (Claude Sonnet 4.6)  
**Date:** 2025-07-25  
**Status:** Complete

---

## 📊 Task Completion

| Task ID     | Status | Notes |
|-------------|--------|-------|
| TASK-P3-001 | ✅     | Canvas Renderer — all 9 canvas files created |
| TASK-P3-002 | ✅     | Mini-Editors — all 15 editor files created |

---

## 🧪 Testing Results

**Unit Tests Passed:** 59 / 59  
**Integration Tests Passed:** N/A (UI tests have no test cases yet)

**Build:**  
- `dotnet build` → 0 errors, 0 warnings (TreatWarningsAsErrors=true, NoWarn=1591)

**Key Test Scenarios Verified:**
- [x] All 59 pre-existing tests in `NodeEditor.Core.Tests` continue to pass
- [x] `NodeEditor.UI` project builds cleanly against ImGui.NET 1.91.6.1
- [x] Extension methods (`ImDrawListExtensions`) resolve correctly from Canvas files
- [x] `PinDefaultValueEditorRegistry.CreateWithBuiltins()` compiles with 10 registered editors

---

## 📁 Files Created

### Utility (2)
| File | Purpose |
|------|---------|
| `src/NodeEditor.UI/Util/ImGuiPushIdScope.cs` | RAII PushID/PopID wrapper for per-item ID scoping |
| `src/NodeEditor.UI/Util/ImDrawListExtensions.cs` | `AddBezierWithArrow`, `AddCircleFilledOutline` |

### Mini-Editors (15)
| File | Editor |
|------|--------|
| `MiniEditors/DragFloatWithExpression.cs` | Float/int drag with expression eval on double-click |
| `MiniEditors/BoolPinEditor.cs` | Checkbox, commits on click |
| `MiniEditors/IntPinEditor.cs` | DragInt with expression, range clamp |
| `MiniEditors/FloatPinEditor.cs` | DragFloat with expression, units suffix, range clamp |
| `MiniEditors/StringPinEditor.cs` | InputText, Enter/focus-out commits |
| `MiniEditors/EnumPinEditor.cs` | Combo from IEnumValueProvider (fallback: DragInt) |
| `MiniEditors/VectorPinEditor.cs` | 2/3/4-component drag fields |
| `MiniEditors/QuaternionPinEditor.cs` | Yaw/Pitch/Roll in degrees → Quaternion |
| `MiniEditors/ColorPinEditor.cs` | Swatch button opens ColorPicker4 popup |
| `MiniEditors/GuidPinEditor.cs` | Truncated display button (`xxxx…xxxx`) |
| `MiniEditors/EntityPinEditor.cs` | Button scaffold (host overrides with picker) |
| `MiniEditors/AssetPinEditor.cs` | Button scaffold (host overrides with picker) |
| `MiniEditors/StructPinEditor.cs` | Read-only type label (split-mode handled by canvas) |
| `MiniEditors/ArrayPinEditor.cs` | Count button → popup with per-element edit + add/remove |
| `MiniEditors/PinDefaultValueEditorRegistry.cs` | `IPinDefaultValueEditorRegistry` impl + `CreateWithBuiltins()` |

### Canvas (8)
| File | Purpose |
|------|---------|
| `Canvas/CanvasLayout.cs` | Per-frame layout data + builder (node screen rects, pin positions, connected-pin set) |
| `Canvas/HitTester.cs` | Hover update: reroutes → pins → wires → comment zones → nodes |
| `Canvas/GridRenderer.cs` | Minor/major dot grid (skips minor below zoom 0.35) |
| `Canvas/WireRenderer.cs` | Cubic bezier wires, exec arrowheads, reroute dots, selected/hovered highlight |
| `Canvas/PinRenderer.cs` | Pin glyphs (circle/exec-triangle), labels, type colors |
| `Canvas/NodeRenderer.cs` | Header strip, title, state overlays, selection outline, inline editors |
| `Canvas/CanvasInput.cs` | Full interaction state machine (pan, drag-nodes, marquee, wire, reroutes, comments, delete) |
| `Canvas/CanvasRenderer.cs` | Orchestrator: layout → hit-test → input → 10-phase draw pipeline |

---

## 📝 Developer Insights

**Q1: What issues did you encounter during implementation? How did you resolve them?**

The task brief specified `IPinDefaultValueEditor.Render(in PinDefaultEditorContext ctx, ref object? value)` but the kernel already had a different signature: `Draw(ref object? value, DefaultEditorContext ctx, out bool committed)`. All editors were implemented using the kernel-authoritative interface. The `DefaultEditorContext` record does not carry host services (no `IEditorHostServices`), which means `EnumPinEditor`, `EntityPinEditor`, and `AssetPinEditor` can't access pickers or enum value providers through the editor context alone — they accept optional constructor parameters instead, allowing hosts to register richer instances.

**Q2: Did you spot any weak points in the existing codebase? What would you improve?**

- `DefaultEditorContext` lacks host service access, forcing workarounds for enum/entity/asset editors. A future pass could add an `IServiceProvider? Services` field or a dedicated `PickerInvoker` delegate.  
- `IGraphModel` has no caching/version token. The canvas rebuilds the spatial index every frame from scratch. For 2000+ nodes this is acceptable but a dirty-flag would eliminate the O(N) rebuild.
- The `CanvasLayoutBuilder` computes text-independent node widths (fixed 160 gu). A proper implementation should call `ImGui.CalcTextSize` on title/pin labels and use the max. This was intentionally deferred.

**Q3: What design decisions did you make beyond the instructions?**

- Split `CanvasRenderer` into a `CanvasLayout`/`CanvasLayoutBuilder` type to make the layout data clearly separate from the draw phases. This made it straightforward to pass pin positions between `HitTester`, `WireRenderer`, and `NodeRenderer` without parameter proliferation.
- `DragFloatWithExpression` uses double-click (not ctrl+click) to enter expression mode. Ctrl+click is already consumed internally by ImGui's `DragFloat` to enter its own numeric text-edit mode. Double-click is the cleaner trigger that avoids the conflict.
- Wire rendering passes `segments=0` to `AddBezierCubic` (ImGui auto-segments), which adapts to zoom level.

**Q4: What edge cases did you discover that weren't mentioned in the spec?**

- Reroute waypoints on a link have no standalone entity ID — they use `RerouteRef(LinkId, WaypointIndex)`. This means the hit-tester must iterate all links × all waypoints each frame (cheap for typical graphs).
- When the mouse exits the canvas child window while panning, `IsWindowHovered()` returns false but the pan should continue. Current implementation stops panning on mouse release regardless — the `IInputSource` is queried directly so release outside the window still fires.
- The `with` expression on `Vector4` for alpha adjustment (`color with { W = 0.15f }`) required no changes — it's supported in C# 12 record structs via `init` setters on `System.Numerics.Vector4` components. This compiled correctly.

**Q5: Are there any performance concerns or optimization opportunities you noticed?**

- `view.Model.Links.Any(l => l.FromPin == pin.Id)` in `PinRenderer` is O(links) per output pin. The `CanvasLayout` pre-computes only connected *input* pins; a second `HashSet<PinId>` for connected output pins would bring this to O(1).
- `DrawComments` calls `.ToList()` + `.Sort()` every frame. Comment count is typically low (< 20), so this is negligible.
- The `DragFloatWithExpression` state dictionary is never pruned. Widgets that are no longer rendered accumulate dead entries. A bounded-LRU or frame-stamp eviction would be ideal for long-running sessions.

---

## ⚠️ Outstanding Items / Next Steps

- [ ] **Inline editor width**: currently fixed at `EditorWidthGu * zoom`. Should be computed from actual pin-label text width so editors fill the available space between label and right edge.
- [ ] **Node auto-width**: `NodeMinWidthGu=160` is used for all nodes. A proper pass measures title text and the widest pin-label pair to determine the correct node width.
- [ ] **Context menu / picker**: `InteractionMode.PickerOpen` is enumerated but the transition into it (right-click on empty canvas → node-picker) is not wired up in `CanvasInput`. This requires the `IPickerRegistry` integration.
- [ ] **IEnumValueProvider in editors**: `EnumPinEditor`, `EntityPinEditor`, `AssetPinEditor` show fallback UI. Hosts must re-register type-specific editors with provider references for full UX.
- [ ] **Undo integration**: `CanvasInput.DeleteSelected` and inline editor commits call `view.Commands.Apply()` directly. They should be routed through `view.Undo.ApplyAndRecord()` once the UndoStack wrapper is confirmed in scope.
