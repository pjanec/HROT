using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Find;
using NodeEditor.UI.Util;
using NodeEditor.Core.Spatial;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Canvas;

/// <summary>
/// Top-level canvas renderer. Orchestrates the full per-frame pipeline:
/// layout build → hit-test → input handling → draw phases (grid, comments-back,
/// wires, nodes, comments-front, pending wire, marquee).
///
/// Usage: create once and call <c>Render</c> every ImGui frame while the
/// canvas child window context is active.
/// </summary>
public sealed class CanvasRenderer
{
    private readonly CanvasLayoutBuilder _layoutBuilder  = new();
    private readonly CanvasLayout        _layout         = new();
    private readonly SpatialIndex        _spatialIndex   = new();
    private readonly HitTester           _hitTester      = new();
    private readonly CanvasInput         _input          = new();
    private readonly GridRenderer        _grid           = new();
    private readonly WireRenderer        _wires          = new();
    private readonly NodeRenderer        _nodes          = new();
    private readonly ContainerRenderer   _containers     = new();
    private readonly AttachmentRenderer  _attachments    = new();

    // Dirty tracking: rebuild the spatial index only when the graph model changes
    // or drag-override positions change, not unconditionally every frame.
    private IGraphModel? _subscribedModel;
    private bool         _spatialDirty          = true;
    private int          _lastDragOverrideCount = -1;

    /// <summary>
    /// Render one frame of the node-editor canvas. Call this inside an ImGui window
    /// (not inside an existing child window). The method opens and closes its own
    /// child window to establish a clip/scroll region.
    /// </summary>
    /// <param name="view">The graph view to render.</param>
    public void Render(GraphView view)
    {
        Render(view, findBar: null);
    }

    /// <summary>
    /// Render one frame of the node-editor canvas, optionally drawing a find overlay.
    /// The find bar (if visible) is drawn as a slim band above the canvas, and matching
    /// nodes receive highlight outlines while non-matching nodes are dimmed.
    /// </summary>
    /// <param name="view">The graph view to render.</param>
    /// <param name="findBar">Optional find bar; overlays are only drawn when <see cref="FindBar.IsVisible"/> is true.</param>
    public void Render(GraphView view, FindBar? findBar)
    {
        // Draw find bar above the canvas
        findBar?.Draw();

        var avail = ImGui.GetContentRegionAvail();
        if (avail.X <= 0 || avail.Y <= 0) return;

        if (!ImGui.BeginChild("##ne_canvas", avail, ImGuiChildFlags.None,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.EndChild();
            return;
        }

        try
        {
            RenderInner(view, findBar);
        }
        finally
        {
            ImGui.EndChild();
        }
    }

    // ── private ───────────────────────────────────────────────────────────────

    private void RenderInner(GraphView view, FindBar? findBar = null)
    {
        var origin = ImGui.GetCursorScreenPos();
        var size   = ImGui.GetContentRegionAvail();
        var dl     = ImGui.GetWindowDrawList();

        // Publish canvas bounds so viewport transforms are correct.
        view.Viewport.CanvasScreenOrigin = origin;
        view.Viewport.CanvasScreenSize   = size;

        // Claim the full canvas area as a hit target to consume clicks and prevent window dragging.
        ImGui.SetCursorScreenPos(origin);
        ImGui.SetNextItemAllowOverlap();
        ImGui.InvisibleButton("##canvas_bg", size);
        bool isCanvasBgActive = ImGui.IsItemActive();
        bool isCanvasHovered = ImGui.IsWindowHovered(
            ImGuiHoveredFlags.AllowWhenBlockedByActiveItem
            | ImGuiHoveredFlags.AllowWhenBlockedByPopup);

        // Subscribe to model changes so we know when to rebuild the spatial index.
        // Unsubscribe from the previous model if the view was switched.
        if (_subscribedModel != view.Model)
        {
            if (_subscribedModel != null) _subscribedModel.Changed -= OnModelChanged;
            _subscribedModel = view.Model;
            _subscribedModel.Changed += OnModelChanged;
            _spatialDirty = true;
        }

        // Drag-override position count changes also require a spatial index rebuild
        // (nodes move in graph-space while dragging, before the command is committed).
        int dragCount = view.Interaction.DragOverridePositions.Count;
        if (dragCount != _lastDragOverrideCount)
        {
            _lastDragOverrideCount = dragCount;
            _spatialDirty = true;
        }

        // 1. Build layout (screen rects, pin positions; spatial index only when dirty).
        _layoutBuilder.Build(view, _layout, _spatialIndex, _spatialDirty);
        _spatialDirty = false;

        // 2. Compute the visible rectangle in graph-space and cull to visible nodes.
        var graphTopLeft     = view.Viewport.ScreenToGraph(origin);
        var graphBottomRight = view.Viewport.ScreenToGraph(origin + size);
        var visibleGraphRect = RectF.FromMinMax(graphTopLeft, graphBottomRight);
        var visibleNodeIds   = _spatialIndex.Query(visibleGraphRect).ToHashSet();

        // 3. Hit-test to update hover info.
        _hitTester.UpdateHover(view, _spatialIndex, _layout.PinScreenPositions, _layout.AttachmentScreenRects, _layout.NodeScreenRects);


        // ── Draw phases ───────────────────────────────────────────────────

        // 5. Grid + background (also fills the solid background color).
        _grid.Draw(view, dl, origin, size);

        // 6. Comment boxes — background layer (below nodes).
        DrawComments(dl, view, foreground: false, visibleGraphRect);

        // 6b. Container fills, headers, and outlines — drawn before wires so wires
        //     render on top of the container background but under child nodes.
        _containers.DrawBackground(view, dl, _layout, visibleNodeIds);

        // 7. Wires — only those whose endpoints or waypoints are in the visible rect.
        _wires.DrawAll(view, dl, _layout.PinScreenPositions, visibleNodeIds, visibleGraphRect);

        // 8. Nodes + inline editors — only the culled visible subset.
        _nodes.DrawAll(view, dl, _layout.NodeScreenRects, _layout.PinScreenPositions, _layout.ConnectedInputPins, visibleNodeIds);

        // 8b. Attachment pills (or low-zoom bars) above host nodes.
        _attachments.DrawAll(view, dl, _layout.AttachmentLayouts, _layout.NodeScreenRects);

        // 4. Process input after widgets are submitted, using snapshotted hover.
        _input.Handle(view, isCanvasHovered, isCanvasBgActive);
        if ((view.Host.Input.Modifiers & KeyModifiers.Alt) != 0
            && view.Interaction.Hover.Kind == HoverKind.Link)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.NotAllowed);
        }

