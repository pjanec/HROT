# BATCH-02: View-Model Layer

**Batch Number:** BATCH-02  
**Tasks:** TASK-P2-001, TASK-P2-002, TASK-P2-003  
**Phase:** Phase 2 — View-Model  
**Estimated Effort:** 4–6 hours  
**Priority:** HIGH  
**Dependencies:** BATCH-01 (completed — all kernel code in place)

---

## 📋 Onboarding & Workflow

### Developer Instructions

Phase 2 adds the pure editor-side state that lives in `NodeEditor.Core` — no ImGui, no rendering.
The view-model is the "instance" of an open editor: viewport, selection, transient interaction, and
the `GraphView` aggregator that ties everything together. Everything must be testable without a UI.

### Required Reading (IN ORDER)

1. **Workflow Guide:** `.dev/.guides/DEV-GUIDE.md`
2. **Ground Rules:** `.dev/final/instructions/00-START-HERE.md`
3. **Spec Brief:** `.dev/final/instructions/01-spec-brief.md` §6 (canvas), §11 (selection), §12 (state machine)
4. **Task Briefs:** `.dev/final/instructions/03-03-tasks-viewmodel.md` — complete briefs for T-09, T-10, T-11
5. **Kernel source for UndoStack API:** `src/NodeEditor.Core/Commands/UndoStack.cs` — read the actual public methods before implementing GraphView
6. **Kernel source for CommandBuilder API:** `src/NodeEditor.Core/Commands/CommandBuilder.cs` — CommandBuilder is instance-based, not static
7. **Kernel source for IGraphCommandSink:** `src/NodeEditor.Core/Interfaces/IGraphCommandSink.cs` — uses `Apply`, not `Dispatch`
8. **Previous Review:** `.dev/final/reviews/BATCH-01-REVIEW.md`

### Source Code Location

- **Work area:** `src/NodeEditor.Core/View/` (create this folder)
- **Tests:** `tests/NodeEditor.Core.Tests/View/`

### Report Submission

**When done, submit your report to:** `.dev/final/reports/BATCH-02-REPORT.md`  
**Questions:** `.dev/final/questions/BATCH-02-QUESTIONS.md`

---

## Context

After this batch:
- `ViewportState` handles all pan/zoom math including anchored zoom and FrameRect.
- `SelectionState` manages the selection set with modifier-key semantics.
- `InteractionState` stores all transient mode/drag/hover state.
- `GraphView` aggregates everything and provides `Execute` / `ExecuteBatch` / undo helpers.

---

## ⚠️ Critical API Alignment Notes

The code skeletons in the task brief (`03-03-tasks-viewmodel.md`) for **T-11 (GraphView)** were written
against a slightly different UndoStack/CommandBuilder API. You **must** adapt the GraphView skeleton
to match the actual kernel code already in the repo:

1. **`IGraphCommandSink.Apply`** — the interface method is `Apply(GraphCommand)`, not `Dispatch`. Do not call `Apply` directly from `GraphView`; route all mutations through `UndoStack.ApplyAndRecord`.

2. **`UndoStack` constructor requires a sink** — `new UndoStack(Commands)`, not `new UndoStack()`. The stack owns the apply call internally.

3. **`UndoStack.ApplyAndRecord(forward, inverse, label)`** — this is the correct mutation path. It applies the forward command via the sink AND records the entry in one call.

4. **`UndoStack.Undo()` / `UndoStack.Redo()`** — these are the correct method names (not `PopUndo`/`PopRedo`). They apply the inverse/forward and return `bool`.

