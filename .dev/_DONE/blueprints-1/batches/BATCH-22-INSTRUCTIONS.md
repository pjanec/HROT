# BATCH-22: TASK-ED-002 + TASK-ED-003 -- Asset Browser, Graph Editor, Inspector Windows + StructEdit Drawers

**Batch Number:** BATCH-22
**Tasks:** TASK-ED-002, TASK-ED-003
**Phase:** 6 -- Editor
**Estimated Effort:** 4-5 days
**Priority:** HIGH
**Dependencies:** BATCH-21 (ED-001 infrastructure in place)

---

## 0. Onboarding

### Required Reading

1. `.dev/blueprints-1/reviews/BATCH-21-REVIEW.md` -- current state
2. `.dev/blueprints-1/TASK-DETAIL.md` §ED-002 (Asset Browser + Graph Editor) + §ED-003 (Inspector + StructEdit)
3. `.dev/blueprints-1/Blueprint_Subsystem_Editor_Detailed_Design.md` §4 (Asset Browser), §5 (Graph Editor), §6 (Inspector), §7 (StructEdit drawers)
4. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/BlueprintEditorWindowBase.cs` -- base class to inherit from
5. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/DirtyTracker.cs` -- shared state
6. `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/EditorSelectionStore.cs` -- selection state

### Important: This batch focuses on data models and logic, NOT rendering

The ED-002 and ED-003 scopes include ImGui rendering calls. However, since this is running without a running editor process, the ImGui draw calls cannot be tested in unit tests. **Focus on:**
1. The **data model** classes (CommandHistory, SelectionState, DrawerRegistry, etc.)
2. The **non-rendering logic** (command undo/redo, drawer registry lookup, dirty notification)
3. **Tests that don't call `DrawUI()`** -- test command history, selection, drawer registry

The `DrawUI()` implementations in windows can be **stubbed** (empty body or throw `NotImplementedException`) -- the Editor DD says they use ImGui, which requires an actual editor runtime. Tests verify the logic models.

### Report Submission

`.dev/blueprints-1/reports/BATCH-22-REPORT.md`

---

## 1. TASK-ED-002: Asset Browser and Graph Editor

### 1.1 IGraphCommand and CommandHistory

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/GraphEditor/IGraphCommand.cs`:
```csharp
namespace Hrot.Blueprints.Editor.GraphEditor;

public interface IGraphCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}
```

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/GraphEditor/CommandHistory.cs`:
```csharp
namespace Hrot.Blueprints.Editor.GraphEditor;

public sealed class CommandHistory
{
    private const int Capacity = 64;
    private readonly IGraphCommand[] _history = new IGraphCommand[Capacity];
    private int _head;
    private int _count;
    private int _undoIndex;  // points to the next command to undo

    public int Count => _count;

    public void Execute(IGraphCommand command)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        // Discard redo history when a new command is executed.
        _count = _undoIndex;
        var idx = (_head + _count) % Capacity;
        _history[idx] = command;
        if (_count < Capacity) _count++;
        else _head = (_head + 1) % Capacity;  // evict oldest
        _undoIndex = _count;
        command.Execute();
    }

    public bool CanUndo => _undoIndex > 0;
    public bool CanRedo => _undoIndex < _count;

    public void Undo()
    {
        if (!CanUndo) return;
        _undoIndex--;
        var idx = (_head + _undoIndex) % Capacity;
        _history[idx].Undo();
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var idx = (_head + _undoIndex) % Capacity;
        _history[idx].Execute();
        _undoIndex++;
    }

    public void Clear()
    {
        _count = 0;
        _head = 0;
        _undoIndex = 0;
        Array.Clear(_history, 0, Capacity);
    }
}
```

### 1.2 SelectionState

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/GraphEditor/SelectionState.cs`:
```csharp
namespace Hrot.Blueprints.Editor.GraphEditor;

public sealed class SelectionState
{
    public HashSet<Guid> SelectedNodes { get; } = new();
    public HashSet<Guid> SelectedLinks { get; } = new();

    public void ClearAll()
    {
        SelectedNodes.Clear();
        SelectedLinks.Clear();
    }

    public bool IsNodeSelected(Guid nodeId)  => SelectedNodes.Contains(nodeId);
    public bool IsLinkSelected(Guid linkId)  => SelectedLinks.Contains(linkId);

