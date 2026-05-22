# BATCH-03: Canvas Renderer + Mini-Editors

**Batch Number:** BATCH-03  
**Tasks:** TASK-P3-001, TASK-P3-002  
**Phase:** Phase 3 — Canvas & Inline Editors  
**Estimated Effort:** 10–14 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 + BATCH-02 (completed)

---

## 📋 Onboarding & Workflow

### Developer Instructions

Phase 3 is the first ImGui-using phase. You're building the entire visual canvas and all the inline
pin editors. After this batch, the editor is visually runnable. These files live in `NodeEditor.UI`.

The canvas renderer is the largest file (~700 LOC). Split your work:
1. First get the scaffolding/structure compiling.
2. Then get node/pin rendering working.
3. Then wire up the input state machine.
4. Finally, implement the mini-editors.

### Required Reading (IN ORDER)

1. **Ground Rules + Solution Layout:** `.dev/final/instructions/00-START-HERE.md`
2. **Task Brief — Canvas Renderer:** `.dev/final/instructions/03-04-task-canvas-renderer.md` (**read the full file**)
3. **Task Brief — Mini-Editors:** `.dev/final/instructions/03-05-task-mini-editors.md` (**read the full file**)
4. **Spec Brief §6–12:** `.dev/final/instructions/01-spec-brief.md` (canvas, nodes, pins, wires, mini-editors, state machine)
5. **Spec Brief Part 2 §17, §28, §29:** `.dev/final/instructions/01-spec-brief-part2.md`
6. **Canvas Interactions Spec:** `.dev/final/specs/A-canvas-interactions.md` (normative — state machine transition table)
7. **Mini-Editors Spec:** `.dev/final/specs/B-mini-editors.md` (full catalog)
8. **Previous Review:** `.dev/final/reviews/BATCH-02-REVIEW.md`

### Source Code Location

- **Work area:** `src/NodeEditor.UI/Canvas/`, `src/NodeEditor.UI/Util/`, `src/NodeEditor.UI/MiniEditors/`
- **Existing Core:** `src/NodeEditor.Core/` — do not modify except `Interfaces/` if a new interface is needed
- **Tests:** There are no required unit tests for this batch (UI tested via demo in T-20). `dotnet test` must still pass all 59 previous tests.

### Report Submission

**When done, submit your report to:** `.dev/final/reports/BATCH-03-REPORT.md`  
**Questions:** `.dev/final/questions/BATCH-03-QUESTIONS.md`

---

## Context

After this batch:
- `CanvasRenderer.Render(view)` draws the full canvas — nodes, pins, wires, grid, marquee, pending wire.
- The input state machine handles all mouse/keyboard gestures per `A-canvas-interactions.md`.
- All 14 built-in pin editors are registered in `PinDefaultValueEditorRegistry.CreateWithBuiltins()`.
- The canvas renderer calls into the registry to render inline editors for unconnected input pins.

---

## ⚠️ Critical Notes

### IPinDefaultValueEditorRegistry already in Core

`src/NodeEditor.Core/Interfaces/IPinDefaultValueEditorRegistry.cs` already exists (from BATCH-01 kernel copy).
Do NOT re-create it. The `IPinDefaultValueEditor` interface and `DefaultEditorContext`/`PinDefaultMetadata` 
records are already in `NodeEditor.Core/Interfaces/ITypeSystem.cs`.

For T-13, you only need to create the **implementations** in `NodeEditor.UI/MiniEditors/`.

### IGraphCommandSink.Apply, not Dispatch

The host's command sink uses `Apply(GraphCommand)`, not `Dispatch`. The canvas renderer should call:
```csharp
view.Execute(forward, inverse, label); // preferred — goes through undo
// OR for fire-and-forget commands that aren't undoable:
view.Commands.Apply(command);
```

### ExpressionEvaluator API

The evaluator returns a result object; check the existing `src/NodeEditor.Core/Expression/ExpressionEvaluator.cs`:
```csharp
var result = ExpressionEvaluator.Evaluate(text);
if (result.Success) value = (float)result.Value;
else /* show error tooltip */;
```

### No tests required for UI

The UI layer is tested through the demo app (built in T-20). Just verify `dotnet build` is clean and
`dotnet test` (existing 59 tests) still pass.

---

## ✅ Tasks

### Task 1: Canvas Renderer (TASK-P3-001)

**Task Definition:** See `.dev/final/instructions/03-04-task-canvas-renderer.md` (full file — read all sections)

**Files to create:**
```
src/NodeEditor.UI/Canvas/CanvasRenderer.cs
src/NodeEditor.UI/Canvas/CanvasInput.cs
src/NodeEditor.UI/Canvas/NodeRenderer.cs
src/NodeEditor.UI/Canvas/PinRenderer.cs
src/NodeEditor.UI/Canvas/WireRenderer.cs
src/NodeEditor.UI/Canvas/GridRenderer.cs
src/NodeEditor.UI/Canvas/HitTester.cs
src/NodeEditor.UI/Util/ImDrawListExtensions.cs
src/NodeEditor.UI/Util/ImGuiPushIdScope.cs
```

