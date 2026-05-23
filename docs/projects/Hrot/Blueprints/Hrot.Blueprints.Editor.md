# Hrot.Blueprints.Editor

| Field      | Value                                                                              |
|------------|------------------------------------------------------------------------------------|
| Project    | `Hrot.Blueprints.Editor`                                                           |
| Path       | `Hrot/Subsystems/Blueprints/Hrot.Blueprints.Editor/`                              |
| Framework  | net8.0                                                                             |
| Nullable   | enabled                                                                            |
| Date       | 2026-05-23                                                                         |

---

## README Validation

**Status: Missing**

No `README.md` exists in the project folder. `BlueprintsEditor.cs` is an explicit
placeholder stub (`// Placeholder for Hrot.Blueprints.Editor assembly.`), indicating
the project was scaffolded but top-level documentation was not written. All
architectural knowledge lives in source code comments and this document.

---

## Executive Overview

`Hrot.Blueprints.Editor` is the ImGui-based UI editor that lets designers and
engineers author, inspect, reload, and debug Blueprint graphs at runtime. It sits
on top of the Blueprints visual scripting stack and ties together:

- **Authoring**: browse, open, and edit Blueprint assets on disk.
- **Graph editing**: a canvas window with a command-history-backed undo/redo system
  for adding/deleting nodes and links.
- **Property inspection**: a tabbed Inspector that shows node, graph, and asset
  metadata. Property values are rendered through a pluggable `DrawerRegistry` of
  typed `IStructEditDrawer<T>` implementations.
- **Hot reload**: two reload pathways -- a fast in-process Quick Reload that
  compiles a single asset through the Blueprint AST compiler and Roslyn, then
  atomically swaps the resulting collectible `AssemblyLoadContext`; and a slower
  Full Rebuild that spawns `dotnet build` out-of-process.
- **Debug session**: a full `IBlueprintDebugSession` implementation providing
  breakpoints, watches, step-into/over/out, per-entity execution history, and
  simulation-time pause control. Debug windows (panel, watches, callstack, reload
  log) are ImGui windows registered with the module.
- **Preferences**: user settings (auto-reload, log limits, grid snap) persisted as
  camelCase JSON.

The authoring workflow is:

```
Open asset (double-click in Asset Browser)
    |
    v
Edit graph nodes / links in Graph Editor
    |-- undo/redo via CommandHistory ring buffer
    |-- mark asset dirty via DirtyTracker
    v
Inspect selected node/graph/asset in Inspector
    |
    v
Save (marks clean) --> Quick Reload (in-process ALC swap)
                  \--> Full Rebuild  (dotnet build, then file-watcher drain)
```

---

## Architecture

### Layer Overview

The editor follows a strict layered architecture:

```
+-------------------------------------------------------------------+
|                      Host Application                             |
|  (provides IWindowRegistrar, IOutputConsole, frame loop)          |
+-------------------------------------------------------------------+
          |                            |
          v                            v
+---------------------+   +--------------------------+
|  BlueprintEditorModule  |   | DI Container                |
|  (orchestrator)     |   | (AddBlueprintEditor(...))   |
+---------------------+   +--------------------------+
          |
          |  owns (List<IBlueprintEditorWindow>)
          |
   +------+------+--------+----------+---------+----------+
   |             |        |          |         |          |
   v             v        v          v         v          v
Asset         Graph    Inspector  Preferences  Debug    HotReload
Browser       Editor   Window     Window       Panel    Log Window
Window        Window
   |             |        |                    |
   v             v        v                    v
IAssetCatalog  CommandHistory  DrawerRegistry  IBlueprintDebugSession
EditorState    SelectionState  IStructEditDrawer<T>
EditorSelection  QuickReloadService
Store           FullRebuildService
```

### Core Services (singletons)

| Service                   | Role                                                          |
|---------------------------|---------------------------------------------------------------|
| `DirtyTracker`            | Tracks which asset GUIDs have unsaved edits                   |
| `EditorSelectionStore`    | Single-selection store; raises `OnSelectionChanged`           |
| `EditorState`             | In-memory asset cache keyed by `Guid`                         |
| `IAssetCatalog`           | Enumerates `AssetCatalogEntry` records from the asset root    |
| `BlueprintEditorModule`   | Registers windows, drives draw loop, routes reload events     |

### Window Lifecycle

Every window implements `IBlueprintEditorWindow` (or extends
`BlueprintEditorWindowBase`). The module calls:

1. `OnActivated()` -- once when the editor is first enabled.
2. `DrawUI()` -- every frame while `IsVisible == true`.
3. `OnDeactivated()` -- when the editor is torn down.

Visibility is toggled from the menu via `IWindowRegistrar.RegisterMenuEntry`.

---

## ASCII Block Diagrams

### Diagram 1 -- Module and Window Wiring

```
+----------------------------+
|    BlueprintEditorModule   |
|  _windows: List<IBlueprintEditorWindow>  |
|  _activated: bool          |
+----------------------------+
      |         |       |
      |         |       +---------------------------+
      |         |                                   |
      v         v                                   v
+-------------+ +-------------------+  +---------------------+
| AssetBrowser| | GraphEditorWindow |  | InspectorWindow     |
| Window      | | CurrentAsset      |  | (tabbed: Node/Graph |
| (table UI)  | | Selection: State  |  |  /Asset)            |
+-------------+ | Commands:History  |  +---------------------+
      |         +-------------------+
      |               |         |
      v               v         v
+---------------+ +----------+ +------------------+
| IAssetCatalog | | Quick    | | FullRebuildService|
| (FS scan)     | | Reload   | | (dotnet build)   |
+---------------+ | Service  | +------------------+
                  +----------+
                       |
              +--------+--------+
              |                 |
              v                 v
   +-------------------+ +-------------------+
   | IBlueprintCompiler| | AiHotReloadCoord  |
   | (AST + Roslyn)    | | (ALC atomic swap) |
   +-------------------+ +-------------------+
```

