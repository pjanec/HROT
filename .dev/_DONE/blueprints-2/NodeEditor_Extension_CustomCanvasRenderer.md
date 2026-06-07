# NodeEditor — Custom Canvas Renderer extension

> **Status:** Specification for a NodeEditor extension. Authored by the AI Editor team for the NodeEditor team.
> **Audience:** NodeEditor implementer.
> **Drives:** HSM editor initial-state arrows, transition labels, region-conflict overlays (per `HSM_Editor_NodeEditor_Host_Design.md`); BTree editor guard-observer connection badges (per `BTree_Editor_NodeEditor_Host_Design.md`); subsystem-specific runtime overlays that aren't a node, link, comment, or attachment.
> **References:** `NodeEdit-docs.txt`, `NodeEditor_Extension_NodeAttachments.md`, `NodeEditor_Extension_ContainerNodes.md`. Section numbers below "in the existing spec" refer to NodeEdit-docs.
> **Doesn't cover:** Per-host content. The extension provides the *when* and *where* of custom drawing; hosts decide what to draw.

---

## Table of Contents

1. Motivation
2. The shape, in one picture
3. Non-goals
4. The model — render slots and pass identity
5. The renderer interface
6. The render context
7. Coordinate spaces
8. Hit testing of custom-drawn content
9. Selection of custom-drawn elements
10. Z-order and pass ordering
11. Threading and lifetime
12. Performance budget
13. Theme additions
14. Backwards compatibility
15. Test plan
16. Migration for the HSM host
17. Migration for the BTree host
18. Followups to other NodeEdit-docs sections

---

## 1. Motivation

NodeEditor's render vocabulary is fixed: nodes, links, comments, reroutes, plus the extensions added so far (attachments, containers). Each is a structured visual primitive with its own model interface, hit-test rules, command set, and theme.

That vocabulary is sufficient for ~95% of authoring surfaces. The remaining 5% is the subsystem-specific visual cruft that doesn't fit anywhere:

