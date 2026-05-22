# BATCH-02 Report

## Tasks Completed

| Task | Status | Notes |
|------|--------|-------|
| TASK-P2-001 — ViewportState + SelectionState + SelectionEntry | ✅ | All three files implemented verbatim from spec, with `RectF.Center` fix |
| TASK-P2-002 — InteractionState + supporting types | ✅ | All four files implemented verbatim from spec |
| TASK-P2-003 — GraphView Aggregator | ✅ | Implemented with corrected API per batch instructions |

## Files Created

**Source:**
- `src/NodeEditor.Core/View/ViewportState.cs`
- `src/NodeEditor.Core/View/SelectionEntry.cs`
- `src/NodeEditor.Core/View/SelectionState.cs`
- `src/NodeEditor.Core/View/InteractionMode.cs`
- `src/NodeEditor.Core/View/HoverInfo.cs`
- `src/NodeEditor.Core/View/PendingWire.cs`
- `src/NodeEditor.Core/View/InteractionState.cs`
- `src/NodeEditor.Core/View/GraphView.cs`

**Tests:**
- `tests/NodeEditor.Core.Tests/View/ViewportStateTests.cs` (6 facts)
- `tests/NodeEditor.Core.Tests/View/SelectionStateTests.cs` (4 facts)
- `tests/NodeEditor.Core.Tests/View/InteractionStateTests.cs` (3 facts)
- `tests/NodeEditor.Core.Tests/View/GraphViewTests.cs` (3 facts)

## Test Results

```
Test Run Successful.
Total tests: 59
     Passed: 59
  Total time: 0.6697 Seconds
```

Previous 43 tests: all still pass.  
New tests added: 16 (6 + 4 + 3 + 3).

## Developer Insights

### API Alignment Applied (T-11)
The task brief skeleton for `GraphView` used a wrong API (`Commands.Dispatch`, `Undo.Push`, `Undo.PopUndo`, etc.). The corrected implementation:
- `Undo = new UndoStack(commands)` — sink passed at construction
- `Execute(forward, inverse, label)` calls `Undo.ApplyAndRecord(forward, inverse, label)`
- `UndoLast()` calls `Undo.Undo()`; `RedoLast()` calls `Undo.Redo()`

### RectF Adaptation (T-09)
The `FrameRect` skeleton referenced `rect.X` and `rect.Y` which don't exist on `RectF`. Used `rect.Center` (existing property) instead, which is equivalent and cleaner.

### Event Stub Pattern
`IGraphModel.Changed` in the test stub uses `{ add { } remove { } }` to avoid CS0067 (unused event error) under `TreatWarningsAsErrors`.

## Build Status

`dotnet build` — **succeeded, 0 warnings, 0 errors**  
`dotnet test` — **59/59 passed**