### Diagram 2 -- Quick Reload Pipeline

```
GraphEditorWindow                QuickReloadService
    |                                   |
    |-- TriggerAsync(asset) ----------->|
    |                                   |
    |                   BuildSiblingSignatures()
    |                        |  catalog.EnumerateAll()
    |                        |  EditorState.GetInMemoryAsset()
    |                        |  BlueprintSignatureBuilder.FromInMemoryAsset()
    |                        v
    |                   IBlueprintCompiler.Compile(asset, options)
    |                        |  [AST compile, emits GeneratedSource]
    |                        v
    |                   InMemoryRoslynCompiler.Compile()
    |                        |  [Roslyn PE + PDB bytes]
    |                        v
    |                   new AssemblyLoadContext (collectible)
    |                        |  alc.LoadFromStream(pe, pdb)
    |                        v
    |                   HsmActionDispatcher.ClearAll()
    |                        v
    |                   Invoke BlueprintRegistrarAttribute methods
    |                        |  into BehaviorRegistry (staging)
    |                        |  into BlueprintRegistryStaging
    |                        v
    |                   IBlueprintDebugSession.RegisterDebugMap()
    |                        v
    |                   AiHotReloadCoordinator.ApplyQuickReload()
    |                        |  [atomic ALC swap, RCU commit]
    |                        v
    |<-- QuickReloadResult(Succeeded, DurationMs) ---|
```

### Diagram 3 -- Debug Session Probe Flow

```
    Running Blueprint (generated code)
          |
          | DebugProbe.Sink.OnNodeEnter(entity, nodeId)
          v
    BlueprintDebugSession
          |
          +-- EntityFilter check --------> skip if wrong entity
          |
          +-- ExecutionHistory.Record()
          |
          +-- Fire OnNodeExecuted event --> CallstackWindow
          |
          +-- Breakpoint lookup (O(1) _bpByNodeString dict)
          |       |
          |       +-- HIT --> HandleBreakpointHit()
          |                       |
          |                       +-- _isPaused = true
          |                       +-- _timeController.RequestPause()
          |                       +-- Fire OnBreakpointHit event --> DebugPanelWindow
          |
          +-- StepMode check (Into/Over/Out)
                  |
                  +-- MATCHED --> HandleBreakpointHit() (pseudo-breakpoint)


    DebugPanelWindow calls:
          Continue() --> _timeController.RequestResume()
          StepInto()  --> _timeController.RequestStepOneTick()
          StepOver()  --> _timeController.RequestStepOneTick()
          StepOut()   --> _timeController.RequestStepOneTick()
```

### Diagram 4 -- Asset Discovery and Selection Flow

```
    FileSystemAssetCatalog
          |
          | EnumerateAll()
          | Directory.EnumerateFiles("*.bp.json", AllDirectories)
          | Parse AssetId from JSON header
          v
    AssetBrowserWindow._catalogEntries  [List<AssetCatalogEntry>]
          |
          | User double-clicks a row
          v
    EditorState.GetInMemoryAsset(entry.AssetId)
          |
          v
    EditorSelectionStore.SelectAsset(asset)
          |
          | OnSelectionChanged event
          v
    GraphEditorWindow.OnSelectionChanged()
          |-- OpenAsset(asset)
          |-- Selection.ClearAll()
          |-- Commands.Clear()
          v
    InspectorWindow.DrawUI()
          |-- reads SelectedAsset from EditorSelectionStore
          |-- renders Node / Graph / Asset tabs
```

### Diagram 5 -- DI Registration

```
IServiceCollection.AddBlueprintEditor(assetRootDirectory)
    |
    +-- AddSingleton<DirtyTracker>
    +-- AddSingleton<EditorSelectionStore>
    +-- AddSingleton<EditorState>
    +-- AddSingleton<IAssetCatalog>(_ => new FileSystemAssetCatalog(assetRootDirectory))
    +-- AddSingleton<BlueprintEditorModule>
```

---

## Source Structure

### Root Namespace: `Hrot.Blueprints.Editor`