5. **`CommandBuilder` is instance-based** — `new CommandBuilder(Model)`. It does NOT have a generic `BuildInverse(model, command)` static method. It has per-command-type factory methods. For `GraphView.Execute`, you have two options:
   - Accept both `forward` and `inverse` as parameters (recommended — callers already know what they're undoing).
   - OR add a simple two-arg overload `Execute(GraphCommand forward, GraphCommand inverse, string label)`.

   The spec intent is clear: callers supply the inverse snapshot before applying. Do NOT try to invent a generic BuildInverse dispatcher.

6. **`GraphView.UndoStack` must be `public UndoStack Undo { get; }`** initialized as `new UndoStack(commands)` inside the constructor (after assigning `Commands`).

---

## ✅ Tasks

### Task 1: ViewportState + SelectionState + SelectionEntry (TASK-P2-001)

**Task Definition:** See `.dev/final/instructions/03-03-tasks-viewmodel.md#t-09--viewportstate-and-selectionstate`

The code skeletons for all three files are given verbatim in the task brief — implement them exactly.

**Files to create:**
- `src/NodeEditor.Core/View/ViewportState.cs`
- `src/NodeEditor.Core/View/SelectionState.cs`
- `src/NodeEditor.Core/View/SelectionEntry.cs`

**Tests to add:** `tests/NodeEditor.Core.Tests/View/ViewportStateTests.cs` and `SelectionStateTests.cs`  
(exact test method names and scenarios in the task brief)

---

### Task 2: InteractionState + supporting types (TASK-P2-002)

**Task Definition:** See `.dev/final/instructions/03-03-tasks-viewmodel.md#t-10--interactionstate`

Implement all four files verbatim from the code skeletons.

**Files to create:**
- `src/NodeEditor.Core/View/InteractionMode.cs`
- `src/NodeEditor.Core/View/HoverInfo.cs`
- `src/NodeEditor.Core/View/PendingWire.cs`
- `src/NodeEditor.Core/View/InteractionState.cs`

**Tests to add:** `tests/NodeEditor.Core.Tests/View/InteractionStateTests.cs`  
(3 facts from task brief)

---

### Task 3: GraphView Aggregator (TASK-P2-003)

**Task Definition:** See `.dev/final/instructions/03-03-tasks-viewmodel.md#t-11--graphview-aggregator`

Implement `GraphView.cs` **adapted** to match the actual kernel API (see Critical API Alignment Notes above).

**Correct implementation pattern:**

```csharp
public sealed class GraphView
{
    public IGraphModel Model { get; }
    public IGraphCommandSink Commands { get; }
    public ILinkValidator Validator { get; }
    public ITypeSystem TypeSystem { get; }
    public INodeCatalog Catalog { get; }
    public IEditorHostServices Host { get; }

    public ViewportState Viewport { get; }
    public SelectionState Selection { get; }
    public InteractionState Interaction { get; }
    public UndoStack Undo { get; }

    public GraphView(IGraphModel model, IGraphCommandSink commands,
                     ILinkValidator validator, ITypeSystem typeSystem,
                     INodeCatalog catalog, IEditorHostServices host)
    {
        Model = model;
        Commands = commands;
        Validator = validator;
        TypeSystem = typeSystem;
        Catalog = catalog;
        Host = host;
        Viewport = new ViewportState();
        Selection = new SelectionState();
        Interaction = new InteractionState();
        Undo = new UndoStack(commands); // sink required
    }

    // Callers supply both forward and inverse (snapshot before applying)
    public GraphCommandResult Execute(GraphCommand forward, GraphCommand inverse, string label)
        => Undo.ApplyAndRecord(forward, inverse, label);

    public void UndoLast() => Undo.Undo();
    public void RedoLast() => Undo.Redo();
}
```

**Files to create:**
- `src/NodeEditor.Core/View/GraphView.cs`

**Tests:** `tests/NodeEditor.Core.Tests/View/GraphViewTests.cs`
- Use a minimal stub for `IGraphModel` (return empty enumerables / null from all methods).
- Use a stub for `IGraphCommandSink` that records `Apply` calls.
- **`Construct_DoesNotThrow`** — just construction with stubs succeeds.
- **`Execute_CallsSinkApply`** — after `Execute(forward, inverse, "test")`, stub sink's log has exactly one entry = `forward`.
- **`UndoLast_CallsSinkWithInverse`** — after execute then `UndoLast()`, the second call to sink is `inverse`.

For the stubs, you will need minimal implementations of all host interfaces. Create them as private inner classes in the test file. Only implement the members that `GraphView`'s constructor and the tested code paths actually use; throw `NotImplementedException` on everything else.

---

## 🧪 Testing Requirements

| Test File | Min Tests |
|-----------|-----------|
| `ViewportStateTests.cs` | 6 facts (round-trip, zoom anchor, clamp min, clamp max, FrameRect, Reset) |
| `SelectionStateTests.cs` | 4 facts (replace-single, add, toggle, nodes-filter) |
| `InteractionStateTests.cs` | 3 facts |
| `GraphViewTests.cs` | 3 facts |

**Quality bar:** Every test asserts on actual values or behavior. No `Assert.True(true)`.

---

## 📊 Report Requirements

Submit `.dev/final/reports/BATCH-02-REPORT.md` with:

```markdown
# BATCH-02 Report

## Tasks Completed
[List each task with ✅/❌ and one-line status]

## Test Results
[Paste `dotnet test` summary — exact counts]

## Developer Insights
1. **Issues encountered:**
2. **Weak points spotted:**
3. **Design decisions beyond spec:**

## Files Created
[List all new files]
```

---

## ✅ Success Criteria

- [ ] `dotnet build` exits 0, 0 errors, 0 warnings
- [ ] `dotnet test` exits 0, all tests from this batch pass (previous 43 still pass)
- [ ] `GraphView.Undo` is initialized with the `Commands` sink
- [ ] `GraphView.Execute` routes through `Undo.ApplyAndRecord`, not `Commands.Apply` directly
- [ ] `BATCH-02-REPORT.md` submitted
