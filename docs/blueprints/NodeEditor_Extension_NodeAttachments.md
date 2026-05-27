# NodeEditor — Node Attachments extension

> **Status:** Specification for a NodeEditor extension. Authored by the AI Editor team for the NodeEditor team (same team, different hat).
> **Audience:** NodeEditor implementer.
> **Drives:** BTree editor decorator-pill rendering (per `BTree_Editor_NodeEditor_Host_Design.md`); HSM editor state-flag badges; potentially Blueprint editor pure-node pills.
> **References:** `NodeEdit-docs.txt` (the NodeEditor spec brief). All section numbers below the line of "in the existing spec" refer to that document.
> **Doesn't cover:** Per-host rendering details. The extension provides the primitives; hosts choose what to render and what the attachments mean semantically.

---

## Table of Contents

1. Motivation
2. The shape, in one picture
3. Non-goals
4. Identity and model additions
5. Visual rendering
6. Hit testing and interaction
7. Selection semantics
8. Commands
9. Reverse mapping for hosts
10. Theme additions
11. Performance budget
12. Default style configurability
13. Backwards compatibility
14. Test plan
15. Migration for the BTree host

---

## 1. Motivation

NodeEditor today models a graph as nodes connected by links. Every node is a rectangle of equal first-class importance; every visible element on the canvas is either a node, a link, a comment, or a reroute. This model is rich enough for Blueprint event/data graphs but it forces an awkward choice for two real authoring surfaces in our world:

**BTree decorator pills.** A BTree node carrying decorators (Inverter, Repeater with count, Cooldown with duration, ForceSuccess, ForceFailure, UntilSuccess, UntilFailure) needs to render the decorator stack visually attached to the host node, as small chips above its header. This is the Unreal-style pattern authors expect. The underlying data model stores each decorator as a *parent node* with one child — that's correct for the runtime. Rendering it that way produces a vertical staircase of nodes that obscures the actual tree shape.

The two-tier choice today:
- **Render decorators as separate nodes** — honest to the data, ugly in practice, breaks the visual scan that makes BTree trees readable.
- **Hide decorators entirely and surface them only in the inspector** — loses the at-a-glance preemption information that's the whole point of the visual editor.

A third path — pill attachments rendered above the host node — is the right UX but requires nothing the editor currently provides.

**HSM state-flag badges.** A state with deferred events should show a small 🕓 chip; a state with a guard-on-entry pseudo-state shows another chip; conflict-marked states (output-lane collisions in parallel regions) show a warning chip. These could in principle live in the header's status icons (which NodeEditor §7 already supports), but status icons are uniform-size monochrome glyphs and convey only six standard states (error, warning, breakpoint, watching, executing, recently-executed). The HSM editor needs *labeled* chips with parameters (the deferred event names, the conflict's competing region IDs), of arbitrary count, attached to specific states.

**Blueprint pure-node pills (optional).** Today a pure function call in Blueprint renders as a green-headered node identical in size to an event node. Pure nodes are typically tiny — one or two pins — and clutter the canvas when there are many. A "pure pill" rendering compresses a chain of pures into a stack of chips above the receiving impure node. This isn't a v1 ask for Blueprint but the extension should accommodate it later without redesign.

The common thread: **small, parameterized, visually-attached annotations whose lifetime is tied to a host node**. NodeEditor needs a primitive for this.

---

## 2. The shape, in one picture

```
                          ┌──────────────────────┐
                          │  ↺×3  │  ⏲ 2.0s  │  !│   ← three "pill" attachments
                          └───┬─────┬────────┬───┘     (stacked above)
                              └─────┴────────┴── attached to host node
                          ┌──────────────────────┐
                          │ ▼ Sequence           │   ← the host node
                          ├──────────────────────┤
                          │ • child 1            │
                          │ • child 2            │
                          └──────────────────────┘
                              │           │
                            (links continue as normal)
```

Three attachments above one Sequence node. Each attachment renders as a small rounded rectangle with a glyph and an optional one-line label. Attachments stack horizontally first, wrapping to a second row above when the host node's width is exceeded. The host node renders exactly as it would without attachments; its size, pin layout, links, and selection state are unaffected.