    public void SelectNode(Guid nodeId, bool addToSelection = false)
    {
        if (!addToSelection) ClearAll();
        SelectedNodes.Add(nodeId);
    }
}
```

### 1.3 AddNodeCommand and DeleteNodeCommand (concrete commands for testing)

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/GraphEditor/GraphCommands.cs`:
```csharp
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Editor.GraphEditor;

public sealed class AddNodeCommand : IGraphCommand
{
    private readonly BlueprintGraph _graph;
    private readonly BlueprintNode _node;

    public string Description => $"Add Node {_node.NodeId}";

    public AddNodeCommand(BlueprintGraph graph, BlueprintNode node)
    {
        _graph = graph;
        _node  = node;
    }

    public void Execute() => _graph.Nodes.Add(_node);
    public void Undo()    => _graph.Nodes.Remove(_node);
}

public sealed class DeleteNodeCommand : IGraphCommand
{
    private readonly BlueprintGraph _graph;
    private readonly BlueprintNode _node;

    public string Description => $"Delete Node {_node.NodeId}";

    public DeleteNodeCommand(BlueprintGraph graph, BlueprintNode node)
    {
        _graph = graph;
        _node  = node;
    }

    public void Execute() => _graph.Nodes.Remove(_node);
    public void Undo()    => _graph.Nodes.Add(_node);
}
```

**IMPORTANT:** Read `BlueprintAsset`, `BlueprintGraph`, and `BlueprintNode` types from `Fdp.Toolkit.Blueprints` before writing this. Verify the actual property/method names on these types match (`Nodes`, `NodeId`, etc.). If `BlueprintGraph.Nodes` is `IList<BlueprintNode>` that's fine; if it's read-only, adapt accordingly.

### 1.4 AssetBrowserWindow (skeleton)

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/AssetBrowserWindow.cs`:

```csharp
using Fdp.Toolkit.Blueprints;

namespace Hrot.Blueprints.Editor;

public sealed class AssetBrowserWindow : BlueprintEditorWindowBase
{
    private readonly IAssetCatalog _catalog;
    private readonly EditorSelectionStore _selectionStore;
    private readonly DirtyTracker _dirtyTracker;
    private readonly EditorState _editorState;

    private List<AssetCatalogEntry> _catalogEntries = new();

    public override string Title => "Asset Browser";

    public AssetBrowserWindow(
        IAssetCatalog catalog,
        EditorSelectionStore selectionStore,
        DirtyTracker dirtyTracker,
        EditorState editorState)
    {
        _catalog        = catalog        ?? throw new ArgumentNullException(nameof(catalog));
        _selectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
        _dirtyTracker   = dirtyTracker   ?? throw new ArgumentNullException(nameof(dirtyTracker));
        _editorState    = editorState    ?? throw new ArgumentNullException(nameof(editorState));
    }

    public void RefreshCatalog()
        => _catalogEntries = _catalog.EnumerateAll().ToList();

    public IReadOnlyList<AssetCatalogEntry> CatalogEntries => _catalogEntries;

    public override void DrawUI()
    {
        // ImGui rendering -- requires editor runtime. Stub for Slice 1.
    }

    public override void OnActivated()   => RefreshCatalog();
    public override void OnDeactivated() { }
}
```

### 1.5 GraphEditorWindow (skeleton)

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/GraphEditorWindow.cs`:

```csharp
using Fdp.Toolkit.Blueprints;
using Hrot.Blueprints.Editor.GraphEditor;

namespace Hrot.Blueprints.Editor;

public sealed class GraphEditorWindow : BlueprintEditorWindowBase
{
    private readonly EditorSelectionStore _selectionStore;
    private readonly DirtyTracker _dirtyTracker;
    private readonly EditorState _editorState;

    public override string Title => "Graph Editor";

    public BlueprintAsset? CurrentAsset { get; private set; }
    public SelectionState Selection { get; } = new();
    public CommandHistory Commands { get; } = new();

    public GraphEditorWindow(
        EditorSelectionStore selectionStore,
        DirtyTracker dirtyTracker,
        EditorState editorState)
    {
        _selectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
        _dirtyTracker   = dirtyTracker   ?? throw new ArgumentNullException(nameof(dirtyTracker));
        _editorState    = editorState    ?? throw new ArgumentNullException(nameof(editorState));
    }

    public void OpenAsset(BlueprintAsset asset)
    {
        CurrentAsset = asset;
        Selection.ClearAll();
        Commands.Clear();
    }

    public override void DrawUI()
    {
        // ImGui canvas rendering -- requires editor runtime. Stub for Slice 1.
    }

    public override void OnDeactivated()
    {
        Selection.ClearAll();
    }
}
```

---

## 2. TASK-ED-003: Inspector and StructEdit Drawers

### 2.1 DrawContext

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Inspector/DrawContext.cs`:
```csharp
namespace Hrot.Blueprints.Editor.Inspector;

public sealed record DrawContext(
    bool IsReadOnly = false,
    string IdPrefix = "",
    object? TypeRegistry = null);
