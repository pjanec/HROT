# BATCH-07 — NodeAttachments: Rendering, Hit-Testing, Low-Zoom, Demo

## Tasks Covered
- **TASK-NEA-05** — Attachment rendering (pills, glyphs, state outlines)
- **TASK-NEA-06** — Hit-testing for attachments (HoverKind extension + HitTester)
- **TASK-NEA-10** — Low-zoom bar rendering (below zoom 0.5)
- **Demo supplement** — S34 demo scenario for visual validation (bonus — NEA-11 already marked done)

## Prerequisites
All of BATCH-05 and BATCH-06 are committed (commit da6f4875). The following types exist
and are ready to use:
- `AttachmentId`, `IAttachmentModel`, `AttachmentCategory`, `AttachmentState`
- `AttachmentLayout`, `AttachmentPlacement`, `AttachmentLayoutEngine`
- `IEditorTheme` with 8 attachment default members (AttachmentDecoratorColor, AttachmentFlagColor,
  AttachmentPureColor, AttachmentCustomColor, AttachmentHeight, AttachmentCornerRadius,
  AttachmentGapAboveHost, AttachmentInterGap)
- `SelectionEntry.OfAttachment(...)`, `SelectionEntryKind.Attachment`
- `IAttachmentContextMenuProvider`
- `IGraphModel` default members: `GetAttachmentsForNode`, `Attachments`, `FindAttachment`

## Step-by-Step Instructions

### Step 1 — TASK-NEA-06 (part 1): Extend HoverInfo

**File:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Core/View/HoverInfo.cs`

Make these changes:

1. Add `AttachmentId Attachment { get; init; }` as a new field on `HoverInfo`.
2. Add `Attachment` to the `HoverKind` enum.

The file currently reads:

```csharp
using NodeEditor.Primitives;

namespace NodeEditor.Core.View;

/// <summary>
/// What the cursor is currently over. Computed every frame by the canvas renderer
/// during hit-testing and consumed by event-handling code.
/// Mutually exclusive: only one of the IDs is non-empty.
/// </summary>
public readonly record struct HoverInfo
{
    public HoverKind Kind { get; init; }
    public NodeId Node { get; init; }
    public PinId Pin { get; init; }
    public LinkId Link { get; init; }
    public CommentId Comment { get; init; }
    public RerouteRef Reroute { get; init; }
    /// <summary>For comments: whether the cursor is on the title bar (drag), the body, or a resize handle.</summary>
    public CommentHoverZone CommentZone { get; init; }

    public static HoverInfo None => default;
}

public enum HoverKind { None, Node, Pin, Link, Comment, Reroute }

public enum CommentHoverZone { None, Header, Body, ResizeHandle }
```

Replace with:

```csharp
using NodeEditor.Primitives;

namespace NodeEditor.Core.View;

/// <summary>
/// What the cursor is currently over. Computed every frame by the canvas renderer
/// during hit-testing and consumed by event-handling code.
/// Mutually exclusive: only one of the IDs is non-empty.
/// </summary>
public readonly record struct HoverInfo
{
    public HoverKind Kind { get; init; }
    public NodeId Node { get; init; }
    public PinId Pin { get; init; }
    public LinkId Link { get; init; }
    public CommentId Comment { get; init; }
    public RerouteRef Reroute { get; init; }
    public AttachmentId Attachment { get; init; }
    /// <summary>For comments: whether the cursor is on the title bar (drag), the body, or a resize handle.</summary>
    public CommentHoverZone CommentZone { get; init; }

    public static HoverInfo None => default;
}

public enum HoverKind { None, Node, Pin, Link, Comment, Reroute, Attachment }

public enum CommentHoverZone { None, Header, Body, ResizeHandle }
```

---

### Step 2 — TASK-NEA-05 (part 1): Extend CanvasLayout

**File:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasLayout.cs`

The `using` block at the top already has all needed imports:
```
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Spatial;
using NodeEditor.Primitives;
```

**Change A — CanvasLayout class: add two new dictionary properties**