```
Hrot.Blueprints.Editor/
|-- BlueprintsEditor.cs                     -- assembly placeholder stub
|-- BlueprintEditorModule.cs                -- module orchestrator
|-- BlueprintEditorServiceCollectionExtensions.cs  -- DI registration
|-- BlueprintEditorConfiguration.cs         -- compile-time config record
|-- BlueprintEditorPreferences.cs           -- user preferences (JSON)
|-- BlueprintDebugSession.cs                -- IBlueprintDebugSession impl
|-- IWindowRegistrar.cs                     -- menu/toolbar/shortcut registration
|-- IOutputConsole.cs                       -- logging abstraction
|-- IAssetCatalog.cs                        -- asset enumeration contract
|-- IBlueprintEditorWindow.cs               -- window contract
|-- BlueprintEditorWindowBase.cs            -- abstract base window
|-- ReloadInfo.cs                           -- ReloadSource + ReloadCompletedInfo
|-- EditorState.cs                          -- in-memory asset cache
|-- EditorSelectionStore.cs                 -- single-selection store + event
|-- DirtyTracker.cs                         -- dirty-flag tracker
|-- FileSystemAssetCatalog.cs               -- IAssetCatalog on filesystem
|-- AssetBrowserWindow.cs                   -- asset table browser window
|-- GraphEditorWindow.cs                    -- graph canvas + toolbar window
|-- InspectorWindow.cs                      -- tabbed property inspector window
|-- PreferencesWindow.cs                    -- editor preferences window
|
|-- GraphEditor/
|   |-- IGraphCommand.cs                    -- command interface (Execute/Undo)
|   |-- GraphCommands.cs                    -- AddNodeCommand, DeleteNodeCommand
|   |-- CommandHistory.cs                   -- ring-buffer undo/redo stack (cap 64)
|   |-- SelectionState.cs                   -- selected node/link Guid sets
|
|-- Inspector/
|   |-- IStructEditDrawer.cs                -- generic typed drawer interface
|   |-- DrawContext.cs                      -- rendering context record
|   |-- DrawerRegistry.cs                   -- type-keyed drawer dictionary
|   |-- PrimitiveDrawers.cs                 -- float/int/bool/string stub drawers
|
|-- Reload/
|   |-- QuickReloadService.cs               -- in-process hot reload coordinator
|   |-- QuickReloadResult.cs                -- result record
|   |-- FullRebuildService.cs               -- out-of-process dotnet build
|   |-- FullRebuildResult.cs                -- result record
|   |-- BlueprintSignatureBuilder.cs        -- in-memory asset -> BlueprintSignature
|
|-- Debug/
    |-- DebugPanelWindow.cs                 -- pause/step controls window
    |-- WatchPanelWindow.cs                 -- pin watch table window
    |-- CallstackWindow.cs                  -- node execution trail window
    |-- HotReloadLogWindow.cs               -- reload event log window
    |-- HotReloadLogModel.cs                -- queue ring buffer (max 1000)
    |-- ReloadLogEntry.cs                   -- log entry record
    |-- MasterSyncTimeControllerAdapter.cs  -- IBlueprintTimeController adapter
```

---

## Public API Reference

### Root Namespace

#### `BlueprintsEditor` (class, `Hrot.Blueprints.Editor`)
Assembly placeholder stub. No public members beyond the implicit default constructor.

---

#### `IBlueprintEditorWindow` (interface)

```csharp
public interface IBlueprintEditorWindow
{
    string Title { get; }
    bool IsVisible { get; set; }
    void ToggleVisible();
    void DrawUI();
    void OnActivated();
    void OnDeactivated();
}
```

Implemented by every editor panel. `DrawUI()` is called each frame when
`IsVisible` is true. `OnActivated` / `OnDeactivated` bracket the editor session.

---

#### `BlueprintEditorWindowBase` (abstract class)

```csharp
public abstract class BlueprintEditorWindowBase : IBlueprintEditorWindow
{
    public abstract string Title { get; }
    public bool IsVisible { get; set; }
    public void ToggleVisible();
    public abstract void DrawUI();
    public virtual void OnActivated();
    public virtual void OnDeactivated();
}
```

Base implementation that provides default no-op `OnActivated/OnDeactivated` and
the `ToggleVisible()` flip. Derived classes override `Title` and `DrawUI()`.

---

#### `BlueprintEditorModule` (sealed class)

```csharp
public sealed class BlueprintEditorModule
{
    public BlueprintEditorModule(
        IWindowRegistrar windowRegistrar,
        DirtyTracker dirtyTracker,
        EditorSelectionStore selectionStore,
        EditorState editorState,
        IAssetCatalog catalog,
        IOutputConsole outputConsole);

    public IReadOnlyList<IBlueprintEditorWindow> Windows { get; }

    public void RegisterWindow(IBlueprintEditorWindow window);
    public void OnEditorActivated();
    public void OnEditorDeactivated();
    public void DrawAllWindows();
    public void OnReloadCompleted(ReloadCompletedInfo info);
}
```

Central orchestrator. Call `RegisterWindow` for each panel before calling
`OnEditorActivated`. `DrawAllWindows` is the per-frame draw call. Routes reload
completion events to the output console.

---

#### `BlueprintEditorServiceCollectionExtensions` (static class)

```csharp
public static class BlueprintEditorServiceCollectionExtensions
{
    public static IServiceCollection AddBlueprintEditor(
        this IServiceCollection services,
        string assetRootDirectory);
}
```

Registers the five core singletons: `DirtyTracker`, `EditorSelectionStore`,
`EditorState`, `IAssetCatalog` (as `FileSystemAssetCatalog`), and
`BlueprintEditorModule`.

---

#### `BlueprintEditorConfiguration` (sealed record)

```csharp
public sealed record BlueprintEditorConfiguration(
    string DebugMapsOutputDirectory,
    string BehaviorsDllDirectory,
    string BehaviorsBuildTarget = "");
```

Immutable compile-time configuration. Injected wherever the editor needs to locate
generated DLLs or debug map files.

---

#### `BlueprintEditorPreferences` (sealed class)

```csharp
public sealed class BlueprintEditorPreferences
{
    public bool  AutoReloadOnSave         { get; set; }  // default: false
    public bool  WatchPanelVisible        { get; set; }  // default: true
    public float GraphEditorGridSnap      { get; set; }  // default: 8.0f
    public int   NodeHistorySize          { get; set; }  // default: 64
    public int   HotReloadLogMaxEntries   { get; set; }  // default: 1000

    public static BlueprintEditorPreferences Defaults { get; }

    public void Save(string path);
    public static BlueprintEditorPreferences Load(string path);
}
```

JSON-serialized (camelCase, indented). `Load` returns defaults when the file is
absent or malformed -- never throws.

---

#### `IWindowRegistrar` (interface)

```csharp
public interface IWindowRegistrar
{
    void RegisterMenuEntry(string path, Action onSelected);
    void RegisterToolbarEntry(string label, Action onClicked);
    void RegisterShortcut(string keybind, Action onTriggered);
}
```

