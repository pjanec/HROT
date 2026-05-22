# Kernel 01 — Host Contract Interfaces

These interfaces live in `NodeEditor.Core/Interfaces/`. They define the
boundary between the editor and any host.

---

## File: `NodeEditor.Core/Interfaces/IGraphModel.cs`

```csharp
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Read-only view of a graph's data. Implemented by the host. The editor
/// never mutates this directly; mutations go through <see cref="IGraphCommandSink"/>.
/// </summary>
public interface IGraphModel
{
    /// <summary>Stable identifier for this graph.</summary>
    GraphId Id { get; }

    /// <summary>Display name shown in tabs and breadcrumbs.</summary>
    string DisplayName { get; }

    /// <summary>Descriptor for the kind of graph (event graph, function body, …).</summary>
    GraphKindDescriptor Kind { get; }

    /// <summary>All nodes currently in this graph.</summary>
    IReadOnlyCollection<INodeModel> Nodes { get; }

    /// <summary>All links currently in this graph.</summary>
    IReadOnlyCollection<ILinkModel> Links { get; }

    /// <summary>All comment boxes currently in this graph.</summary>
    IReadOnlyCollection<ICommentModel> Comments { get; }

    /// <summary>Find a node by id, or null if not present.</summary>
    INodeModel? FindNode(NodeId id);

    /// <summary>Find a pin by id, or null if not present.</summary>
    IPinModel? FindPin(PinId id);

    /// <summary>Find a link by id, or null if not present.</summary>
    ILinkModel? FindLink(LinkId id);

    /// <summary>
    /// Raised when graph data changes externally. The editor subscribes and
    /// updates view state (selection, viewport hold, badges, undo invalidation).
    /// </summary>
    event Action<GraphChangeNotification>? Changed;
}

/// <summary>Descriptor for the kind of graph (event/function/macro/…).</summary>
public sealed record GraphKindDescriptor(
    string Id,
    string DisplayName,
    bool AllowsLatent,
    bool RequiresEntryNode);

/// <summary>Payload describing what changed in a graph.</summary>
public sealed record GraphChangeNotification(
    GraphChangeKind Kind,
    IReadOnlySet<NodeId>? AffectedNodes,
    IReadOnlySet<LinkId>? AffectedLinks,
    string? Reason);

/// <summary>Coarse classification of a graph change.</summary>
public enum GraphChangeKind
{
    NodesAdded,
    NodesRemoved,
    NodesModified,
    NodesMoved,
    LinksAdded,
    LinksRemoved,
    VariablesChanged,
    Wholesale,
}
```

---

## File: `NodeEditor.Core/Interfaces/INodeModel.cs`

```csharp
using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Read-only view of a single node. Implemented by host.
/// </summary>
public interface INodeModel
{
    /// <summary>Stable id.</summary>
    NodeId Id { get; }

    /// <summary>The kind of node, used by catalog lookups and Details panel routing.</summary>
    NodeKindKey Kind { get; }

    /// <summary>Display title shown in the header.</summary>
    string Title { get; }

    /// <summary>Optional subtitle line under the title.</summary>
    string? Subtitle { get; }

    /// <summary>Coarse category, drives header color and icon.</summary>
    NodeCategory Category { get; }

    /// <summary>Canvas position of the node's top-left corner.</summary>
    Vector2 Position { get; }

    /// <summary>Explicit size override; if null, the editor auto-sizes based on content.</summary>
    Vector2? SizeOverride { get; }

    /// <summary>Bit flags for current state (disabled, executing, …).</summary>
    NodeState State { get; }

    /// <summary>Tooltip shown when hovering the node's status icons.</summary>
    string? StatusTooltip { get; }

    /// <summary>Whether the node is rendered collapsed.</summary>
    bool IsCollapsed { get; }

    /// <summary>Whether advanced pins are shown (otherwise hidden behind disclosure).</summary>
    bool ShowAdvancedPins { get; }

    /// <summary>The node's pins in declaration order.</summary>
    IReadOnlyList<IPinModel> Pins { get; }
}
```

---

## File: `NodeEditor.Core/Interfaces/IPinModel.cs`