Hit testing prefers attachments over host node when they overlap (they don't overlap in normal layout, but selection on the host's top edge is ambiguous and the rule resolves it). Clicking an attachment selects it. Right-clicking opens an attachment-specific context menu. The Details panel displays the attachment's properties when one is selected.

---

## 3. Non-goals

- **Not a general "child node" feature.** Attachments are not nodes. They cannot have pins. They cannot have links. They cannot themselves carry attachments. They are visual annotations on a host.
- **Not a replacement for status icons.** Status icons (error/warning/breakpoint/watching) remain a separate, fixed-vocabulary feature of node headers. Attachments are user-defined per host.
- **Not a layout primitive for inner content.** A composite state's children rendered inside the composite's body is a different problem (container nodes — separate spec).
- **Not animated.** Pills don't pulse, don't fade in/out on add, don't reflow with smooth interpolation. Static add/remove/move.
- **Not draggable across host boundaries.** Reordering pills within a single host's stack is a host-driven operation (the host emits commands; the canvas does not allow drag-and-drop reordering directly). Moving a pill from one host to another is not supported in v1.

---

## 4. Identity and model additions

### 4.1 New identity type

```csharp
// Add to NodeEditor.Primitives
public readonly record struct AttachmentId(Guid Value);
```

Same conventions as the other identity types (NodeEdit §2): GUID-wrapped, generated via the existing `IdGenerator` (deterministic-when-needed; random otherwise). Attachments are uniquely identified across the graph; two hosts cannot share an attachment id.

### 4.2 New model interface

```csharp
// Add to NodeEditor.Core.Interfaces
public interface IAttachmentModel
{
    AttachmentId Id { get; }
    NodeId HostNodeId { get; }

    /// <summary>
    /// Stable categorization. Determines header color and default visual.
    /// Host-defined; NodeEditor doesn't interpret the value.
    /// </summary>
    AttachmentCategory Category { get; }

    /// <summary>
    /// Optional short glyph rendered first in the pill body.
    /// One or two characters; rendered larger than the label.
    /// Null means no glyph.
    /// </summary>
    string? Glyph { get; }

    /// <summary>
    /// Optional one-line label rendered after the glyph.
    /// Truncated with ellipsis if too long.
    /// Null means no label (glyph-only pill).
    /// </summary>
    string? Label { get; }

    /// <summary>Tooltip on hover. Multi-line allowed.</summary>
    string? Tooltip { get; }

    /// <summary>
    /// State flags affecting visual treatment.
    /// Identical semantics to NodeState (NodeEdit §3) for the shared bits.
    /// </summary>
    AttachmentState State { get; }

    /// <summary>
    /// Ordering position within the host's attachment stack.
    /// Lower values render to the left; equal values are stable-sorted by Id.
    /// </summary>
    int StackIndex { get; }
}

public enum AttachmentCategory
{
    Decorator,    // BTree decorator (Inverter, Repeater, etc.)
    Flag,         // HSM state flag (deferred-events, has-history, conflict)
    Pure,         // Blueprint pure-call (future)
    Custom,       // Host-defined; uses Theme.Custom color
}

[Flags]
public enum AttachmentState
{
    Normal      = 0,
    Disabled    = 1 << 0,
    Error       = 1 << 1,
    Warning     = 1 << 2,
    Executing   = 1 << 3,       // debug only
    RecentlyExecuted = 1 << 4,  // debug only
    Selected    = 1 << 5,       // editor-managed, not host-driven
}
```

`AttachmentState` mirrors `NodeState` for `Disabled`/`Error`/`Warning`/`Executing`/`RecentlyExecuted` so debug-overlay and validation feedback work uniformly across nodes and attachments. `Selected` is editor-managed (set by the canvas when the user clicks the pill); the host never sets it.

### 4.3 Graph-model extension

```csharp
// Extend IGraphModel (existing in NodeEdit §3)
public interface IGraphModel
{
    // ... existing members ...

    IReadOnlyCollection<IAttachmentModel> Attachments { get; }
    IAttachmentModel? FindAttachment(AttachmentId id);
    IReadOnlyList<IAttachmentModel> GetAttachmentsForNode(NodeId hostId);
}
```

