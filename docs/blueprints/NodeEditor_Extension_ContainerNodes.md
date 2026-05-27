# NodeEditor — Container Nodes extension

> **Status:** Specification for a NodeEditor extension. Authored by the AI Editor team for the NodeEditor team (same team, different hat).
> **Audience:** NodeEditor implementer.
> **Drives:** HSM editor nested composite-state rendering (per `HSM_Editor_NodeEditor_Host_Design.md`); orthogonal-region dividers; potentially Blueprint editor composite-node groupings in Slice 2+.
> **References:** `NodeEdit-docs.txt` (the NodeEditor spec brief); `NodeEditor_Extension_NodeAttachments.md` (the previous extension; cross-referenced where attachment behavior on container headers comes up). All section numbers below the line of "in the existing spec" refer to `NodeEdit-docs.txt`.
> **Doesn't cover:** HSM-specific semantics (initial-state arrows, history pseudo-states, transition kinds, region priorities). Those live in the HSM host design. This spec is the canvas primitive only.

---

## Table of Contents

1. Motivation
2. The shape, in one picture
3. Non-goals
4. Identity and model additions
5. Layout
6. Visual rendering
7. Hit testing and interaction
8. Link routing across container boundaries
9. Selection semantics
10. Drag and drop into / out of containers
11. Commands
12. Auto-resize and reflow
13. Region dividers
14. Z-order and overlap
15. Save / serialization order
16. Theme additions
17. Performance budget
18. Backwards compatibility
19. Test plan
20. Migration for the HSM host
21. Comparison with comments

---

## 1. Motivation

NodeEditor today represents the graph as a flat collection of nodes with absolute positions in canvas space. This model is sufficient for Unreal-style event/data graphs where the graph topology is the only structure that matters and visual proximity is purely cosmetic.

HSM authoring fundamentally disagrees. A statechart's hierarchy *is* its meaning:

- A composite state visually contains its child states. The containment relationship is the data model — exiting the composite exits all children; entering the composite enters its initial child. You can't faithfully represent this with absolute-position rectangles that happen to be near each other.
- An orthogonal-region composite holds multiple sub-machines running in parallel, separated by dashed dividers. Each region is a sub-area within the composite; states inside one region must visually live in that region.
- The hierarchy is deep — combat AIs of 5+ levels are common in the literature, and the editor must scan them at a glance.

Flat layout with proximity-as-meaning is what the rest of the industry rejected fifteen years ago. UML statechart editors, the Yakindu Statechart Tool, itemis CREATE, and QM all use nested-box rendering. Force-flat rendering on HSM and the editor degrades from a tool into a maze; force-flat with manual "containing" comments and you've reinvented containers badly.

There's a secondary use case in Blueprint: collapsing a sub-graph into a "Composite Node" that visually contains its inner graph. Unreal supports this; today's NodeEditor doesn't. The extension should accommodate it.

The common requirement: **a node that contains other nodes as first-class children, with the container's bounds dynamically enclosing its children, and the child nodes' positions expressed relative to the container's interior.**

---

## 2. The shape, in one picture

```
       ┌─────── EnemyBrain (Composite) ────────────────────┐
       │ Header strip                                       │
       ├────────────────────────────────────────────────────┤
       │                                                    │
       │      ⦿─→ ┌─────┐                                   │
       │          │Idle │  ── OnSight ──→ ┌────────────┐    │
       │          └─────┘                  │   Alert    │    │
       │                                   │ (Composite)│    │
       │                                   └────────────┘    │
       │                                                    │
       │      ┌─── Combat (Parallel Composite) ─────┐       │
       │      │ Header                              │       │
       │      ├──────────────┬──────────────────────┤       │
       │      │ Region: Loco │ Region: Weapon       │       │
       │      ├──────────────┼──────────────────────┤       │
       │      │  ⦿─→[Walk]   │  ⦿─→[Aim] ── Fire ──→│       │
       │      │     ↕        │      ↓               │       │
       │      │  [Sprint]    │   [Reload]           │       │
       │      └──────────────┴──────────────────────┘       │
       │                                                    │
       └────────────────────────────────────────────────────┘
```

The outer rounded rectangle is a container node `EnemyBrain`. Three children: a regular node `Idle`, a regular node `Alert` (which happens to itself be a container — nesting is unbounded), and a parallel-region container `Combat` with two regions (`Loco` and `Weapon`) each holding its own sub-children.

The container's bounds auto-grow to enclose its children plus padding. Children's positions are stored relative to the container's interior origin; moving the container moves all children with it. The container has its own header (selectable, draggable) and its own status icons. Links between children render inside the container; links between a child and an outside node render across the boundary normally.

Attachments (per `NodeEditor_Extension_NodeAttachments.md`) work on container headers exactly as on regular nodes.

---

## 3. Non-goals

- **Not a layout engine.** The extension provides containment primitives. Auto-layout of children within a container (tidy-tree, force-directed, etc.) is host-driven — hosts call into a separate layout service that writes child positions, then this extension just renders the result.
- **Not a virtualization mechanism.** Containers don't paginate or scroll their contents in v1. A container with 200 children renders all 200; the host is responsible for keeping that scale reasonable (collapse, refactor into sub-graph, etc.).
- **Not a separate-graph mechanism.** A container's children are part of the same `IGraphModel` as its host. Diving into a container does not switch graphs; navigation happens on the same canvas. This is the right call because containers can hold children of arbitrary nesting; switching graphs at every level would explode breadcrumb cognitive load.
- **Not a pin-routing system.** Containers don't have pins. Links between children of a container connect via the children's pins directly, not through the container's edge. (HSM transitions, the primary motivating use case, are not pin-based — they go state-to-state, modeled separately.)
- **Not a generic "Group" feature.** Comments (NodeEdit §22) already provide the visual-grouping use case for flat graphs. Containers are for *structural* hierarchy where the parent-child relationship is itself the data.