```csharp
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>Read-only view of a single pin on a node.</summary>
public interface IPinModel
{
    /// <summary>Stable id.</summary>
    PinId Id { get; }

    /// <summary>Id of the owning node.</summary>
    NodeId OwnerNodeId { get; }

    /// <summary>Display label of the pin.</summary>
    string Label { get; }

    /// <summary>Input vs output side.</summary>
    PinDirection Direction { get; }

    /// <summary>Execution-control vs typed-data.</summary>
    PinKind Kind { get; }

    /// <summary>Type key; null for Exec pins.</summary>
    TypeKey? Type { get; }

    /// <summary>Visual shape (Circle for single data, Diamond for array, …).</summary>
    PinShape Shape { get; }

    /// <summary>True if the pin is "advanced" (hidden behind disclosure by default).</summary>
    bool IsAdvanced { get; }

    /// <summary>True if the pin is optional (rendered with subdued styling).</summary>
    bool IsOptional { get; }

    /// <summary>Tooltip when hovering this pin.</summary>
    string? Tooltip { get; }

    /// <summary>
    /// Default value for input data pins when no wire is connected;
    /// null for exec pins, output pins, or input pins with no editor.
    /// </summary>
    IPinDefaultValue? Default { get; }

    /// <summary>
    /// True if this pin accepts multiple simultaneous connections.
    /// Computed; not stored.
    /// </summary>
    bool AcceptsMultipleConnections =>
        (Direction == PinDirection.Output && Kind == PinKind.Data) ||
        (Direction == PinDirection.Input && Kind == PinKind.Exec);
}

/// <summary>
/// Opaque container for a pin's default value. The actual value is
/// retrieved via the registered <c>IPinDefaultValueEditor</c> for the
/// pin's type.
/// </summary>
public interface IPinDefaultValue
{
    /// <summary>The current value (boxed). Type matches the pin's TypeKey.</summary>
    object? Value { get; }

    /// <summary>Metadata controlling editor presentation (range, units, …).</summary>
    PinDefaultMetadata Metadata { get; }
}

/// <summary>Metadata controlling how a default-value editor presents itself.</summary>
public sealed record PinDefaultMetadata(
    double? RangeMin,
    double? RangeMax,
    double? Step,
    string? Units,
    string? PickerSourceKey,
    string? PlaceholderText,
    bool ClampToRange);
```

---

## File: `NodeEditor.Core/Interfaces/ILinkModel.cs`

```csharp
using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>Read-only view of a single wire (link) connecting two pins.</summary>
public interface ILinkModel
{
    /// <summary>Stable id.</summary>
    LinkId Id { get; }

    /// <summary>Source pin (output side).</summary>
    PinId FromPin { get; }

    /// <summary>Target pin (input side).</summary>
    PinId ToPin { get; }

    /// <summary>Wire style (solid/dashed/etc).</summary>
    LinkStyle Style { get; }

    /// <summary>
    /// Reroute waypoint positions, in canvas coordinates, ordered along the
    /// wire from source to target. Empty if no reroutes.
    /// </summary>
    IReadOnlyList<Vector2> Waypoints { get; }
}

/// <summary>Wire rendering style.</summary>
public enum LinkStyle
{
    Solid,
    Dashed,
}
```

---

## File: `NodeEditor.Core/Interfaces/ICommentModel.cs`

```csharp
using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>Read-only view of a comment box.</summary>
public interface ICommentModel
{
    /// <summary>Stable id.</summary>
    CommentId Id { get; }

    /// <summary>Comment text. May contain '\n' for multi-line.</summary>
    string Text { get; }

    /// <summary>Top-left canvas position.</summary>
    Vector2 Position { get; }

    /// <summary>Size in canvas units.</summary>
    Vector2 Size { get; }

    /// <summary>
    /// Color (RGBA). Header strip uses full alpha; body uses ~20% alpha.
    /// </summary>
    Vector4 Color { get; }

    /// <summary>Higher draws on top.</summary>
    int ZOrder { get; }

    /// <summary>If true, dragging the comment moves enclosed nodes too.</summary>
    bool MoveWithContents { get; }
}
```

---

## File: `NodeEditor.Core/Interfaces/ILinkValidator.cs`

```csharp
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Validates whether two pins can be linked. The host provides this.
/// The editor consults it during wire-drag to highlight valid/invalid drops
/// and on commit to enforce rules.
/// </summary>
public interface ILinkValidator
{
    /// <summary>Validate a proposed link.</summary>
    LinkValidationResult Validate(PinId from, PinId to);
}

/// <summary>Outcome of validating a proposed link.</summary>
public readonly record struct LinkValidationResult(
    LinkValidity Verdict,
    string? Reason,
    bool RequiresCast,
    NodeKindKey? AutoInsertCast);

/// <summary>Validity classes.</summary>
public enum LinkValidity
{
    /// <summary>Cannot be connected.</summary>
    Invalid,

    /// <summary>Can be connected directly.</summary>
    Valid,

    /// <summary>Connectable only by inserting a cast node first.</summary>
    ValidWithCast,
}
```