Provided by the host application. The module registers `Blueprint/<Title>` menu
entries for each window during `OnEditorActivated`.

---

#### `IOutputConsole` (interface)

```csharp
public interface IOutputConsole
{
    void LogInfo(string message);
    void LogWarning(string message);
    void LogError(string message);
    void LogDebug(string message);
    void LogDiagnostic(Diagnostic diagnostic);  // Microsoft.CodeAnalysis.Diagnostic
}
```

Abstracts the output channel. Both reload services write progress and errors here.

---

#### `IAssetCatalog` / `AssetCatalogEntry` (interface + record)

```csharp
public sealed record AssetCatalogEntry(Guid AssetId, string Path);

public interface IAssetCatalog
{
    IEnumerable<AssetCatalogEntry> EnumerateAll();
}
```

`EnumerateAll` is called lazily; implementations may scan the filesystem on each
call.

---

#### `FileSystemAssetCatalog` (sealed class)

```csharp
public sealed class FileSystemAssetCatalog : IAssetCatalog
{
    public FileSystemAssetCatalog(string rootDirectory);
    public IEnumerable<AssetCatalogEntry> EnumerateAll();
}
```

Scans `rootDirectory` recursively for `*.bp.json` files, parses the `AssetId`
property from each JSON root object, and yields `AssetCatalogEntry` records.
Skips unreadable or malformed files silently.

---

#### `EditorState` (sealed class)

```csharp
public sealed class EditorState
{
    public void SetInMemoryAsset(BlueprintAsset asset);
    public BlueprintAsset? GetInMemoryAsset(Guid assetId);
    public void RemoveInMemoryAsset(Guid assetId);
    public IReadOnlyDictionary<Guid, BlueprintAsset> InMemoryAssets { get; }
}
```

Thread-unsafe in-memory overlay. Stores the "live" working copy of each open
asset keyed by `Guid`. `QuickReloadService` reads from here when building sibling
signatures for the compiler.

---

#### `EditorSelectionStore` (sealed class)

```csharp
public sealed class EditorSelectionStore
{
    public BlueprintAsset? SelectedAsset { get; }
    public event Action? OnSelectionChanged;
    public void SelectAsset(BlueprintAsset? asset);
}
```

Raises `OnSelectionChanged` synchronously on every call to `SelectAsset`, even
when `asset` is the same value.

---

#### `DirtyTracker` (sealed class)

```csharp
public sealed class DirtyTracker
{
    public void MarkDirty(Guid assetId);
    public void MarkClean(Guid assetId);
    public bool IsDirty(Guid assetId);
    public IReadOnlySet<Guid> DirtyAssets { get; }
}
```

HashSet-backed. `IsDirty` is O(1). `DirtyAssets` exposes the live set directly --
do not mutate.

---

#### `ReloadSource` (enum) / `ReloadCompletedInfo` (record)

```csharp
public enum ReloadSource
{
    QuickReloadViaApi,
    FullRebuildViaFileWatcher,
}

public sealed record ReloadCompletedInfo(
    ReloadSource Source,
    Guid[]       ReloadedAssetIds,
    string?      DllPath,
    long         DurationMs);
```

Passed to `BlueprintEditorModule.OnReloadCompleted`. `DllPath` is non-null only
for full rebuilds.

---

### Window Classes

#### `AssetBrowserWindow` (sealed)

```csharp
public sealed class AssetBrowserWindow : BlueprintEditorWindowBase
{
    public override string Title => "Asset Browser";
    public IReadOnlyList<AssetCatalogEntry> CatalogEntries { get; }
    public void RefreshCatalog();
    public override void DrawUI();
    public override void OnActivated();   // calls RefreshCatalog()
    public override void OnDeactivated();
}
```

Renders a 4-column ImGui table (Name, Dispatch, Hostings, Status). Supports free-
text filter. Dirty assets are prefixed with `*` and shown with amber color in the
Status column. Double-click opens the asset in the graph editor by calling
`EditorSelectionStore.SelectAsset`.

---

#### `GraphEditorWindow` (sealed)

```csharp
public sealed class GraphEditorWindow : BlueprintEditorWindowBase
{
    public override string Title => "Graph Editor";
    public BlueprintAsset? CurrentAsset { get; }
    public SelectionState  Selection    { get; }   // GraphEditor.SelectionState
    public CommandHistory  Commands     { get; }   // GraphEditor.CommandHistory
    public void OpenAsset(BlueprintAsset asset);
    public override void DrawUI();
    public override void OnDeactivated();   // clears selection
}
```

Toolbar buttons: **Save** (marks current asset clean), **Quick Reload** (disabled
when asset is clean), **Full Rebuild**. Canvas is a child window placeholder ready
for integration with a node-graph rendering library.

---

#### `InspectorWindow` (sealed)

```csharp
public sealed class InspectorWindow : BlueprintEditorWindowBase
{
    public override string Title => "Inspector";
    public override void DrawUI();
}
```

Three tabs: Node (placeholder for selected-node properties), Graph (lists graph
names), Asset (shows name, AssetId, Dispatch, dirty state).

---

#### `PreferencesWindow` (sealed)

```csharp
public sealed class PreferencesWindow : BlueprintEditorWindowBase
{
    public override string Title => "Blueprint Preferences";
    public override void DrawUI();
}
```

Checkbox for `AutoReloadOnSave`, integer input for `HotReloadLogMaxEntries`. Save
button calls `BlueprintEditorPreferences.Save(savePath)`. Reset button restores
`Defaults`.

---

### GraphEditor Namespace

#### `IGraphCommand` (interface)

```csharp
public interface IGraphCommand
{
    string Description { get; }
    void Execute();
    void Undo();
}
```