---

## 4. Identity and model additions

### 4.1 Reuse `NodeId`, don't introduce a new identity

A container is a node. It has a `NodeId`, a `Title`, a `Category`, optional pins (HSM containers have none; Blueprint composite-nodes have entry/exit pins), and everything else `INodeModel` provides. The extension adds *additional* capabilities, not a parallel type.

### 4.2 New model interface

```csharp
// Add to NodeEditor.Core.Interfaces
public interface IContainerNodeModel : INodeModel
{
    /// <summary>
    /// True if this node is a container. A regular INodeModel returns false
    /// (the default extension method below provides this).
    /// </summary>
    bool IsContainer { get; }

    /// <summary>
    /// Child node IDs. Order is significant for sibling sorting (z-order
    /// within the container) and for save/serialization determinism.
    /// </summary>
    IReadOnlyList<NodeId> ChildNodeIds { get; }

    /// <summary>
    /// Region descriptors. Empty for non-parallel containers. Non-empty for
    /// orthogonal-region containers. Region count = ChildNodeIds.Count(by region).
    /// </summary>
    IReadOnlyList<RegionDescriptor> Regions { get; }

    /// <summary>
    /// For nodes inside a region container, which region index they belong to.
    /// Indexed by child position; out of range for non-region containers.
    /// </summary>
    int GetRegionIndexForChild(NodeId childId);

    /// <summary>
    /// Padding inside the container, from the inside edge to the child layout
    /// area. Used by auto-resize to compute container bounds.
    /// </summary>
    ContainerPadding Padding { get; }

    /// <summary>
    /// Minimum interior size; container auto-resize never shrinks below this.
    /// Useful for an empty container that should still be a clickable target.
    /// </summary>
    Vector2 MinimumInteriorSize { get; }

    /// <summary>
    /// True when collapsed (children hidden, container renders as a tall pill
    /// the size of its header). Hosts surface collapse via UI; renderer respects.
    /// </summary>
    bool IsCollapsed { get; }
}

public sealed record RegionDescriptor(
    int Index,
    string Name,
    int Priority,
    Vector4? CustomColor);

public sealed record ContainerPadding(
    float Top,
    float Right,
    float Bottom,
    float Left);
```

Default extension methods on `INodeModel`:

```csharp
public static class INodeModelExtensions
{
    public static bool IsContainerNode(this INodeModel node) =>
        node is IContainerNodeModel { IsContainer: true };

    public static IContainerNodeModel? AsContainer(this INodeModel node) =>
        node is IContainerNodeModel c && c.IsContainer ? c : null;
}
```

`IContainerNodeModel : INodeModel` means a host that wants a node to be a container implements the more specific interface; non-container nodes implement only `INodeModel`. Casting checks are cheap (`is` pattern); the canvas calls `AsContainer()` once per node per frame.

### 4.3 Position semantics for children

Each child's `INodeModel.Position` is in **the parent container's interior coordinate space**, not in canvas space. Specifically:

- A child node at position `(20, 30)` whose parent container is at canvas position `(500, 400)` with padding `Left=8, Top=24` renders at canvas position `(500 + 8 + 20, 400 + 24 + 30) = (528, 454)`.
- A child of a child: positions stack. The transform from child-local to canvas is the chain of ancestor positions plus paddings.
- The root level (children of "the graph" itself, not of any container) uses canvas coordinates directly.

This is a significant change from the current "all `INodeModel.Position` values are canvas-absolute" rule. Migration:

- **Non-container nodes whose parent is not a container**: position is canvas-absolute. Unchanged. Existing graphs continue to work.
- **Non-container nodes whose parent is a container**: position is parent-local. Hosts that adopt containers must update their `INodeModel.Position` accessor.
- **Container nodes**: their own position is in *their* parent's coordinate space (root containers use canvas; nested containers use their parent's interior).

A helper utility on `GraphView` handles transformation:

```csharp
public sealed class GraphView
{
    // ... existing members ...

    public Vector2 NodeCanvasPosition(NodeId id);
    public Vector2 NodeLocalPosition(NodeId id);   // returns INodeModel.Position
    public Rect NodeCanvasBounds(NodeId id);
    public Rect NodeInteriorBounds(NodeId id);     // for containers
    public NodeId? GetParentContainer(NodeId id);  // null = root level
}
```

The canvas uses `NodeCanvasPosition` everywhere it would have used `INodeModel.Position` directly; host code that doesn't care about containers continues to read `INodeModel.Position` (which returns parent-local).

### 4.4 Parent relationship — where it lives

Two design alternatives:
- **(A) Parent ID on `INodeModel`.** Every node carries a `NodeId? ParentContainerId`. Bidirectional lookup (parent → children, child → parent) is O(1).
- **(B) Children list on `IContainerNodeModel` only.** Parent is computed by lookup. Containers know their children; children don't know their parent.

**The spec chooses (A).** A nullable parent pointer on `INodeModel`:

```csharp
public interface INodeModel
{
    // ... existing members ...

    NodeId? ParentContainerId { get; }  // NEW; null = root level
}
```

Rationale: child-to-parent lookup is on every hit-test and every link-routing pass; doing it via reverse-search on container children lists would be expensive at scale. The cost of the new property on every node is negligible (one Guid? per node); the cost of computing it on-the-fly is not.

Both directions must stay in sync. Hosts implementing the model maintain it; the `IGraphModel.Changed` notification covers it via `NodesModified` when membership changes.

---

## 5. Layout

### 5.1 Container bounds

A container's outer bounds are computed by the canvas from:
- The container's header height (constant: 24 px, same as regular nodes).
- The interior area, which must enclose all child node bounds plus padding.
- Plus 1 px outline width.