---

## File: `NodeEditor.Core/Interfaces/INodeCatalog.cs`

```csharp
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Catalog of all node kinds known to the host. Used by the search popup
/// and picker to populate "Add Node" lists.
/// </summary>
public interface INodeCatalog
{
    /// <summary>All registered node kinds.</summary>
    IReadOnlyList<NodeCatalogEntry> All { get; }

    /// <summary>Top-level categories used for grouping.</summary>
    IReadOnlyList<NodeCategoryDescriptor> Categories { get; }

    /// <summary>Search by free text and optional filters.</summary>
    IReadOnlyList<NodeCatalogEntry> Query(NodeSearchQuery q);

    /// <summary>
    /// Search filtered by pin context: source pin's direction and type,
    /// to support "drag wire onto empty canvas → only compatible nodes".
    /// </summary>
    IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q);
}

/// <summary>One entry in the catalog (corresponds to a node kind).</summary>
public sealed record NodeCatalogEntry(
    NodeKindKey Kind,
    string DisplayName,
    string? Description,
    string? CategoryPath,
    IReadOnlyList<string> Keywords,
    string? IconKey,
    bool IsPure,
    bool IsLatent,
    bool IsDeprecated,
    IReadOnlyList<PinSignature> Inputs,
    IReadOnlyList<PinSignature> Outputs);

/// <summary>Signature of a single pin used at catalog lookup time.</summary>
public sealed record PinSignature(
    string Label,
    PinKind Kind,
    TypeKey? Type,
    bool IsWildcard);

/// <summary>Descriptor for a top-level catalog category.</summary>
public sealed record NodeCategoryDescriptor(
    string Path,
    string DisplayName,
    string? IconKey);

/// <summary>Search query for the catalog.</summary>
public sealed record NodeSearchQuery(
    string Text,
    string? CategoryFilter = null,
    TypeKey? TypeFilter = null,
    bool IncludeDeprecated = false);

/// <summary>Query for "what can connect to this pin?"</summary>
public sealed record PinContextQuery(
    PinId SourcePin,
    PinDirection SourceDirection,
    PinKind SourceKind,
    TypeKey? SourceType,
    string Text);
```

---

## File: `NodeEditor.Core/Interfaces/ITypeSystem.cs`

```csharp
using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Host-defined type system. Provides display info, colors, shapes, and
/// compatibility rules for typed pins.
/// </summary>
public interface ITypeSystem
{
    /// <summary>Try to fetch display info for a type key.</summary>
    bool TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info);

    /// <summary>Pin color for the given data type.</summary>
    Vector4 GetPinColor(TypeKey key);

    /// <summary>Pin shape for the given type and container kind.</summary>
    PinShape GetPinShape(TypeKey key, ContainerKind container);

    /// <summary>Get the registered default-value editor for the type, if any.</summary>
    IPinDefaultValueEditor? GetDefaultEditor(TypeKey key);

    /// <summary>True if a value of type <paramref name="from"/> can be used where <paramref name="to"/> is expected.</summary>
    bool AreCompatible(TypeKey from, TypeKey to);

    /// <summary>True if the compatibility is implicit (no cast node needed).</summary>
    bool IsImplicitCast(TypeKey from, TypeKey to);
}

/// <summary>Display info for a type.</summary>
public sealed record TypeDisplayInfo(
    string DisplayName,
    string? Description,
    string? IconKey);

/// <summary>Editor for a pin's default value.</summary>
public interface IPinDefaultValueEditor
{
    /// <summary>
    /// Render and edit a value.
    /// </summary>
    /// <param name="value">Current value (boxed). Modified in place on edit.</param>
    /// <param name="ctx">Context describing pin, max width, metadata.</param>
    /// <param name="committed">True when the change should be committed as an undoable command.</param>
    /// <returns>True if the value changed this frame.</returns>
    bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed);
}

/// <summary>Context passed to default-value editors during Draw.</summary>
public readonly record struct DefaultEditorContext(
    PinId Pin,
    TypeKey Type,
    float MaxWidth,
    bool IsReadOnly,
    PinDefaultMetadata Metadata);
```

---

## File: `NodeEditor.Core/Interfaces/IGraphCommandSink.cs`