---

#### `AddNodeCommand` / `DeleteNodeCommand` (sealed classes)

```csharp
public sealed class AddNodeCommand : IGraphCommand
{
    public AddNodeCommand(Graph graph, Node node);
    public string Description { get; }   // "Add Node <id>"
    public void Execute();   // graph.Nodes.Add(node)
    public void Undo();      // graph.Nodes.Remove(node)
}

public sealed class DeleteNodeCommand : IGraphCommand
{
    public DeleteNodeCommand(Graph graph, Node node);
    public string Description { get; }   // "Delete Node <id>"
    public void Execute();   // graph.Nodes.Remove(node)
    public void Undo();      // graph.Nodes.Add(node)
}
```

---

#### `CommandHistory` (sealed)

```csharp
public sealed class CommandHistory
{
    public const int Capacity = 64;
    public int Count { get; }
    public bool CanUndo { get; }
    public bool CanRedo { get; }
    public void Execute(IGraphCommand command);
    public void Undo();
    public void Redo();
    public void Clear();
}
```

Ring-buffer with capacity 64. Executing a new command while `_undoIndex < _count`
discards all redo history (standard linear undo model). When full, the oldest
entry is evicted.

---

#### `SelectionState` (sealed)

```csharp
public sealed class SelectionState
{
    public HashSet<Guid> SelectedNodes { get; }
    public HashSet<Guid> SelectedLinks { get; }
    public void ClearAll();
    public bool IsNodeSelected(Guid nodeId);
    public bool IsLinkSelected(Guid linkId);
    public void SelectNode(Guid nodeId, bool addToSelection = false);
}
```

`SelectNode` with `addToSelection = false` clears both sets first.

---

### Inspector Namespace

#### `IStructEditDrawer<T>` (interface)

```csharp
public interface IStructEditDrawer<T>
{
    bool Draw(string label, ref T value, DrawContext ctx);
}
```

Returns `true` if the call modified `value`. Implementations call ImGui widgets.

---

#### `DrawContext` (sealed record)

```csharp
public sealed record DrawContext(
    bool   IsReadOnly    = false,
    string IdPrefix      = "",
    object? TypeRegistry = null);
```

Passed into every drawer call to communicate rendering constraints and shared
context.

---

#### `DrawerRegistry` (sealed)

```csharp
public sealed class DrawerRegistry
{
    public void Register<T>(IStructEditDrawer<T> drawer);
    public bool TryGet<T>(out IStructEditDrawer<T> drawer);
}
```

Type-keyed dictionary. `Register` overwrites an existing registration for the
same type.

---

#### Primitive Drawers

`FloatDrawer`, `IntDrawer`, `BoolDrawer`, `StringDrawer` -- all implement
`IStructEditDrawer<T>` with stub bodies (ImGui calls commented out, return
`false`). They check `ctx.IsReadOnly` and skip modification when set.

---

### Reload Namespace

#### `QuickReloadService` (sealed)

```csharp
public sealed class QuickReloadService
{
    public IReadOnlyList<BlueprintSignature>? LastSignaturesUsedForTesting { get; }

    public QuickReloadService(
        IAssetCatalog catalog,
        EditorState editorState,
        IOutputConsole outputConsole,
        IBlueprintCompiler compiler,
        AiHotReloadCoordinator coordinator,
        IBlueprintDebugSession? session = null);

    public Task<QuickReloadResult> TriggerAsync(BlueprintAsset asset);
}
```

`TriggerAsync` is synchronous internally (no `await`), wrapped in
`Task.FromResult` for a future-compatible signature. The full 7-step pipeline:

1. Build sibling signatures (in-memory overrides + disk fallback).
2. AST compile via `IBlueprintCompiler.Compile`.
3. Roslyn compile generated C# source to PE + PDB bytes.
4. Load into a new collectible `AssemblyLoadContext`.
5. Clear `HsmActionDispatcher` static state.
6. Invoke `BlueprintRegistrarAttribute` methods into staging registries.
7. Register debug map then call `AiHotReloadCoordinator.ApplyQuickReload`.

Registrar parameter injection enforces two invariants:
- `BlueprintRegistry` is **forbidden** (violates RCU contract).
- `HsmActionDispatcher` is **forbidden** (static class -- must be called directly).

---

#### `FullRebuildService` (sealed)

```csharp
public sealed class FullRebuildService
{
    public bool PendingDrainAfterBuild { get; }

    public FullRebuildService(IOutputConsole outputConsole, string buildTarget = "");

    public async Task<FullRebuildResult> TriggerAsync();
}
```

Runs `dotnet build [buildTarget]` as a child process with redirected stdout.
Streams each output line to `_outputConsole.LogInfo`. Sets
`PendingDrainAfterBuild = true` on success so the caller can trigger a file-
watcher drain pass.

---

#### `QuickReloadResult` / `FullRebuildResult` (records)

```csharp
public sealed record QuickReloadResult(bool Succeeded, string? ErrorMessage, long DurationMs);
public sealed record FullRebuildResult(bool Succeeded, int ExitCode, long DurationMs);
```

---

#### `BlueprintSignatureBuilder` (static class)

```csharp
public static class BlueprintSignatureBuilder
{
    public static BlueprintSignature FromInMemoryAsset(BlueprintAsset asset);
}
```

Projects a live `BlueprintAsset` to a `BlueprintSignature` without any disk I/O.
Computes `BlueprintId` via `BlueprintIdHash.Compute(asset.AssetId)`, sanitizes the
name, and extracts exported function names from graphs of `GraphKind.Function`.

---

### Debug Namespace

#### `BlueprintDebugSession` (sealed, lives in `Hrot.Blueprints.Core.Debug` namespace)

