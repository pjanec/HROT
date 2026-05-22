# Host Contract — Interfaces the Host Implements

This file enumerates every interface the host must (or may) implement, with a high-level description. Full source with XML doc comments lives in `04_kernel_code/K02_host_interfaces.md`; this file is the architectural index.

## Mandatory (editor cannot run without these)

| Interface | Purpose |
|---|---|
| `IGraphModel` | Read access to nodes, links, comments, sub-graphs in one graph |
| `INodeModel` | One node's properties (title, position, pins, etc.) |
| `IPinModel` | One pin's properties (name, type, direction, default value) |
| `ILinkModel` | One link's endpoints + reroutes |
| `ICommentModel` | One comment's position, color, text |
| `INodeCatalog` | All available node kinds (for the search popup) |
| `ITypeSystem` | Type compatibility, pin colors, default values per type |
| `ILinkValidator` | Whether a wire from pin A to pin B is allowed |
| `IGraphCommandSink` | Applies mutation commands |
| `IEditorHostServices` | Misc services: time source, paths, asset lookup |

## Optional (degrades gracefully when absent)

| Interface | Purpose | When absent |
|---|---|---|
| `IPickerRegistry` | Registers picker sources beyond the built-ins | Built-in source set is fine for many hosts |
| `IClipboard` | OS clipboard interop | Falls back to in-process clipboard (paste only works within this editor instance) |
| `IIconProvider` | Resolves icon keys to texture handles | Falls back to text label / default glyph |
| `IDiagnosticsSink` | Compile errors and warnings | No error overlays shown |
| `IDebugSession` | Pause/step/breakpoints | No debug visuals; F-keys do nothing |
| `IBlueprintDebugSession` | Asset-aware debug (your engine) | Falls back to `IDebugSession` if available |
| `IInputSource` | Custom keyboard handling | Falls back to ImGui's input |
| `IEditorTheme` | Colors, fonts | Built-in default theme used |
| `IExternalChangeNotifier` | File-watcher integration | No external-change banner |
| `IEditorReadOnlyState` | Lock state | Always editable |
| `IMyBlueprintModel` | Asset's declarations (variables, functions, etc.) | My Blueprint panel hidden |
| `IDetailsViewProvider` | Custom details panels | Default reflection-based property tree used |
| `IGraphSearchProvider` | Extends what's searchable in find-in-graph | Default name/type-based search used |

## Group 1 — Model interfaces

### `IGraphModel`

Read-only access to one graph's contents. The host owns the actual data. Editor reads, never writes directly — all mutations go through `IGraphCommandSink`.

Key responsibilities:

- Enumerate nodes / links / comments.
- Resolve IDs to model objects.
- Notify on changes via `Changed` and `ChangedDetailed` events.

### `INodeModel`, `IPinModel`, `ILinkModel`, `ICommentModel`

See `data_model.md` for their property surfaces. Each represents one entity in the graph.

### `INodeCatalog`

Lists all available node kinds the user can create. Each entry includes:

- `NodeKindKey`.
- Display name, category, keywords (for search).
- Pin schema (what input/output pins this kind will produce).
- Description, icon key, deprecation flag.

```csharp
public interface INodeCatalog
{
    IReadOnlyList<NodeCatalogEntry> All { get; }
    NodeCatalogEntry? Get(NodeKindKey key);
    IEnumerable<NodeCatalogEntry> Search(string query);
    event Action? Changed;            // catalog modified (hot reload, etc.)
}

public sealed record NodeCatalogEntry(
    NodeKindKey Kind,
    string DisplayName,
    string? Category,
    string? Description,
    string? IconKey,
    Vector4 HeaderColor,
    bool IsDeprecated,
    IReadOnlyList<NodeCatalogPinDescriptor> InputPins,
    IReadOnlyList<NodeCatalogPinDescriptor> OutputPins,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Aliases,
    IReadOnlyDictionary<string, string>? Metadata);
```

The catalog is the source of truth for "what nodes can the user add."

### `ITypeSystem`

Maps `TypeKey` to visual + behavioral attributes:

- Pin color (for wires).
- Default value (for unconnected input pins).
- Is-container (array/map detection for split-pin behavior).
- Type display name (for tooltips and pickers).

```csharp
public interface ITypeSystem
{
    Vector4 GetPinColor(TypeKey type);
    string GetDisplayName(TypeKey type);
    object? GetDefaultValue(TypeKey type);
    bool IsContainer(TypeKey type, out TypeKey? elementType);
    bool IsStruct(TypeKey type, out IReadOnlyList<(string Name, TypeKey Type)>? fields);
    bool IsEnum(TypeKey type, out IReadOnlyList<(string Name, long Value)>? values);

    IEnumerable<TypeKey> AllTypes { get; }
}
```

### `ILinkValidator`

Decides if a wire can connect two pins. Returns one of three results.

```csharp
public interface ILinkValidator
{
    LinkValidationResult Validate(IPinModel from, IPinModel to);
}

public abstract record LinkValidationResult
{
    public sealed record Valid : LinkValidationResult;
    public sealed record ValidWithCast(NodeKindKey CastNodeKind) : LinkValidationResult;
    public sealed record Invalid(string Reason) : LinkValidationResult;
}
```

Three results match Unreal's behavior:

- `Valid`: connect directly.
- `ValidWithCast`: editor auto-inserts a cast node between them.
- `Invalid`: prevent connection, show reason in tooltip.

## Group 2 — Mutation

### `IGraphCommandSink`

The single point through which the editor mutates the model.