```

### 2.2 IStructEditDrawer

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Inspector/IStructEditDrawer.cs`:
```csharp
namespace Hrot.Blueprints.Editor.Inspector;

public interface IStructEditDrawer<T>
{
    /// <summary>Returns true if the value was modified.</summary>
    bool Draw(string label, ref T value, DrawContext ctx);
}
```

### 2.3 DrawerRegistry

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Inspector/DrawerRegistry.cs`:
```csharp
namespace Hrot.Blueprints.Editor.Inspector;

public sealed class DrawerRegistry
{
    private readonly Dictionary<Type, object> _drawers = new();

    public void Register<T>(IStructEditDrawer<T> drawer)
        => _drawers[typeof(T)] = drawer ?? throw new ArgumentNullException(nameof(drawer));

    public bool TryGet<T>(out IStructEditDrawer<T> drawer)
    {
        if (_drawers.TryGetValue(typeof(T), out var obj) && obj is IStructEditDrawer<T> d)
        {
            drawer = d;
            return true;
        }
        drawer = null!;
        return false;
    }
}
```

### 2.4 Primitive Drawers (stubs -- actual ImGui calls require editor runtime)

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/Inspector/PrimitiveDrawers.cs`:
```csharp
namespace Hrot.Blueprints.Editor.Inspector;

/// <summary>ImGui-based float input drawer. Requires editor runtime to render.</summary>
public sealed class FloatDrawer : IStructEditDrawer<float>
{
    public bool Draw(string label, ref float value, DrawContext ctx)
    {
        if (ctx.IsReadOnly) return false;
        // ImGui.InputFloat(label, ref value) would go here.
        return false;  // No modification without ImGui runtime.
    }
}

public sealed class IntDrawer : IStructEditDrawer<int>
{
    public bool Draw(string label, ref int value, DrawContext ctx)
    {
        if (ctx.IsReadOnly) return false;
        return false;
    }
}

public sealed class BoolDrawer : IStructEditDrawer<bool>
{
    public bool Draw(string label, ref bool value, DrawContext ctx)
    {
        if (ctx.IsReadOnly) return false;
        return false;
    }
}

public sealed class StringDrawer : IStructEditDrawer<string>
{
    public bool Draw(string label, ref string value, DrawContext ctx)
    {
        if (ctx.IsReadOnly) return false;
        return false;
    }
}
```

### 2.5 InspectorWindow (skeleton)

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/InspectorWindow.cs`:
```csharp
using Hrot.Blueprints.Editor.Inspector;

namespace Hrot.Blueprints.Editor;

public sealed class InspectorWindow : BlueprintEditorWindowBase
{
    private readonly EditorSelectionStore _selectionStore;
    private readonly DirtyTracker _dirtyTracker;
    private readonly DrawerRegistry _drawerRegistry;

    public override string Title => "Inspector";

    public InspectorWindow(
        EditorSelectionStore selectionStore,
        DirtyTracker dirtyTracker,
        DrawerRegistry drawerRegistry)
    {
        _selectionStore = selectionStore ?? throw new ArgumentNullException(nameof(selectionStore));
        _dirtyTracker   = dirtyTracker   ?? throw new ArgumentNullException(nameof(dirtyTracker));
        _drawerRegistry = drawerRegistry ?? throw new ArgumentNullException(nameof(drawerRegistry));
    }

    public override void DrawUI()
    {
        // Three-tab layout: Node, Graph, Asset -- requires ImGui runtime.
    }
}
```

---

## 3. Tests Required

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/CommandHistoryTests.cs`:

**SC1: `AddNode_ThenUndo_RestoresNodeCount`**
- Create a `BlueprintAsset` with an empty graph. `Commands.Execute(new AddNodeCommand(graph, node))`.
- Assert graph.Nodes.Count == 1. `Commands.Undo()`. Assert graph.Nodes.Count == 0.

**SC2: `AddNode_Undo_Redo`**
- Execute AddNodeCommand. Undo. Redo. Assert Nodes.Count == 1.

**SC3: `CommandHistory_CanUndo_CanRedo`**
- After Execute: `CanUndo == true`, `CanRedo == false`.
- After Undo: `CanUndo == false`, `CanRedo == true`.

**SC4: `CommandHistory_Execute_After_Undo_Discards_Redo`**
- Execute A. Execute B. Undo (B undone). Execute C. `CanRedo == false` (redo of B is gone).

