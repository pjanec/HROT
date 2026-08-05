using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core;
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
    private NodeId? _pendingRenameNodeId;
    private string  _nodeRenameText = "";
    private bool    _showNodeRenameModal;
    private PinId? _pendingPromotePinId;
    private bool _pendingPromoteIsLocal;
    private bool _showPromoteVariableModal;
    private string _promoteVariableName = "NewVariable";
    private string _promoteVariableCategoryPath = "";
    private IEditorCommands? _editorCommands;

    // Hover-tooltip delay: only show after the cursor rests on the same target for a moment,
    // so tooltips don't strobe as the mouse sweeps across the graph.
    private string? _tooltipKey;
    private double  _tooltipSince;
    private const double TooltipDelaySeconds = 0.5;

    /// <summary>
    /// Render one frame of the node-editor canvas. Call this inside an ImGui window
    /// (not inside an existing child window). The method opens and closes its own
    /// child window to establish a clip/scroll region.
    /// </summary>
    /// <param name="view">The graph view to render.</param>
    public void Render(GraphView view)
    {
        Render(view, findBar: null, commands: null);
    }

    /// <summary>
    /// Render one frame of the node-editor canvas, optionally drawing a find overlay.
    /// The find bar (if visible) is drawn as a slim band above the canvas, and matching
    /// nodes receive highlight outlines while non-matching nodes are dimmed.
    /// </summary>
    /// <param name="view">The graph view to render.</param>
    /// <param name="findBar">Optional find bar; overlays are only drawn when <see cref="FindBar.IsVisible"/> is true.</param>
    /// <param name="commands">Optional command dispatcher used by context-menu actions.</param>
    public void Render(GraphView view, FindBar? findBar, IEditorCommands? commands = null)
    {
        _editorCommands = commands;
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
                    // The item id is the authoritative identity (the host resolves it to the
                    // declaration); the display name is a fallback for hosts that key by name.
                    // Shipping only the name left the host with nothing precise to bind to.
                    var props = new Dictionary<string, object?>
                    {
                        ["EventId"]   = evtId,
                        ["EventName"] = evtName,
                    };
                    var cb = new CommandBuilder(view.Model);
                    var (fwd, inv) = cb.AddNode(kind, dropPos, props);
                    view.Execute(fwd, inv, "Call Custom Event");
                }
            }

            var macroPayload = ImGui.AcceptDragDropPayload(MyBlueprintDragSource.Macro);
            unsafe
            {
                if (macroPayload.NativePtr != null && MyBlueprintDragSource.CurrentItemId is not null)
                {
                    var macroId = MyBlueprintDragSource.CurrentItemId;
                    var macroName = MyBlueprintDragSource.CurrentDisplayName ?? macroId;
                    var dropPos = view.Viewport.ScreenToGraph(ImGui.GetMousePos());

                    var kind = new NodeKindKey("Macro.Call");
                    var props = new Dictionary<string, object?> { ["MacroName"] = macroName };
                    var cb = new CommandBuilder(view.Model);
                    var (fwd, inv) = cb.AddNode(kind, dropPos, props);
                    view.Execute(fwd, inv, "Call Macro");
                }
            }

            var dispPayload = ImGui.AcceptDragDropPayload(MyBlueprintDragSource.EventDispatcher);
            unsafe
            {
                if (dispPayload.NativePtr != null && MyBlueprintDragSource.CurrentItemId is not null)
                {
                    var dispId = MyBlueprintDragSource.CurrentItemId;
                    var dispName = MyBlueprintDragSource.CurrentDisplayName ?? dispId;

                    // Reuse pending drop fields to pass data to the dispatcher popup.
                    _pendingVariableDropId = dispId;
                    _pendingVariableDropName = dispName;
                    _pendingVariableDropPos = view.Viewport.ScreenToGraph(ImGui.GetMousePos());
                    ImGui.OpenPopup("##canvas_drop_dispatcher");
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
        _renderCtx.BeginFrame(view, dl, visibleNodeIds, visibleLinkIds, _layout);
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

        // 8c. Hover tooltip: show the model-supplied tooltip for the hovered node/pin.
        DrawHoverTooltip(view);

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

        bool varPopupOpen = ImGui.BeginPopup("##canvas_drop_var");
        if (varPopupOpen)
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

        bool dispPopupOpen = ImGui.BeginPopup("##canvas_drop_dispatcher");
        if (dispPopupOpen)
        {
            ImGui.TextDisabled("Dispatcher Action");
            ImGui.Separator();
            if (ImGui.MenuItem("Call"))
            {
                PlaceDispatcherNode(view, _pendingVariableDropId!, _pendingVariableDropName ?? _pendingVariableDropId!, _pendingVariableDropPos, "Event.CallDispatcher");
                _pendingVariableDropId = null;
                _pendingVariableDropName = null;
            }
            if (ImGui.MenuItem("Bind"))
            {
                PlaceDispatcherNode(view, _pendingVariableDropId!, _pendingVariableDropName ?? _pendingVariableDropId!, _pendingVariableDropPos, "Event.BindDispatcher");
                _pendingVariableDropId = null;
                _pendingVariableDropName = null;
            }
            if (ImGui.MenuItem("Unbind"))
            {
                PlaceDispatcherNode(view, _pendingVariableDropId!, _pendingVariableDropName ?? _pendingVariableDropId!, _pendingVariableDropPos, "Event.UnbindDispatcher");
                _pendingVariableDropId = null;
                _pendingVariableDropName = null;
            }
            if (ImGui.MenuItem("Unbind All"))
            {
                PlaceDispatcherNode(view, _pendingVariableDropId!, _pendingVariableDropName ?? _pendingVariableDropId!, _pendingVariableDropPos, "Event.UnbindAllDispatcher");
                _pendingVariableDropId = null;
                _pendingVariableDropName = null;
            }
            ImGui.EndPopup();
        }

        if (!varPopupOpen && !dispPopupOpen)
        {
            _pendingVariableDropId = null;
            _pendingVariableDropName = null;
        }

        DrawPromoteVariableModal(view);
        DrawNodeRenameModal(view);
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

    private void PlaceDispatcherNode(GraphView view, string dispatcherId, string dispatcherName, Vector2 graphPos, string kindId)
    {
        var kind = new NodeKindKey(kindId);
        var props = new Dictionary<string, object?> { ["DispatcherId"] = dispatcherId, ["DispatcherName"] = dispatcherName };
        var cb = new CommandBuilder(view.Model);
        var (fwd, inv) = cb.AddNode(kind, graphPos, props);
        view.Execute(fwd, inv, $"Add {kindId.Split('.').Last()}");
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

        float zoom = view.Viewport.Zoom;
        float thickness = MathF.Max(0.75f,
            (isExec ? view.Host.Theme.WireThicknessExec : view.Host.Theme.WireThicknessData) * zoom);
        var (c1, c2) = HitTester.WireTangents(a, b, view.Model.Kind.Orientation, zoom);

        if (isExec)
            dl.AddBezierWithArrow(a, c1, c2, b, wireColor, thickness, thickness * 2.5f);
        else
            dl.AddBezierCubic(a, c1, c2, b, wireColor, thickness);
    }

    // ── Hover tooltip ───────────────────────────────────────────────────────────

    /// <summary>
    /// Draws the hover tooltip for the node or pin the cursor is over, reading the text from the
    /// model (<see cref="IPinModel.Tooltip"/> / <see cref="INodeModel.StatusTooltip"/>). The canvas
    /// renderer stays case-agnostic — the host injects the actual text via its model projection.
    /// Suppressed while interacting (wiring, marquee, picker) so it never fights an active gesture.
    /// </summary>
    private void DrawHoverTooltip(GraphView view)
    {
        if (view.Interaction.Mode != InteractionMode.Idle) { _tooltipKey = null; return; }

        var hover = view.Interaction.Hover;
        string? text;
        string? key;
        switch (hover.Kind)
        {
            case HoverKind.Pin:  text = view.Model.FindPin(hover.Pin)?.Tooltip;      key = "p:" + hover.Pin.Value;  break;
            case HoverKind.Node: text = view.Model.FindNode(hover.Node)?.StatusTooltip; key = "n:" + hover.Node.Value; break;
            default:             text = null; key = null; break;
        }
        if (string.IsNullOrEmpty(text)) { _tooltipKey = null; return; }

        // Require the cursor to rest on the same target for TooltipDelaySeconds before showing.
        double now = ImGui.GetTime();
        if (key != _tooltipKey) { _tooltipKey = key; _tooltipSince = now; return; }
        if (now - _tooltipSince < TooltipDelaySeconds) return;

        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
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
            case HoverKind.None:
            {
                if (ImGui.MenuItem("Add Node...", "Tab"))
                {
                    view.Interaction.Mode = InteractionMode.PickerOpen;
                    var graphPos = _contextMenuGraphPos;
                    view.Host.Pickers.Open(
                        "nodes.all",
                        ImGui.GetMousePos(),
                        pick =>
                        {
                            if (pick is NodeCatalogEntry entry)
                                PaletteEntryExecutor.Execute(view, entry, graphPos);
                            view.Interaction.ResetToIdle();
                        },
                        () => view.Interaction.ResetToIdle());
                }

                if (ImGui.MenuItem("Add Return Node"))
                {
                    var cb = new CommandBuilder(view.Model);
                    var (fwd, inv) = cb.AddNode(new NodeKindKey("Function.Return"), _contextMenuGraphPos, null);
                    view.Execute(fwd, inv, "Add Return");
                }

                bool hasSelection = view.Selection.Nodes.Any();
                if (ImGui.MenuItem("Add Comment", "C", false, hasSelection))
                {
                    CanvasCommands.AddCommentAroundSelection(view);
                }

                ImGui.Separator();
                // BP-23a: Paste was hard-disabled (the trailing `false, false` = unselected,
                // DISABLED) because no host implemented it. It is live whenever a host registers
                // editor.paste, and pastes at the cursor rather than at the copy's origin.
                bool canPaste = IsCommandEnabled(CommandCatalog.Paste);
                if (ImGui.MenuItem("Paste", "Ctrl+V", false, canPaste))
                    _editorCommands?.Invoke(CommandCatalog.Paste, new EditorCommandContext(
                        ScreenPos: null, CanvasPos: _contextMenuGraphPos, Args: null));
                ImGui.Separator();

                if (ImGui.MenuItem("Frame All", "Home"))
                {
                    if (view.Model.Nodes.Count > 0)
                    {
                        float minX = float.MaxValue, minY = float.MaxValue;
                        float maxX = float.MinValue, maxY = float.MinValue;
                        foreach (var n in view.Model.Nodes)
                        {
                            var size = n.SizeOverride ?? new Vector2(160, 64);
                            if (n.Position.X < minX) minX = n.Position.X;
                            if (n.Position.Y < minY) minY = n.Position.Y;
                            if (n.Position.X + size.X > maxX) maxX = n.Position.X + size.X;
                            if (n.Position.Y + size.Y > maxY) maxY = n.Position.Y + size.Y;
                        }
                        view.Viewport.FrameRect(new RectF(new Vector2(minX, minY), new Vector2(maxX - minX, maxY - minY)));
                    }
                }

                if (ImGui.MenuItem("Reset Zoom", "Ctrl+0"))
                {
                    view.Viewport.Reset();
                }
                break;
            }

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
                {
                    // BP-02: snapshot the current value first so the reset is reversible; this
                    // previously discarded the old value with no way back.
                    var oldDefault = view.Model.FindPin(pinId)?.Default?.Value;
                    view.Execute(
                        new Core.Commands.GraphCommand.SetPinDefault(pinId, null),
                        new Core.Commands.GraphCommand.SetPinDefault(pinId, oldDefault),
                        "Reset Pin to Default");
                }

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
                var node = view.Model.FindNode(target.Node);

                bool canNavigate = node != null &&
                                   (node.Kind.Id == "Function.Call" ||
                                    node.Kind.Id == "Macro.Call" ||
                                    node.Kind.Id == "Event.CallCustom");

                if (ImGui.MenuItem("Go to Definition", "F12", false, canNavigate))
                {
                    if (!isHoveredSelected) view.Selection.ReplaceWith(SelectionEntry.OfNode(target.Node));
                    _editorCommands?.Invoke(CommandCatalog.GoToDefinition);
                }

                bool canExpand = node != null &&
                                 (node.Kind.Id == "Function.Call" ||
                                  node.Kind.Id == "Macro.Call" ||
                                  node.Title == "ScaleBy");

                if (ImGui.MenuItem("Expand Node", null, false, canExpand))
                {
                    if (!isHoveredSelected) view.Selection.ReplaceWith(SelectionEntry.OfNode(target.Node));

                    var callNode = view.Model.FindNode(target.Node);
                    if (callNode != null)
                    {
                        // 1. Snapshot the external links attached to the call node before destruction.
                        var externalLinks = view.Model.Links
                            .Where(l => view.Model.FindPin(l.FromPin)?.OwnerNodeId == target.Node ||
                                        view.Model.FindPin(l.ToPin)?.OwnerNodeId == target.Node)
                            .ToList();

                        var invs = new List<Core.Commands.GraphCommand>();

                        // 2. Predict IDs of internal nodes the backend will spawn.
                        var n1Id = IdGenerator.DeterministicNodeId(target.Node.Value.ToString() + "_exp1");
                        var n2Id = IdGenerator.DeterministicNodeId(target.Node.Value.ToString() + "_exp2");
                        invs.Add(new Core.Commands.GraphCommand.RemoveNodes(new[] { n1Id, n2Id }));

                        // 3. Restore the original call node with precise pin IDs.
                        var props = new Dictionary<string, object?> { ["PinIds"] = callNode.Pins.Select(p => p.Id).ToList() };
                        invs.Add(new Core.Commands.GraphCommand.AddNode(callNode.Id, callNode.Kind, callNode.Position, props));

                        // 4. Restore external wires.
                        foreach (var l in externalLinks)
                        {
                            invs.Add(new Core.Commands.GraphCommand.AddLink(l.Id, l.FromPin, l.ToPin));
                        }

                        // 5. Dispatch command with exact inverse.
                        var fwd = new Core.Commands.GraphCommand.ExpandNode(target.Node);
                        view.Execute(fwd, new Core.Commands.GraphCommand.Batch("Undo Expand", invs), "Expand Node");
                    }
                }

                ImGui.Separator();

                // BP-17: a custom header. The generated title becomes the subtitle, so renaming a
                // node never costs the only indication of what it actually is.
                if (ImGui.MenuItem("Rename\u2026", "F2"))
                {
                    if (!isHoveredSelected) view.Selection.ReplaceWith(SelectionEntry.OfNode(target.Node));
                    OpenNodeRenameModal(target.Node, node?.Title ?? "");
                }

                // BP-18: IsCollapsed was hardcoded false, so the flag NodeRenderer already honours
                // could never be set.
                bool isCollapsed = node?.IsCollapsed == true;
                if (ImGui.MenuItem(isCollapsed ? "Expand Node" : "Collapse Node"))
                {
                    foreach (var id in targetNodes)
                        view.Execute(
                            new Core.Commands.GraphCommand.SetNodeCollapsed(id, !isCollapsed),
                            new Core.Commands.GraphCommand.SetNodeCollapsed(id, isCollapsed),
                            isCollapsed ? "Expand Node" : "Collapse Node");
                }

                ImGui.Separator();

                // BP-13: nine alignment commands that CommandCatalog declared and nothing
                // implemented. Grouped in a submenu so the node menu does not grow by nine rows.
                if (ImGui.BeginMenu("Align"))
                {
                    DrawAlignItem("Left",                CommandCatalog.AlignLeft);
                    DrawAlignItem("Right",               CommandCatalog.AlignRight);
                    DrawAlignItem("Top",                 CommandCatalog.AlignTop);
                    DrawAlignItem("Bottom",              CommandCatalog.AlignBottom);
                    ImGui.Separator();
                    DrawAlignItem("Center Horizontally", CommandCatalog.AlignCenterH);
                    DrawAlignItem("Center Vertically",   CommandCatalog.AlignCenterV);
                    ImGui.Separator();
                    DrawAlignItem("Distribute Horizontally", CommandCatalog.DistributeH);
                    DrawAlignItem("Distribute Vertically",   CommandCatalog.DistributeV);
                    ImGui.Separator();
                    DrawAlignItem("Straighten Connection",   CommandCatalog.StraightenConn);
                    ImGui.EndMenu();
                }

                ImGui.Separator();

                // BP-23a: the three clipboard actions a designer reaches for first. Right-clicking
                // an unselected node targets just that node, matching Delete below.
                bool hasClipboardHost = IsCommandEnabled(CommandCatalog.Copy);
                if (ImGui.MenuItem("Copy", "Ctrl+C", false, hasClipboardHost))
                {
                    if (!isHoveredSelected) view.Selection.ReplaceWith(SelectionEntry.OfNode(target.Node));
                    _editorCommands?.Invoke(CommandCatalog.Copy);
                }

                if (ImGui.MenuItem("Cut", "Ctrl+X", false, hasClipboardHost))
                {
                    if (!isHoveredSelected) view.Selection.ReplaceWith(SelectionEntry.OfNode(target.Node));
                    _editorCommands?.Invoke(CommandCatalog.Cut);
                }

                if (ImGui.MenuItem("Duplicate", "Ctrl+D", false, hasClipboardHost))
                {
                    if (!isHoveredSelected) view.Selection.ReplaceWith(SelectionEntry.OfNode(target.Node));
                    _editorCommands?.Invoke(CommandCatalog.Duplicate);
                }

                ImGui.Separator();

                if (ImGui.MenuItem(targetNodes.Count > 1 ? $"Delete {targetNodes.Count} Nodes" : "Delete Node", "Del"))
                {
                    if (!isHoveredSelected) view.Selection.ReplaceWith(SelectionEntry.OfNode(target.Node));
                    // Route through the same undoable path as the Del key (BP-59). A raw
                    // Commands.Apply(RemoveNodes) records no inverse, so the nodes were
                    // unrecoverable; DeleteSelectedUndoable also removes the implicitly
                    // orphaned links, which the raw command left dangling.
                    EditCommands.DeleteSelectedUndoable(view);
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

                ImGui.Separator();
                if (ImGui.MenuItem("Toggle Breakpoint", "F9"))
                {
                    if (!isHoveredSelected)
                        view.Selection.ReplaceWith(SelectionEntry.OfNode(target.Node));
                    _editorCommands?.Invoke(CommandCatalog.ToggleBreakpoint);
                }

                var nodeMenuProvider = view.Host.NodeContextMenu;
                if (nodeMenuProvider != null)
                {
                    var nodeItems = nodeMenuProvider.GetItemsFor(target.Node, targetNodes);
                    if (nodeItems.Count > 0)
                    {
                        ImGui.Separator();
                        RenderItems(nodeItems);
                    }
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

                // BP-02: every comment mutation below goes through view.Execute with an explicit
                // inverse snapshotted from the CURRENT model state, so Ctrl+Z reverses it. These
                // previously called view.Commands.Apply directly and were silently un-undoable.
                var oldColor = comment.Color;
                var oldZ     = comment.ZOrder;
                var oldMwc   = comment.MoveWithContents;

                // Inverse restoring only the colour channel.
                Core.Commands.GraphCommand ColorInverse() =>
                    new Core.Commands.GraphCommand.UpdateComment(
                        commentId, null, null, null, oldColor, null, null);

                void SetColor(Vector4 c) =>
                    view.Execute(
                        new Core.Commands.GraphCommand.UpdateComment(
                            commentId, null, null, null, c, null, null),
                        ColorInverse(),
                        "Set Comment Color");

                ImGui.Separator();
                if (ImGui.BeginMenu("Color"))
                {
                    if (ImGui.MenuItem("Blue"))   SetColor(new Vector4(0.29f, 0.56f, 0.88f, 1f));
                    if (ImGui.MenuItem("Green"))  SetColor(new Vector4(0.49f, 0.82f, 0.13f, 1f));
                    if (ImGui.MenuItem("Yellow")) SetColor(new Vector4(0.97f, 0.90f, 0.11f, 1f));
                    if (ImGui.MenuItem("Orange")) SetColor(new Vector4(0.96f, 0.65f, 0.14f, 1f));
                    if (ImGui.MenuItem("Red"))    SetColor(new Vector4(0.81f, 0.01f, 0.11f, 1f));
                    if (ImGui.MenuItem("Purple")) SetColor(new Vector4(0.56f, 0.07f, 0.99f, 1f));
                    if (ImGui.MenuItem("Cyan"))   SetColor(new Vector4(0.31f, 0.89f, 0.76f, 1f));
                    if (ImGui.MenuItem("Brown"))  SetColor(new Vector4(0.54f, 0.34f, 0.16f, 1f));
                    ImGui.EndMenu();
                }

                ImGui.Separator();
                if (ImGui.MenuItem("Bring to Front"))
                {
                    int maxZ = view.Model.Comments.Count > 0 ? view.Model.Comments.Max(c => c.ZOrder) : 0;
                    view.Execute(
                        new Core.Commands.GraphCommand.UpdateComment(commentId, null, null, null, null, maxZ + 1, null),
                        new Core.Commands.GraphCommand.UpdateComment(commentId, null, null, null, null, oldZ, null),
                        "Bring Comment to Front");
                }
                if (ImGui.MenuItem("Send to Back"))
                {
                    int minZ = view.Model.Comments.Count > 0 ? view.Model.Comments.Min(c => c.ZOrder) : 0;
                    view.Execute(
                        new Core.Commands.GraphCommand.UpdateComment(commentId, null, null, null, null, minZ - 1, null),
                        new Core.Commands.GraphCommand.UpdateComment(commentId, null, null, null, null, oldZ, null),
                        "Send Comment to Back");
                }

                ImGui.Separator();
                ImGui.BeginDisabled();
                ImGui.MenuItem("Resize to Fit Contents");
                ImGui.EndDisabled();

                bool mwc = oldMwc;
                if (ImGui.MenuItem("Move with Contents", null, ref mwc))
                {
                    view.Execute(
                        new Core.Commands.GraphCommand.UpdateComment(commentId, null, null, null, null, null, mwc),
                        new Core.Commands.GraphCommand.UpdateComment(commentId, null, null, null, null, null, oldMwc),
                        "Toggle Move with Contents");
                }

                ImGui.Separator();
                if (ImGui.MenuItem("Delete", "Del"))
                {
                    // Inverse re-adds the comment with its original id and every property, so undo
                    // restores it intact rather than leaving a hole.
                    view.Execute(
                        new Core.Commands.GraphCommand.RemoveComment(commentId),
                        new Core.Commands.GraphCommand.AddComment(
                            commentId, comment.Text, comment.Position, comment.Size, oldColor, oldMwc),
                        "Delete Comment");
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
                    RenderItems(items);
                }
                // If no matching provider, context menu popup is empty -- intended fallback.
                break;
            }
        }
    }

    /// <summary>
    /// Recursively renders a list of <see cref="ContextMenuItem"/>s using ImGui.
    /// Items with non-empty <c>Children</c> are rendered as submenus;
    /// leaf items are rendered as plain menu entries.
    /// </summary>
    private static void RenderItems(IReadOnlyList<ContextMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.Children is { Count: > 0 })
            {
                if (ImGui.BeginMenu(item.Label, item.Enabled))
                {
                    RenderItems(item.Children);
                    ImGui.EndMenu();
                }
            }
            else
            {
                if (ImGui.MenuItem(item.Label, "", false, item.Enabled))
                    item.Execute();
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

    /// <summary>
    /// One row of the Align submenu. Greyed out (rather than hidden) when the selection is too
    /// small, so the menu shape is stable and the requirement is discoverable.
    /// </summary>
    private void DrawAlignItem(string label, string commandId)
    {
        bool enabled = IsCommandEnabled(commandId);
        if (ImGui.MenuItem(label, null, false, enabled))
            _editorCommands?.Invoke(commandId);
        if (!enabled && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(commandId.Contains("distribute")
                ? "Select at least three nodes"
                : "Select at least two nodes");
    }

    /// <summary>
    /// True when a host has registered <paramref name="commandId"/> and it is currently enabled.
    /// Menu items for host-owned actions (BP-23a's clipboard set) grey out rather than disappear,
    /// so the canvas menu keeps a stable shape whichever host it is embedded in.
    /// </summary>
    private bool IsCommandEnabled(string commandId)
        => _editorCommands?.Get(commandId)?.IsEnabled() == true;

    private void OpenNodeRenameModal(NodeId nodeId, string currentTitle)
    {
        _pendingRenameNodeId = nodeId;
        _nodeRenameText      = currentTitle;
        _showNodeRenameModal = true;
    }

    /// <summary>
    /// BP-17 — the node-title prompt. Clearing the field restores the generated title rather than
    /// storing an empty header, so a rename is always reversible without undo.
    /// </summary>
    private void DrawNodeRenameModal(GraphView view)
    {
        if (_showNodeRenameModal)
        {
            ImGui.OpenPopup("##canvas_rename_node");
            _showNodeRenameModal = false;
        }

        bool open = true;
        if (!ImGui.BeginPopupModal("##canvas_rename_node", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        if (_pendingRenameNodeId is null)
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            return;
        }

        if (ImGui.IsWindowAppearing()) ImGui.SetKeyboardFocusHere();

        ImGui.TextDisabled("Node title");
        ImGui.SetNextItemWidth(240f);
        bool entered = ImGui.InputText("##node_title", ref _nodeRenameText, 128,
            ImGuiInputTextFlags.AutoSelectAll | ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.TextDisabled("Leave blank to restore the generated title.");

        ImGui.Separator();

        if (ImGui.Button("Rename", new Vector2(110, 0)) || entered)
        {
            var nodeId = _pendingRenameNodeId.Value;
            // The inverse needs the value the node had before this edit — the sink no longer
            // snapshots for the caller (BP-11).
            var previous = view.Model.FindNode(nodeId) is { } current && current.Subtitle is not null
                ? current.Title          // already renamed: the custom title is what shows
                : null;                  // generated title: the override was unset

            var next = string.IsNullOrWhiteSpace(_nodeRenameText) ? null : _nodeRenameText.Trim();
            view.Execute(
                new Core.Commands.GraphCommand.SetNodeProperty(nodeId, "Title", next),
                new Core.Commands.GraphCommand.SetNodeProperty(nodeId, "Title", previous),
                "Rename Node");

            _pendingRenameNodeId = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(110, 0)))
        {
            _pendingRenameNodeId = null;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
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

            // BP-60: prefer a host handler for editor.promote-to-variable when one is registered.
            //
            // Promotion is not a single primitive — it declares a variable, places a node and links
            // it — and GraphCommand.PromoteToVariable hides all three behind one opaque command
            // whose new-node id the sink allocates internally. That is why this site could not build
            // an inverse and was left on Commands.Apply by BP-02, and why in the Blueprint editor it
            // reached a sink with no case for it: the `default:` arm, which returns success and does
            // nothing. A host that composes the gesture from primitives owns every id in it and can
            // record one proper undo entry.
            //
            // The single-command path remains for hosts whose sink implements it directly
            // (NodeEditor.Demo's FakeCommandSink).
            var promoted = _editorCommands?.Invoke(
                CommandCatalog.PromoteToVariable,
                new EditorCommandContext(
                    ScreenPos: null,
                    CanvasPos: null,
                    Args: new Dictionary<string, object?>
                    {
                        ["pinId"]        = _pendingPromotePinId.Value,
                        ["name"]         = _promoteVariableName.Trim(),
                        ["isLocal"]      = _pendingPromoteIsLocal,
                        ["categoryPath"] = categoryPath,
                    }));

            if (promoted is not { Success: true })
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