**HSM initial-state arrows.** A composite state has an `⦿─→[StateName]` marker pointing from a small filled circle to its initial child. This is not a transition (it's not stored in the kernel's transition table; it's not a runtime event-routed edge). It's not a link in NodeEditor's sense (no pin-to-pin endpoint). It's a piece of visual metadata that exists because UML statecharts have always drawn it that way and users will be lost without it.

**HSM transition labels.** A transition between two states displays `Event[Guard]/Action` at the link's midpoint. NodeEditor links don't carry labels. Multi-line labels with formatting (event name in bold, guard in italics, action in regular weight) push beyond what a simple `string Label` field would accommodate.

**HSM region-conflict warning overlays.** When two states in different orthogonal regions write to the same `CommandLane`, the editor draws a yellow ⚠ glyph and a thin yellow connecting line between the two contributing states. This is a *cross-element* visual element — it belongs to neither state, exists only when both are present, and is computed dynamically from validation.

**BTree observer-selector guard badges.** When a Selector is an Observer Selector, child connections leading to *guard nodes* (Condition / Observer) carry an `👁 OBSERVES` badge near the link. This is a per-link annotation, but the trigger (parent is Observer Selector + child is guard) is contextual; it's not a property of the link itself.

**Per-subsystem runtime overlays.** Pulsing outlines on currently-executing nodes, fading wires for recently-executed paths, debug heatmaps coloring nodes by entry frequency. Some of this is already covered by `NodeState` flags and the existing debug-visualization (NodeEdit §25), but only the standard cases. Subsystem-specific overlays — for example, a BTree subtree-boundary indicator showing where `StackPointer` traversal entered a subtree — don't fit the standard vocabulary.

The pattern: **hosts need to draw to the canvas at specific render points, with access to the same ImGui draw list NodeEditor uses, but without owning the canvas.** No extension yet provides this. Hosts working around the gap end up either (a) rendering on top of the canvas using their own ImGui calls outside NodeEditor's render frame, which produces z-order surprises and breaks pan/zoom transformation, or (b) trying to wedge their concept into one of the existing primitives, which usually fails.

---

## 2. The shape, in one picture

```
NodeEditor's per-frame canvas render, with custom-renderer extension points:

  1. Background + grid                              ─┐
  2. Custom: BeforeContent pass                       │  hosts can draw here
  3. Comments                                          │  (e.g., faint sub-region
  4. Containers (fills + headers, no children)         │   tinting)
  5. Wires                                              │
  6. Custom: AfterWires pass                            │  hosts can draw here
  7. Child nodes / regular nodes                        │  (e.g., transition labels
  8. Attachments                                         │   above wire midpoints)
  9. Reroutes                                            │
 10. Custom: AfterNodes pass                              │  hosts can draw here
 11. Selection outlines                                    │  (e.g., HSM region-conflict
 12. Hover effects                                          │   warning lines)
 13. Active drag preview                                    │
 14. Custom: TopMost pass                                    │  hosts can draw here
                                                           ─┘  (e.g., debug tooltips)
```

Four named render passes. Hosts register `ICustomCanvasRenderer` implementations against the passes they need; NodeEditor invokes them at the right point with a context carrying the ImGui draw list, the current viewport state, and the visible-elements set for culling.

Renderers are passive — they render but don't dictate the topology, don't generate commands directly, don't own state that NodeEditor knows about. If they want to be interactive (hit-testable, selectable), they implement an additional interface (`ICustomCanvasHitTester`) that the canvas queries during hit-test.

---

## 3. Non-goals

- **Not a replacement for any standard primitive.** Nodes, links, comments, reroutes, attachments, and containers remain the canonical model. Custom renderers are *additive overlays*, not alternative implementations. A host that draws "nodes" via custom renderers is misusing the extension.
- **Not a way to bypass the IGraphCommand pipeline.** Custom renderers cannot mutate the graph directly. If a custom-rendered element triggers an action (e.g., clicking a transition label opens its inspector), the renderer signals through normal selection/command channels.
- **Not a path to arbitrary input handling.** Custom renderers can be hit-tested and contribute to selection; they cannot install gesture handlers, hotkey bindings, or modal interaction states. Those go through the existing `IEditorCommands` / interaction state machine (NodeEdit §12, §26).
- **Not animated unless the host drives the animation.** NodeEditor doesn't manage animation state for custom renderers. The host updates whatever state drives the animation (e.g., a phase value), the renderer reads it, draws accordingly per frame. NodeEditor invokes the renderer every frame regardless of dirty state.
- **Not GPU-direct.** Renderers use ImGui's draw list. No direct access to the underlying graphics API. This is the same constraint NodeEditor itself follows (NodeEdit §1.1).
- **Not a substitute for shaders or post-processing.** Effects like canvas-wide bloom, distortion, or screen-space shaders are outside this extension. Hosts that want such effects work outside NodeEditor entirely (post-render compositing in the host's frame pipeline).

---

## 4. The model — render slots and pass identity

### 4.1 The four passes

```csharp
// Add to NodeEditor.Core
public enum CanvasRenderPass
{
    /// <summary>
    /// After background and grid; before any content.
    /// Use for canvas-wide overlays that should sit behind everything
    /// (faint region tinting, large-scale debug heatmaps).
    /// </summary>
    BeforeContent,

    /// <summary>
    /// After wires render; before nodes render.
    /// Use for content that should sit on top of wires but below nodes
    /// (transition labels at wire midpoints, link-decoration badges).
    /// </summary>
    AfterWires,

    /// <summary>
    /// After all nodes, attachments, and reroutes; before selection outlines.
    /// Use for overlays that sit on top of the rendered graph but below
    /// selection feedback (region-conflict lines, initial-state arrows,
    /// subtree-boundary indicators).
    /// </summary>
    AfterNodes,

    /// <summary>
    /// After selection outlines, hover effects, drag previews — the very last layer.
    /// Use for tooltips, floating annotations, anything that must overlay the
    /// entire canvas including selection feedback.
    /// </summary>
    TopMost,
}
```

Four passes is the minimum that covers the realistic needs we've identified (transition labels need *AfterWires*; region-conflict overlays need *AfterNodes*; initial-state arrows are ambiguous between *AfterWires* and *AfterNodes* — see §16 for the HSM-specific choice; debug tooltips need *TopMost*; BTree subtree-boundary indicators are *BeforeContent* to sit behind the actual nodes). Five would be one too many; three would force false sharing.

### 4.2 Renderer registration

Renderers are registered through `IEditorHostServices` (NodeEdit §3). Add a new collection-typed service:

```csharp
public interface IEditorHostServices
{
    // ... existing members ...

    IReadOnlyList<ICustomCanvasRenderer> CustomCanvasRenderers { get; }
}
```

Hosts populate the list at editor construction. Order in the list determines order within a pass: earlier renderers draw first (so later renderers can overlay them within the same pass). Across passes, the pass order is fixed (per §4.1).

A renderer is registered against exactly one pass. If a host wants to draw at two passes (e.g., draw transition labels at AfterWires AND draw selection feedback for selected transitions at AfterNodes), it registers two separate renderers.

### 4.3 Stable identification

For testing, debugging, and config-driven enable/disable, each renderer has a stable string identifier:

```csharp
public interface ICustomCanvasRenderer
{
    /// <summary>Unique identifier across the editor session.</summary>
    string Id { get; }

    /// <summary>Which pass this renderer runs in.</summary>
    CanvasRenderPass Pass { get; }

    /// <summary>Render this frame.</summary>
    void Render(ICanvasRenderContext ctx);

    /// <summary>
    /// True if this renderer needs to draw this frame.
    /// NodeEditor calls IsActive before Render; renderers that return false
    /// are skipped entirely (including BeginGroup / EndGroup, hit-test
    /// contributions, performance accounting).
    /// Defaults to true if not overridden.
    /// </summary>
    bool IsActive => true;
}
```

The `Id` is a host-chosen string (e.g., `"hsm.transition_labels"`, `"hsm.region_conflicts"`, `"btree.observer_guard_badges"`). NodeEditor uses it for diagnostic logging and for the per-renderer perf accounting (§12).

---

## 5. The renderer interface

The core interface (already shown above) is small. The behaviors that matter live in the render context (§6) and in companion interfaces for renderers that want to participate in canvas state beyond pure drawing.

### 5.1 Companion: hit testing

```csharp
public interface ICustomCanvasHitTester
{
    /// <summary>
    /// Test whether a canvas-coordinate point hits any element drawn by
    /// the associated renderer. Return non-null with a stable element key
    /// if hit; null if miss.
    /// </summary>
    CustomElementHit? HitTest(Vector2 canvasPoint, IHitTestContext ctx);
}

public readonly record struct CustomElementHit(
    string ElementKey,           // host-stable identifier for the hit element
    CustomElementKind Kind,      // host-tagged kind: "transition_label", "region_conflict", etc.
    Rect Bounds);                // canvas-coord AABB of the hit element

public enum CustomElementKind
{
    LinkDecoration,    // attached to a link (transition label)
    NodeAdornment,     // attached to a node (initial-state arrow target)
    Standalone,        // free-standing (region-conflict indicator)
    Tooltip,           // ephemeral display (debug tooltip — usually not hit-testable)
}
```

A renderer implementing both `ICustomCanvasRenderer` and `ICustomCanvasHitTester` participates in canvas hit-test (§8). A renderer implementing only `ICustomCanvasRenderer` is purely visual — the user cannot click on its output.

### 5.2 Companion: selection

```csharp
public interface ICustomCanvasSelectable
{
    /// <summary>
    /// Hosts implementing this interface can have their custom-drawn elements
    /// be part of the selection set. Returns the canonical identifier for
    /// the element so the canvas can re-render it as selected.
    /// </summary>
    void OnElementSelected(string elementKey, CustomElementHit hit);
    void OnElementDeselected(string elementKey);
}
```

Implemented by hosts that want their custom-drawn elements (e.g., HSM transition labels) to be selectable. The canvas's selection state extends to track these as a side list (§9).

### 5.3 Lifetime

Renderers are constructed at editor startup and disposed at editor shutdown. NodeEditor does not call `Dispose` on renderers between frames or between graphs. The host is responsible for any per-graph state inside the renderer.

If a host needs a renderer to "switch off" temporarily (e.g., a debug-only renderer when debug mode is off), it returns `IsActive => false` from the renderer. NodeEditor skips inactive renderers without disposing them. Reactivating is just returning `IsActive => true` on the next frame.

---

## 6. The render context

```csharp
public interface ICanvasRenderContext
{
    /// <summary>
    /// Direct access to ImGui's draw list. Use this for all drawing.
    /// The draw list is positioned in screen coordinates; use the
    /// CanvasToScreen helpers to transform canvas-space positions.
    /// </summary>
    ImDrawListPtr DrawList { get; }

    /// <summary>The viewport's current pan/zoom.</summary>
    ViewportState Viewport { get; }

    /// <summary>The current pass being rendered.</summary>
    CanvasRenderPass Pass { get; }

    /// <summary>The active editor theme.</summary>
    IEditorTheme Theme { get; }

    /// <summary>The graph being rendered.</summary>
    IGraphModel Graph { get; }

    /// <summary>
    /// Selection state at the start of this frame. Renderers may read this
    /// to render selection feedback; they may not mutate it.
    /// </summary>
    SelectionState Selection { get; }

    /// <summary>
    /// Visible-elements set. The set of node IDs whose bounds intersect the
    /// current viewport. Use for culling: renderers should typically only
    /// draw elements associated with visible nodes or visible regions.
    /// </summary>
    IReadOnlySet<NodeId> VisibleNodes { get; }
    IReadOnlySet<LinkId> VisibleLinks { get; }

    /// <summary>
    /// Transform a canvas-coord point to screen-coord.
    /// Identical to Viewport.CanvasToScreen but provided here for ergonomics.
    /// </summary>
    Vector2 CanvasToScreen(Vector2 canvasPoint);
    Vector2 ScreenToCanvas(Vector2 screenPoint);

    /// <summary>
    /// Transform a canvas-coord rect to screen-coord.
    /// </summary>
    Rect CanvasToScreen(Rect canvasRect);

    /// <summary>
    /// The current zoom level. Renderers may use this to scale line widths
    /// and font sizes proportionally.
    /// </summary>
    float Zoom { get; }

    /// <summary>
    /// True if low-zoom mode is active (zoom &lt; 0.5). Renderers should
    /// typically simplify or skip their output in low-zoom mode.
    /// </summary>
    bool IsLowZoom { get; }

    /// <summary>
    /// Per-frame scratch dictionary for hosts to pass data between two
    /// renderers in the same frame. Cleared between frames.
    /// </summary>
    IDictionary<string, object?> FrameScratch { get; }

    /// <summary>
    /// Provided by the host's IDebugSession (if any). Renderers that draw
    /// runtime overlays read from this for execution state.
    /// </summary>
    IDebugSession? DebugSession { get; }
}
```

The context is constructed per-frame by the canvas; it's not retained across frames. Hosts must not store the context object or any of its referenced collections (the collections are pooled and reused).

### 6.1 Per-pass clip rect

NodeEditor sets up a clip rect for each pass that matches the canvas's viewport rectangle (the on-screen extent of the canvas widget). Renderers can rely on drawing outside this rect being invisible — there's no need for hosts to perform their own clipping for content that falls outside the canvas widget's display area.

Renderers should NOT call `DrawList.PushClipRect` themselves except for very specific reasons (e.g., clipping a transition label to fit within a region container's interior). If a renderer pushes a clip rect, it MUST pop it before returning.

### 6.2 Draw list channels

ImGui's draw list supports channels for layered drawing within a frame. NodeEditor uses channels internally (e.g., wires-then-nodes within a pass). Custom renderers MAY use channels to layer their own output internally, but each renderer must clean up its own channel state — split into channels at the start, merge at the end. The canvas resets channel state between passes and between renderers within a pass.

The simpler default: don't use channels; rely on draw order within `Render()` to layer your own output. Channels are only worth it for renderers that produce many disjoint layered elements (e.g., a renderer drawing both background tints and foreground labels in a single pass).

---

## 7. Coordinate spaces

### 7.1 Canvas vs. screen

NodeEditor uses two coordinate systems (NodeEdit §6):
- **Canvas coords:** the abstract space where nodes "live." Pan and zoom transform canvas to screen.
- **Screen coords:** pixel space.

Custom renderers think in **canvas coords** and convert at the last moment. This is the same convention NodeEditor's internal renderers follow. The render context provides `CanvasToScreen` / `ScreenToCanvas` for the conversion; `Viewport.Zoom` for scaling lengths/sizes proportionally.

A typical pattern:

```csharp
public void Render(ICanvasRenderContext ctx)
{
    foreach (var (state1, state2) in GetConflictingStates(ctx.Graph))
    {
        var canvasStart = ctx.Graph.FindNode(state1)!.GetBounds().Center;
        var canvasEnd   = ctx.Graph.FindNode(state2)!.GetBounds().Center;

        var screenStart = ctx.CanvasToScreen(canvasStart);
        var screenEnd   = ctx.CanvasToScreen(canvasEnd);

        ctx.DrawList.AddLine(
            screenStart, screenEnd,
            ImGui.GetColorU32(ctx.Theme.Warning),
            2f * ctx.Zoom);
    }
}
```

The `2f * ctx.Zoom` line width is the standard way to make lines preserve their on-screen thickness regardless of zoom. Renderers that want fixed-pixel widths (regardless of zoom) skip the multiply.

### 7.2 Container-local coords

For renderers that draw inside a container (e.g., HSM initial-state arrows live inside their composite), the renderer reads the container's `IContainerNodeModel` and uses `GraphView.NodeCanvasPosition(childId)` to get the child's effective canvas position. The host renderer doesn't need to know the local-vs-canvas transformation rule — the `IGraphModel` projection already handles it (per `NodeEditor_Extension_ContainerNodes.md` §4.3).

### 7.3 Font sizing

Renderers drawing text use ImGui's font with explicit size:

```csharp
ctx.DrawList.AddText(
    ImGui.GetFont(),
    fontSize: 11f * ctx.Zoom,    // 11 px at zoom 1.0
    pos: ctx.CanvasToScreen(canvasPos),
    col: ImGui.GetColorU32(ctx.Theme.TextDefault),
    text_begin: text);
```

The standard convention: font sizes in the renderer are in canvas pixels at zoom 1.0; multiply by `ctx.Zoom` to convert to on-screen pixels. Renderers that want fixed on-screen font size (independent of zoom) skip the multiply.

The render context exposes the editor theme's default font size for consistency:

```csharp
public interface IEditorTheme
{
    // ... existing members ...

    float CustomRendererTextSizeDefault     { get; }  // default: 11 px @ zoom 1.0
    float CustomRendererLabelSizeDefault    { get; }  // default: 12 px @ zoom 1.0
    float CustomRendererTooltipSizeDefault  { get; }  // default: 12 px @ zoom 1.0
}
```

---

## 8. Hit testing of custom-drawn content

### 8.1 Hit-test priority

When a renderer implements `ICustomCanvasHitTester`, its hit areas participate in canvas hit-test. The extended priority (extending the priority lists from NodeAttachments §6.1 and ContainerNodes §7.1):

1. Reroutes
2. Pins
3. Wires
4. **Custom renderers — `TopMost` pass, registration-order reverse** ← NEW
5. Attachments
6. **Custom renderers — `AfterNodes` pass, registration-order reverse** ← NEW
7. Container collapse-arrow chevrons
8. Container header strips
9. Comment title bars
10. **Custom renderers — `AfterWires` pass, registration-order reverse** ← NEW
11. Node bodies (regular nodes and container children)
12. **Custom renderers — `BeforeContent` pass, registration-order reverse** ← NEW
13. Container interiors (empty areas)
14. Comment bodies (pass-through)
15. Empty canvas

The reasoning: custom-drawn content layered visually above the standard primitives should hit-test in the same relative order. A transition label drawn in `AfterWires` sits visually above wires and below nodes; clicking on a wire-and-label overlap selects the label.

Within a pass, later-registered renderers' hit-test wins over earlier-registered ones (matching the "later draws on top" rule).

### 8.2 Hit-test invocation

NodeEditor invokes `ICustomCanvasHitTester.HitTest` for each registered hit-tester in priority order, passing the canvas-coord point. The first non-null return wins.

Hit-test runs every frame for mouse-hover detection (to drive cursor changes and tooltips). It also runs on mouse-down to determine which canvas element was clicked. Renderers must keep `HitTest` fast — O(visible elements) at most, ideally O(log n) via spatial indexing if the renderer draws many elements.

### 8.3 The IHitTestContext

```csharp
public interface IHitTestContext
{
    ViewportState Viewport { get; }
    IGraphModel Graph { get; }
    IReadOnlySet<NodeId> VisibleNodes { get; }
    IReadOnlySet<LinkId> VisibleLinks { get; }
    float Zoom { get; }
}
```

Smaller than `ICanvasRenderContext` because hit-test doesn't need draw-list access. Same coordinate-space conventions.

### 8.4 Hit area sizing

Renderers should make hit areas at least 1.5× the visible glyph size (matching the existing pin hit-area rule from NodeEdit §8). This forgives off-by-a-pixel clicks at zoom levels where visual elements get small.

Renderers can return a `Bounds` AABB that's larger than the visually-drawn element — useful for, e.g., transition labels where the hit area might extend below the label by a few pixels to make clicking easier.

---

## 9. Selection of custom-drawn elements

### 9.1 Extending SelectionState

Per `NodeEditor_Extension_NodeAttachments.md` §7.1, `SelectionState` extends with `SelectedAttachments`. Custom-drawn elements get a third extension:

```csharp
public sealed class SelectionState
{
    // ... existing fields ...

    public IReadOnlySet<AttachmentId> SelectedAttachments { get; }
    public IReadOnlySet<CustomElementRef> SelectedCustomElements { get; }   // NEW
}

public readonly record struct CustomElementRef(
    string RendererId,
    string ElementKey);
```

The compound key `(RendererId, ElementKey)` uniquely identifies a custom-drawn element across the editor session. The renderer's `Id` plus an opaque host-stable string scopes the key to one renderer.

### 9.2 Click-to-select flow

When the user clicks a hit-tested custom element:

1. Canvas calls `HitTest`, gets a `CustomElementHit`.
2. Canvas updates `SelectionState.SelectedCustomElements` per click rules (single vs. modifier).
3. Canvas fires the standard selection-changed event.
4. The renderer's `ICustomCanvasSelectable.OnElementSelected` is called.
5. On the next frame, the renderer's `Render` reads `ctx.Selection.SelectedCustomElements` and draws selection feedback for its own elements.

The pattern matches how attachments and nodes work: the canvas owns selection state; the renderer reads it during render to apply visual feedback.

### 9.3 Details panel routing

The Details panel (NodeEdit §19) extends `DetailsTarget`:

```csharp
public abstract record DetailsTarget
{
    // ... existing cases ...

    public sealed record CustomElement(CustomElementRef Element) : DetailsTarget;
    public sealed record MultipleCustomElements(IReadOnlyList<CustomElementRef> Elements) : DetailsTarget;
}
```

Host providers handle these by routing to their own per-element inspector. The HSM host's transition-label renderer provides a DetailsView showing the transition's event/guard/action properties when its label is selected.

### 9.4 Context menu

The right-click context menu for a custom-drawn element comes from a host-provided provider, parallel to `IAttachmentContextMenuProvider`:

```csharp
public interface ICustomElementContextMenuProvider
{
    string RendererId { get; }
    IReadOnlyList<ContextMenuItem> GetItemsFor(string elementKey, CustomElementHit hit);
}
```

Registered via `IEditorHostServices`; matched by `RendererId` to the renderer that owns the element.

---

## 10. Z-order and pass ordering

### 10.1 Within a pass

Renderers within a single pass execute in registration order. The first-registered renderer draws first; later renderers draw over it. Hosts that need a specific ordering register accordingly.

The canvas does *not* re-sort renderers each frame. Order is fixed at registration. If a host wants dynamic ordering, it implements one composite renderer that internally dispatches in the desired order — not multiple renderers competing for position.

### 10.2 Across passes

Pass order is fixed: `BeforeContent → AfterWires → AfterNodes → TopMost`. A renderer in an earlier pass cannot overlay output from a later pass. If a host needs that, it's a misuse of the pass system; reconsider the design.

### 10.3 Within-pass channel use

If a renderer uses ImGui draw-list channels internally (§6.2), the channel state is local to that renderer. Other renderers in the same pass are unaffected.

---

## 11. Threading and lifetime

### 11.1 Thread of execution

Custom renderers run on the same thread as NodeEditor's own rendering — the ImGui thread (typically the main thread; see NodeEdit §1.1). Renderers MUST NOT call into NodeEditor from worker threads, and they must not block.

Renderers may read graph model state during `Render`. The graph model is also accessed by the canvas during the same frame; this is single-threaded by construction.

### 11.2 State held by renderers

Renderers may hold their own state across frames. Typical use cases:
- Cached layout (e.g., transition-label positions computed once after a graph change).
- Hit-test spatial index for the renderer's own elements.
- Animation phase counters (for blinking, fading, etc.).

Such state must be invalidated when the graph changes. The renderer subscribes to `IGraphModel.Changed` and reacts to the relevant `GraphChangeKind` values.

### 11.3 Disposal

Renderers are disposed when the editor process shuts down. The canvas does not explicitly dispose renderers on graph close or perspective change; if a renderer needs cleanup at those points, it subscribes to host-level events.

`ICustomCanvasRenderer` extends `IDisposable` if the renderer holds disposable resources (file handles, GPU resources via texture handles, etc.):

```csharp
public interface ICustomCanvasRenderer : IDisposable
{
    // ... members from §4.3 ...
}
```

The default `IDisposable.Dispose` may be no-op for stateless renderers.

---

## 12. Performance budget

Custom renderers add work proportional to the count of elements they draw. NodeEditor's existing performance budget (NodeEdit §27) doesn't allocate explicit headroom for custom renderers; in practice, the host's renderers must fit within the existing slack (typically 1–3 ms unused at zoom 1.0 / 500 nodes).

### 12.1 Realistic load

HSM editor at 80 states / 70 transitions has roughly:
- 70 transition labels (`AfterWires` pass) at ~6 µs each = 0.42 ms
- 5 initial-state arrows (`AfterNodes` pass) at ~10 µs each = 0.05 ms
- 0–3 region-conflict overlays (`AfterNodes` pass) at ~20 µs each = up to 0.06 ms
- Total custom-render cost: ~0.53 ms

BTree editor at 200 nodes / 150 links is smaller:
- 30 observer-guard badges (`AfterWires` pass) at ~5 µs each = 0.15 ms
- 0–8 subtree-boundary indicators (`BeforeContent` pass) at ~30 µs each = up to 0.24 ms
- Total: ~0.39 ms

Both well within reasonable headroom.

### 12.2 Pathological load

A renderer drawing N labels in a tight loop without spatial culling would cost O(N) per frame regardless of viewport. With the spec's culling discipline (`ctx.VisibleNodes` / `ctx.VisibleLinks`), well-behaved renderers cost O(visible) per frame, not O(total). The NodeEditor implementer should consider adding a per-renderer perf counter to identify renderers that ignore the visible sets.

### 12.3 Per-renderer accounting

For debugging and profiling, NodeEditor records per-renderer timing in a side table accessible via `IEditorIndicators.Snapshot`:

```csharp
public readonly record struct EditorStatusSnapshot(
    // ... existing fields ...

    IReadOnlyDictionary<string, RendererPerfRecord>? CustomRendererPerf
);

public readonly record struct RendererPerfRecord(
    float LastFrameMs,
    float AvgFrameMs,
    float MaxFrameMs,
    int CallsThisSession);
```

Hosts can surface this in a debug panel. Not used in normal operation.

### 12.4 Renderer guidelines

For implementers writing custom renderers, follow these:
- Cull aggressively. Iterate `ctx.VisibleNodes` / `ctx.VisibleLinks`, not the full graph collection.
- Cache layout results across frames. Invalidate only on `IGraphModel.Changed`.
- Skip in low-zoom mode (`ctx.IsLowZoom`) unless the renderer adds value at low zoom (e.g., a heatmap might want to render even at low zoom; text labels definitely should not).
- Avoid `DrawList.AddText` for many small labels at low zoom — text rendering dominates draw cost. Consider rendering as colored dots and showing labels only above a zoom threshold.

---

## 13. Theme additions

Minimal — the renderers themselves bring their own visual style via the host. The shared theme exposes some general defaults (already listed in §7.3 for font sizes), plus:

```csharp
public interface IEditorTheme
{
    // ... existing members ...

    Vector4 CustomElementSelectionAccent    { get; }  // default: same as SelectionAccent (yellow #FFD700)
    Vector4 CustomElementHoverAccent        { get; }  // default: 70% white, 30% theme accent
    float   CustomElementHitAreaPadding     { get; }  // default: 4 px @ zoom 1.0
}
```

Renderers requiring more specific styling read from the host's own theme (host-extended `IEditorTheme` or host-private theme objects).

---

## 14. Backwards compatibility

All additions are additive.

- `IEditorHostServices.CustomCanvasRenderers` defaults to an empty list. Existing hosts that don't register renderers see no change.
- `SelectionState.SelectedCustomElements` defaults empty. Existing consumers ignore it.
- `DetailsTarget.CustomElement` / `MultipleCustomElements` are new cases; existing `IDetailsViewProvider` implementations don't handle them and fall through naturally.
- The new `CanvasRenderPass` enum and its `Render` calls add no overhead when no renderers are registered (NodeEditor invokes the pass-renderer loop with an empty list).
- Hit-test priority extends to include custom renderers; with no renderers registered, the new priority steps are no-ops.

The Blueprint editor host, which has no v1 need for custom renderers, requires zero code change. BTree and HSM host implementations register renderers explicitly during their editor module setup.

---

## 15. Test plan

### 15.1 Unit tests (NodeEditor.Core.Tests)

- **`CustomRendererRegistrationTests`** — registered renderers are invoked once per matching pass; inactive renderers are skipped; renderers without hit-tester don't participate in hit-test.
- **`CustomRendererPassOrderingTests`** — across passes, render order matches the enum order; within a pass, order matches registration order; verified via a fake-draw-list that records call sequence.
- **`CustomRendererHitTestTests`** — given a fixture of renderers with known hit-rects, verify hit-test priority by pass; registration-reverse order within a pass; misses fall through to underlying canvas elements.
- **`CustomRendererSelectionTests`** — clicking a custom element updates `SelectedCustomElements`; calling `OnElementSelected` on the renderer; multi-select extends; Esc deselects.
- **`CustomRendererPerfAccountingTests`** — per-renderer time is recorded; idle renderers (returning IsActive=false) contribute zero time.

### 15.2 Visual tests (NodeEditor.Demo)

The demo gains a "Custom Renderers" scenario:
- A `BeforeContent` renderer that tints two regions of the canvas with faint color overlays.
- An `AfterWires` renderer that adds labels at wire midpoints.
- An `AfterNodes` renderer that draws yellow warning lines between two pairs of nodes.
- A `TopMost` renderer that shows a debug tooltip following the mouse cursor.

Manual checks:
- Render order is correct (background tint behind everything; labels on top of wires; warning lines on top of nodes; tooltip on top of selection outlines).
- Pan and zoom transformations apply correctly to all custom renderers.
- Hit-test for selectable custom elements works at appropriate priority.
- Multi-select with Shift extends correctly.
- Right-click on a custom element shows the host-provided context menu.

### 15.3 Performance tests

- **`CustomRendererPerf`** — 100 custom-drawn elements across passes complete within 1.5 ms (sub-budget).
- **`CustomRendererCullingPerf`** — same scenario with 90% of elements off-screen; render time drops below 0.3 ms.

---

## 16. Migration for the HSM host

Documenting the HSM host's adoption path so the extension is sanity-checked against its primary consumer.

### 16.1 Renderers the HSM host registers

| Renderer ID | Pass | Purpose |
|---|---|---|
| `hsm.transition_labels` | `AfterWires` | Renders `Event[Guard]/Action` at each transition's midpoint. Hit-testable for selection. |
| `hsm.initial_state_arrows` | `AfterNodes` | Renders `⦿─→` from a small circle inside each composite to its initial child. Not hit-testable (the initial marker is informational only; selection goes via the composite state). |
| `hsm.region_conflicts` | `AfterNodes` | Renders thin yellow lines between conflicting states across orthogonal regions, plus warning glyphs. Hit-testable; click reveals the conflict details in the inspector. |
| `hsm.history_glyphs` | `AfterNodes` | Renders the `H` and `H*` pseudo-state circles inside their composites. Hit-testable (history pseudo-states are selectable like regular states; but they're rendered as custom because they're tiny and have a specific look). |
| `hsm.runtime_overlay` | `AfterNodes` | When `DebugSession` is attached, renders pulsing outlines on currently-active states, fading transition arrows for recent transitions, deferred-event count badges. |

### 16.2 Hit-test cooperation

The transition-labels renderer implements `ICustomCanvasHitTester` and contributes `CustomElementHit` records keyed by transition GUID. Clicking a transition label selects the underlying HSM transition (the host's `OnElementSelected` updates `EditorSelectionStore.SubSelection` with `HsmTransitionSelection`).

The region-conflicts renderer is also hit-testable; clicking a warning glyph shows a popup explaining the conflict (which lanes, which states) and offers "Suppress this warning" / "Mark as intentional."

### 16.3 Coordinate-space considerations

Transition labels live in canvas space. The label's position is the midpoint of the rendered Bezier curve, which the renderer computes per frame (cheap; cached per-link with invalidation on link-waypoints change).

Initial-state arrows draw inside a composite's interior — the renderer reads the composite's interior origin via `GraphView.NodeInteriorBounds` and the initial child's `NodeCanvasPosition`. No host-specific transform math required.

### 16.4 Runtime overlay deactivation

When `ctx.DebugSession` is null (or detached), `hsm.runtime_overlay`'s `IsActive` returns false; the renderer is skipped entirely. No conditional logic inside `Render`; the canvas simply doesn't call it.

---

## 17. Migration for the BTree host

The BTree host's needs are simpler.

### 17.1 Renderers the BTree host registers

| Renderer ID | Pass | Purpose |
|---|---|---|
| `btree.observer_guard_badges` | `AfterWires` | Renders `👁 OBSERVES` badges on connections leading from an Observer Selector to a guard child (Condition / Observer). Hit-testable; clicking shows a tooltip explaining the abort behavior. |
| `btree.subtree_boundaries` | `BeforeContent` | When `DebugSession` is attached and `StackPointer > 0`, draws a faint blue dashed rectangle around the subtree the kernel is currently executing inside. Not hit-testable (informational only). |
| `btree.runtime_overlay` | `AfterNodes` | When `DebugSession` is attached, renders pulsing outlines on the currently-running node, stack-ancestry glow, async-pending clock badges. |
| `btree.heatmap_overlay` | `BeforeContent` | When the asset browser shows multi-instance heatmap mode for this asset, tints each node by entry-frequency or execution-time. Not hit-testable. |

### 17.2 Observer-guard badge specifics

The `btree.observer_guard_badges` renderer iterates visible links. For each link, it inspects:
- Source node's category: if not Observer Selector, skip.
- Target node's category: if not a guard kind (Condition / Observer), skip.

When both checks pass, it draws the badge at ~30% of the way along the link from source to target, using `LinkBezier.GetPointAt(0.3)` (a NodeEdit-provided utility).

### 17.3 Subtree-boundary rendering

The renderer reads the live `BehaviorTreeState` via `ctx.DebugSession?.GetCurrentSnapshot()`. The `NodeIndexStack[0..StackPointer]` identifies the current subtree's entry point. The renderer:
- Finds the subtree-entry node's canvas bounds.
- Walks the subtree's nodes (via `IGraphModel`'s subtree relationship — host-supplied lookup) to compute the AABB.
- Draws a dashed rectangle around the AABB in `BeforeContent` so it sits behind the actual nodes.

---

## 18. Followups to other NodeEdit-docs sections

- **§3** — Add `CustomCanvasRenderers` to `IEditorHostServices`. Add `IDebugSession?` if not already present (used by `ICanvasRenderContext`).
- **§4** — Add `SelectedCustomElements` to `SelectionState`.
- **§6** — Extend paint order with four custom-renderer passes (per §2 here).
- **§11** — Extend selection rules with custom-element click/multi-select cases.
- **§12** — Extend hit-test priority with the four pass-scoped custom-renderer steps (per §8.1 here).
- **§19** — Extend `DetailsTarget` with `CustomElement` / `MultipleCustomElements` cases.
- **§26** — Extend `EditorStatusSnapshot` with `CustomRendererPerf` (per §12.3 here).
- **§27** — Note that custom renderers consume budget at the host's discretion; no separate allocation.

Each is a small edit to the existing NodeEdit spec, not a separate document.

---