```csharp
public interface IGraphCommandSink
{
    CommandResult Apply(GraphCommand command);
    CommandResult Apply(IReadOnlyList<GraphCommand> batch);
}

public sealed record CommandResult(
    bool Success,
    string? ErrorMessage,
    GraphCommand? InverseCommand);     // for undo
```

Every mutation is a `GraphCommand` record. The host validates and applies; on success, the inverse command lets the editor's undo stack reverse the operation.

The full `GraphCommand` discriminated union is in `K02_host_interfaces.md`. Examples: `AddNode`, `RemoveNodes`, `MoveNodes`, `AddLink`, `RemoveLinks`, `SetPinDefault`, `AddComment`, `SetCommentText`, `AddReroute`, `RemoveReroute`, `Batch`.

## Group 3 — UI extensibility

### `IPickerRegistry`

Registers `IPickerSource<T>` implementations by string key. See `C_generic_picker.md` for the picker architecture.

### `IClipboard`

OS clipboard interop with custom MIME-type payload support.

```csharp
public interface IClipboard
{
    void SetPayload(string mimeType, ReadOnlySpan<byte> data);
    bool TryGetPayload(string mimeType, out byte[] data);
    bool HasPayload(string mimeType);
    void Clear();
}
```

Standard payload type used by the editor: `"application/x-node-editor-selection"` (the JSON of copied nodes/links/comments).

### `IIconProvider`

Resolves icon keys to texture handles for ImGui drawing.

```csharp
public interface IIconProvider
{
    nint? GetIconTexture(string iconKey, out Vector2 sizePx);
}
```

ImGui uses `nint` (the texture ID) — host returns whatever its renderer wraps.

### `IEditorTheme`

All colors, font sizes, spacing. Built-in default in `K08_constants_and_theme.md`.

## Group 4 — Diagnostics and debug

### `IDiagnosticsSink`

Surface compile errors, warnings, info messages. See `D10_hot_reload_indicators.md §D.10.3` for visuals.

```csharp
public interface IDiagnosticsSink
{
    IReadOnlyList<Diagnostic> Current { get; }
    event Action? Changed;
}
```

### `IDebugSession`

Generic debug protocol — pause, step, breakpoint set, watched values.

```csharp
public interface IDebugSession
{
    bool IsAttached { get; }
    bool IsPaused { get; }
    NodeId? CurrentNode { get; }

    void Continue();
    void StepOver();
    void StepInto();
    void StepOut();

    bool HasBreakpoint(NodeId id);
    void SetBreakpoint(NodeId id);
    void ClearBreakpoint(NodeId id);
    IReadOnlyList<NodeId> AllBreakpoints { get; }

    bool IsWatched(PinId id);
    object? GetWatchedValue(PinId id);
    void Watch(PinId id);
    void Unwatch(PinId id);
    IReadOnlyList<PinId> AllWatched { get; }

    event Action? StateChanged;
    event Action<RecentExecutionEvent>? RecentlyExecuted;
}

public sealed record RecentExecutionEvent(
    LinkId Link,
    DateTimeOffset Timestamp);
```

`IBlueprintDebugSession` (an extended interface) can add asset-aware features: `IsStale` per breakpoint, hit counts, custom variables.

## Group 5 — Misc services

### `IInputSource`

Abstracts keyboard / mouse polling so the editor can be tested headlessly. Default implementation is an ImGui pass-through.

```csharp
public interface IInputSource
{
    bool IsKeyPressed(EditorKey key);
    bool IsKeyDown(EditorKey key);
    bool IsKeyReleased(EditorKey key);
    KeyModifiers Modifiers { get; }
    Vector2 MousePosition { get; }
    Vector2 MouseDelta { get; }
    float MouseWheel { get; }
    bool IsMouseButtonPressed(int button);
    bool IsMouseButtonDown(int button);
    bool IsMouseButtonReleased(int button);
    bool IsCapturedByUi { get; }      // true when ImGui owns the input this frame
}
```

`IsCapturedByUi` is what lets the canvas yield to mini-editor focus, picker focus, etc.

### `IEditorHostServices`

Catch-all for misc services. Time source (for animations), session-state path, "current asset is dirty" callback, etc.

```csharp
public interface IEditorHostServices
{
    DateTimeOffset Now { get; }
    string SessionStatePath { get; }
    void NotifyDirty(bool dirty);

    string AssetId { get; }            // stable identifier for bookmarks etc.
}
```

### `IExternalChangeNotifier`

File-watcher events. See `D10_hot_reload_indicators.md §D.10.1`.

### `IEditorReadOnlyState`

Asset lock state. See `D10_hot_reload_indicators.md §D.10.7`.

## Group 6 — Outliner and details

### `IMyBlueprintModel`

The host exposes the asset's declarations (variables, functions, etc.) for the My Blueprint panel. See `D6_my_blueprint_panel.md` for full UX.

### `IDetailsViewProvider`

Per-target-type custom property editors. See `D7_details_panel.md` for full UX.

### `IGraphSearchProvider`

Extends `editor.find-in-graph` with additional searchable text per node. See `D4_find_in_graph.md`.

## Interaction summary

Putting these together, the editor's main loop is roughly:

```
each frame:
    inputSource.PollInputs()
    interactionDispatcher.Update(view)
    if interactionDispatcher.ProducedCommands:
        for cmd in commands:
            commandSink.Apply(cmd) → host applies, fires Changed
    pickerWindow.UpdateAndDraw()
    canvasRenderer.Draw(view, theme, iconProvider, diagSink, debugSession)
    panels.Draw()
    indicators.Refresh()
```

Each `Apply` may trigger a `Changed` event, which the editor receives and uses to invalidate caches before the next frame.