**SC5: `CommandHistory_Clear_ResetsAll`**
- Execute command. `Commands.Clear()`. `CanUndo == false`, `CanRedo == false`.

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/DrawerRegistryTests.cs`:

**SC1: `DrawerRegistry_Register_ThenTryGet_Returns_Drawer`**
- `registry.Register<float>(new FloatDrawer())`. `registry.TryGet<float>(out var d)` returns true, d != null.

**SC2: `DrawerRegistry_TryGet_Missing_ReturnsFalse`**
- `registry.TryGet<double>(out var d)` returns false.

**SC3: `DrawerRegistry_Register_Overwrite_Succeeds`**
- Register two `FloatDrawer` instances. Second registration replaces first. `TryGet<float>` returns the second.

Create `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/AssetBrowserWindowTests.cs`:

**SC1: `AssetBrowserWindow_EmptyCatalog_CatalogEntriesIsEmpty`**
- Create `AssetBrowserWindow` with a stub catalog returning no entries. `RefreshCatalog()`. `CatalogEntries.Count == 0`.

**SC2: `AssetBrowserWindow_OnActivated_RefreshesCatalog`**
- Create stub catalog with 1 entry. `OnActivated()`. `CatalogEntries.Count == 1`.

**IMPORTANT:** Before writing `CommandHistoryTests.cs`, read the actual `BlueprintAsset`, `BlueprintGraph`, and `BlueprintNode` types from `Fdp.Toolkit.Blueprints`. Check what properties exist (`Nodes` collection, `NodeId`, etc.). If these types don't have a suitable in-memory editable graph, substitute with a simple test-only `List<string>` command instead.

---

## 4. Verification

```powershell
dotnet build Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor -v quiet
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests --filter "FullyQualifiedName~Editor" -v minimal
dotnet test Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests -v minimal
```

Expected: 0 errors, 0 failures. Total count >= 449 (439 + ~10 new tests).

---

## 5. Mandatory Task Progression

1. Read BATCH-22-INSTRUCTIONS.md (this file).
2. Read `Hrot.Blueprints.Editor.csproj` and the existing ED-001 files.
3. **Critically:** Read `Fdp.Toolkit.Blueprints` types (BlueprintAsset, BlueprintGraph, BlueprintNode) to understand the actual data model before writing commands. Look in `FDP/Toolkits/Fdp.Toolkits/Blueprints/` or wherever the toolkit code lives.
4. Create GraphEditor folder + `IGraphCommand`, `CommandHistory`, `SelectionState`, `GraphCommands`.
5. Create `AssetBrowserWindow`, `GraphEditorWindow` skeletons.
6. Create Inspector folder + `DrawContext`, `IStructEditDrawer`, `DrawerRegistry`, `PrimitiveDrawers`.
7. Create `InspectorWindow` skeleton.
8. Build Editor project. Fix errors.
9. Create test files (CommandHistoryTests, DrawerRegistryTests, AssetBrowserWindowTests).
10. Build Tests project. Fix errors.
11. Run Editor-filter tests. Fix failures.
12. Run full suite. Fix failures.
13. Commit.
14. Write report.

**DO NOT STOP.** Complete all tasks.

---

## 6. Commit

```powershell
git add Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/
git add Hrot/Subsystems/Blueprints/Hrot.Blueprints.Tests/Editor/
git commit -m "feat(blueprints): BATCH-22 ED-002 asset browser graph editor + ED-003 inspector drawers

- IGraphCommand + CommandHistory (undo/redo, capacity 64, evict oldest)
- SelectionState: SelectedNodes/SelectedLinks HashSets, ClearAll, SelectNode
- AddNodeCommand + DeleteNodeCommand: Execute/Undo with graph mutation
- AssetBrowserWindow: RefreshCatalog, CatalogEntries, stub DrawUI
- GraphEditorWindow: OpenAsset, Selection + Commands, stub DrawUI
- DrawContext record: IsReadOnly, IdPrefix, TypeRegistry
- IStructEditDrawer<T> interface
- DrawerRegistry: Register<T>/TryGet<T> type-keyed lookup
- PrimitiveDrawers: FloatDrawer, IntDrawer, BoolDrawer, StringDrawer stubs
- InspectorWindow: skeleton with DrawerRegistry dependency
- CommandHistoryTests.cs: SC1-SC5
- DrawerRegistryTests.cs: SC1-SC3
- AssetBrowserWindowTests.cs: SC1-SC2

Baseline: 439 total -> 449+ pass / 5 skip / 0 fail"
```

---

## 7. Report

`.dev/blueprints-1/reports/BATCH-22-REPORT.md`

---

## Success Criteria

| SC | Check |
|----|-------|
| SC1-SC2 | CommandHistory Undo/Redo restores node state |
| SC3 | CanUndo/CanRedo after execute/undo transitions |
| SC4 | Execute after Undo discards redo stack |
| SC5 | Clear resets all history state |
| SC1-SC3 | DrawerRegistry register, retrieve, overwrite |
| SC1-SC2 | AssetBrowserWindow catalog refresh |
| Build | `dotnet build Hrot.Blueprints.Editor` zero errors |
| Tests | 0 failures full suite |