The graph model owns the attachment list the same way it owns nodes and links. `GetAttachmentsForNode` is a convenience that NodeEditor will call repeatedly during rendering; hosts implementing the interface may want to maintain an internal `Dictionary<NodeId, List<IAttachmentModel>>` for O(1) lookup. NodeEditor itself caches the result per frame in its viewport state.

### 4.4 Change-notification extension

```csharp
// Extend GraphChangeKind (existing in NodeEdit §3)
public enum GraphChangeKind
{
    NodesAdded, NodesRemoved, NodesModified, NodesMoved,
    LinksAdded, LinksRemoved,
    VariablesChanged,
    AttachmentsAdded,        // NEW
    AttachmentsRemoved,      // NEW
    AttachmentsModified,     // NEW
    Wholesale
}

// Extend GraphChangeNotification (existing in NodeEdit §3)
public sealed record GraphChangeNotification(
    GraphChangeKind Kind,
    IReadOnlySet<NodeId>? AffectedNodes,
    IReadOnlySet<LinkId>? AffectedLinks,
    IReadOnlySet<AttachmentId>? AffectedAttachments,    // NEW
    string? Reason);
```

The new field is nullable, additive; existing hosts that don't produce attachment change notifications pass `null` and behave identically to today.

---

## 5. Visual rendering

### 5.1 Layout

Attachments render above the host node's header. The "above" region is laid out left-to-right; multiple attachments wrap to additional rows when the line exceeds the host node's width.

```
At zoom 1.0:

   ┌──┐  ┌────┐  ┌──┐  ┌───────┐         ← attachment row 2 (if needed)
   │↺3│  │⏲2s │  │ !│  │UntilOK│
   └─┬┘  └─┬──┘  └─┬┘  └───┬───┘
     ├─────┴───────┴──────┘                ← attachment row 1 (or only row)
     │
   ┌─┴────────────────────────────┐
   │ ▼ Sequence                   │       ← host node
```