```
Container outer bounds (canvas coords):
    x:      container.Position.X
    y:      container.Position.Y
    width:  max(MinimumInteriorSize.X, max_child_extent_x) + Padding.Left + Padding.Right + 2 * outline
    height: header_height + max(MinimumInteriorSize.Y, max_child_extent_y) + Padding.Top + Padding.Bottom + 2 * outline

Where max_child_extent_x = max over children of (child.LocalPosition.X + child.OuterWidth)
And similarly for Y.
```

Empty containers render at their minimum size (default 200 × 100 at zoom 1.0). Hosts override `MinimumInteriorSize` per container if desired (e.g., HSM regions might want different defaults).

### 5.2 Interior origin and padding defaults

The "interior origin" is the top-left of the area where children render. It's offset from the container's outer top-left by:
- `Padding.Left` horizontally
- `header_height + Padding.Top` vertically

Default padding at zoom 1.0:
- Top: 8 px (a bit of space below header)
- Left: 12 px
- Right: 12 px
- Bottom: 12 px
- For region containers: an additional 18 px is reserved at the top of each region for the region header (see §13).

### 5.3 Auto-resize policy

The container auto-resizes whenever:
- A child is added or removed.
- A child's local position changes.
- A child's bounds change (the child itself was resized or had attachments added/removed).
- The container's padding or minimum-interior-size changes.

Auto-resize is the *only* way a container's size changes in v1. The user does not manually resize a container by drag (no resize handles on the corners). The container's size is a pure function of its contents.

This is opinionated. It diverges from comments (NodeEdit §22, which are user-resizable). The rationale: containers in HSM are *structural*; their size is data-derived. A user-resized container that doesn't enclose its children would be a UI bug, and constantly fighting the user's manual sizing against auto-fit produces a worse experience than just doing the right thing automatically. Hosts that want a different behavior (Blueprint composite-nodes might want manual size) can request a future `AllowManualResize` toggle; deferred to Slice 2+.

### 5.4 Reflow on child move

When a child moves to a position that would exceed the container's interior (positive or negative), the container's bounds grow to fit. The container's *position* (its origin in its own parent's space) does NOT move — only its size changes. If a child drags toward negative X, the container's width grows by the same amount and the container's right edge extends; the container's left edge stays put.

If the user wants the container to "slide" to follow a child to negative space, that's done by selecting and dragging the container itself. The canvas doesn't auto-translate containers.

### 5.5 Nested container layout

Nested containers compute their bounds recursively, leaves first. The recursion is naturally bounded by the tree depth (HSM has a hard kernel limit of depth 16); recompute on each affected hierarchy edit, not every frame. Cache per-container bounds in the view-model; invalidate up the chain when a child changes.

### 5.6 Region layout