**Key implementation requirements:**
- `CanvasRenderer.Render(GraphView view)` — orchestrator, see task brief for the exact call order
- Grid: two-level dot grid (minor every 16gu, major every 128gu; skip minor below 0.4 zoom)
- Nodes: header strip, title, left-column inputs, right-column outputs, selection/error/debug outline
- Position lookup: check `DragOverridePositions` first, then `Model.FindNode`
- Low-zoom mode: body+title only, skip pins/labels/editors at zoom < 0.4
- Pins: circle for data, triangle for exec; color from TypeSystem, fallback to DefaultTypeColors
- Wires: cubic bezier per task brief formula; wire hit-testing via 24-point sampling
- Hit-test priority: reroutes > pins > wires > comment headers > node bodies > comment bodies > empty
- State machine: implement all transitions from A-canvas-interactions.md §1
- Zoom: mouse wheel via `view.Viewport.ZoomAt`; pan: RMB-drag via `view.Viewport.PanScreen`
- Culling: per-frame SpatialIndex rebuild on model version change; only draw nodes in visible rect

**Acceptance:**
- Compiles
- `dotnet build` 0 errors, 0 warnings

---

### Task 2: Built-in Mini-Editors (TASK-P3-002)

**Task Definition:** See `.dev/final/instructions/03-05-task-mini-editors.md` (full file)

**Files to create:**
```
src/NodeEditor.UI/MiniEditors/PinDefaultValueEditorRegistry.cs
src/NodeEditor.UI/MiniEditors/DragFloatWithExpression.cs
src/NodeEditor.UI/MiniEditors/BoolPinEditor.cs
src/NodeEditor.UI/MiniEditors/IntPinEditor.cs
src/NodeEditor.UI/MiniEditors/FloatPinEditor.cs
src/NodeEditor.UI/MiniEditors/StringPinEditor.cs
src/NodeEditor.UI/MiniEditors/EnumPinEditor.cs
src/NodeEditor.UI/MiniEditors/VectorPinEditor.cs
src/NodeEditor.UI/MiniEditors/QuaternionPinEditor.cs
src/NodeEditor.UI/MiniEditors/ColorPinEditor.cs
src/NodeEditor.UI/MiniEditors/GuidPinEditor.cs
src/NodeEditor.UI/MiniEditors/EntityPinEditor.cs
src/NodeEditor.UI/MiniEditors/AssetPinEditor.cs
src/NodeEditor.UI/MiniEditors/StructPinEditor.cs
src/NodeEditor.UI/MiniEditors/ArrayPinEditor.cs
```

**Key requirements:**
- Each editor implements the `IPinDefaultValueEditor` interface from `NodeEditor.Core`
- `DragFloatWithExpression`: on Enter/focus-loss, evaluate as expression; on fail, restore previous value + tooltip
- `VectorPinEditor`: code skeleton is in the task brief — follow it exactly
- `PinDefaultValueEditorRegistry.CreateWithBuiltins()`: register all primitive editors (bool, int, float, string, Vector2/3/4, Quaternion, Color, Guid)
- The context struct is `PinDefaultEditorContext` or `DefaultEditorContext` — check `ITypeSystem.cs` to see the exact type name already defined

**Note on context struct name:** The kernel defines `DefaultEditorContext` in `ITypeSystem.cs`. The task brief calls it `PinDefaultEditorContext`. Use whichever name is already in the kernel file — do not create a duplicate.

**Acceptance:**
- Compiles
- `PinDefaultValueEditorRegistry.CreateWithBuiltins()` returns a registry with 9+ entries

---

## 📊 Report Requirements

Submit `.dev/final/reports/BATCH-03-REPORT.md`:

```markdown
# BATCH-03 Report

## Tasks Completed
[TASK-P3-001 ✅/❌, TASK-P3-002 ✅/❌ — one-line status each]

## Build & Test Results
[dotnet build output — 0 warnings? dotnet test — all 59 previous tests still pass?]

## Developer Insights
1. **Issues encountered:**
2. **Weak points spotted:** (anything that will likely need revisiting in T-20 demo)
3. **Design decisions beyond spec:**

## Files Created
[list]
```

---

## ✅ Success Criteria

- [ ] `dotnet build` exits 0, **0 errors, 0 warnings**
- [ ] `dotnet test` exits 0, all **59 previous tests** still pass
- [ ] `CanvasRenderer.Render(GraphView)` exists and compiles
- [ ] `PinDefaultValueEditorRegistry.CreateWithBuiltins()` registers at least 9 editors
- [ ] `BATCH-03-REPORT.md` submitted