Despite its location in the Editor project, `BlueprintDebugSession` is declared in
`Hrot.Blueprints.Core.Debug`. It is the full production implementation of
`IBlueprintDebugSession`.

Key public members:

```csharp
// Probe sink (IBlueprintProbeSink)
void OnNodeEnter(Entity self, string nodeId);
void OnPinValueChanged<T>(Entity self, string pinId, T value) where T : unmanaged;
void OnPeerCallEnter(Entity entity, string targetAssetName, string targetGraphName);
void OnPeerCallExit(Entity entity);

// Lifecycle
bool IsAttached { get; }
void Detach();

// Breakpoints
BreakpointId SetBreakpoint(Guid assetId, Guid graphId, Guid nodeId);
void ClearBreakpoint(BreakpointId id);
void ClearAllBreakpoints();
IReadOnlyList<Breakpoint> GetBreakpoints();
bool IsAnyBreakpointActive { get; }

// Watches
WatchId AddWatch(Guid assetId, Guid graphId, Guid pinId, string displayName, Type expectedType);
void RemoveWatch(WatchId id);
void ClearAllWatches();
IReadOnlyList<Watch> GetWatches();
bool IsAnyWatchActive { get; }

// Pause state
bool IsPaused { get; }
Breakpoint? PausedAt { get; }
Entity? PausedOnEntity { get; }

// Pause control
void Continue();
void Pause();
void StepOver();
void StepInto();
void StepOut();

// Inspection
BlueprintStateSnapshot? GetCurrentStateSnapshot();
IReadOnlyList<NodeExecuted> GetRecentNodeHistory(int maxCount = 100);
IReadOnlyList<NodeHistoryEntry> GetNodeHistory(Entity entity, int maxCount = 100);

// Debug map registration
void RegisterDebugMap(DebugMap map);
void UnregisterDebugMap(Guid assetId);

// Entity filter
void SetEntityFilter(Entity? entity);
Entity? GetEntityFilter();

// Active entity tracking
IReadOnlyList<Entity> GetActiveEntities(Guid assetId);

// Hot reload hooks
void OnHotReloadBegin();
void OnHotReloadCompleted(Guid[] reloadedAssetIds);

// PDB locator
void RegisterPdbLocator(Guid assetId, Func<string> pdbPathResolver);

// Events
event Action<BreakpointHit>? OnBreakpointHit;
event Action? OnSessionStateChanged;
event Action<Guid>? OnBreakpointListChanged;
event Action<NodeExecuted>? OnNodeExecuted;         // IBlueprintDebugSession explicit
event Action<PinValueChanged>? OnPinValueChangedEvent; // IBlueprintDebugSession explicit
```

Internal storage uses two parallel dictionaries for O(1) breakpoint and watch
lookup: one keyed by `BreakpointId`/`WatchId` (for management) and one keyed by
the node/pin string representation (for hot probe path). Per-entity execution
histories are stored in `ExecutionHistory` ring buffers; the history size is
bounded by `BlueprintEditorPreferences.NodeHistorySize` (default 64).

Soft-pause semantics (Patch 1): `HandleBreakpointHit` sets `_isPaused = true` and
calls `_timeController.RequestPause()` but returns immediately without blocking
the simulation thread. The current tick completes normally; the engine pauses at
the next frame boundary.

---

#### `DebugPanelWindow` (sealed)

```csharp
public sealed class DebugPanelWindow : BlueprintEditorWindowBase
{
    public override string Title => _session.IsPaused ? "Debug [PAUSED]" : "Debug";
}
```

Title dynamically reflects pause state. `DrawUI` is a stub (Slice 1 placeholder).

---

#### `WatchPanelWindow` (sealed)

```csharp
public sealed class WatchPanelWindow : BlueprintEditorWindowBase
{
    public override string Title => "Watches";
    public override void OnActivated();    // subscribes to OnPinValueChangedEvent
    public override void OnDeactivated();  // unsubscribes
}
```

Subscribes/unsubscribes from `IBlueprintDebugSession.OnPinValueChangedEvent` so
the table row data stays live only while the window is visible.

---

#### `CallstackWindow` (sealed)

```csharp
public sealed class CallstackWindow : BlueprintEditorWindowBase
{
    public override string Title => "Callstack";
}
```

Renders node execution trail for the selected entity. `DrawUI` is a stub.

---

#### `HotReloadLogWindow` (sealed)

```csharp
public sealed class HotReloadLogWindow : BlueprintEditorWindowBase
{
    public HotReloadLogModel Model { get; }
    public override string Title => "Hot Reload Log";
    public void OnReloadCompleted(ReloadCompletedInfo info);
    public void OnReloadFailed(string message, ReloadSource source);
}
```

`OnReloadCompleted` creates a success `ReloadLogEntry`; `OnReloadFailed` creates a
failure entry. The model caps entries at 1000.

---

#### `HotReloadLogModel` (sealed)

```csharp
public sealed class HotReloadLogModel
{
    public const int MaxEntries = 1000;
    public IReadOnlyCollection<ReloadLogEntry> Entries { get; }
    public int Count { get; }
    public void AddEntry(ReloadLogEntry entry);
    public void Clear();
}
```

Queue-based ring buffer: enqueue, then dequeue oldest when over limit.

---

#### `ReloadLogEntry` (sealed record)

```csharp
public sealed record ReloadLogEntry(
    DateTime     Timestamp,
    ReloadSource Source,
    bool         Succeeded,
    string?      Message,
    long         DurationMs);
```

---

#### `MasterSyncTimeControllerAdapter` (sealed)