```csharp
using NodeEditor.Core.Commands;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Sink for all editor-initiated mutations. The host implements this to
/// apply commands to its data store. Multiple commands per user action
/// are batched via <see cref="GraphCommand.Batch"/>.
/// </summary>
public interface IGraphCommandSink
{
    /// <summary>Apply a command. The host should treat the command atomically.</summary>
    GraphCommandResult Apply(GraphCommand command);
}

/// <summary>Result of applying a command.</summary>
public readonly record struct GraphCommandResult(bool Success, string? Message);
```

---

## File: `NodeEditor.Core/Interfaces/IEditorHostServices.cs`

```csharp
namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Bundle of services the host provides to the editor at construction.
/// Wraps all the optional and required adapters in one container so the
/// editor's constructor doesn't take 12 parameters.
/// </summary>
public interface IEditorHostServices
{
    /// <summary>Node catalog for search popup and picker.</summary>
    INodeCatalog NodeCatalog { get; }

    /// <summary>Type system for colors, shapes, compatibility.</summary>
    ITypeSystem TypeSystem { get; }

    /// <summary>Link validator (rejects illegal connections).</summary>
    ILinkValidator LinkValidator { get; }

    /// <summary>Sink for all mutations.</summary>
    IGraphCommandSink CommandSink { get; }

    /// <summary>Picker registry (host registers sources here).</summary>
    IPickerRegistry Pickers { get; }

    /// <summary>Clipboard adapter.</summary>
    IClipboard Clipboard { get; }

    /// <summary>Icon provider for catalog icons, status icons, etc.</summary>
    IIconProvider Icons { get; }

    /// <summary>Optional diagnostics sink for logging/telemetry.</summary>
    IDiagnosticsSink? Diagnostics { get; }

    /// <summary>Optional debug session (breakpoints, watches, executing-node viz).</summary>
    IDebugSession? Debug { get; }

    /// <summary>Input source for the canvas (mouse + keyboard abstraction).</summary>
    IInputSource Input { get; }

    /// <summary>Theme (colors, fonts, sizes).</summary>
    IEditorTheme Theme { get; }
}
```

---

## File: `NodeEditor.Core/Interfaces/IPickerRegistry.cs`

```csharp
using System.Numerics;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Registry of picker sources. The host registers per-context sources
/// (variables, types, assets, etc.) at startup; the editor opens them via
/// <see cref="Open"/>.
/// </summary>
public interface IPickerRegistry
{
    /// <summary>Register a typed picker source under a string key.</summary>
    void Register<TItem>(string sourceKey, IPickerSource<TItem> source);

    /// <summary>Look up a typed source by key.</summary>
    IPickerSource<TItem>? Get<TItem>(string sourceKey);

    /// <summary>
    /// Open the picker for the given source. The picker calls
    /// <paramref name="onPick"/> with the selected item (or list, for multi-select).
    /// </summary>
    void Open(
        string sourceKey,
        Vector2 screenPos,
        Action<object> onPick,
        Action? onCancel = null,
        IReadOnlyDictionary<string, object?>? context = null);
}

/// <summary>A source of pickable items. Generic on item type.</summary>
public interface IPickerSource<TItem>
{
    string Title { get; }
    string EmptyResultText { get; }
    PickerLayout PreferredLayout { get; }
    PickerSelectionMode SelectionMode { get; }
    QueryCost Cost { get; }
    bool IsAsync { get; }
    bool AllowsDragOut { get; }
    bool AllowsDragIn { get; }
    bool AllowArbitraryTextInput { get; }

    IReadOnlyList<TItem> Query(string text, IReadOnlyDictionary<string, object?>? context);

    Task<IReadOnlyList<TItem>> QueryAsync(
        string text,
        IReadOnlyDictionary<string, object?>? context,
        CancellationToken ct);

    void RenderItem(TItem item, bool selected, bool keyboardFocused, IPickerRenderContext ctx);
    void RenderPreview(TItem item, IPickerRenderContext ctx);
    bool IsPreviewExpensive(TItem item);

    string GetSearchableText(TItem item);
    string GetItemKey(TItem item);
    bool CanAcceptDrop(object payload);
}

public enum PickerLayout { Standard, Compact, Wide, Grid, Tree }
public enum PickerSelectionMode { Single, Multi, MultiOrdered }
public enum QueryCost { Cheap, Moderate, Heavy }

/// <summary>Rendering context handed to picker source's RenderItem/RenderPreview.</summary>
public interface IPickerRenderContext
{
    IIconProvider Icons { get; }
    IEditorTheme Theme { get; }
    IReadOnlyList<int>? MatchPositions { get; }
}
```

---

## File: `NodeEditor.Core/Interfaces/IClipboard.cs`