Per-attachment dimensions at zoom 1.0:
- **Height:** 20 px (smaller than node header's 24 px, distinct silhouette).
- **Min width:** 24 px (glyph-only).
- **Padding:** 6 px horizontal, 2 px vertical inside the pill.
- **Corner radius:** 8 px (half the height — full rounded ends; visually distinct from nodes' 4 px radius).
- **Inter-attachment gap:** 4 px horizontal, 3 px vertical between rows.
- **Gap above host header:** 6 px.

Pill width = `glyph_width + (glyph and label both present ? 4 px : 0) + label_width + (padding * 2)`.

When the total row width exceeds the host node's width, attachments wrap to a new row above. Each attachment-row is 23 px tall (20 px pill + 3 px gap), so a host with two rows of attachments needs 46 px of extra layout space above its header.

### 5.2 Coordinate system and node bounds

The attachment stack is part of the host node's visual bounds for the purpose of:
- Viewport culling (a host node with attachments above the visible viewport is still off-screen if both the node and its attachments are off).
- Hit-testing region computation.
- `Home` / `F` (frame all / frame selection) zoom-to-fit calculations.

It is NOT part of the host node for:
- Link-endpoint computation (links connect to the host node's header/body, not to the attachment region).
- Move offsets (moving a node moves the attachments with it; they have no independent position).
- `INodeModel.Position` reporting (the position is still the host node's logical position; attachments are derived from `StackIndex`).

### 5.3 Color

Pill background color is taken from `AttachmentCategory`:

| Category | Color |
|---|---|
| Decorator | purple (#8E44AD) — mirrors the existing Macro category for visual continuity |
| Flag | teal (#16A085) — distinct from any existing node category |
| Pure | green (#27AE60) — matches the existing Pure category |
| Custom | mid-gray (#7F8C8D) — matches the existing Custom node category |

Pill text and glyph render in the editor theme's "Text default" color (#E0E0E0). Pill outline is 1 px in the same color as the background but 30% darker, providing edge definition against the dark canvas background.

State overrides:
- **Selected** — 2 px outline in `Theme.SelectionAccent` (yellow #FFD700), same as node selection.
- **Error** — outline in `Theme.Error` (#FF4444). Background tinted ~15% red.
- **Warning** — outline in `Theme.Warning` (#FFAA00). Background tinted ~15% yellow.
- **Disabled** — text and background desaturated to ~50%; outline dashed.
- **Executing** — pulsing outline at 2 Hz, same as `NodeState.Executing`.
- **RecentlyExecuted** — fading outline over ~500 ms.

### 5.4 Low-zoom rendering

Below zoom 0.5 (matching the existing node low-zoom threshold per NodeEdit §6):
- Attachments collapse to a single small colored bar above the host (3 px tall, same width as the host node).
- Bar color is the category color of the leftmost attachment in row 1.
- If the host has attachments of mixed categories, the bar is split horizontally proportional to count (e.g., 3 Decorators + 2 Flags = 60% purple, 40% teal).
- Selection is not indicated at low zoom for attachments (the host's outline is sufficient).

This collapses cognitive noise at low zoom without removing the visual presence; the user knows "this host has attachments" without seeing the detail.

### 5.5 Render order

Attachments draw immediately after their host node, in this order:
1. Host node body
2. Host node header
3. Host node status icons
4. Host's attachments, by `StackIndex` ascending
5. Selection outlines (host first, then selected attachments) — drawn last so they're always on top

Attachments of a single host are mutually exclusive in space; they don't overlap with one another by construction (layout enforces it). They do not overlap with other hosts' nodes or attachments because nodes have non-overlapping bounds (NodeEditor doesn't allow overlapping nodes today).

### 5.6 What attachments do NOT render

For clarity, here's what's deliberately absent:
- **No pins.** Attachments have no inputs or outputs.
- **No inline editors.** Editing attachment properties happens in the Details panel.
- **No internal layout.** No icons-plus-rows. Just glyph + label.
- **No nested attachments.** An attachment cannot itself have attachments.
- **No connection lines.** Attachments aren't connected to anything visible; the spatial proximity to the host is the visual link.

---

## 6. Hit testing and interaction

### 6.1 Hit-test priority

For a point `p` in canvas coordinates, the existing hit-test order is (NodeEdit §6 / interaction state machine §12):
1. Reroutes
2. Pins
3. Links
4. Node headers / bodies
5. Comments

New order with attachments:
1. Reroutes
2. Pins
3. Links
4. **Attachments (highest first by `StackIndex`)** ← NEW position
5. Node headers / bodies
6. Comments

Attachments sit above nodes in hit-test priority because they visually sit above; a click on the top edge of a node area that overlaps an attachment's bottom-edge zone should select the attachment, not the host.

### 6.2 Spatial index

The spatial index (NodeEdit §1.1) handles attachments the same way it handles nodes: insert by AABB, query by point or rect. Attachment AABBs are recomputed when:
- The host node moves.
- The host's attachment list changes (add/remove/reorder).
- The host node's size changes (which affects wrap point).

Recomputation is cheap (a few floating-point ops per attachment).

### 6.3 Mouse interactions

| Interaction | Behavior |
|---|---|
| Click attachment | Selects the attachment. Single-selection unless modifier held. |
| Ctrl+click attachment | Toggles attachment in multi-selection. |
| Shift+click attachment | Extends selection along the visual stack (left-to-right, row-by-row). |
| Click attachment then drag | **No drag.** Click is a select-only operation. Pills do not move via drag. |
| Right-click attachment | Opens attachment-specific context menu (host-provided; see §6.4). |
| Hover attachment | Shows tooltip after 500 ms (matches existing NodeEdit tooltip delay). Background brightens 10%. |
| Click empty canvas | Deselects all attachments (matches node behavior). |
| Marquee selection over attachment | Includes attachment in marquee selection if AABB overlaps marquee rect. |

### 6.4 Context menu

NodeEditor doesn't define attachment context-menu content; it dispatches to the host:

```csharp
// New interface, parallel to ILinkValidator (NodeEdit §3)
public interface IAttachmentContextMenuProvider
{
    IReadOnlyList<ContextMenuItem> GetItemsFor(AttachmentId id);
}
```

The host registers an implementation via `IEditorHostServices.AttachmentContextMenu` (new field on the bundle). NodeEditor invokes it on right-click; the returned list renders in the standard context menu visual. If no provider is registered, right-clicking an attachment falls through to the canvas's empty-area context menu.

The BTree host's provider returns "Remove decorator," "Edit count," "Replace decorator type," etc. The HSM host returns "Edit deferred events," "Suppress warning," etc.

### 6.5 Keyboard interactions

Attachments are reachable through the normal selection cycle:
- **Tab** within a selected node cycles to its first attachment (StackIndex ascending), then through subsequent attachments, then back to the host node body, then to the next node.
- **Arrow keys** with an attachment selected move selection to the next/previous attachment in stack order; on an end-of-row, arrow up/down moves between attachment rows.
- **Delete** with attachments selected fires `RemoveAttachments` command.
- **Esc** with an attachment selected returns selection to the host node.

These bindings don't appear in §13 of the existing NodeEdit spec; they should be added there as part of integration.

---

## 7. Selection semantics

### 7.1 Mixed selection

Selection can contain a mix of nodes, links, comments, reroutes, AND attachments. The view-model's `SelectionState` (NodeEdit §4) extends:

```csharp
public sealed class SelectionState
{
    // ... existing fields ...

    public IReadOnlySet<AttachmentId> SelectedAttachments { get; }
    public AttachmentId? PrimaryAttachment { get; }   // for "primary" highlight

    // ... existing methods ...
}
```

Selection state changes fire the existing selection-changed event; consumers (Details panel, command sink) read the new fields.

### 7.2 Details panel routing

The Details panel (NodeEdit §19) currently routes by `DetailsTarget`. New target type:

```csharp
public abstract record DetailsTarget
{
    // ... existing cases ...

    public sealed record Attachment(AttachmentId Id) : DetailsTarget;
    public sealed record MultipleAttachments(IReadOnlyList<AttachmentId> Ids) : DetailsTarget;
}
```

Host providers implementing `IDetailsViewProvider.CanHandle(DetailsTarget)` may opt to handle these. The BTree host registers a provider that renders a `RepeaterFacet` / `CooldownFacet` / etc. depending on the selected attachment's `Category` and host-supplied subtype hint.

### 7.3 Selection precedence

When selecting a node, attachments owned by that node are NOT automatically selected. They're independent selection units. This matches the principle that attachments are visual annotations, not subordinate UI fragments — selecting a Sequence should not select its decorator pills, just as selecting a comment doesn't select the nodes inside.

If the user wants "select node and its attachments," that's a host-defined shortcut (e.g., a "Select host with attachments" command); NodeEditor doesn't provide it by default.

---

## 8. Commands

Extend the existing `GraphCommand` taxonomy (NodeEdit §5):

```csharp
public abstract record GraphCommand
{
    // ... existing records ...

    public sealed record AddAttachment(
        AttachmentId NewId,
        NodeId HostNodeId,
        AttachmentCategory Category,
        string? Glyph,
        string? Label,
        string? Tooltip,
        int StackIndex,
        IReadOnlyDictionary<string, object?>? HostProperties) : GraphCommand;

    public sealed record RemoveAttachments(
        IReadOnlyList<AttachmentId> AttachmentIds) : GraphCommand;

    public sealed record SetAttachmentProperty(
        AttachmentId Id,
        string Key,
        object? Value) : GraphCommand;

    public sealed record ReorderAttachments(
        NodeId HostNodeId,
        IReadOnlyList<AttachmentId> NewOrder) : GraphCommand;

    public sealed record MoveAttachment(
        AttachmentId Id,
        NodeId NewHostNodeId,
        int NewStackIndex) : GraphCommand;
}
```

`AddAttachment.HostProperties` is the escape hatch for host-specific attachment payload (Repeater's `Count`, Cooldown's `Duration`, etc.) — the dictionary is opaque to NodeEditor; the host's command sink interprets it.

`MoveAttachment` exists for completeness (moving an attachment from one host to another). The canvas doesn't expose this via drag in v1, but it's available for host-driven refactors (BTree's "promote subtree" might emit a sequence of `MoveAttachment` commands).

### 8.1 Undo

Each command's inverse is produced via the existing snapshot mechanism (NodeEdit §5). Specifically:
- Inverse of `AddAttachment` is `RemoveAttachments([NewId])`.
- Inverse of `RemoveAttachments` captures the full `IAttachmentModel` state before remove and emits `AddAttachment` per removed attachment on undo.
- Inverse of `SetAttachmentProperty` captures the old value and emits a setter to restore it.
- Inverse of `ReorderAttachments` captures the previous order.
- Inverse of `MoveAttachment` captures the previous host and index.

### 8.2 Batching

Multi-attachment operations (e.g., user removes a node that owns 3 attachments — the attachments must be removed too) batch into a single `GraphCommand.Batch` so one undo restores the full state. The graph command sink in the host is responsible for the batching when a `RemoveNodes` command would orphan attachments.

---

## 9. Reverse mapping for hosts

Hosts need to ask "which host node does this attachment belong to" and "given a host, what attachments does it have." Both are on `IGraphModel`:

```csharp
IAttachmentModel? IGraphModel.FindAttachment(AttachmentId id);
IReadOnlyList<IAttachmentModel> IGraphModel.GetAttachmentsForNode(NodeId hostId);
```

The host implementation typically maintains:

```csharp
private Dictionary<AttachmentId, IAttachmentModel> _byId;
private Dictionary<NodeId, List<IAttachmentModel>> _byHost;
```

Both updated on attachment add/remove/move. NodeEditor's canvas caches per-frame results from `GetAttachmentsForNode` for visible hosts only, so the cost of repeated lookup is bounded by the visible-node set, not the total attachment count.

---

## 10. Theme additions

Extend `IEditorTheme` (NodeEdit §3) with attachment-specific colors. Defaults from `DefaultTheme`:

```csharp
public interface IEditorTheme
{
    // ... existing members ...

    Vector4 AttachmentDecoratorColor { get; }    // default: #8E44AD purple
    Vector4 AttachmentFlagColor      { get; }    // default: #16A085 teal
    Vector4 AttachmentPureColor      { get; }    // default: #27AE60 green
    Vector4 AttachmentCustomColor    { get; }    // default: #7F8C8D gray

    float   AttachmentHeight         { get; }    // default: 20 px @ zoom 1.0
    float   AttachmentCornerRadius   { get; }    // default: 8 px
    float   AttachmentGapAboveHost   { get; }    // default: 6 px
    float   AttachmentInterGap       { get; }    // default: 4 px
}
```

Hosts may override any of these via a custom `IEditorTheme` (same mechanism as today for node colors).

---

## 11. Performance budget

The existing performance budget (NodeEdit §27) targets 500 nodes at ≤6 ms total canvas budget and 2000 nodes at ≤12 ms.

Attachments add work proportional to attachment count. Realistic scenario: a BTree with 200 nodes and ~150 decorator pills (typical for combat AI) — that's about 0.75 pills per node, sometimes 2–3 stacked. HSM with 80 states and ~30 flag chips. Blueprint usually has no attachments.

Budget for attachments at 200 nodes / 300 attachments:

| Phase | Cost added |
|---|---|
| Hit-testing | +0.05 ms (spatial index scaled by 1.5×) |
| Spatial index update | +0.1 ms (300 more AABBs) |
| Visible enumeration | +0.02 ms |
| Attachment rendering | +1.2 ms (worst case all visible) |
| Total | **+1.4 ms** |

This is within the 4 ms headroom in the 500-node budget. At low zoom, attachment rendering collapses to a single colored bar per host (§5.4) so the cost drops to nearly zero.

Render optimizations:
- **Cache per-attachment measurement** (glyph + label text size). Recompute only when label/glyph change.
- **Cache attachment layout** (per-host: positions, wrap point, row count). Recompute only when host width changes or attachment set changes.
- **Skip rendering** of attachments belonging to hosts that are off-screen (the host bounds include the attachment region for culling, so this is automatic).

For the worst case (a project with 2000 nodes and 1500 attachments), expected overhead is around 7 ms — pushes the total budget to ~19 ms (over the 16.6 ms frame budget). At that scale the user should be working at lower zoom, where attachments collapse, dropping the cost dramatically. Document this as a known limit; the editor remains usable, just not at 60 fps when fully zoomed in on dense regions.

---

## 12. Default style configurability

Three layers of style customization, from cheapest to most invasive:

**Per-host theme override** (cheap). Host registers a custom `IEditorTheme` with different attachment colors / sizes. All attachments of that host use the override.

**Per-category override** (cheap). Host provides a `Func<AttachmentCategory, Vector4>` color resolver. Allows e.g. different decorator-pill colors per BTree decorator type.

**Per-attachment custom drawer** (advanced). Host registers a `IAttachmentRenderer` that fully takes over rendering for a specific category or set of attachments:

```csharp
public interface IAttachmentRenderer
{
    AttachmentCategory? TargetCategory { get; }   // null = match all
    bool CanRender(IAttachmentModel attachment);
    void Render(IAttachmentModel attachment, Rect bounds, IAttachmentRenderContext ctx);
}
```

Registered via `IEditorHostServices.AttachmentRenderers`. NodeEditor checks each registered renderer in registration order; first matching `CanRender` wins. If none matches, the default renderer (rounded pill + glyph + label) is used.

Useful for, e.g., a BTree decorator with a small embedded sparkline of recent tick counts, or an HSM flag with a custom icon. Out of v1 scope but the interface is wired in so it's a Slice 2+ no-redesign add.

---

## 13. Backwards compatibility

Every API addition is additive. Hosts that don't implement the new interfaces behave exactly as today:

- `IGraphModel.Attachments` defaults to an empty collection if the host doesn't override.
- `IGraphModel.GetAttachmentsForNode(id)` returns empty for any node id if not overridden.
- `IAttachmentContextMenuProvider` / `IAttachmentRenderer` registrations are optional; absent means default behavior or no-op.
- `GraphChangeNotification.AffectedAttachments` is nullable; existing notifications continue to pass null.
- `SelectionState.SelectedAttachments` defaults empty.
- New `GraphCommand` records are records; pattern-matching in existing command sinks falls through to the existing exhaustiveness check (`_ => throw new NotSupportedException`) gracefully — the sink either handles them or throws clearly.

The Blueprint editor host, which has no need for attachments in v1, requires zero code change to work after the extension lands.

---

## 14. Test plan

### 14.1 Unit tests (NodeEditor.Core.Tests)

- **`AttachmentLayoutTests`** — given a host of width W with N attachments of varying widths, verify wrap points, row counts, total height, individual attachment positions.
- **`AttachmentHitTestTests`** — given a host with attachments at known positions, verify hit-test priority (attachment over host on top-edge overlap, attachment over attachment by StackIndex).
- **`AttachmentCommandsTests`** — verify `AddAttachment`, `RemoveAttachments`, `SetAttachmentProperty`, `ReorderAttachments`, `MoveAttachment` produce correct model state changes; inverse commands restore.
- **`AttachmentSelectionTests`** — verify mixed selection (nodes + attachments), tab/arrow navigation, marquee inclusion, batch delete cascades correctly when removing a host node.
- **`AttachmentSpatialIndexTests`** — verify spatial index updates correctly on attachment add/remove/move and on host move (which translates attachments).

### 14.2 Visual / integration tests (NodeEditor.Demo)

The demo app gains a "Attachments" scenario:
- A scene with mixed nodes: some with no attachments, some with 1, some with many (10+).
- Attachments of all four categories.
- Various states (normal, error, warning, disabled, executing).
- Wrap-point edge cases (exactly fits one row, exactly fits two rows, etc.).
- Low-zoom collapse verification at zoom 0.4 and below.

Manual verification checklist:
- Pills render above their host in the correct order.
- Selection outlines render cleanly without obscuring adjacent pills.
- Tooltips appear on hover after 500 ms.
- Tab cycles through pills then to next node.
- Multi-host pan/zoom doesn't cause pill jitter.
- Adding a 3rd pill that would exceed host width pushes the row to wrap; removing a pill un-wraps cleanly.
- Right-click brings up the host-supplied context menu (demo includes a stub provider).

### 14.3 Performance tests

- **`AttachmentRenderingPerf`** — render 200 nodes × 1.5 average attachments at zoom 1.0 in under 2 ms (sub-budget). Render 2000 nodes × same ratio at zoom 0.4 in under 1 ms (low-zoom collapse path).

---

## 15. Migration for the BTree host

Documenting the BTree host's adoption path so the extension is sanity-checked against its primary consumer.

### 15.1 Mapping the BTree data model

| BTree concept | NodeEditor representation |
|---|---|
| Composite (Sequence, Selector, ObserverSelector, Parallel) | Standard node |
| Leaf (Action, Condition, Wait) | Standard node |
| Subtree reference | Standard node (no inner detail) |
| Inverter / Repeater / Cooldown / ForceSuccess / ForceFailure / UntilSuccess / UntilFailure | Attachment on the *inner* node (the decorator wrapper's single child) |
| Root | Standard node |

The kernel's data model represents each decorator as a parent node with one child. The host's projection layer (already designed in `BTree_Editor_NodeEditor_Host_Design.md`, forthcoming) collapses chains of decorator-parents into attachments on their innermost descendant. The mapping is one-way: read from compiled assembly → editor model with attachments; emit from editor model → fluent C# with nested `.Inverter(...).Repeater(3, ...)` calls.

### 15.2 BTree host's attachment categories

All BTree decorator pills use `AttachmentCategory.Decorator`. The glyph carries the decorator type:

| Decorator | Glyph | Label |
|---|---|---|
| Inverter | `!` | none |
| Repeater | `↺` | `×N` (the count) |
| Cooldown | `⏲` | `Ns` (the duration) |
| ForceSuccess | `→` | `S` |
| ForceFailure | `→` | `F` |
| UntilSuccess | `⟳→` | `S` |
| UntilFailure | `⟳→` | `F` |

Outermost-in-source decorator gets the highest `StackIndex` (rightmost in the row, or topmost if wrapped). This matches the conceptual "evaluates result-bubbling last" rule from the BTree host design.

### 15.3 BTree host's interactions

- Right-click attachment → context menu: "Remove decorator," "Replace with…" (submenu of decorator types), "Edit parameters" (focuses Details panel).
- Click attachment → selects; Details panel shows the appropriate facet (Repeater's Count, Cooldown's Duration).
- Delete key on selected attachment → emits `RemoveAttachments`; the host's command sink emits a matching `RemoveNodes` for the underlying decorator-parent node in the kernel model.

### 15.4 BTree host's attachment lifecycle

When the user does "Add Decorator → Repeater(3)" on a Sequence node:

1. Host command sink receives a higher-level "AddDecorator" command (host-internal, not a NodeEditor `GraphCommand`).
2. Host updates its kernel-model representation: inserts a Repeater parent-node between the Sequence's parent and the Sequence itself.
3. Host updates its NodeEditor projection: emits a NodeEditor `AddAttachment` command on the Sequence's NodeId.
4. The canvas re-renders with the new pill above the Sequence.
5. The `ScheduleSave` debouncer eventually triggers a `.cs` file write that emits `.Repeater(3, r => r.Sequence(...))`.

Removing a decorator pill is the inverse. The kernel parent-node disappears; the projection emits `RemoveAttachments`.

---

This is the minimum surface to land for v1. The HSM state-flag use case slots in with no further changes; Blueprint pure-pills are deferred but the interface accommodates them.

Followups noted in this spec for tracking:
- §13 keyboard bindings table in NodeEdit-docs §13 needs Tab/Arrow extensions for attachments.
- §15.3 hit-test priority order in NodeEdit-docs §12 needs the "Attachments" line inserted.
- §27 perf budget in NodeEdit-docs §27 needs an attachment-budget paragraph.
- Each of these is a small edit to the existing NodeEdit spec, not a separate extension.

---