```csharp
public sealed class MasterSyncTimeControllerAdapter : IBlueprintTimeController
{
    public MasterSyncTimeControllerAdapter(MasterSyncController masterSync);
    public bool IsPausedByDebugger { get; }
    public void RequestPause();       // SwitchToDeterministic(empty roster)
    public void RequestResume();      // SwitchToContinuous()
    public void RequestStepOneTick(); // Step(1/60 s) when paused
}
```

Bridges the engine's `MasterSyncController` to `IBlueprintTimeController`.
Transitioning to deterministic mode with an empty slave roster pauses the local
simulation clock without waiting for network acknowledgements.

---

## Dependencies

### Project References

| Reference                   | Purpose                                                         |
|-----------------------------|-----------------------------------------------------------------|
| `Hrot.Blueprints.Core`      | Asset model (`BlueprintAsset`, `Graph`, `Node`); compiler interfaces (`IBlueprintCompiler`, `CompileOptions`); debug interfaces (`IBlueprintDebugSession`, `IBlueprintProbeSink`, `DebugMap`); catalog helpers (`BlueprintSignatureParser`) |
| `Fdp.Core`                  | Entity type, `ISimulationView`, core primitives                  |
| `Fdp.Presentation`          | ImGui host (ImGuiNET bindings, render loop integration)          |
| `Fdp.Toolkits`              | `AiHotReloadCoordinator`, `BlueprintRegistry`, `BehaviorRegistry`, `BlueprintRegistryStaging`, `BlueprintIdHash`, `HsmActionDispatcher`, `MasterSyncController`, time controller types |

### NuGet Packages

| Package                                      | Version | Purpose                           |
|----------------------------------------------|---------|-----------------------------------|
| `Microsoft.Extensions.DependencyInjection`   | 8.0.0   | DI container for service wiring   |

`Microsoft.CodeAnalysis` (Roslyn) is a transitive dependency via
`Hrot.Blueprints.Core` (the compiler uses Roslyn). `IOutputConsole.LogDiagnostic`
accepts `Microsoft.CodeAnalysis.Diagnostic` directly.

---

## Usage Examples

### Example 1 -- Bootstrapping the Editor with DI

```csharp
// Host-side setup: configure the DI container and activate the module.
var services = new ServiceCollection();

// Register editor singletons (catalog root points to the .bp.json asset folder).
services.AddBlueprintEditor(assetRootDirectory: @"D:\Project\Assets\Blueprints");

// Host provides its own IWindowRegistrar and IOutputConsole implementations.
services.AddSingleton<IWindowRegistrar, MyWindowRegistrar>();
services.AddSingleton<IOutputConsole, MyOutputConsole>();

// Register the reload services (provide compiler and coordinator from Hrot.Blueprints.Core).
services.AddSingleton<IBlueprintCompiler, BlueprintCompiler>();
services.AddSingleton<AiHotReloadCoordinator>();
services.AddSingleton<QuickReloadService>();
services.AddSingleton<FullRebuildService>(sp =>
    new FullRebuildService(sp.GetRequiredService<IOutputConsole>(), buildTarget: ""));

var provider = services.BuildServiceProvider();

// Instantiate and configure windows.
var module = provider.GetRequiredService<BlueprintEditorModule>();
module.RegisterWindow(new AssetBrowserWindow(
    provider.GetRequiredService<IAssetCatalog>(),
    provider.GetRequiredService<EditorSelectionStore>(),
    provider.GetRequiredService<DirtyTracker>(),
    provider.GetRequiredService<EditorState>()));
module.RegisterWindow(provider.GetRequiredService<GraphEditorWindow>());
module.RegisterWindow(new InspectorWindow(
    provider.GetRequiredService<EditorSelectionStore>(),
    provider.GetRequiredService<DirtyTracker>(),
    new DrawerRegistry()));

// Activate the module (registers menu entries, calls OnActivated on all windows).
module.OnEditorActivated();

// Per-frame loop (called by the ImGui host):
// module.DrawAllWindows();
```

---

### Example 2 -- Triggering a Quick Reload from Code

```csharp
// Retrieve services.
var quickReload = provider.GetRequiredService<QuickReloadService>();
var editorState = provider.GetRequiredService<EditorState>();
var dirtyTracker = provider.GetRequiredService<DirtyTracker>();
var outputConsole = provider.GetRequiredService<IOutputConsole>();

// Assume asset has been edited and stored in EditorState.
BlueprintAsset asset = editorState.GetInMemoryAsset(myAssetId)!;

// Trigger an in-process hot reload.
var result = await quickReload.TriggerAsync(asset);

if (result.Succeeded)
{
    dirtyTracker.MarkClean(asset.AssetId);
    outputConsole.LogInfo($"Reload succeeded in {result.DurationMs} ms.");
}
else
{
    outputConsole.LogError($"Reload failed: {result.ErrorMessage}");
}
```

---

### Example 3 -- Setting a Breakpoint and Stepping

```csharp
// Obtain the debug session (typically injected as IBlueprintDebugSession).
IBlueprintDebugSession session = provider.GetRequiredService<IBlueprintDebugSession>();

// Register the debug map produced by the compiler (done automatically by QuickReloadService).
// session.RegisterDebugMap(compilationResult.DebugMap);

// Set a breakpoint on a specific node in a graph.
Guid assetId = myBlueprintAsset.AssetId;
Guid graphId = myBlueprintAsset.Graphs[0].Id;
Guid nodeId  = myBlueprintAsset.Graphs[0].Nodes[0].Id;
BreakpointId bpId = session.SetBreakpoint(assetId, graphId, nodeId);

// Subscribe to the hit event to update the debug panel.
session.OnBreakpointHit += hit =>
{
    Console.WriteLine($"Breakpoint hit at node {hit.NodeId} by entity {hit.Entity}");
};

// -- later, after the simulation fires OnNodeEnter and the session pauses --
if (session.IsPaused)
{
    // Step to the next node in the same call depth.
    session.StepOver();
    // Or descend into calls:
    // session.StepInto();
    // Or resume:
    // session.Continue();
}

// Clear the breakpoint when done.
session.ClearBreakpoint(bpId);
```

