using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core.Action;
using NodeEditor.Core.Canvas;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.UI.Find;
using NodeEditor.UI.Panels;
using NodeEditor.UI.Util;
using NodeEditor.UI.Action;
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
    private readonly CanvasLayoutBuilder     _layoutBuilder  = new();
    private readonly CanvasLayout            _layout         = new();
    private readonly SpatialIndex            _spatialIndex   = new();
    private readonly HitTester               _hitTester      = new();
    private readonly CanvasInput             _input          = new();
    private readonly GridRenderer            _grid           = new();
    private readonly WireRenderer            _wires          = new();
    private readonly NodeRenderer            _nodes          = new();
    private readonly ContainerRenderer       _containers     = new();
    private readonly AttachmentRenderer      _attachments    = new();
    private readonly CanvasRenderContextImpl _renderCtx      = new();

    // Per-renderer perf accumulators (keyed by renderer Id).
    private readonly Dictionary<string, MutablePerfRecord> _perfRecords = new();

    // Dirty tracking: rebuild the spatial index only when the graph model changes
    // or drag-override positions change, not unconditionally every frame.
    private IGraphModel? _subscribedModel;
    private bool         _spatialDirty          = true;
    private int          _lastDragOverrideCount = -1;
    private Vector2      _contextMenuGraphPos;
    private string? _pendingVariableDropId;
    private string? _pendingVariableDropName;
    private Vector2 _pendingVariableDropPos;
    private PinId? _pendingPromotePinId;
    private bool _pendingPromoteIsLocal;
    private bool _showPromoteVariableModal;
    private string _promoteVariableName = "NewVariable";
    private string _promoteVariableCategoryPath = "";

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
        if (ImGui.BeginDragDropTarget())
        {
            var payload = ImGui.AcceptDragDropPayload(MyBlueprintDragSource.Variable);
            unsafe
            {
                if (payload.NativePtr != null && MyBlueprintDragSource.CurrentItemId is not null)
                {
                    var varId = MyBlueprintDragSource.CurrentItemId;
                    var varName = MyBlueprintDragSource.CurrentDisplayName ?? varId;
                    var dropPos = view.Viewport.ScreenToGraph(ImGui.GetMousePos());
                    var mods = view.Host.Input.Modifiers;

                    if (mods.HasFlag(KeyModifiers.Ctrl))
                    {
                        PlaceVariableNode(view, varId, varName, dropPos, isGet: true);
                    }
                    else if (mods.HasFlag(KeyModifiers.Alt))
                    {
                        PlaceVariableNode(view, varId, varName, dropPos, isGet: false);
                    }
                    else
                    {
                        _pendingVariableDropId = varId;
                        _pendingVariableDropName = varName;
                        _pendingVariableDropPos = dropPos;
                        ImGui.OpenPopup("##canvas_drop_var");
                    }
                }
            }

            var evtPayload = ImGui.AcceptDragDropPayload(MyBlueprintDragSource.CustomEvent);
            unsafe
            {
                if (evtPayload.NativePtr != null && MyBlueprintDragSource.CurrentItemId is not null)
                {
                    var evtId = MyBlueprintDragSource.CurrentItemId;
                    var evtName = MyBlueprintDragSource.CurrentDisplayName ?? evtId;
                    var dropPos = view.Viewport.ScreenToGraph(ImGui.GetMousePos());

                    var kind = new NodeKindKey("Event.CallCustom");
                    var props = new Dictionary<string, object?> { ["EventName"] = evtName };
                    var cb = new CommandBuilder(view.Model);
                    var (fwd, inv) = cb.AddNode(kind, dropPos, props);
                    view.Execute(fwd, inv, "Call Custom Event");
                }
            }
            ImGui.EndDragDropTarget();
        }
        bool isCanvasHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        bool isCanvasDirectlyFocused = ImGui.IsWindowFocused(ImGuiFocusedFlags.None);

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
        var visibleLinkIds   = ComputeVisibleLinks(view, visibleNodeIds);

        // 3. Hit-test to update hover info.
        // Prepare the custom-renderer context before hit-testing so hit-testers
        // have access to the current viewport and visible sets.
        _renderCtx.BeginFrame(view, dl, visibleNodeIds, visibleLinkIds);
        _hitTester.UpdateHover(view, _spatialIndex, _layout.PinScreenPositions, _layout.AttachmentScreenRects, _layout.NodeScreenRects, _renderCtx);

        // ── Draw phases ───────────────────────────────────────────────────

        // 5. Grid + background (also fills the solid background color).
        _grid.Draw(view, dl, origin, size);

        // 6. Custom: BeforeContent pass — after grid, before comments and containers.
        InvokeCustomRenderers(view, CanvasRenderPass.BeforeContent);

        // 6a. Comment boxes — background layer (below nodes).
        CommentsRenderer.RenderBackground(dl, view, visibleGraphRect);

        // 6b. Container fills, headers, and outlines — drawn before wires so wires
        //     render on top of the container background but under child nodes.
        _containers.DrawBackground(view, dl, _layout, visibleNodeIds);

        // 7. Wires — only those whose endpoints or waypoints are in the visible rect.
        _wires.DrawAll(view, dl, _layout.PinScreenPositions, visibleNodeIds, visibleGraphRect);

        // 7b. Custom: AfterWires pass — after wires, before regular/child nodes.
        InvokeCustomRenderers(view, CanvasRenderPass.AfterWires);

        // 8. Nodes + inline editors — only the culled visible subset.
        bool isNodeBgActive = _nodes.DrawAll(view, dl, _layout.NodeScreenRects, _layout.PinScreenPositions, _layout.ConnectedInputPins, visibleNodeIds);

        // 8b. Attachment pills (or low-zoom bars) above host nodes.
        _attachments.DrawAll(view, dl, _layout.AttachmentLayouts, _layout.NodeScreenRects);

        // 4. Process input after widgets are submitted, using snapshotted hover.
        _input.Handle(view, isCanvasHovered, isCanvasBgActive, isNodeBgActive, isCanvasDirectlyFocused, _spatialIndex);
        if ((view.Host.Input.Modifiers & KeyModifiers.Alt) != 0
            && view.Interaction.Hover.Kind == HoverKind.Link)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.NotAllowed);
        }

        // 9. Comment boxes — foreground layer (header text on top of nodes).
        CommentsRenderer.RenderForeground(dl, view, visibleGraphRect);

        // 10. Custom: AfterNodes pass — after all nodes, attachments, reroutes; before selection outlines.
        InvokeCustomRenderers(view, CanvasRenderPass.AfterNodes);

        // 11. Pending wire being dragged.
        DrawPendingWire(view, dl);

        // 12. Marquee selection rectangle.
        DrawMarquee(view, dl);

        // 12b. Custom: TopMost pass — above selection outlines, hover effects, drag preview.
        InvokeCustomRenderers(view, CanvasRenderPass.TopMost);

        // 13. Find overlay (match highlights + dim pass).
        if (findBar?.IsVisible == true && findBar.Results.Count > 0)
            DrawFindOverlay(view, dl, findBar, _layout.NodeScreenRects);

        // 14. Context menu popup request/dispatch.
        if (view.Interaction.ContextMenuScreen.HasValue)
        {
            ImGui.SetNextWindowPos(view.Interaction.ContextMenuScreen.Value);
            _contextMenuGraphPos = view.Viewport.ScreenToGraph(view.Interaction.ContextMenuScreen.Value);
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

        if (_showPromoteVariableModal)
        {
            ImGui.OpenPopup("##canvas_promote_var");
            _showPromoteVariableModal = false;
        }

        if (ImGui.BeginPopup("##canvas_drop_var"))
        {
            ImGui.TextDisabled("Variable Action");
            ImGui.Separator();
            if (ImGui.MenuItem("Get"))
            {
                PlaceVariableNode(view, _pendingVariableDropId!, _pendingVariableDropName ?? _pendingVariableDropId!, _pendingVariableDropPos, isGet: true);
                _pendingVariableDropId = null;
                _pendingVariableDropName = null;
            }
            if (ImGui.MenuItem("Set"))
            {
                PlaceVariableNode(view, _pendingVariableDropId!, _pendingVariableDropName ?? _pendingVariableDropId!, _pendingVariableDropPos, isGet: false);
                _pendingVariableDropId = null;
                _pendingVariableDropName = null;
            }
            ImGui.EndPopup();
        }
        else
        {
            _pendingVariableDropId = null;
            _pendingVariableDropName = null;
        }

        DrawPromoteVariableModal(view);
        ImGui.PopStyleVar();
    }

    private void PlaceVariableNode(GraphView view, string variableId, string variableName, Vector2 graphPos, bool isGet)
    {
        var kind = new NodeKindKey(isGet ? "Util.GetVar" : "Util.SetVar");
        var props = new Dictionary<string, object?> { ["VariableId"] = variableId, ["VariableName"] = variableName };
        var cb = new CommandBuilder(view.Model);
        var (fwd, inv) = cb.AddNode(kind, graphPos, props);
        view.Execute(fwd, inv, isGet ? "Add Get Variable" : "Add Set Variable");
    }

    // Invokes active custom renderers for the given pass, in registration order.
    private void InvokeCustomRenderers(GraphView view, CanvasRenderPass pass)
    {
        var renderers = view.Host.CustomCanvasRenderers;
        if (renderers.Count == 0) return;
        _renderCtx._pass = pass;
        var sw = Stopwatch.StartNew();
        foreach (var renderer in renderers)
        {
            if (renderer.Pass != pass || !renderer.IsActive) continue;
            sw.Restart();
            renderer.Render(_renderCtx);
            sw.Stop();
            float ms = (float)(sw.Elapsed.TotalMilliseconds);
            if (!_perfRecords.TryGetValue(renderer.Id, out var rec))
            {
                rec = new MutablePerfRecord();
                _perfRecords[renderer.Id] = rec;
            }
            rec.Record(ms);
        }
    }

    // Returns the set of link IDs whose endpoints are in or near the visible nodes set.
    private static HashSet<LinkId> ComputeVisibleLinks(GraphView view, HashSet<NodeId> visibleNodeIds)
    {
        var result = new HashSet<LinkId>();
        if (visibleNodeIds.Count == 0) return result;
        foreach (var link in view.Model.Links)
        {
            var fromPin = view.Model.FindPin(link.FromPin);
            var toPin   = view.Model.FindPin(link.ToPin);
            if ((fromPin != null && visibleNodeIds.Contains(fromPin.OwnerNodeId)) ||
                (toPin   != null && visibleNodeIds.Contains(toPin.OwnerNodeId)))
                result.Add(link.Id);
        }
        return result;
    }

    // ── Comments ──────────────────────────────────────────────────────────────

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

    private void DrawContextMenu(GraphView view)
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
                        .ToList();

                    if (linksToRemove.Count > 0)
                    {
                        var fwds = new List<Core.Commands.GraphCommand>();
                        var invs = new List<Core.Commands.GraphCommand>();
                        fwds.Add(new Core.Commands.GraphCommand.RemoveLinks(linksToRemove.Select(l => l.Id).ToList()));
                        foreach (var l in linksToRemove)
                        {
                            invs.Add(new Core.Commands.GraphCommand.AddLink(l.Id, l.FromPin, l.ToPin));
                        }
                        invs.Reverse();

                        view.Execute(
                            new Core.Commands.GraphCommand.Batch("Break Links", fwds),
                            new Core.Commands.GraphCommand.Batch("Break Links", invs),
                            "Break Links");
                    }
                }

                ImGui.Separator();
                if (ImGui.MenuItem("Promote to Variable..."))
                    OpenPromoteToVariableModal(pinId, false);
                if (ImGui.MenuItem("Promote to Local Variable..."))
                    OpenPromoteToVariableModal(pinId, true);

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
                {
                    var link = view.Model.FindLink(linkId);
                    if (link != null)
                    {
                        var fwd = new Core.Commands.GraphCommand.RemoveLinks(new[] { linkId });
                        var inv = new Core.Commands.GraphCommand.AddLink(link.Id, link.FromPin, link.ToPin);
                        view.Execute(fwd, inv, "Break Link");
                    }
                }

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
                    var link = view.Model.FindLink(linkId);
                    if (link != null)
                    {
                        var fwd = new Core.Commands.GraphCommand.InsertReroute(linkId, _contextMenuGraphPos);
                        var inv = new Core.Commands.GraphCommand.RemoveReroute(linkId, link.Waypoints.Count);
                        view.Execute(fwd, inv, "Insert Reroute");
                    }
                }

                ImGui.BeginDisabled();
                ImGui.MenuItem("Hide Wire");
                ImGui.EndDisabled();
                break;
            }

            case HoverKind.Node:
            {
                var selectedNodes = view.Selection.Nodes.ToList();
                bool isHoveredSelected = selectedNodes.Contains(target.Node);

                // If right-clicking an unselected node, target just that node.
                // If right-clicking a selected node, target the whole group.
                var targetNodes = isHoveredSelected ? selectedNodes : new List<NodeId> { target.Node };

                if (ImGui.MenuItem(targetNodes.Count > 1 ? $"Delete {targetNodes.Count} Nodes" : "Delete Node", "Del"))
                {
                    if (!isHoveredSelected) view.Selection.ReplaceWith(SelectionEntry.OfNode(target.Node));
                    view.Commands.Apply(new Core.Commands.GraphCommand.RemoveNodes(targetNodes));
                }

                ImGui.Separator();

                if (ImGui.MenuItem("Add Comment", "C"))
                {
                    if (!isHoveredSelected)
                    {
                        view.Selection.ReplaceWith(SelectionEntry.OfNode(target.Node));
                    }

                    CanvasCommands.AddCommentAroundSelection(view);
                }
                break;
            }

            case HoverKind.Comment:
            {
                var commentId = target.Comment;
                var comment = view.Model.Comments.FirstOrDefault(c => c.Id == commentId);
                if (comment == null) break;

                if (ImGui.MenuItem("Rename", "F2"))
                {
                    view.Interaction.RenamingComment = commentId;
                }

                ImGui.Separator();
                if (ImGui.BeginMenu("Color"))
                {
                    if (ImGui.MenuItem("Blue"))   view.Commands.Apply(new Core.Commands.GraphCommand.UpdateComment(commentId, null, null, null, new Vector4(0.29f, 0.56f, 0.88f, 1f), null, null));
                    if (ImGui.MenuItem("Green"))  view.Commands.Apply(new Core.Commands.GraphCommand.UpdateComment(commentId, null, null, null, new Vector4(0.49f, 0.82f, 0.13f, 1f), null, null));
                    if (ImGui.MenuItem("Yellow")) view.Commands.Apply(new Core.Commands.GraphCommand.UpdateComment(commentId, null, null, null, new Vector4(0.97f, 0.90f, 0.11f, 1f), null, null));
                    if (ImGui.MenuItem("Orange")) view.Commands.Apply(new Core.Commands.GraphCommand.UpdateComment(commentId, null, null, null, new Vector4(0.96f, 0.65f, 0.14f, 1f), null, null));
                    if (ImGui.MenuItem("Red"))    view.Commands.Apply(new Core.Commands.GraphCommand.UpdateComment(commentId, null, null, null, new Vector4(0.81f, 0.01f, 0.11f, 1f), null, null));
                    if (ImGui.MenuItem("Purple")) view.Commands.Apply(new Core.Commands.GraphCommand.UpdateComment(commentId, null, null, null, new Vector4(0.56f, 0.07f, 0.99f, 1f), null, null));
                    if (ImGui.MenuItem("Cyan"))   view.Commands.Apply(new Core.Commands.GraphCommand.UpdateComment(commentId, null, null, null, new Vector4(0.31f, 0.89f, 0.76f, 1f), null, null));
                    if (ImGui.MenuItem("Brown"))  view.Commands.Apply(new Core.Commands.GraphCommand.UpdateComment(commentId, null, null, null, new Vector4(0.54f, 0.34f, 0.16f, 1f), null, null));
                    ImGui.EndMenu();
                }

                ImGui.Separator();
                if (ImGui.MenuItem("Bring to Front"))
                {
                    int maxZ = view.Model.Comments.Count > 0 ? view.Model.Comments.Max(c => c.ZOrder) : 0;
                    view.Commands.Apply(new Core.Commands.GraphCommand.UpdateComment(commentId, null, null, null, null, maxZ + 1, null));
                }
                if (ImGui.MenuItem("Send to Back"))
                {
                    int minZ = view.Model.Comments.Count > 0 ? view.Model.Comments.Min(c => c.ZOrder) : 0;
                    view.Commands.Apply(new Core.Commands.GraphCommand.UpdateComment(commentId, null, null, null, null, minZ - 1, null));
                }

                ImGui.Separator();
                ImGui.BeginDisabled();
                ImGui.MenuItem("Resize to Fit Contents");
                ImGui.EndDisabled();

                bool mwc = comment.MoveWithContents;
                if (ImGui.MenuItem("Move with Contents", null, ref mwc))
                {
                    view.Commands.Apply(new Core.Commands.GraphCommand.UpdateComment(commentId, null, null, null, null, null, mwc));
                }

                ImGui.Separator();
                if (ImGui.MenuItem("Delete", "Del"))
                {
                    view.Commands.Apply(new Core.Commands.GraphCommand.RemoveComment(commentId));
                }
                break;
            }

            case HoverKind.CustomElement:
            {
                var ceRef    = target.CustomElement;
                var provider = view.Host.CustomElementContextMenu;
                if (provider != null && provider.RendererId == ceRef.RendererId)
                {
                    var hit   = new CustomElementHit(ceRef.ElementKey, CustomElementKind.Standalone, default);
                    var items = provider.GetItemsFor(ceRef.ElementKey, hit);
                    foreach (var item in items)
                    {
                        if (ImGui.MenuItem(item.Label, "", false, item.Enabled))
                            item.Execute();
                    }
                }
                // If no matching provider, context menu popup is empty -- intended fallback.
                break;
            }
        }
    }

    // ── Find overlay ─────────────────────────────────────────────────────────

    private static void DrawFindOverlay(GraphView view, ImDrawListPtr dl, FindBar findBar, Dictionary<NodeId, RectF> nodeScreenRects)
    {
        var matchNodeIds = new HashSet<NodeId>();
        foreach (var r in findBar.Results)
            if (r.Node.HasValue) matchNodeIds.Add(r.Node.Value);

        // Dim non-matching nodes
        foreach (var node in view.Model.Nodes)
        {
            if (matchNodeIds.Contains(node.Id)) continue;
            if (!nodeScreenRects.TryGetValue(node.Id, out var rect)) continue;

            dl.AddRectFilled(rect.Min, rect.Max, ImGui.GetColorU32(new Vector4(0, 0, 0, 0.6f)), 4f);
        }

        // Yellow outline for matching nodes
        for (int i = 0; i < findBar.Results.Count; i++)
        {
            var result = findBar.Results[i];
            if (!result.Node.HasValue) continue;
            var node = view.Model.FindNode(result.Node.Value);
            if (node is null) continue;
            if (!nodeScreenRects.TryGetValue(node.Id, out var rect)) continue;
            bool isActive = (i == findBar.ActiveIndex);

            // Apply a 2 Hz sine pulse for the active match.
            float pulseAlpha = isActive
                ? 0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * MathF.PI * 4f)
                : 1f;

            var outlineColor = isActive
                ? new Vector4(1f, 0.9f, 0.1f, pulseAlpha)
                : new Vector4(1f, 0.85f, 0.0f, 0.7f);
            float thickness = isActive ? 3.0f : 1.5f;
            dl.AddRect(rect.Min, rect.Max, ImGui.GetColorU32(outlineColor), 4f, ImDrawFlags.None, thickness);
        }
    }

    private void OpenPromoteToVariableModal(PinId pinId, bool isLocal)
    {
        _pendingPromotePinId = pinId;
        _pendingPromoteIsLocal = isLocal;
        _promoteVariableName = isLocal ? "NewLocalVariable" : "NewVariable";
        _promoteVariableCategoryPath = "";
        _showPromoteVariableModal = true;
    }

    private void DrawPromoteVariableModal(GraphView view)
    {
        bool open = true;
        if (!ImGui.BeginPopupModal("##canvas_promote_var", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        if (_pendingPromotePinId is null)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        if (ImGui.IsWindowAppearing())
            ImGui.SetKeyboardFocusHere();

        ImGui.TextDisabled(_pendingPromoteIsLocal ? "Promote to Local Variable" : "Promote to Variable");
        ImGui.Separator();

        var inputFlags = ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue;
        bool inputEnter = ImGui.InputText("Name", ref _promoteVariableName, 128, inputFlags);
        ImGui.InputText("Category", ref _promoteVariableCategoryPath, 256);

        bool canPromote = !string.IsNullOrWhiteSpace(_promoteVariableName);
        bool globalEnter = ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter);

        ImGui.BeginDisabled(!canPromote);
        if (ImGui.Button("Promote", new Vector2(120, 0)) || ((inputEnter || globalEnter) && canPromote))
        {
            string? categoryPath = string.IsNullOrWhiteSpace(_promoteVariableCategoryPath) ? null : _promoteVariableCategoryPath.Trim();
            view.Commands.Apply(new Core.Commands.GraphCommand.PromoteToVariable(
                _pendingPromotePinId.Value,
                _promoteVariableName.Trim(),
                _pendingPromoteIsLocal,
                categoryPath));
            _pendingPromotePinId = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120, 0)) || !open || ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            _pendingPromotePinId = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    // ── Model change tracking ─────────────────────────────────────────────────

    private void OnModelChanged(GraphChangeNotification _) => _spatialDirty = true;

    // ── Perf data ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a snapshot of per-renderer timing data collected during the last
    /// Render call. Returns null when no custom renderers have been invoked yet.
    /// </summary>
    public IReadOnlyDictionary<string, RendererPerfRecord>? GetRendererPerf()
    {
        if (_perfRecords.Count == 0) return null;
        var result = new Dictionary<string, RendererPerfRecord>(_perfRecords.Count);
        foreach (var (id, rec) in _perfRecords)
            result[id] = rec.ToSnapshot();
        return result;
    }
}

// Mutable accumulator used internally to track per-renderer timing.
internal sealed class MutablePerfRecord
{
    private float _lastMs;
    private double _sumMs;
    private float _maxMs;
    private int _calls;

    public void Record(float ms)
    {
        _lastMs = ms;
        _sumMs  += ms;
        _calls++;
        if (ms > _maxMs) _maxMs = ms;
    }

    public RendererPerfRecord ToSnapshot() =>
        new(_lastMs,
            _calls > 0 ? (float)(_sumMs / _calls) : 0f,
            _maxMs,
            _calls);
}