        // 9. Comment boxes — foreground layer (header text on top of nodes).
        DrawComments(dl, view, foreground: true, visibleGraphRect);

        // 10. Reroute waypoints.
        ReroutesRenderer.Render(view.Model, view.Selection, view.Viewport, view.TypeSystem, visibleNodeIds, visibleGraphRect);

        // 11. Pending wire being dragged.
        DrawPendingWire(view, dl);

        // 12. Marquee selection rectangle.
        DrawMarquee(view, dl);

        // 13. Find overlay (match highlights + dim pass).
        if (findBar?.IsVisible == true && findBar.Results.Count > 0)
            DrawFindOverlay(view, dl, findBar);

        // 14. Context menu popup request/dispatch.
        if (view.Interaction.ContextMenuScreen.HasValue)
        {
            ImGui.SetNextWindowPos(view.Interaction.ContextMenuScreen.Value);
            ImGui.OpenPopup("##canvas_ctx");
            view.Interaction.ContextMenuScreen = null;
        }

        // Restore normal popup content spacing even when the canvas window uses zero padding.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8f, 8f));
        if (ImGui.BeginPopup("##canvas_ctx"))
        {
            DrawContextMenu(view);
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar();
    }

    // ── Comments ──────────────────────────────────────────────────────────────

    private static void DrawComments(ImDrawListPtr dl, GraphView view, bool foreground, RectF visibleGraphRect)
    {
        var theme = view.Host.Theme;
        var comments = view.Model.Comments.ToList();
        comments.Sort((a, b) => a.ZOrder.CompareTo(b.ZOrder));

        foreach (var comment in comments)
        {
            var commentRect = new RectF(comment.Position, comment.Size);
            if (!commentRect.Intersects(visibleGraphRect))
                continue;

            var min = view.Viewport.GraphToScreen(comment.Position);
            var max = view.Viewport.GraphToScreen(comment.Position + comment.Size);
            float headerH = 20f * view.Viewport.Zoom;

            bool selected = view.Selection.Contains(SelectionEntry.OfComment(comment.Id));

            if (!foreground)
            {
                // Body fill (semi-transparent)
                var bodyColor = comment.Color with { W = 0.15f };
                dl.AddRectFilled(min, max, ImGui.GetColorU32(bodyColor), 4f);

                // Header strip
                var headerMax = new Vector2(max.X, min.Y + headerH);
                dl.AddRectFilled(min, headerMax, ImGui.GetColorU32(comment.Color with { W = 0.65f }), 4f,
                    ImDrawFlags.RoundCornersTop);

                // Border
                uint borderColor = selected
                    ? ImGui.GetColorU32(theme.SelectionAccent)
                    : ImGui.GetColorU32(comment.Color);
                dl.AddRect(min, max, borderColor, 4f, ImDrawFlags.None, selected ? 2f : 1f);
            }
            else
            {
                // Header text
                uint textColor = ImGui.GetColorU32(theme.TextDefault);
                dl.AddText(min + new Vector2(6f, 4f), textColor, comment.Text.Split('\n')[0]);
            }
        }
    }