After the line:
```csharp
    /// <summary>Pre-computed set of input pins that have at least one wire connected.</summary>
    public HashSet<PinId> ConnectedInputPins { get; } = [];
```

Add:
```csharp
    /// <summary>Attachment layouts (screen-pixel coords) for nodes that have attachments.</summary>
    public Dictionary<NodeId, AttachmentLayout> AttachmentLayouts { get; } = [];

    /// <summary>Screen-space bounding rects for each attachment, keyed by AttachmentId.</summary>
    public Dictionary<AttachmentId, RectF> AttachmentScreenRects { get; } = [];
```

**Change B — CanvasLayout.Clear(): clear the new dictionaries**

The existing Clear method body:
```csharp
    public void Clear()
    {
        NodeScreenRects.Clear();
        PinScreenPositions.Clear();
        ConnectedInputPins.Clear();
    }
```

Replace with:
```csharp
    public void Clear()
    {
        NodeScreenRects.Clear();
        PinScreenPositions.Clear();
        ConnectedInputPins.Clear();
        AttachmentLayouts.Clear();
        AttachmentScreenRects.Clear();
    }
```

**Change C — CanvasLayoutBuilder.Build: compute attachment layout per node**

In `CanvasLayoutBuilder.Build`, locate the block that ends with:
```csharp
            layout.NodeScreenRects[node.Id] = rect;
            entries?.Add((node.Id, new RectF(graphPos, new Vector2(nodeWGu, nodeHGu))));
```

Immediately after those two lines, insert the following block:
```csharp
            // Compute screen-space attachment layout for this node.
            var nodeAttachments = view.Model.GetAttachmentsForNode(node.Id);
            if (nodeAttachments.Count > 0)
            {
                var attachLayout = AttachmentLayoutEngine.Compute(
                    nodeAttachments,
                    sw,
                    a =>
                    {
                        float w = 0f;
                        if (!string.IsNullOrEmpty(a.Glyph))
                            w += ImGui.CalcTextSize(a.Glyph).X;
                        if (!string.IsNullOrEmpty(a.Label))
                        {
                            if (w > 0f) w += 4f;
                            w += ImGui.CalcTextSize(a.Label).X;
                        }
                        return w;
                    });
                layout.AttachmentLayouts[node.Id] = attachLayout;
                foreach (var (aId, placement) in attachLayout.Placements)
                    layout.AttachmentScreenRects[aId] = new RectF(rect.Min + placement.TopLeft, placement.Size);
            }
```

Note: `sw` is already computed earlier in the loop body as `float sw = nodeWGu * zoom;`. `rect.Min` is the screen-space top-left of the node.

---

### Step 3 — TASK-NEA-05 (part 2): Create AttachmentRenderer

**File (NEW):** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/AttachmentRenderer.cs`

Create this file with the following content exactly:

```csharp
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Spatial;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Canvas;

/// <summary>
/// Renders attachment pills (or low-zoom bars) above their host nodes.
/// Call DrawAll after node rendering is complete for the current frame.
/// </summary>
internal sealed class AttachmentRenderer
{
    // NEA-10: below this zoom level, draw a single colored bar instead of pills.
    private const float LowZoomThreshold = 0.5f;
    // Height of the low-zoom bar in screen pixels.
    private const float LowZoomBarHeight = 3f;

    /// <summary>
    /// Draw all attachment pills for nodes that have attachments.
    /// At zoom below LowZoomThreshold, draws a 3 px colored bar instead.
    /// </summary>
    public void DrawAll(
        GraphView view,
        ImDrawListPtr dl,
        Dictionary<NodeId, AttachmentLayout> attachmentLayouts,
        Dictionary<NodeId, RectF> nodeScreenRects)
    {
        if (attachmentLayouts.Count == 0) return;

        float zoom = view.Viewport.Zoom;
        bool lowZoom = zoom < LowZoomThreshold;
        var theme = view.Host.Theme;

        foreach (var (nodeId, layout) in attachmentLayouts)
        {
            if (!nodeScreenRects.TryGetValue(nodeId, out var nodeRect)) continue;
            var attachments = view.Model.GetAttachmentsForNode(nodeId);
            if (attachments.Count == 0) continue;

            if (lowZoom)
                DrawLowZoomBar(dl, nodeRect, attachments, theme);
            else
                DrawPills(dl, nodeRect, layout, attachments, theme);
        }
    }