```csharp
namespace NodeEditor.Core.Interfaces;

/// <summary>Editor's clipboard abstraction. Wraps either OS clipboard or in-process buffer.</summary>
public interface IClipboard
{
    /// <summary>Read clipboard text.</summary>
    string? GetText();

    /// <summary>Write clipboard text.</summary>
    void SetText(string text);
}
```

---

## File: `NodeEditor.Core/Interfaces/IIconProvider.cs`

```csharp
namespace NodeEditor.Core.Interfaces;

/// <summary>Lookup for icons by string key. The host or theme provides icons.</summary>
public interface IIconProvider
{
    /// <summary>Try to resolve an icon key to a renderable handle. Returns false if unknown.</summary>
    bool TryGet(string key, out IconHandle handle);
}

/// <summary>Opaque handle to a renderable icon. Implementation defined by host.</summary>
public readonly record struct IconHandle(nint TextureId, uint Width, uint Height);
```

---

## File: `NodeEditor.Core/Interfaces/IDiagnosticsSink.cs`

```csharp
namespace NodeEditor.Core.Interfaces;

/// <summary>Optional sink for editor logs and telemetry.</summary>
public interface IDiagnosticsSink
{
    void Log(DiagnosticSeverity severity, string message, Exception? exception = null);
}

public enum DiagnosticSeverity
{
    Trace,
    Debug,
    Info,
    Warning,
    Error,
}
```

---

## File: `NodeEditor.Core/Interfaces/IDebugSession.cs`

```csharp
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Optional integration with a debugger. The host supplies this when a
/// debug session is attached.
/// </summary>
public interface IDebugSession
{
    bool IsAttached { get; }
    bool IsPaused { get; }
    NodeId? CurrentlyExecutingNode { get; }
    IReadOnlySet<NodeId> RecentlyExecutedNodes { get; }
    IReadOnlySet<NodeId> Breakpoints { get; }
    IReadOnlySet<PinId> WatchedPins { get; }

    void ToggleBreakpoint(NodeId node);
    void ToggleWatch(PinId pin);
    void Continue();
    void StepOver();
    void StepInto();
    void StepOut();

    /// <summary>Get the current value at a watched pin (only valid while paused).</summary>
    object? GetWatchValue(PinId pin);

    event Action? StateChanged;
}
```

---

## File: `NodeEditor.Core/Interfaces/IInputSource.cs`

```csharp
using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Per-frame input snapshot for the canvas. The host provides an
/// implementation matching its windowing backend (raylib, SDL, …).
/// </summary>
public interface IInputSource
{
    /// <summary>Mouse position in screen coordinates.</summary>
    Vector2 MousePosition { get; }

    /// <summary>Mouse movement this frame.</summary>
    Vector2 MouseDelta { get; }

    /// <summary>Mouse wheel delta this frame (positive = scroll up).</summary>
    float WheelDelta { get; }

    bool IsMouseDown(MouseButton btn);
    bool IsMousePressed(MouseButton btn);
    bool IsMouseReleased(MouseButton btn);
    bool IsMouseDoubleClicked(MouseButton btn);

    bool IsKeyDown(EditorKey k);
    bool IsKeyPressed(EditorKey k, bool allowRepeat = false);
    bool IsKeyReleased(EditorKey k);

    KeyModifiers Modifiers { get; }

    /// <summary>Text input received this frame (for text editing widgets).</summary>
    ReadOnlySpan<char> TextThisFrame { get; }
}
```

---

## File: `NodeEditor.Core/Interfaces/IEditorTheme.cs`

```csharp
using System.Numerics;

namespace NodeEditor.Core.Interfaces;

/// <summary>Theme — colors, fonts, sizes used by the editor.</summary>
public interface IEditorTheme
{
    Vector4 BackgroundColor { get; }
    Vector4 GridMinorColor { get; }
    Vector4 GridMajorColor { get; }
    Vector4 SelectionAccent { get; }
    Vector4 PrimarySelectionAccent { get; }
    Vector4 ErrorColor { get; }
    Vector4 WarningColor { get; }
    Vector4 TextDefault { get; }
    Vector4 TextMuted { get; }

    Vector4 GetCategoryHeaderColor(Primitives.NodeCategory category);

    float NodeCornerRadius { get; }
    float NodeBorderThickness { get; }
    float NodeHeaderHeight { get; }
    float PinGlyphSize { get; }
    float WireThicknessExec { get; }
    float WireThicknessData { get; }
}
```

---

That covers the host contract. Most interfaces are simple; the heavy
implementation is in the editor's own view-model layer (next file).