    // ── Pending wire ──────────────────────────────────────────────────────────

    private void DrawPendingWire(GraphView view, ImDrawListPtr dl)
    {
        var pw = view.Interaction.PendingWire;
        if (pw == null || view.Interaction.Mode != InteractionMode.PendingWire) return;

        _layout.PinScreenPositions.TryGetValue(pw.SourcePin, out var a);
        if (a == default) a = view.Host.Input.MousePosition;

        Vector2 b = _layout.PinScreenPositions.TryGetValue(
            pw.CandidateTarget ?? default, out var snapPos)
            ? snapPos
            : view.Viewport.GraphToScreen(pw.CursorGraph);

        var srcPin  = view.Model.FindPin(pw.SourcePin);
        bool isExec = srcPin?.Kind == PinKind.Exec;

        uint wireColor = pw.CandidateTarget.HasValue
            ? pw.CandidateValid
                ? ImGui.GetColorU32(new Vector4(0.3f, 1f, 0.3f, 1f))
                : ImGui.GetColorU32(new Vector4(1f, 0.3f, 0.3f, 1f))
            : ImGui.GetColorU32(new Vector4(0.8f, 0.8f, 0.8f, 0.85f));

        float thickness = isExec ? view.Host.Theme.WireThicknessExec : view.Host.Theme.WireThicknessData;
        var (c1, c2) = HitTester.WireTangents(a, b);

        if (isExec)
            dl.AddBezierWithArrow(a, c1, c2, b, wireColor, thickness, thickness * 2.5f);
        else
            dl.AddBezierCubic(a, c1, c2, b, wireColor, thickness);
    }

    // ── Marquee ───────────────────────────────────────────────────────────────

    private static void DrawMarquee(GraphView view, ImDrawListPtr dl)
    {
        if (view.Interaction.Mode != InteractionMode.MarqueeSelecting) return;

        var marquee = view.Interaction.MarqueeGraph;
        var min = view.Viewport.GraphToScreen(marquee.Min);
        var max = view.Viewport.GraphToScreen(marquee.Min + marquee.Size);

        var theme = view.Host.Theme;
        dl.AddRectFilled(min, max, ImGui.GetColorU32(theme.SelectionAccent with { W = 0.1f }));
        dl.AddRect(min, max, ImGui.GetColorU32(theme.SelectionAccent), 0f, ImDrawFlags.None, 1.5f);
    }