    // ── Low-zoom bar (NEA-10) ─────────────────────────────────────────────────

    private static void DrawLowZoomBar(
        ImDrawListPtr dl,
        RectF nodeRect,
        IReadOnlyList<IAttachmentModel> attachments,
        IEditorTheme theme)
    {
        // Single 3 px bar above the host, colored by the leftmost attachment category.
        IAttachmentModel? leftmost = null;
        foreach (var a in attachments)
        {
            if (leftmost == null
                || a.StackIndex < leftmost.StackIndex
                || (a.StackIndex == leftmost.StackIndex
                    && a.Id.Value.CompareTo(leftmost.Id.Value) < 0))
                leftmost = a;
        }
        if (leftmost == null) return;

        var color = GetCategoryColor(leftmost.Category, theme);
        var barMin = new Vector2(nodeRect.Min.X, nodeRect.Min.Y - LowZoomBarHeight);
        var barMax = new Vector2(nodeRect.Min.X + nodeRect.Size.X, nodeRect.Min.Y);
        dl.AddRectFilled(barMin, barMax, ImGui.GetColorU32(color));
    }

    // ── Normal-zoom pills (NEA-05) ────────────────────────────────────────────

    private static void DrawPills(
        ImDrawListPtr dl,
        RectF nodeRect,
        AttachmentLayout layout,
        IReadOnlyList<IAttachmentModel> attachments,
        IEditorTheme theme)
    {
        // Build lookup table so we can find a model given its id.
        var modelMap = new Dictionary<AttachmentId, IAttachmentModel>(attachments.Count);
        foreach (var a in attachments)
            modelMap[a.Id] = a;

        foreach (var (id, placement) in layout.Placements)
        {
            if (!modelMap.TryGetValue(id, out var model)) continue;

            // TopLeft is relative to the host node Min, in screen pixels.
            var pillMin = nodeRect.Min + placement.TopLeft;
            var pillMax = pillMin + placement.Size;

            float cornerRadius = theme.AttachmentCornerRadius;

            var bgColor = GetCategoryColor(model.Category, theme);
            if ((model.State & AttachmentState.Disabled) != 0)
                bgColor = bgColor with { W = 0.6f };

            dl.AddRectFilled(pillMin, pillMax, ImGui.GetColorU32(bgColor), cornerRadius);

            // State outlines drawn on top of fill.
            if ((model.State & AttachmentState.Selected) != 0)
                dl.AddRect(pillMin, pillMax, ImGui.GetColorU32(theme.SelectionAccent), cornerRadius, ImDrawFlags.None, 2f);
            else if ((model.State & AttachmentState.Error) != 0)
                dl.AddRect(pillMin, pillMax, ImGui.GetColorU32(theme.ErrorColor), cornerRadius, ImDrawFlags.None, 1f);
            else if ((model.State & AttachmentState.Warning) != 0)
                dl.AddRect(pillMin, pillMax, ImGui.GetColorU32(theme.WarningColor), cornerRadius, ImDrawFlags.None, 1f);

            // Text content: optional glyph then optional label.
            float textLineH = ImGui.GetTextLineHeight();
            float textY = pillMin.Y + (placement.Size.Y - textLineH) * 0.5f;
            float textX = pillMin.X + AttachmentLayoutEngine.PillPaddingH;
            uint textColor = ImGui.GetColorU32(theme.TextDefault);

            if (!string.IsNullOrEmpty(model.Glyph))
            {
                dl.AddText(new Vector2(textX, textY), textColor, model.Glyph);
                textX += ImGui.CalcTextSize(model.Glyph).X;
                if (!string.IsNullOrEmpty(model.Label))
                    textX += 4f;
            }
            if (!string.IsNullOrEmpty(model.Label))
                dl.AddText(new Vector2(textX, textY), textColor, model.Label);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Vector4 GetCategoryColor(AttachmentCategory category, IEditorTheme theme) =>
        category switch
        {
            AttachmentCategory.Decorator => theme.AttachmentDecoratorColor,
            AttachmentCategory.Flag      => theme.AttachmentFlagColor,
            AttachmentCategory.Pure      => theme.AttachmentPureColor,
            _                            => theme.AttachmentCustomColor,
        };
}
```

---

### Step 4 — TASK-NEA-06 (part 2): Extend HitTester

**File:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/HitTester.cs`

**Change A — Update UpdateHover signature**

Current signature:
```csharp
    public void UpdateHover(
        GraphView view,
        SpatialIndex spatialIndex,
        Dictionary<PinId, Vector2> pinPositions)
```

Replace with (add `attachmentScreenRects` parameter):
```csharp
    public void UpdateHover(
        GraphView view,
        SpatialIndex spatialIndex,
        Dictionary<PinId, Vector2> pinPositions,
        Dictionary<AttachmentId, RectF> attachmentScreenRects)
```

**Change B — Add attachment hit-testing between sections 2 and 3**

Locate the section that begins with the comment `// 2. Wires` and ends just before `// 3. Nodes and Pins`. After the wire loop ends (closing brace of the wire foreach) and before the node loop comment, insert:

```csharp
        // 2b. Attachment pills (below nodes in z-order when unobscured).
        int attachIndex = 0;
        foreach (var (attachId, screenRect) in attachmentScreenRects)
        {
            attachIndex++;
            if (screenRect.Contains(mouse))
                SubmitHit(new HoverInfo { Kind = HoverKind.Attachment, Attachment = attachId }, 2, attachIndex, 1);
        }
```

The full splice context — find this exact existing text:
```csharp
        // 2. Wires
        int wireIndex = 0;
        foreach (var link in view.Model.Links)
        {
            wireIndex++;
            if (!pinPositions.TryGetValue(link.FromPin, out var a)) continue;
            if (!pinPositions.TryGetValue(link.ToPin, out var b)) continue;

            if (HitsWire(mouse, a, b, link, view.Viewport))
                SubmitHit(new HoverInfo { Kind = HoverKind.Link, Link = link.Id }, 1, wireIndex, 1);
        }

        // 3. Nodes and Pins (same sub-layer uses model draw order).
```

Replace with:
```csharp
        // 2. Wires
        int wireIndex = 0;
        foreach (var link in view.Model.Links)
        {
            wireIndex++;
            if (!pinPositions.TryGetValue(link.FromPin, out var a)) continue;
            if (!pinPositions.TryGetValue(link.ToPin, out var b)) continue;

            if (HitsWire(mouse, a, b, link, view.Viewport))
                SubmitHit(new HoverInfo { Kind = HoverKind.Link, Link = link.Id }, 1, wireIndex, 1);
        }

        // 2b. Attachment pills (below nodes in z-order when unobscured).
        int attachIndex = 0;
        foreach (var (attachId, screenRect) in attachmentScreenRects)
        {
            attachIndex++;
            if (screenRect.Contains(mouse))
                SubmitHit(new HoverInfo { Kind = HoverKind.Attachment, Attachment = attachId }, 2, attachIndex, 1);
        }

        // 3. Nodes and Pins (same sub-layer uses model draw order).
```

---

### Step 5 — Wire everything into CanvasRenderer

**File:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.UI/Canvas/CanvasRenderer.cs`

**Change A — Add AttachmentRenderer field**

Locate the renderer field declarations block:
```csharp
    private readonly CanvasLayoutBuilder _layoutBuilder = new();
    private readonly CanvasLayout        _layout        = new();
    private readonly SpatialIndex        _spatialIndex  = new();
    private readonly HitTester           _hitTester     = new();
    private readonly CanvasInput         _input         = new();
    private readonly GridRenderer        _grid          = new();
    private readonly WireRenderer        _wires         = new();
    private readonly NodeRenderer        _nodes         = new();
```

Replace with:
```csharp
    private readonly CanvasLayoutBuilder _layoutBuilder  = new();
    private readonly CanvasLayout        _layout         = new();
    private readonly SpatialIndex        _spatialIndex   = new();
    private readonly HitTester           _hitTester      = new();
    private readonly CanvasInput         _input          = new();
    private readonly GridRenderer        _grid           = new();
    private readonly WireRenderer        _wires          = new();
    private readonly NodeRenderer        _nodes          = new();
    private readonly AttachmentRenderer  _attachments    = new();
```

**Change B — Pass attachment screen rects to UpdateHover**

Locate:
```csharp
        // 3. Hit-test to update hover info.
        _hitTester.UpdateHover(view, _spatialIndex, _layout.PinScreenPositions);
```

Replace with:
```csharp
        // 3. Hit-test to update hover info.
        _hitTester.UpdateHover(view, _spatialIndex, _layout.PinScreenPositions, _layout.AttachmentScreenRects);
```

**Change C — Draw attachments after nodes**

Locate:
```csharp
        // 8. Nodes + inline editors — only the culled visible subset.
        _nodes.DrawAll(view, dl, _layout.NodeScreenRects, _layout.PinScreenPositions, _layout.ConnectedInputPins, visibleNodeIds);

        // 4. Process input after widgets are submitted, using snapshotted hover.
```

Replace with:
```csharp
        // 8. Nodes + inline editors — only the culled visible subset.
        _nodes.DrawAll(view, dl, _layout.NodeScreenRects, _layout.PinScreenPositions, _layout.ConnectedInputPins, visibleNodeIds);

        // 8b. Attachment pills (or low-zoom bars) above host nodes.
        _attachments.DrawAll(view, dl, _layout.AttachmentLayouts, _layout.NodeScreenRects);

        // 4. Process input after widgets are submitted, using snapshotted hover.
```

---

### Step 6 — TASK-NEA-06 (part 3): Tests for HoverInfo.Attachment

**File (NEW):** `FDP/ExtDeps/NodeEdit/tests/NodeEditor.Core.Tests/View/AttachmentHitTestTests.cs`

Create with the following content:

```csharp
using FluentAssertions;
using NodeEditor.Core.View;
using NodeEditor.Primitives;
using Xunit;

namespace NodeEditor.Core.Tests.View;

/// <summary>Smoke tests for HoverKind.Attachment and HoverInfo.Attachment field.</summary>
public sealed class AttachmentHitTestTests
{
    [Fact]
    public void HoverInfo_WithAttachmentKind_StoresId()
    {
        var id = AttachmentId.NewId();
        var info = new HoverInfo { Kind = HoverKind.Attachment, Attachment = id };

        info.Kind.Should().Be(HoverKind.Attachment);
        info.Attachment.Should().Be(id);
    }

    [Fact]
    public void HoverInfo_None_HasEmptyAttachment()
    {
        var info = HoverInfo.None;

        info.Kind.Should().Be(HoverKind.None);
        info.Attachment.Should().Be(AttachmentId.Empty);
    }

    [Fact]
    public void HoverKind_HasAttachmentValue()
    {
        var names = Enum.GetNames<HoverKind>();
        names.Should().Contain("Attachment");
    }
}
```

---

### Step 7 — Demo supplement: FakeAttachmentModel

**File (NEW):** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/FakeBlueprint/FakeAttachmentModel.cs`

Create with the following content:

```csharp
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Demo.FakeBlueprint;

/// <summary>Simple mutable attachment model for demo scenarios.</summary>
public sealed class FakeAttachmentModel : IAttachmentModel
{
    public AttachmentId Id { get; }
    public NodeId HostNodeId { get; }
    public AttachmentCategory Category { get; set; }
    public string? Glyph { get; set; }
    public string? Label { get; set; }
    public string? Tooltip { get; set; }
    public AttachmentState State { get; set; }
    public int StackIndex { get; set; }

    public FakeAttachmentModel(
        AttachmentId id,
        NodeId hostNodeId,
        AttachmentCategory category,
        string? glyph,
        string? label,
        int stackIndex = 0)
    {
        Id          = id;
        HostNodeId  = hostNodeId;
        Category    = category;
        Glyph       = glyph;
        Label       = label;
        StackIndex  = stackIndex;
        State       = AttachmentState.Normal;
    }
}
```

---

### Step 8 — Demo supplement: FakeGraphModel attachment support

**File:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/FakeBlueprint/FakeGraphModel.cs`

**Change A — Add using directive at top**

The file currently starts with:
```csharp
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using System.Numerics;
```

Replace with:
```csharp
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
```

**Change B — Add attachment backing store**

Locate:
```csharp
    private readonly Dictionary<NodeId,    FakeNodeModel>    _nodes    = new();
    private readonly Dictionary<LinkId,    FakeLinkModel>    _links    = new();
    private readonly Dictionary<CommentId, FakeCommentModel> _comments = new();
```

Replace with:
```csharp
    private readonly Dictionary<NodeId,         FakeNodeModel>         _nodes       = new();
    private readonly Dictionary<LinkId,         FakeLinkModel>         _links       = new();
    private readonly Dictionary<CommentId,      FakeCommentModel>      _comments    = new();
    private readonly Dictionary<AttachmentId,   FakeAttachmentModel>   _attachments = new();
```

**Change C — Override attachment interface members**

Locate the mutable helpers section that begins with:
```csharp
    // ── mutable helpers (called by FakeCommandSink) ───────────────────────────
```

Immediately before that comment, insert:

```csharp
    // ── IGraphModel attachment members ────────────────────────────────────────

    public override IReadOnlyCollection<IAttachmentModel> Attachments =>
        (IReadOnlyCollection<IAttachmentModel>)_attachments.Values;

    public override IAttachmentModel? FindAttachment(AttachmentId id) =>
        _attachments.TryGetValue(id, out var v) ? v : null;

    public override IReadOnlyList<IAttachmentModel> GetAttachmentsForNode(NodeId nodeId) =>
        _attachments.Values.Where(a => a.HostNodeId == nodeId).ToList();

```

Wait — `IGraphModel` uses `default interface members`, not `virtual` methods. `FakeGraphModel` cannot use `override` here. Instead, it must just implement the methods explicitly (since the interface has default members, we add them as regular instance members):

Actually, default interface members in C# cannot be overridden like virtual methods. To "override" a default interface member in the implementing class, you just declare the member in the class (which shadows the default). So the modifier should NOT be `override`.

**Change C — CORRECT version: Override attachment interface members**

Locate the mutable helpers section that begins with:
```csharp
    // ── mutable helpers (called by FakeCommandSink) ───────────────────────────
```

Immediately before that comment, insert:

```csharp
    // ── IGraphModel attachment members ────────────────────────────────────────

    public IReadOnlyCollection<IAttachmentModel> Attachments =>
        (IReadOnlyCollection<IAttachmentModel>)_attachments.Values;

    public IAttachmentModel? FindAttachment(AttachmentId id) =>
        _attachments.TryGetValue(id, out var v) ? v : null;

    public IReadOnlyList<IAttachmentModel> GetAttachmentsForNode(NodeId nodeId) =>
        _attachments.Values.Where(a => a.HostNodeId == nodeId).ToList();

```

**Change D — Add AddAttachment mutable helper**

In the mutable helpers section, after `public void RemoveComment(CommentId id)` and before `public void NotifyChanged(...)`, insert:

```csharp
    public FakeAttachmentModel AddAttachment(
        NodeId hostNodeId,
        AttachmentCategory category,
        string? glyph,
        string? label,
        int stackIndex = 0)
    {
        var id = AttachmentId.NewId();
        var a = new FakeAttachmentModel(id, hostNodeId, category, glyph, label, stackIndex);
        _attachments[id] = a;
        return a;
    }

    public void RemoveAttachment(AttachmentId id) => _attachments.Remove(id);

```

---

### Step 9 — Demo supplement: S34 scenario

**File (NEW):** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/Scenarios/S34_NodeAttachments.cs`

Create with the following content:

```csharp
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Demo.FakeBlueprint;
using System.Numerics;

namespace NodeEditor.Demo.Scenarios;

/// <summary>S34: Nodes with attachment pills of various categories and states.</summary>
public sealed class S34_NodeAttachments : Scenario
{
    public override string Name        => "34 -- Node Attachments";
    public override string Description => "Nodes with decorator/flag/pure/custom pills. Zoom out below 0.5 to see low-zoom bars.";

    public override void Build(GraphView view, FakeGraphModel graph, FakeCommandSink sink, FakeNodeCatalog catalog)
    {
        // Node 1: two Decorator attachments.
        var n1 = AddNode(graph, catalog, "Event.BeginPlay", new Vector2(100, 200));
        graph.AddAttachment(n1, AttachmentCategory.Decorator, "I", "Inverter", stackIndex: 0);
        graph.AddAttachment(n1, AttachmentCategory.Decorator, "R", "Repeat x3", stackIndex: 1);

        // Node 2: Flag + Pure combination.
        var n2 = AddNode(graph, catalog, "Util.Print", new Vector2(400, 200));
        graph.AddAttachment(n2, AttachmentCategory.Flag, "H", "Has History", stackIndex: 0);
        var errAtch = graph.AddAttachment(n2, AttachmentCategory.Pure, "P", "Pure", stackIndex: 1);
        errAtch.State = AttachmentState.Error;

        // Node 3: Custom category with Warning state.
        var n3 = AddNode(graph, catalog, "Flow.Delay", new Vector2(700, 200));
        var warnAtch = graph.AddAttachment(n3, AttachmentCategory.Custom, null, "Custom Tag", stackIndex: 0);
        warnAtch.State = AttachmentState.Warning;

        // Node 4: Many attachments to exercise wrapping.
        var n4 = AddNode(graph, catalog, "Event.BeginPlay", new Vector2(100, 450));
        for (int i = 0; i < 6; i++)
            graph.AddAttachment(n4, (AttachmentCategory)(i % 4), null, $"Tag {i + 1}", stackIndex: i);

        // Wire n1 -> n2 for visual context.
        LinkNodes(graph, n1, 0, n2, 0);
        LinkNodes(graph, n2, 0, n3, 0);
    }
}
```

---

### Step 10 — Demo supplement: Register S34 in DemoShell

**File:** `FDP/ExtDeps/NodeEdit/src/NodeEditor.Demo/DemoShell.cs`

Locate the scenario registration list. Find:
```csharp
        _scenarios.Add(new S33_BigGraph());

        ApplyScenario(0);
```

Replace with:
```csharp
        _scenarios.Add(new S33_BigGraph());
        _scenarios.Add(new S34_NodeAttachments());

        ApplyScenario(0);
```

---

## Build and Test

After all changes, run the build and tests:

```
cd FDP\ExtDeps\NodeEdit
dotnet build NodeEditor.sln -c Debug
dotnet test NodeEditor.sln -c Debug --no-build
```

Expected results:
- 0 errors, 0 warnings in the build
- All previously passing tests continue to pass
- 3 new tests in `AttachmentHitTestTests` pass (total test count increases by 3)

## Report

Write a report in `.dev/blueprints-2/reports/BATCH-07-REPORT.md` documenting:
- Which files were created/modified
- Final test count (before and after)
- Any deviations from the instructions and why

## Important Rules

1. No Unicode characters in comments or string literals (ASCII only — no arrows, special symbols, etc.).
2. Minimize diffs: only change what is needed for the tasks described.
3. Do NOT rewrite, reflow, or "clean up" existing comments unless they are wrong.
4. The build MUST be 0 errors before finishing.
5. RectF constructor is `RectF(Vector2 Min, Vector2 Size)`. The first parameter is `Min` (top-left), NOT `Position`.
   Always use `rect.Min` to access the top-left corner.
6. `IGraphModel.Attachments`, `FindAttachment`, `GetAttachmentsForNode` are default interface members —
   implementing classes add them as regular (non-override) instance members.
7. `AttachmentLayoutEngine.PillPaddingH` is a `public const float = 6f` — use it for horizontal text padding.