For parallel-region containers (`Regions.Count > 0`):
- The interior is divided horizontally OR vertically. Direction is host-supplied via a property on `RegionDescriptor` (or container-level); default vertical-stacking (each region is a horizontal strip).
- Each region's vertical extent = (interior_height − total_header_heights) / region_count, where region headers eat 18 px each.
- Children inside a region are positioned in the region's local coordinate space (interior origin = top-left of region's content area, after region header).
- Dragging a child *across* a region boundary updates its `RegionIndex` (the host's `GetRegionIndexForChild` recomputes); see §10 for drag-into-region details.

---

## 6. Visual rendering

### 6.1 Container outline

```
At zoom 1.0:

   ┌─────── Header (24 px) ───────┐    ← rounded top corners (4 px radius)
   │ ▼ Title                  ⚠ ●│    ← title, status icons, optional collapse arrow
   ├──────────────────────────────┤    ← horizontal divider, 1 px
   │                              │
   │   (interior content)         │
   │                              │
   └──────────────────────────────┘    ← rounded bottom corners
```

- Outline: 2 px in container's category color (50% darker than the header for definition).
- Header: same color as `NodeCategory` defines (NodeEdit §29), at full alpha.
- Interior background: container's category color at 8% alpha over the canvas background. Distinct enough to see the boundary; faint enough that nested colors layer without clashing.
- Header divider: 1 px line in container category color at 40% alpha.
- Corner radius: 6 px (slightly larger than regular nodes' 4 px, distinguishes containers).
- Selection outline: same as regular nodes — 2 px theme accent.

### 6.2 Header collapse affordance

A small ▼ / ▶ chevron at the left of the title indicates collapsible state. Click to toggle; emits `SetNodeCollapsed` command (already in NodeEdit §5). When collapsed, the container renders as a tall pill the height of its header only; children disappear from the canvas (but still exist in the model). Links between children of a collapsed container are not rendered. Links from outside to/from children of a collapsed container terminate at the container's edge with a small visual indicator (a dot on the boundary) and a hover tooltip showing the hidden endpoint.

### 6.3 Title bar

Identical to regular nodes (NodeEdit §7) — category icon, title text (two lines max with ellipsis), status icons. Attachments per `NodeEditor_Extension_NodeAttachments.md` render above the title bar exactly as on regular nodes. The container's status state (Error / Warning / Disabled) propagates the same outline / desaturation rules as regular nodes.

### 6.4 Interior rendering

Children are rendered recursively. Each container, when drawn:
1. Compute outer bounds (cached).
2. Draw fill, header, outline.
3. Set a clip rect to the interior area.
4. Draw region dividers (§13) if applicable.
5. Recursively draw each child in stack-index order.
6. Pop clip rect.
7. Draw selection outline if selected.

Step 3 is essential: children that overflow the container's interior (because the container is mid-resize, or because of a layout glitch) are clipped at the container's bound. They aren't lost; just visually clipped. This catches bugs early.

### 6.5 Low-zoom rendering

Below zoom 0.5 (matches the existing low-zoom threshold per NodeEdit §6):
- Container renders as a solid rectangle in its category color, with the container's header strip in a slightly brighter shade.
- Children of the container also follow low-zoom rendering recursively.
- Region dividers (§13) are drawn as faint lines.

The visual hierarchy survives at low zoom because the container colors and outlines remain visible; the user can still see the structural shape of a deeply-nested HSM at 0.25× zoom even if individual states have no readable text.

### 6.6 Render order globally

The existing paint order (NodeEdit §6) needs to extend:

1. Background + grid
2. Comments (back to front by ZOrder)
3. Containers — outermost first (this is essential to get nesting layering right)
   - For each container: fill, header, outline (without children)
4. Wires (so wires render over container fills but under nodes — same as today)
5. Children of containers, recursively. Each child is drawn over its container, and a child container's interior children are drawn over the child container.
6. Regular (root-level) nodes
7. Attachments (per the NodeAttachments extension)
8. Reroutes
9. Selection outlines
10. Hover effects
11. Active drag preview

Specifically, containers are split: their fills go in step 3 (before wires), and their children go in step 5 (after wires) — this ensures wires don't accidentally render on top of child nodes that happen to sit inside a container.

---

## 7. Hit testing and interaction

### 7.1 Hit-test priority

The existing order (NodeEdit §12) extends:

1. Reroutes
2. Pins
3. Wires
4. Attachments (per the NodeAttachments extension)
5. **Container collapse-arrow chevron** ← NEW
6. **Container header strip (excluding chevron)** ← NEW
7. Comment title bars
8. Node bodies (regular nodes and container children)
9. **Container interior (empty area not covered by a child)** ← NEW
10. Comment bodies (pass-through)
11. Empty canvas

Two key rules:
- **Children before container interior.** Clicking on a child node selects the child, not the parent container.
- **Container header is hot.** Clicking the header selects (and is the drag handle for) the container itself.
- **Container interior (empty) is selectable.** Clicking an empty area inside a container selects the container — useful for an empty container that the user wants to interact with. Press Esc deselects.

### 7.2 Marquee selection

Marquee inside a container selects children whose AABB is contained in the marquee rect (using canvas coordinates after the local→canvas transform). Marquee does *not* select the container itself even if the marquee fully encloses the container; the container is its own selection target via its header.

Holding Alt while marqueing INSIDE a container scopes the marquee to that container's children — children of a different container aren't selected even if the marquee extends over them. Useful for picking a subset of states without grabbing children of an adjacent region.

### 7.3 Pan and zoom

Pan and zoom are global; containers don't have their own viewport. A user wishing to "zoom into" a container does so by ordinary canvas zoom centered on the container, or by `F` (frame selection — if a container is the primary selection, frame fits its outer bounds).

A future Slice 2+ feature might add "Enter container" (navigates as if the container were the graph root) — but v1 keeps all containers visible on one canvas. The HSM editor's largest realistic graphs (60–80 states, depth 4–5) work well at moderate zoom without needing dive-in navigation.

### 7.4 Hover

Hovering a container's header highlights the header (10% brightening). Hovering the interior empty area does *not* highlight — the container background stays its usual 8% alpha — because hovers in the interior signal "I'm pointing into the interior to interact with children," not at the container itself.

---

## 8. Link routing across container boundaries

### 8.1 Endpoint-relative routing

Links don't change at all conceptually — they connect pins. What changes is the routing of the Bezier curve when one or both pins are inside containers.

Existing routing (NodeEdit §10): Bezier between source-output pin and target-input pin, tangent strength `max(50, abs(dx) * 0.5)`.

New routing: the same algorithm, but using canvas-coordinate positions of the pins. Since `GraphView.NodeCanvasPosition` already provides the transform, and pins inherit their node's transform, this is automatic — no special-casing in the wire renderer.

### 8.2 Crossing container boundaries

A wire from a child of container A to a child of container B (or to a root node) renders normally. It visually exits A's outline, crosses canvas space, and enters B's outline. This is fine and matches what HSM authors expect — a transition from a state inside one composite to a state in another composite is a real, drawable arrow.

The wire is clipped at neither container boundary; it crosses freely.

### 8.3 Hidden endpoints (collapsed containers)

When a wire's endpoint is inside a collapsed container, the endpoint is not visible. The wire terminates at the container's boundary with a small filled circle (~8 px) and a hover tooltip showing the hidden endpoint's owner. Clicking the indicator selects the collapsed container; double-clicking expands it.

### 8.4 Self-links

A self-link on a state (HSM-style) connects a state's "output" to its own "input" — except HSM states don't have pins. This is solved differently in the HSM host (transitions are first-class records, not pin-routed links). The extension doesn't change link semantics for self-loops; the HSM host renders its self-transitions as a custom link kind.

For *pin-based* self-links (a Blueprint output pin to one of its own input pins), routing is the existing NodeEditor behavior — a loop curve. Unaffected by containers.

---

## 9. Selection semantics

### 9.1 Container as selection target

A container is selectable like any other node. Selecting a container does NOT auto-select its children; selecting a child does NOT auto-select its container. They're independent selection units.

### 9.2 Container in mixed selection

A selection set can include the container and some-or-all of its children. Operations apply per-selected-item:
- Delete with container + some children selected: removes only the items in the selection. The container's children list updates; the container survives (if it itself is not in the selection set), now with fewer children.
- Move (drag) with container + a child selected (child is also a descendant): the child moves *with* the container by virtue of being inside it, plus moves *additionally* by the same drag delta as the container. Net effect: child appears to move twice as far as the container. This is confusing.

To avoid the confusion, the canvas applies a rule: **if a selected item's ancestor is also selected, only the ancestor's drag is applied to it.** Selecting both a container and one of its children, then dragging, moves the container (and its children naturally come along); the additional selection of the child is ignored for drag purposes. The child's selection is still preserved for delete and other operations.

### 9.3 "Select all descendants"

A keyboard shortcut (host-bindable; default `Ctrl+Shift+A` when a container is selected) selects the container and all its descendants. Useful for cutting / copying an entire sub-structure.

---

## 10. Drag and drop into / out of containers

### 10.1 Drop targets

During node drag, the canvas computes the "drop target" once per frame:
- If the cursor is over a container's interior (after children are subtracted), the drop target is that container.
- If the cursor is over a child of a container, the drop target is the *child's parent container* (we don't drop *onto* nodes; we drop *next to* them, into their parent's interior).
- If the cursor is over a region within a parallel container, the drop target is that container AND that region.
- If the cursor is over empty canvas, the drop target is the graph root (no parent).

The drop target highlights with a faint accent-color outline (2 px, theme accent) on the container's interior. The user can see exactly where the drop will land.

### 10.2 Drop semantics

On drop:
- The dragged node's `ParentContainerId` is set to the drop target's NodeId (or null if root).
- The dragged node's `Position` is recomputed: the cursor position in canvas coords minus the new parent's interior-origin in canvas coords. This means the node visually lands at where the cursor released, but its stored position is now parent-local.
- For region containers, the dragged node's `RegionIndex` is also set (host-provided lookup based on which region the cursor was in).
- The container's auto-resize fires.

The graph-level change emits a `ChangeParent` command (§11).

### 10.3 Dragging multiple nodes into a container

A drag operation may have a multi-selection. All dragged nodes target the same parent (the cursor's container); positions are computed relative to that parent.

If some dragged nodes are descendants of the target container and others are not, the operation still works coherently: the descendants stay descendants (no reparenting needed; positions update for the drag delta); the non-descendants get reparented.

### 10.4 Dragging out of a container

Dragging a child outside its container's interior (cursor leaves the container's bounds) reparents it to the container's parent or the root, depending on which container's interior the cursor ends up over. Same logic as §10.1.

A subtle case: a child dragged just past the container's edge but still over the parent container of *that* container becomes a child of the parent. This is "promoting" a node up one level. The canvas shows the drop target on the parent container during the drag so the user knows the promotion is happening.

### 10.5 Forbidden drops

- A container cannot be dropped into itself (would create a cycle).
- A container cannot be dropped into one of its descendants (would also create a cycle).

Both are detected by walking up the target's ancestor chain looking for the dragged container. Detection runs every frame during drag; on detection, the drop target turns red and the operation is rejected on release.

---

## 11. Commands

Extend `GraphCommand` (NodeEdit §5):

```csharp
public abstract record GraphCommand
{
    // ... existing records ...

    public sealed record ChangeParent(
        NodeId NodeId,
        NodeId? NewParentContainerId,    // null = move to root
        int? NewRegionIndex,             // applicable if new parent is region container
        Vector2 NewLocalPosition) : GraphCommand;

    public sealed record ChangeParentMultiple(
        IReadOnlyList<ChangeParentMove> Moves) : GraphCommand;

    public sealed record SetContainerCollapsed(
        NodeId ContainerId,
        bool IsCollapsed) : GraphCommand;

    // Region-specific:
    public sealed record AddRegion(
        NodeId ContainerId,
        int InsertAtIndex,
        string RegionName,
        int Priority) : GraphCommand;

    public sealed record RemoveRegion(
        NodeId ContainerId,
        int RegionIndex,
        ChildRedistributionPolicy Policy) : GraphCommand;

    public sealed record ReorderRegions(
        NodeId ContainerId,
        IReadOnlyList<int> NewOrder) : GraphCommand;

    public sealed record SetRegionProperty(
        NodeId ContainerId,
        int RegionIndex,
        string Key,
        object? Value) : GraphCommand;
}

public sealed record ChangeParentMove(
    NodeId NodeId,
    NodeId? NewParentContainerId,
    int? NewRegionIndex,
    Vector2 NewLocalPosition);

public enum ChildRedistributionPolicy
{
    DeleteChildren,        // children of removed region are deleted
    MoveToFirstRegion,     // moved to region 0 (or, if it doesn't exist, container's first region)
    MoveToParent,          // promoted out to the container itself (no region)
}
```

### 11.1 Undo

- Inverse of `ChangeParent` captures the old `ParentContainerId`, `RegionIndex`, and `Position`; restores them.
- Inverse of `SetContainerCollapsed` toggles back.
- Inverse of `AddRegion` is `RemoveRegion`.
- Inverse of `RemoveRegion` captures all removed children and their positions and re-adds them with the region.

`ChangeParentMultiple` is a single undo step; multi-node reparent during drag.

### 11.2 Forbidden-state validation

The graph model's command sink rejects commands that would create cycles (container parented into itself or a descendant). Rejection happens at command application; the canvas pre-validates during drag (§10.5) to avoid producing the command in the first place.

---

## 12. Auto-resize and reflow

### 12.1 Trigger events

Auto-resize fires on:
- `AddNode` where the node's parent is a container.
- `RemoveNode` where the node's parent is a container.
- `MoveNodes` where a moved node's parent is a container.
- `ChangeParent` (entering or leaving a container).
- Any command that changes a child node's outer bounds (size override, advanced-pins-shown change, attachment add/remove).
- `AddRegion`, `RemoveRegion`, `ReorderRegions` on a region container.

### 12.2 Algorithm

```
Resize(container):
    interior_size = max(MinimumInteriorSize, BoundingBoxOfChildren(container) + (1, 1))
    new_outer_size = (
        interior_size.X + Padding.Left + Padding.Right + 2 * outline_width,
        interior_size.Y + Padding.Top + Padding.Bottom + header_height + 2 * outline_width
    )
    if container.OuterSize != new_outer_size:
        container.OuterSize = new_outer_size
        if container.ParentContainerId is not null:
            Resize(GetContainer(container.ParentContainerId))   // propagate
```

Recursive propagation up the chain stops when a container's size doesn't change. In practice, most edits affect a leaf or one level of ancestors; the recursion depth is bounded by HSM depth (≤16) regardless.

### 12.3 Sibling shift on insert

When a new child is added at a position that would overlap an existing sibling, the canvas does *not* automatically shift siblings. The host's command (typically `AddNode` from a user gesture) is responsible for choosing a non-overlapping position. If the host wants automatic placement (e.g., HSM "add state" command picks a position based on existing-state layout), the host implements that policy; the canvas just renders.

For drag-and-drop into a container, the drop position is whatever the cursor was at — the user is responsible for placement. If they drop on top of an existing child, the result is visual overlap (which is allowed in NodeEditor today; no policy change for containers).

### 12.4 Throttling

Repeated auto-resize during a drag is throttled. The canvas defers the size change until end-of-frame to avoid recomputing layout twice if multiple commands fire in one input cycle. Auto-resize is fast (a few floating-point ops per affected container) but the throttling matters because a drag of N nodes inside a container would otherwise trigger N resizes per frame.

---

## 13. Region dividers

### 13.1 Visual

Inside a region container, dashed lines separate adjacent regions:
- Dash pattern: 4 px on, 3 px off.
- Color: container's category color at 50% alpha (less prominent than the outer outline).
- Width: 1 px.

Region headers are drawn at the top of each region's content area:
- Height: 18 px.
- Background: container's category color at 25% alpha (a subtle band).
- Content: small region-name label on the left, region priority indicator on the right (small badge "P:2").
- Optional region-specific color tint via `RegionDescriptor.CustomColor` overlays as the background instead of the default.

### 13.2 Region interactions

- Click region header → selects the *container* (not the region itself — regions are not selection targets). The Details panel can route to a "Container with focus on region N" target if the host registers one.
- Drag region header divider → no behavior (regions are equal-sized by default; manual resize deferred to Slice 2+).
- Right-click region header → context menu (host-provided): "Rename region," "Change priority," "Delete region," "Add region above/below."

### 13.3 Region direction

Per `RegionDescriptor`, regions can stack vertically (horizontal dividers) or horizontally (vertical dividers). Default is vertical-stack. Direction is per-container; mixing within one container isn't supported.

For vertical stacking, the dividers are horizontal lines and region headers run along the top of each region's strip. For horizontal stacking, dividers are vertical and headers run along the left edge of each column. Both share the same data; only the rendering loop differs.

### 13.4 Empty regions

A region with no children is allowed. It renders as an empty strip the height of `MinimumInteriorSize / region_count`. The container's auto-resize ensures the strip has at least enough space for the region header plus a small drop-target hint area.

---

## 14. Z-order and overlap

### 14.1 Containers vs. comments

Comments (NodeEdit §22) are at paint step 2 (behind wires and nodes). Containers are at paint step 3 (also behind wires, but in front of comments). The order: **comments < containers < wires < nodes/children < attachments < selection outlines**.

A comment can visually surround a container — the container's interior renders over the comment's body. A container can visually surround comments inside its bounds, but those comments are rendered first (their ZOrder), then the container's interior fill is laid over them.

Edge case: if a container is positioned over the same canvas region as a comment, they interleave correctly because containers render before comments… wait, that contradicts the previous paragraph. Let me restate:

**Comments draw first (step 2). Containers draw second (step 3). Wires draw third (step 4). Children draw fourth (step 5).**

A comment positioned where a container also is: comment renders, then container overlays (so the container is on top of the comment). A user wanting a comment to "surround" a container should size the comment slightly larger and position it lower in ZOrder — same as today's comment-vs-node z-order rule.

### 14.2 Nested containers

A child container renders after its parent container's fill but the child's own fill renders over the parent's interior fill. Recursively: each descendant level layers over its ancestors. This is exactly how UML statecharts read — innermost states are visually crisp.

### 14.3 Children's z-order within a container

The container's `ChildNodeIds` list is order-significant: earlier children render first (behind later ones). This matches the existing implicit z-order of `IGraphModel.Nodes`. Hosts can reorder via a `SetChildNodeOrder` command (a new sub-case of `ChangeParent` with the same `NewParentContainerId` but a different position in the children list — represented via the `NewLocalPosition` parameter and the parent's natural reordering on receive).

Actually, simpler: add `BringChildToFront(NodeId)` / `SendChildToBack(NodeId)` commands. Out of v1 scope for now; siblings z-order rarely matters for HSM (states don't visually overlap).

---

## 15. Save / serialization order

### 15.1 The determinism requirement

The fluent-C# emitter must produce byte-identical output across runs (AI Editor Shared Infrastructure §6.2). For container nodes this means: the children must be emitted in a deterministic order.

The order is: **the children list's order in the model.** Specifically, when the emitter walks a container, it iterates `IContainerNodeModel.ChildNodeIds` in order. Hosts maintain this list in a stable order — typically the order of original insertion, modified by explicit reorder operations.

### 15.2 Reorder commands and persistence

Auto-resize, drag, and other layout-only operations don't change child order. Adding a new child appends to the end. Removing a child compacts. Explicit reorder commands (a future `ReorderChildren` if added) shift positions.

This means a child list `[A, B, C]` round-trips: emit produces `.Child(A).Child(B).Child(C)`; reflection-on-load reads them in that order; the model's children list is `[A, B, C]`. The fluent builder for HSM (`HsmBuilder.State(...).AddChild(name).AddChild(name)`) preserves insertion order naturally.

### 15.3 Region children order

A region container's children list is partitioned by region in storage:
- Children of region 0 first, in their stable order.
- Then children of region 1.
- And so on.

The `RegionIndex` per child plus the list order determines the emit sequence:

```csharp
.State("Alert")
    .Parallel()
    .Region("Locomotion")  // region 0
        .AddChild("Walk")
        .AddChild("Sprint")
    .EndRegion()
    .Region("Combat")      // region 1
        .AddChild("Aim")
        .AddChild("Fire")
    .EndRegion()
```

The fluent-C# emit follows this nesting; the editor's child-list partitions children by region for serialization.

---

## 16. Theme additions

Extend `IEditorTheme`:

```csharp
public interface IEditorTheme
{
    // ... existing members ...

    float   ContainerCornerRadius          { get; }  // default: 6 px
    float   ContainerOutlineWidth          { get; }  // default: 2 px
    float   ContainerHeaderHeight          { get; }  // default: 24 px (matches regular node header)
    float   ContainerInteriorAlpha         { get; }  // default: 0.08
    float   ContainerRegionHeaderHeight    { get; }  // default: 18 px

    Vector4 ContainerRegionDividerColor    { get; }  // default: container category color at 50% alpha
    float   ContainerRegionDividerWidth    { get; }  // default: 1 px
    Vector2 ContainerRegionDividerDashLen  { get; }  // default: (4, 3) = 4 on, 3 off

    ContainerPadding ContainerDefaultPadding { get; }  // default: 8/12/12/12
    Vector2 ContainerDefaultMinimumInterior  { get; }  // default: (200, 100)
}
```

All values are at zoom 1.0; scale with zoom.

---

## 17. Performance budget

NodeEdit §27 budgets 6 ms total canvas at 500 nodes, 12 ms at 2000.

Container rendering adds:
- An extra recursive walk over the node tree for layout (each container computes interior bounds from children).
- Per-container fill/header/outline draws (a few primitives per container).
- A clip-rect push/pop per container for interior rendering.
- Auto-resize propagation up the ancestor chain on relevant edits.

Realistic HSM scenario: 80 states, 5 composite containers, 2 region containers with 2 regions each. Per-frame additional cost:

| Phase | Cost added |
|---|---|
| Container bounds computation (cached, invalidate-on-change) | +0.05 ms |
| Container fill/header/outline rendering | +0.3 ms |
| Region divider rendering | +0.05 ms |
| Clip-rect ops | +0.15 ms |
| Total | **+0.55 ms** |

Well within the existing budget. The bigger concern is depth-16 nesting: deeply-nested layout invalidation cascades. Empirically, deep nesting in HSM is uncommon (most real machines are 3–5 levels), and the recursion is bounded by depth, not by total node count.

Worst-case adversarial: 2000 nodes with 200 containers in a fully-balanced 5-level tree. Container overhead estimated at ~2 ms. Still under the 12 ms budget at 2000-node scale.

Render optimizations:
- **Cache container bounds** in a side table; invalidate on the specific events listed in §12.1.
- **Skip rendering** of containers that are fully off-screen *and* whose children are also fully off-screen (cheap AABB check against viewport).
- **Coalesce auto-resize** within a single frame: track which containers need resize during input processing, recompute once at end of frame.

---

## 18. Backwards compatibility

All additions are additive:

- `INodeModel.ParentContainerId` is nullable; existing hosts pass null and behave as today.
- `IContainerNodeModel` is opt-in; nodes that don't implement it remain flat.
- The new `GraphCommand` records are records; existing command sinks that don't match them fall through to the existing exhaustiveness check.
- `GraphView`'s new helper methods (`NodeCanvasPosition`, etc.) reduce to the existing `INodeModel.Position` when no containers are involved.
- The render-order extension preserves the existing relative order of nodes, wires, comments, reroutes. Containers slot in as a new layer; non-container hosts see no behavior change.
- Theme additions all have defaults; existing themes don't need to set them.

The Blueprint editor host, which doesn't currently use containers, requires zero code change to continue working after the extension lands. Blueprint adopts containers later (for composite-node grouping) at its own pace.

---

## 19. Test plan

### 19.1 Unit tests (NodeEditor.Core.Tests)

- **`ContainerBoundsTests`** — given a container with N children at known positions, verify computed outer bounds, interior origin, auto-resize on child add/move/remove.
- **`ContainerTransformTests`** — `NodeCanvasPosition`, `NodeLocalPosition` round-trip; nested containers stack correctly; root nodes unchanged.
- **`ContainerHitTestTests`** — clicking a child selects the child, not the container; clicking interior empty area selects the container; nested containers — clicking the deepest child wins.
- **`ContainerDragTests`** — drag container moves children with it; drag child within container updates parent bounds; drag child out of container reparents; drag container into a descendant is rejected.
- **`ContainerCycleDetectionTests`** — parenting a container into itself rejected; parenting into descendant rejected at command sink.
- **`RegionLayoutTests`** — regions partition interior correctly (vertical and horizontal); empty regions have minimum height; region count change reflows children.
- **`ContainerCommandsTests`** — `ChangeParent`, `SetContainerCollapsed`, `AddRegion`, `RemoveRegion`, `ReorderRegions` produce correct state changes; inverse commands restore.
- **`ChildOrderDeterminismTests`** — round-trip a model with N containers and M children each; verify emit order equals load order.

### 19.2 Visual / integration tests (NodeEditor.Demo)

The demo gains a "Containers" scenario:
- Single container with a few children (basic case).
- Nested containers, 4 levels deep.
- A parallel-region container with 3 regions, each holding 2–3 children.
- An empty container (verify minimum size).
- A collapsed container (verify children hidden, link indicators on boundary).
- Mixed: containers with regular nodes at the root level, wires crossing container boundaries.

Manual verification checklist:
- Drag container moves children.
- Drag child stays inside container; container resizes if needed.
- Drag child outside container reparents to root or another container correctly.
- Drag a parent into a child (or self) is rejected with red drop-target indicator.
- Collapse / expand container respects link indicators.
- Region dividers render correctly; region headers are clickable for context menu.
- Low-zoom (< 0.5×) container collapse renders correctly with nested colors.
- Selection of container and child + drag: only container drag delta applies (per §9.2).

### 19.3 Performance tests

- **`ContainerRenderingPerf`** — render 80 states with 5 composite containers at zoom 1.0 in under 1 ms (HSM realistic case).
- **`DeepNestingPerf`** — 16-deep nested container chain. Verify auto-resize propagation completes in under 5 ms even when leaf changes.
- **`ManyRegionsPerf`** — 2000-node graph with 200 region containers. Total render budget < 10 ms.

---

## 20. Migration for the HSM host

Documenting the HSM host's adoption path so the extension is sanity-checked against its primary consumer.

### 20.1 Mapping the HSM data model

| HSM concept | NodeEditor representation |
|---|---|
| Simple state | Regular node (`INodeModel`) |
| Composite state | Container node (`IContainerNodeModel` with `Regions.Count == 0`) |
| Parallel composite state | Container node with `Regions.Count > 0` |
| Orthogonal region | `RegionDescriptor` within a parallel container |
| Final state | Regular node with `Category = Custom` and a small dot glyph |
| History pseudo-state (H, H\*) | Regular node, ~20 px square, special category |
| Transition | Standard link (FromPin/ToPin point at state nodes — the HSM host gives every state two invisible "any" pins for transition routing) |
| State flags (deferred events, conflicts) | Attachments on the state's header (per NodeAttachments extension) |
| Initial-state marker | An attachment glyph `⦿` plus an arrow rendered separately by the host (using NodeEditor's wire-renderer extension hooks if needed; for v1 the host owns custom rendering of the initial-arrow as a non-link visual) |

### 20.2 HSM container flow

When the user creates a composite state by right-clicking and choosing "Make composite":

1. Host command sink receives "MakeComposite" (host-internal).
2. Host's editor model upgrades the state's representation to `IContainerNodeModel`.
3. Existing children (if any — there shouldn't be unless the user is converting a state with multiple selected siblings into a composite) are reparented to it.
4. Container is rendered.
5. On next save, the fluent-C# emit writes `.State("X").AddChild("Y")` rather than `.State("X")` + separate `.State("Y")`.

When the user creates a region:

1. Host command sink emits `AddRegion`.
2. NodeEditor adds the region to the container's `Regions` list.
3. Container auto-resize fires.
4. The user drags states into the new region (drop target highlights the region).

### 20.3 Initial-state arrow rendering

HSM's `⦿─→[State]` initial-state marker isn't a link in NodeEditor's sense. The host renders it as a custom-draw overlay inside the container's interior — the host has access to NodeEditor's per-frame draw callback via `IEditorHostServices.CustomCanvasRenderer` (a new optional service the extension would benefit from registering; if not available in v1, the host renders the arrow on top of the canvas at end-of-frame using its own ImGui draw list).

Out of strict v1 scope but flagged as a known need.

### 20.4 Transitions vs. NodeEditor links

HSM transitions have semantics NodeEditor links don't: priority, sync groups, internal/external/local kinds, guards, actions. NodeEditor links carry only "from pin → to pin." The host represents a transition as an `ILinkModel` plus a sidecar map `Dictionary<LinkId, HsmTransitionMetadata>` for the extra properties. The canvas renders the link normally; the host renders the label (event/guard/action text) at the link's midpoint via the same custom-draw mechanism as the initial-state arrow.

### 20.5 Forbidden HSM-specific operations

- Dropping a state outside its current container at the canvas edge promotes it to the parent composite (or to root). Hosts may reject specific moves (e.g., a state inside a parallel region cannot be promoted directly to the parent of the parallel container without first being moved out of the parallel container) by rejecting the `ChangeParent` command at the host's command sink.

---

## 21. Comparison with comments

For clarity, since comments (NodeEdit §22) and containers superficially resemble each other:

| Aspect | Comment | Container |
|---|---|---|
| Data model | Standalone `CommentBox` record | Specialized `INodeModel` subtype |
| Lifecycle | Independent of any node | Has identity as a graph node |
| Children | Encloses by visual overlap | Owns children explicitly |
| Move | "Move with contents" optional, opt-in per comment | Always moves contents |
| Resize | User-resizable handles | Auto-resize only (v1) |
| Z-order | Behind nodes and wires | Behind wires, in front of comments |
| Visual emphasis | Faint pastel body, slight outline | Stronger outline, category color |
| Role in serialization | Pure decoration | Structural; affects topology / emit order |
| Hierarchy | Flat (one ZOrder integer) | Tree (parent-child relationships) |
| Pins | None | None (but contained nodes have pins) |

Comments stay. Containers join. Hosts pick whichever matches the semantics: comments for organizational grouping, containers for structural hierarchy. HSM uses containers. Blueprint uses both (comments for grouping, future composite-nodes as containers).

---

## 22. Followups to other NodeEdit-docs sections

This extension touches existing sections; flagged for synchronized edits in NodeEdit-docs.

- **§6 paint order** — insert containers at step 3, child rendering at step 5 (per §6.6 here).
- **§7 node visuals** — add note that containers extend node visuals with the additions in §6 here.
- **§10 wire mechanics** — no change; routing extends naturally via canvas-space pin positions.
- **§12 interaction state machine** — extend hit-test priority with container hot zones (per §7.1 here).
- **§13 keyboard shortcuts** — add `Ctrl+Shift+A` (select all descendants of selected container).
- **§19 details panel** — add `DetailsTarget.Container(NodeId)` and `DetailsTarget.Region(NodeId, int)`.
- **§22 comments & reroutes** — add the comparison table from §21 here as a "see also" pointer.
- **§27 perf budget** — add a containers budget paragraph.
- **§29 color conventions** — add the container interior/outline/region-divider entries from §16 here.

Each is a small edit to the existing NodeEdit spec, not a separate document.

---