    private static void DrawContextMenu(GraphView view)
    {
        var target = view.Interaction.ContextMenuTarget;
        switch (target.Kind)
        {
            case HoverKind.Pin:
            {
                var pinId = target.Pin;
                if (ImGui.MenuItem("Break Link(s)"))
                {
                    var linksToRemove = view.Model.Links
                        .Where(l => l.FromPin == pinId || l.ToPin == pinId)
                        .Select(l => l.Id)
                        .ToList();
                    if (linksToRemove.Count > 0)
                        view.Commands.Apply(new Core.Commands.GraphCommand.RemoveLinks(linksToRemove));
                }

                ImGui.Separator();
                if (ImGui.MenuItem("Promote to Variable..."))
                    view.Commands.Apply(new Core.Commands.GraphCommand.PromoteToVariable(pinId, "NewVariable", false, null));
                if (ImGui.MenuItem("Promote to Local Variable..."))
                    view.Commands.Apply(new Core.Commands.GraphCommand.PromoteToVariable(pinId, "NewLocalVariable", true, null));

                ImGui.BeginDisabled();
                ImGui.MenuItem("Split Struct Pin");
                ImGui.MenuItem("Recombine Struct Pin");
                ImGui.MenuItem("Watch this Value");
                ImGui.EndDisabled();

                if (ImGui.MenuItem("Reset to Default"))
                    view.Commands.Apply(new Core.Commands.GraphCommand.SetPinDefault(pinId, null));

                ImGui.BeginDisabled();
                ImGui.MenuItem("Convert to Reroute Node");
                ImGui.EndDisabled();
                break;
            }

            case HoverKind.Link:
            {
                var linkId = target.Link;
                if (ImGui.MenuItem("Break Link"))
                    view.Commands.Apply(new Core.Commands.GraphCommand.RemoveLinks(new[] { linkId }));

                if (ImGui.MenuItem("Select Connected Nodes"))
                {
                    var link = view.Model.FindLink(linkId);
                    if (link != null)
                    {
                        var fromNode = view.Model.FindPin(link.FromPin)?.OwnerNodeId;
                        var toNode = view.Model.FindPin(link.ToPin)?.OwnerNodeId;
                        var entries = new List<SelectionEntry>();
                        if (fromNode.HasValue) entries.Add(SelectionEntry.OfNode(fromNode.Value));
                        if (toNode.HasValue) entries.Add(SelectionEntry.OfNode(toNode.Value));
                        view.Selection.ReplaceWith(entries);
                    }
                }

                if (ImGui.MenuItem("Insert Reroute Node Here"))
                {
                    var graphPos = view.Viewport.ScreenToGraph(ImGui.GetMousePos());
                    view.Commands.Apply(new Core.Commands.GraphCommand.InsertReroute(linkId, graphPos));
                }

                ImGui.BeginDisabled();
                ImGui.MenuItem("Hide Wire");
                ImGui.EndDisabled();
                break;
            }

            case HoverKind.Node:
                if (ImGui.MenuItem("Delete Node"))
                {
                    var nodeId = target.Node;
                    view.Commands.Apply(new Core.Commands.GraphCommand.RemoveNodes(new[] { nodeId }));
                }
                break;
        }
    }

    // ── Find overlay ─────────────────────────────────────────────────────────

    private static void DrawFindOverlay(GraphView view, ImDrawListPtr dl, FindBar findBar)
    {
        var matchNodeIds = new HashSet<NodeId>();
        foreach (var r in findBar.Results)
            if (r.Node.HasValue) matchNodeIds.Add(r.Node.Value);

        // Dim non-matching nodes
        foreach (var node in view.Model.Nodes)
        {
            if (matchNodeIds.Contains(node.Id)) continue;
            var pos  = node.Position;
            var size = node.SizeOverride ?? new Vector2(160, 64);
            var min  = view.Viewport.GraphToScreen(pos);
            var max  = view.Viewport.GraphToScreen(pos + size);
            dl.AddRectFilled(min, max, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.6f)), 4f);
        }

        // Yellow outline for matching nodes
        for (int i = 0; i < findBar.Results.Count; i++)
        {
            var result = findBar.Results[i];
            if (!result.Node.HasValue) continue;
            var node = view.Model.FindNode(result.Node.Value);
            if (node is null) continue;

            var size  = node.SizeOverride ?? new Vector2(160, 64);
            var min   = view.Viewport.GraphToScreen(node.Position);
            var max   = view.Viewport.GraphToScreen(node.Position + size);
            bool isActive = (i == findBar.ActiveIndex);

            var outlineColor = isActive
                ? new Vector4(1f, 0.9f, 0.1f, 1f)
                : new Vector4(1f, 0.85f, 0.0f, 0.7f);
            float thickness = isActive ? 3.0f : 1.5f;
            dl.AddRect(min, max, ImGui.GetColorU32(outlineColor), 4f, ImDrawFlags.None, thickness);
        }
    }

    // ── Model change tracking ─────────────────────────────────────────────────

    private void OnModelChanged(GraphChangeNotification _) => _spatialDirty = true;
}


