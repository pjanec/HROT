# 01 — Spec Brief (Authoritative)

This is the **single source of truth** for the editor's behavior. When in
doubt, this document is correct. Detailed specs in `../specs/` provide
rationale and edge-case discussion but never override the brief.

## Table of contents

1. [Scope & layering](#1-scope--layering)
2. [Technology boundaries](#11-technology-boundaries-normative)
3. [Identity & types](#2-identity--types)
3. [Core interfaces (host contract)](#3-core-interfaces-host-contract)
4. [The view-model](#4-the-view-model)
5. [Commands & undo](#5-commands--undo)
6. [Canvas mechanics](#6-canvas-mechanics)
7. [Node visuals](#7-node-visuals)
8. [Pin visuals](#8-pin-visuals)
9. [Inline default editors](#9-inline-default-editors)
10. [Wire mechanics](#10-wire-mechanics)
11. [Selection rules](#11-selection-rules)
12. [Interaction state machine](#12-interaction-state-machine)
13. [Keyboard shortcuts](#13-keyboard-shortcuts)
14. [Context menus](#14-context-menus)
15. [Search popup (node creation)](#15-search-popup-node-creation)
16. [Generic picker](#16-generic-picker)
17. [Mini-editor catalog](#17-mini-editor-catalog)
18. [My Blueprint panel](#18-my-blueprint-panel)
19. [Details panel](#19-details-panel)
20. [Authoring flows](#20-authoring-flows)
21. [Find / navigation](#21-find--navigation)
22. [Comments & reroutes](#22-comments--reroutes)
23. [Bookmarks](#23-bookmarks)
24. [Hot-reload indicators](#24-hot-reload-indicators)
25. [Debug visualization](#25-debug-visualization)
26. [Command/indicator API](#26-commandindicator-api)
27. [Performance budgets](#26-performance-budgets)
28. [Timing constants](#27-timing-constants)
29. [Color conventions](#28-color-conventions)
30. [Input abstraction](#29-input-abstraction)

---

## 1. Scope & layering

Build a generic node-graph editor library that:
- Renders a canvas of nodes, pins, wires, comments, reroutes via ImGui.NET.
- Reads graph data through `IGraphModel` / `INodeModel` / `IPinModel` /
  `ILinkModel` interfaces — the editor never owns graph data.
- Issues mutations through `IGraphCommand` records applied via
  `IGraphCommandSink` — never mutates host data directly.
- Provides a generic picker reusable for 12+ "long-list-with-IntelliSense"
  selection contexts.
- Provides a command/indicator API so the host's UI shell (toolbar, menu,
  status bar) is decoupled from the editor.

**Layers, strictly enforced via .csproj dependencies:**

```
Primitives  →  Core  →  UI  →  (Host)
                       Demo  ←
```

- **Primitives:** IDs, geometry, zero deps. `netstandard2.1` or `net8.0`.
- **Core:** view-model, undo, interfaces, spatial index, fuzzy matcher. No
  ImGui dep. `net8.0`.
- **UI:** ImGui rendering of canvas + panels + picker + mini-editors.
  Depends on Core and ImGui.NET. `net8.0`.
- **Demo:** raylib-cs + rlImGui-cs + fake-host. Proves the editor works
  standalone.

## 1.1 Technology boundaries (normative)

These rules are absolute. They are how the editor stays reusable across
hosts.

### Graphics

- **The editor library renders exclusively through ImGui.NET.** All canvas
  drawing uses `ImGui.GetWindowDrawList()` (or `GetBackgroundDrawList()`
  when explicitly needed for behind-window overlays). No GL/D3D/Vulkan/SDL
  calls anywhere in `NodeEditor.UI`.
- **`NodeEditor.UI.csproj` has exactly one external graphics dependency:
  `ImGui.NET`.** No Raylib-cs, no rlImGui-cs, no Veldrid, no Silk.NET.
- **The host is responsible for the ImGui backend.** The host creates the
  ImGui context, runs the per-frame `NewFrame()`/`Render()` cycle, and
  uploads draw data to the GPU through whatever backend it uses
  (rlImGui-cs in the demo, but it could be Veldrid, SDL2, or anything
  else). The editor never touches `ImGui.CreateContext`,
  `ImGui.NewFrame`, or `ImGui.Render`.
- **Raylib-cs and rlImGui-cs appear only in `NodeEditor.Demo`.** They
  are the demo's chosen backend; they are NOT part of the editor's
  contract. A different real host (the user's Blueprint subsystem) may
  use a different ImGui backend, and nothing in `NodeEditor.UI` must
  change to support that.
- **Textures and icons cross the boundary via `IconHandle`** (defined in
  `IIconProvider`), which carries a raw `nint TextureId` plus dimensions.
  The editor passes the `TextureId` to ImGui's `Image()` calls. How the
  host uploaded that texture is invisible to the editor.

### Input

- **The editor reads input exclusively through `IInputSource`.** Defined
  in `NodeEditor.Core.Interfaces`. Methods: `MousePosition`, `MouseDelta`,
  `WheelDelta`, `IsMouseDown/Pressed/Released/DoubleClicked`,
  `IsKeyDown/Pressed/Released`, `Modifiers`, `TextThisFrame`.
- **The host implements `IInputSource`** with an adapter matching its
  windowing backend. Two adapters ship:
    - `RaylibInputSource` in `NodeEditor.Demo` — translates from `Raylib.IsKeyDown(...)` etc.
    - `FakeInputSource` in `NodeEditor.Core.Tests` — scriptable for unit tests.
- **The editor library has no reference to any windowing API.** No `using
  Raylib_cs;` in `NodeEditor.UI`. The compiler will catch a violation.
- **Canvas input vs. ImGui widget input:** ImGui owns input for any
  active ImGui widget. The canvas yields whenever
  `ImGui.IsAnyItemActive()` is true that frame. Outside that case, the
  canvas reads from `IInputSource` directly. This avoids double-handling
  (e.g., text being entered into a search field also panning the canvas).

### Hotkeys

- **The editor does NOT hook hotkeys directly.** It publishes commands
  via `IEditorCommands.All`, each with an optional `DefaultKey`.
- **The host's hotkey dispatcher** walks `commands.All` each frame,
  matches against `IInputSource`, and calls `commands.Invoke(commandId)`
  on hits. The demo includes a reference `HotkeyDispatcher`; real hosts
  reuse or replace it.
- **Exception: gestures within the canvas** (LMB drag for marquee, RMB
  drag for pan, Alt+click on pin, etc.) are NOT commands. They are
  direct canvas interactions handled in `CanvasInput` and stay there.
- **Rule of thumb:** if it works the same way regardless of canvas
  state, it's a command (host-bound). If it's a tool gesture inside the
  canvas, it's a direct interaction (editor-internal).

### Clipboard, text, files

- **Clipboard via `IClipboard`** (`GetText`/`SetText` only; no binary
  formats in MVP).
- **No `Console.WriteLine` or `File.*` calls in editor code** except in
  `NodeEditor.Demo`. Logging goes through `IDiagnosticsSink` (optional;
  no-op if not provided).

### Threading

- **Editor UI code runs on the ImGui thread** (typically the main
  thread). The editor is not thread-safe; do not call into it from
  worker threads.
- **`IGraphModel.Changed` may fire on any thread**, but the editor
  consumes it lazily during `Render`. If a host raises it off the UI
  thread, the host must marshal — or the editor's `ChangeNotifier`
  (T-24) can queue per-frame.

### What `NodeEditor.UI` is allowed to reference

| Allowed | Forbidden |
|---|---|
| `ImGui.NET` | `Raylib_cs`, `rlImGuiNET` |
| `System.Numerics` | `OpenTK`, `Veldrid`, `Silk.NET` |
| `NodeEditor.Core` | `SkiaSharp`, `System.Drawing.Common` |
| `NodeEditor.Primitives` | Any GPU API |
| .NET BCL | Windowing / native window handles |

A `csproj` analyzer is not provided, but the agent should periodically
verify by inspecting `NodeEditor.UI/NodeEditor.UI.csproj` package refs.

## 2. Identity & types

All IDs are GUIDs wrapped in `record struct`. Never expose raw GUIDs in public
APIs; always wrap. Identity types defined in `NodeEditor.Primitives`:

```csharp
public readonly record struct NodeId(Guid Value);
public readonly record struct PinId(Guid Value);
public readonly record struct LinkId(Guid Value);
public readonly record struct GraphId(Guid Value);
public readonly record struct CommentId(Guid Value);
public readonly record struct RerouteId(Guid Value);
public readonly record struct TypeKey(string Id);
public readonly record struct NodeKindKey(string Id);
```

`TypeKey` and `NodeKindKey` are string-keyed because the host owns these
namespaces; the editor never knows specific types.

ID generation: see `kernel/IdGenerator.cs` — deterministic-when-needed
(SHA-256-derived from a stable input string) and random otherwise.

## 3. Core interfaces (host contract)

The host implements these interfaces; the editor consumes them. **Never add
mutation methods to these interfaces** — mutation goes through
`IGraphCommandSink`.

### `IGraphModel`

```csharp
public interface IGraphModel
{
    GraphId Id { get; }
    string DisplayName { get; }
    GraphKindDescriptor Kind { get; }

    IReadOnlyCollection<INodeModel> Nodes { get; }
    IReadOnlyCollection<ILinkModel> Links { get; }
    IReadOnlyCollection<ICommentModel> Comments { get; }

    INodeModel? FindNode(NodeId id);
    IPinModel?  FindPin(PinId id);
    ILinkModel? FindLink(LinkId id);

    event Action<GraphChangeNotification>? Changed;
}
```

### `INodeModel`

```csharp
public interface INodeModel
{
    NodeId Id { get; }
    NodeKindKey Kind { get; }
    string Title { get; }
    string? Subtitle { get; }
    NodeCategory Category { get; }      // for header color
    Vector2 Position { get; }
    Vector2? SizeOverride { get; }      // null = auto-size
    NodeState State { get; }            // Normal/Disabled/Error/Warning flags
    string? StatusTooltip { get; }
    bool IsCollapsed { get; }
    bool ShowAdvancedPins { get; }
    IReadOnlyList<IPinModel> Pins { get; }
}
```

### `IPinModel`

```csharp
public interface IPinModel
{
    PinId Id { get; }
    NodeId OwnerNodeId { get; }
    string Label { get; }
    PinDirection Direction { get; }     // Input | Output
    PinKind Kind { get; }               // Exec | Data
    TypeKey? Type { get; }              // null only for Exec
    PinShape Shape { get; }             // Circle | Diamond | Square | …
    bool IsAdvanced { get; }
    bool IsOptional { get; }
    string? Tooltip { get; }
    IPinDefaultValue? Default { get; }  // null = no inline editor
}
```

**Computed, not stored:** `bool AcceptsMultipleConnections =>
(Direction == Output && Kind == Data) || (Direction == Input && Kind == Exec)`.
This encodes the Unreal-style connection rules from §10.

### `ILinkModel`

```csharp
public interface ILinkModel
{
    LinkId Id { get; }
    PinId FromPin { get; }     // output side
    PinId ToPin { get; }       // input side
    LinkStyle Style { get; }
    IReadOnlyList<Vector2> Waypoints { get; }   // reroute positions; may be empty
}
```

### `ICommentModel`

```csharp
public interface ICommentModel
{
    CommentId Id { get; }
    string Text { get; }
    Vector2 Position { get; }
    Vector2 Size { get; }
    Vector4 Color { get; }              // RGBA; header = full alpha, body = 0.20
    int ZOrder { get; }
    bool MoveWithContents { get; }
}
```

### Supporting enums and records

```csharp
public enum PinDirection { Input, Output }
public enum PinKind { Exec, Data }
public enum PinShape { Circle, Diamond, Square, Pentagon, Triangle }
public enum NodeCategory { Function, Event, Pure, VariableGet, VariableSet,
                          FlowControl, Macro, Comment, Custom }

[Flags]
public enum NodeState
{
    Normal      = 0,
    Disabled    = 1 << 0,
    Error       = 1 << 1,
    Warning     = 1 << 2,
    Executing   = 1 << 3,   // debug only
    RecentlyExecuted = 1 << 4,
}

public sealed record GraphKindDescriptor(
    string Id,                    // "event-graph", "function", "macro", …
    string DisplayName,
    bool AllowsLatent,
    bool RequiresEntryNode);

public sealed record GraphChangeNotification(
    GraphChangeKind Kind,
    IReadOnlySet<NodeId>? AffectedNodes,
    IReadOnlySet<LinkId>? AffectedLinks,
    string? Reason);

public enum GraphChangeKind
{
    NodesAdded, NodesRemoved, NodesModified, NodesMoved,
    LinksAdded, LinksRemoved,
    VariablesChanged,
    Wholesale
}
```

### `ILinkValidator`

```csharp
public interface ILinkValidator
{
    LinkValidationResult Validate(PinId from, PinId to);
}

public readonly record struct LinkValidationResult(
    LinkValidity Verdict,
    string? Reason,
    bool RequiresCast,
    NodeKindKey? AutoInsertCast);

public enum LinkValidity { Invalid, Valid, ValidWithCast }
```

### `INodeCatalog`

Searchable list of all node kinds the host knows about. Used by the search
popup (§15) and by the picker (§16) for the "all nodes" and "by pin context"
sources.

```csharp
public interface INodeCatalog
{
    IReadOnlyList<NodeCatalogEntry> All { get; }
    IReadOnlyList<NodeCategoryDescriptor> Categories { get; }
    IReadOnlyList<NodeCatalogEntry> Query(NodeSearchQuery q);
    IReadOnlyList<NodeCatalogEntry> QueryForPinContext(PinContextQuery q);
}
```

The full struct definitions live in `kernel/Interfaces.NodeCatalog.cs`.

### `ITypeSystem`

```csharp
public interface ITypeSystem
{
    bool TryGetTypeInfo(TypeKey key, out TypeDisplayInfo info);
    Vector4 GetPinColor(TypeKey key);
    PinShape GetPinShape(TypeKey key, ContainerKind container);
    IPinDefaultValueEditor? GetDefaultEditor(TypeKey key);
    bool AreCompatible(TypeKey from, TypeKey to);
    bool IsImplicitCast(TypeKey from, TypeKey to);
}
```

### `IGraphCommandSink`

```csharp
public interface IGraphCommandSink
{
    GraphCommandResult Apply(GraphCommand command);
}
```

Every editor-initiated mutation goes through this. Commands are records
defined in `kernel/Commands.cs`. See §5.

### `IEditorHostServices`

The bundle the host passes to the editor at construction:

```csharp
public interface IEditorHostServices
{
    INodeCatalog       NodeCatalog       { get; }
    ITypeSystem        TypeSystem        { get; }
    ILinkValidator     LinkValidator     { get; }
    IGraphCommandSink  CommandSink       { get; }
    IPickerRegistry    Pickers           { get; }
    IClipboard         Clipboard         { get; }
    IIconProvider      Icons             { get; }
    IDiagnosticsSink?  Diagnostics       { get; }
    IDebugSession?     Debug             { get; }
    IInputSource       Input             { get; }
    IEditorTheme       Theme             { get; }
}
```

## 4. The view-model

The editor maintains an internal view-model **per opened graph**:

```csharp
public sealed class GraphView
{
    public IGraphModel Model { get; }
    public ViewportState Viewport { get; }
    public SelectionState Selection { get; }
    public InteractionState Interaction { get; }
    public SpatialIndex SpatialIndex { get; }
    public NodeRenderCache RenderCache { get; }

    // Per-frame entry point from UI layer
    public void BeginFrame();
    public void EndFrame();
}
```

The view-model is allowed to mutate freely without going through the command
sink — but only for **transient state** (drag previews, hover, in-progress
wire, selection). All durable mutations go through commands.

`ViewportState` holds pan/zoom; `SelectionState` holds the selected sets;
`InteractionState` holds the current mode (idle / box-select / drag-wire /
…) and any per-mode scratch data; `SpatialIndex` accelerates hit-tests and
viewport culling; `NodeRenderCache` caches measured sizes and bounds.

## 5. Commands & undo

All mutations are records inheriting from `GraphCommand`:

```csharp
public abstract record GraphCommand
{
    public sealed record MoveNodes(IReadOnlyList<(NodeId, Vector2)> Moves) : GraphCommand;
    public sealed record AddNode(NodeKindKey Kind, Vector2 Pos, ...) : GraphCommand;
    public sealed record RemoveNodes(IReadOnlyList<NodeId>) : GraphCommand;
    public sealed record AddLink(PinId From, PinId To) : GraphCommand;
    public sealed record RemoveLinks(IReadOnlyList<LinkId>) : GraphCommand;
    public sealed record ReplaceLinkEndpoint(LinkId, LinkEndpoint, PinId New) : GraphCommand;
    public sealed record SetPinDefault(PinId, object? NewValue) : GraphCommand;
    public sealed record SetNodeProperty(NodeId, string Key, object? Value) : GraphCommand;
    public sealed record SetNodeCollapsed(NodeId, bool) : GraphCommand;
    public sealed record SetNodeAdvancedShown(NodeId, bool) : GraphCommand;
    public sealed record AddComment(string, Vector2, Vector2, Vector4) : GraphCommand;
    public sealed record UpdateComment(CommentId, ...) : GraphCommand;
    public sealed record InsertReroute(LinkId, Vector2) : GraphCommand;
    public sealed record MoveReroute(LinkId, int Idx, Vector2) : GraphCommand;
    public sealed record PromoteToVariable(PinId, string VarName) : GraphCommand;
    public sealed record CollapseToFunction(IReadOnlyList<NodeId>, string Name) : GraphCommand;
    public sealed record CollapseToComment(IReadOnlyList<NodeId>) : GraphCommand;
    public sealed record Batch(string Label, IReadOnlyList<GraphCommand>) : GraphCommand;
}
```

Full definitions in `kernel/Commands.cs`.

**Undo stack** is owned by the editor (`UndoStack` class). The editor produces
inverse commands by snapshotting affected entities before mutation. See
`kernel/UndoStack.cs` for the authoritative implementation.

**Batching:** any single user action that produces multiple mutations
(multi-node drag, paste, refactor) is wrapped in `Batch`. One user action =
one undo step.

**Drag mutations are deferred:** during a node drag, positions are updated
locally in the view-model only. On mouse-up, a single `MoveNodes` command is
emitted. Esc during drag reverts the local state and emits nothing.

## 6. Canvas mechanics

### Camera

- **Pan:** middle-mouse drag (default), or right-mouse drag (alt), or
  Space+LMB.
- **Zoom:** mouse wheel, zoomed *toward the cursor position*. Range 0.25–3.0.
- **Low-zoom mode:** below 0.5×, nodes render as solid colored blocks with
  no pins or text; wires render as straight lines.
- **Frame all / selection:** F (selection-or-all), Home (all). Animated
  ~180ms ease-out-cubic.
- **Reset zoom:** Ctrl+0.

### Grid

- Dotted, two-level (minor every 16 canvas units, major every 80).
- Fades to nearly invisible at low zoom.
- Origin marker: faint cross at (0,0).

### Background

Solid color (theme-defined). Grid overlaid.

### Coordinate system

- **Canvas coordinates:** the abstract space nodes live in. Y down.
- **Screen coordinates:** pixel space.
- Conversion via `ViewportState.CanvasToScreen(v)` and `ScreenToCanvas(v)`.

## 7. Node visuals

Each node is a rectangle with:
- Header (colored by `NodeCategory`, ~24 px tall at zoom 1.0).
- Body (darker bg) holding pin rows.
- 4-px corner radius.
- 2-px outline (selection color when selected; brighter for primary selection;
  red/yellow for error/warning states).

Header contents (left to right):
- Optional 16×16 category icon.
- Title (truncated with ellipsis, two lines max).
- Status icons (error ⚠, warning ⚠, breakpoint ●, watching 👁).

Body contents:
- Input pins on the left, output pins on the right.
- Each row: pin glyph + label + (input only) inline default editor.
- "Advanced" pins hidden behind a `▼ Advanced` disclosure unless
  `ShowAdvancedPins` is true.

### Sizing

- Width = `max(header_width, max_pin_row_width)` + padding.
- Height grows vertically with pin count. No internal scrolling.
- `SizeOverride` from `INodeModel` allows host to fix size if needed
  (rare; reserved for resizable nodes like the comment, but comments are
  handled separately).

### States

- **Selected:** outline at theme accent color, 2 px.
- **Primary-selected:** outline at brighter accent + glow.
- **Error:** red outline; ⚠ icon in header.
- **Warning:** yellow outline; ⚠ icon.
- **Disabled:** body desaturated to ~50%; dashed outline.
- **Executing (debug):** pulsing outline at ~2 Hz.
- **Recently executed (debug):** fading outline, ~500 ms.

### Low-zoom rendering

Below zoom 0.5, node renders as a colored rectangle (no header detail, no
pins, no text). The color is the category color, full saturation. Selection
outline still shown if selected.

## 8. Pin visuals

- **Exec pin:** filled triangle ▶ when connected, outline when not.
- **Data pin:** filled circle ◯ when connected, outline when not.
- **Container pins:** diamond (array), square (map), pentagon (set).
- **Color:** from `ITypeSystem.GetPinColor(type)` for data pins. White for
  exec.
- **Hit area:** 1.5× the visible glyph size, for forgiving click targets.
- **Hover state:** scaled to 1.2× and brightened.

### Pin row layout

```
INPUT side:                              OUTPUT side:
[glyph]  Label   [inline editor]         Label  [glyph]
```

The inline editor only renders for input data pins with no incoming wire
and a registered editor for the pin's type.

When a wire is connected to an input pin that has an editor, the editor
hides and the row shows italicized gray text "← wired".

## 9. Inline default editors

Defined in detail in §17. Renderers implement `IPinDefaultValueEditor`:

```csharp
public interface IPinDefaultValueEditor
{
    bool Draw(ref object? value, DefaultEditorContext ctx, out bool committed);
}
```

`committed` is true when the change should generate an undo entry
(mouse-up on drag, Enter, focus-out). Per-frame changes during a drag
have `committed = false` and only update the view-model locally.

## 10. Wire mechanics

### Connection rules (Unreal-exact)

| Direction | Rule |
|---|---|
| One data output → many data inputs | **Allowed.** |
| Many data outputs → one data input | **Forbidden.** New connection replaces old (no prompt). |
| One exec output → many exec inputs | **Forbidden.** Use Sequence node. |
| Many exec outputs → one exec input | **Allowed.** |

### Wire appearance

- **Bezier curve** between source-output and target-input pin positions.
- Tangent strength = `max(50, abs(dx) * 0.5)` where dx is horizontal distance.
- Exec wires: white/light gray, ~3 px thick, with a small direction arrow
  near the midpoint.
- Data wires: pin-color, ~2 px thick.
- Hit area: ~6–8 px from the curve.
- Hover state: thicker, brighter.

### Reroutes

A wire with N waypoints is rendered as N+1 bezier segments. Each segment
uses the same tangent-strength rule on its segment endpoints. The wire's
LinkId stays the same regardless of reroute count.

### Creating connections

- **Drag from pin to pin:** standard connection.
- **Drag from pin, release on empty canvas:** opens the search popup pre-filtered
  by the source pin's type and direction (§15).
- **Drag from pin, release on a node body (not on a pin):** snap to the
  nearest compatible pin on that node.
- **Auto-cast:** when validator returns `ValidWithCast`, drop creates a
  Batch: AddNode(cast) + AddLink(from, cast.in) + AddLink(cast.out, to).
- **Snap-to-pin:** during drag, when cursor is within 14 px of a compatible
  pin, the wire endpoint snaps to that pin.

### Modifying connections

- **Alt+click on pin:** removes all wires touching that pin.
- **Alt+click on wire:** removes the wire.
- **Ctrl+drag from connected pin's wire endpoint:** picks up that end of the
  wire, lets user re-route to a new pin.
- **LMB-drag from connected output exec pin:** *silently steals* the existing
  wire (since exec-out can only have one wire).
- **LMB-drag from connected input data pin:** creates a new wire originating
  at that input (popup filters for compatible outputs on drop).
  Ctrl+drag for the steal behavior.

### Cycle detection

Editor performs a quick local check during wire drag for direct cycles in
the exec graph. If detected, validation result is `Invalid` with reason
"Would create a cycle." Full cycle detection is the host's compiler's job.

## 11. Selection rules

### Single click on node (no modifier)

- If node NOT in selection: clear selection, select this node, set as primary,
  begin DRAG_NODES.
- If node IS in selection: keep selection, set this node as primary, begin
  DRAG_NODES with the whole selection.

### Shift+click

- Add to selection if not present; primary = clicked node.
- Does NOT initiate drag until threshold crossed.

### Ctrl+click

- Toggle in/out of selection.
- No drag.

### Click on empty space

- Clear selection. Begin BOX_SELECT.

### Shift+click on empty space

- Begin BOX_SELECT in additive mode.

### Ctrl+click on empty space

- Begin BOX_SELECT in toggle mode.

### Drag threshold

4 pixels from mouse-down position. Below threshold, treat as click.

### Box select

- Default: nodes fully enclosed are selected.
- Alt held during drag: nodes touching are selected (label "+ Touching"
  shown near cursor).
- Selected items include: nodes, comments, reroutes.

### Ctrl+A

Select all in current graph.

### Esc

- Cancel current operation (drag, box-select, popup).
- If no operation: clear selection.

## 12. Interaction state machine

Top-level state, exactly one active at a time:

```
IDLE
├─ HOVER_NODE   (LMB-down → DRAG_NODES or DRAG_WIRE_FROM_PIN if on pin)
├─ HOVER_PIN    (LMB-down → DRAG_WIRE_FROM_PIN)
├─ HOVER_WIRE   (LMB-down + Ctrl → DRAG_WIRE_FROM_MID; double-click → INSERT_REROUTE)
├─ HOVER_COMMENT_HEADER  (LMB-down → DRAG_COMMENT)
├─ HOVER_COMMENT_BODY    (click-through to underlying entities)
├─ HOVER_REROUTE  (LMB-down → DRAG_REROUTE)
├─ BOX_SELECT
├─ PAN_CANVAS
├─ DRAG_NODES
├─ DRAG_WIRE_FROM_PIN
├─ DRAG_WIRE_FROM_MID
├─ DRAG_COMMENT
├─ DRAG_REROUTE
├─ RESIZE_COMMENT
├─ INLINE_RENAME
├─ SEARCH_POPUP_OPEN
└─ CONTEXT_MENU_OPEN
```

### Hit-test priority (highest to lowest)

1. Reroutes
2. Pins
3. Wires
4. Comment title bars
5. Node bodies
6. Comment bodies *(passes through to anything underneath; effectively empty)*
7. Empty canvas

### Universal rules

- **Esc** cancels any non-IDLE state back to IDLE.
- **Mouse-leaving-canvas during drag** does NOT cancel; only mouse-up or Esc.
- When a popup or modal panel is open, canvas does not process input.
- When ImGui has an item active (`ImGui.IsAnyItemActive()`), canvas
  suppresses its own input handling for that frame.

## 13. Keyboard shortcuts

Defaults; should be remappable via the command/indicator API (§26).

| Key | Action |
|---|---|
| Ctrl+Z / Ctrl+Shift+Z | Undo / Redo |
| Ctrl+C / Ctrl+X / Ctrl+V | Copy / Cut / Paste |
| Ctrl+D | Duplicate |
| Ctrl+A | Select all |
| Delete / Backspace | Delete selection |
| Tab / Space | Open search popup at cursor |
| F | Frame selection (or all if none selected) |
| Home | Frame all |
| End | Frame primary |
| Ctrl+0 | Reset zoom |
| Ctrl+F | Find in graph |
| Ctrl+Shift+F | Find in asset |
| Ctrl+S | Save (host-defined) |
| F2 | Inline rename |
| F4 | Open Details panel |
| F7 | Compile (host-defined) |
| F9 | Toggle breakpoint |
| F5 | Resume (debug) |
| F10 / F11 / Shift+F11 | Step over / into / out |
| F12 | Go to definition |
| F3 / Shift+F3 | Next / previous match |
| C | Add comment around selection |
| Q | Straighten connection |
| Ctrl+E | Collapse to function |
| Ctrl+G | Collapse to comment |
| Ctrl+1..9 | Jump to bookmark |
| Ctrl+Shift+1..9 | Set bookmark |
| Ctrl+Tab / Ctrl+Shift+Tab | Next / previous tab |
| Esc | Cancel / close |
| ← ↑ → ↓ | Nudge selection by 1 px (Shift = 10 px) |
| Alt+click pin | Disconnect all wires on pin |
| Alt+click wire | Disconnect wire |
| Ctrl+drag wire end | Steal wire endpoint |

## 14. Context menus

### Empty canvas (RMB)
```
Add Node…              Tab
Add Comment            C       (greyed if no selection)
─────
Paste                  Ctrl+V
─────
Frame All              Home
Reset Zoom             Ctrl+0
```

### Node (RMB)
```
Cut                    Ctrl+X
Copy                   Ctrl+C
Duplicate              Ctrl+D
Delete                 Del
─────
Disable Node
Break All Links
─────
Find References        Ctrl+Shift+F
Go to Definition       F12        (if applicable)
Find in Catalog
─────
Refactor →
    Rename             F2
    Collapse to Function   Ctrl+E
    Collapse to Macro
    Collapse to Comment    Ctrl+G
    Expand Node
─────
Toggle Breakpoint      F9
─────
Properties…            F4
```

### Pin (RMB)
```
Break Link(s)
─────
Promote to Variable…
Promote to Local Variable…
Split Struct Pin               (if applicable)
Recombine Struct Pin           (if applicable)
─────
Watch this Value
Reset to Default
─────
Convert to Reroute Node
```

### Wire (RMB)
```
Break Link
Select Connected Nodes
─────
Insert Reroute Node Here
─────
Hide Wire                      (Polish)
```

### Comment (RMB on title bar)
```
Rename                 F2
─────
Color →   (swatch palette + Custom + Reset)
─────
Bring to Front         Ctrl+]
Send to Back           Ctrl+[
─────
Resize to Fit Contents
Move with Contents: ☑  (toggle)
─────
Cut / Copy / Duplicate
Delete                 Del
```

(Continued in `01-spec-brief-part2.md`)