---

### Example 4 -- Registering a Custom Property Drawer

```csharp
// Implement a drawer for a custom value type.
public sealed class Vector2Drawer : IStructEditDrawer<System.Numerics.Vector2>
{
    public bool Draw(string label, ref System.Numerics.Vector2 value, DrawContext ctx)
    {
        if (ctx.IsReadOnly) return false;
        return ImGui.InputFloat2(label, ref value);
    }
}

// Register with the DrawerRegistry singleton.
var registry = new DrawerRegistry();
registry.Register<System.Numerics.Vector2>(new Vector2Drawer());

// The InspectorWindow uses TryGet<T> when rendering node properties:
var ctx = new DrawContext(IsReadOnly: false, IdPrefix: "node_");
if (registry.TryGet<System.Numerics.Vector2>(out var drawer))
{
    System.Numerics.Vector2 pos = node.Position;
    if (drawer.Draw("Position", ref pos, ctx))
        node.Position = pos;
}
```

---

### Example 5 -- Persisting and Loading Preferences

```csharp
string prefsPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "MyApp", "blueprint_editor_prefs.json");

// Load (or get defaults if missing/malformed).
var prefs = BlueprintEditorPreferences.Load(prefsPath);

// Adjust a value.
prefs.AutoReloadOnSave       = true;
prefs.NodeHistorySize        = 128;
prefs.HotReloadLogMaxEntries = 500;

// Save back to disk.
prefs.Save(prefsPath);
```

---

## Best Practices

### 1. Always Register Windows Before Activating the Module

`OnEditorActivated` iterates `_windows` and calls `RegisterMenuEntry` +
`OnActivated` for each entry. Windows added after activation will not have their
menu entries registered and will not receive `OnActivated`.

### 2. Do Not Pass `BlueprintRegistry` into Blueprint Registrars

`QuickReloadService` enforces this at runtime and throws
`HotReloadRegistrarException`. The reason is the RCU (read-copy-update) contract:
staging buffers (`BlueprintRegistryStaging`, `BehaviorRegistry`) must be committed
atomically by `AiHotReloadCoordinator.ApplyQuickReload`. Direct access to the live
registry during registrar invocation would corrupt the atomic handoff.

### 3. Use In-Memory Asset Overrides for Sibling Signatures

When a Quick Reload compiles asset A, the compiler needs the public signatures of
all sibling assets (to resolve cross-blueprint calls). `QuickReloadService`
prefers `EditorState.GetInMemoryAsset` over the disk copy for any asset that has
in-memory changes. Always call `EditorState.SetInMemoryAsset` after modifying an
asset so siblings see the updated signature.

### 4. Detach the Debug Session Before Disposing

`BlueprintDebugSession.Detach()` resumes simulation (if paused), unregisters
`DebugProbe.Sink`, and clears all breakpoints, watches, and history. Skipping
`Detach` may leave the simulation permanently paused.

### 5. Prefer `OnActivated` / `OnDeactivated` for Event Subscriptions

`WatchPanelWindow` demonstrates the correct pattern: subscribe to
`OnPinValueChangedEvent` in `OnActivated` and unsubscribe in `OnDeactivated`.
Subscribing in a constructor and never unsubscribing leaks the subscriber
reference and causes spurious updates after the window is hidden.

### 6. Handle `FullRebuildService.PendingDrainAfterBuild`

After a successful full rebuild the caller is responsible for draining the file
watcher to pick up the new DLL. Check `PendingDrainAfterBuild` after
`TriggerAsync` returns and reset it after the drain pass.

### 7. Scope `DrawContext.IdPrefix` to Avoid ImGui ID Collisions

ImGui uses label strings as widget IDs. When the same drawer is called for
multiple nodes in the same frame, set `IdPrefix` to the node's Guid string to
prevent collisions:

```csharp
var ctx = new DrawContext(IdPrefix: $"node_{node.Id:N}_");
```

---

## Related Projects

| Project                       | Relationship                                                    |
|-------------------------------|-----------------------------------------------------------------|
| `Hrot.Blueprints.Core`        | Provides the asset model, compiler pipeline, and debug session interface that this editor implements and drives. **Mandatory dependency.** |
| `Hrot.Blueprints.Compiler`    | The Roslyn-based code generation backend invoked by `QuickReloadService`. Part of `Hrot.Blueprints.Core`. |
| `Fdp.Presentation`            | ImGui host library. Provides the `ImGuiNET` bindings and the per-frame render loop that calls `DrawAllWindows()`. |
| `Fdp.Toolkits`                | Supplies `AiHotReloadCoordinator` (atomic ALC swap), `BlueprintRegistry`, `BehaviorRegistry`, `HsmActionDispatcher`, and the `MasterSyncController` wrapped by `MasterSyncTimeControllerAdapter`. |
| `Fdp.Core`                    | Core entity system and simulation view types consumed by `BlueprintDebugSession`. |
| `NodeEdit` (ExtDeps)          | Node-graph canvas rendering library intended for future integration into `GraphEditorWindow`. Currently the canvas is a placeholder child window. |
| `StructEdit` (ExtDeps)        | Struct property editing library; the `Inspector/` subsystem (`IStructEditDrawer<T>`, `DrawerRegistry`) mirrors its API for Blueprint-specific property display. |
