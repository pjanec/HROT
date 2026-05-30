using System.Linq;
using System.Numerics;
using ImGuiNET;
using NodeEditor.Core;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Spatial;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Canvas;

/// <summary>
/// Per-frame input handler. Reads from <see cref="IInputSource"/> and ImGui
/// hover/focus state to drive the <see cref="InteractionMode"/> state machine.
///
/// Interactions handled:
/// - Scroll-wheel zoom centred on cursor
/// - RMB drag panning
/// - LMB click/drag: select node, start drag, marquee, pending wire
/// - Release: commit moves, commit wire connection
/// - Delete key: remove selected elements
/// </summary>
internal sealed class CanvasInput
{
    /// <summary>
    /// Process one frame of input for the given view.
    /// Must be called after the canvas child window is active.
    /// </summary>
    public void Handle(GraphView view, bool isCanvasHovered, bool isCanvasBgActive, bool isCanvasDirectlyFocused, SpatialIndex spatialIndex)
    {
        bool canProcess = isCanvasHovered && (!ImGui.IsAnyItemActive() || isCanvasBgActive);

        var input = view.Host.Input;
        var mode  = view.Interaction.Mode;

        // ── Zoom ────────────────────────────────────────────────────────────
        if (canProcess && input.WheelDelta != 0f)
        {
            float factor = 1f + input.WheelDelta * 0.1f;
            view.Viewport.ZoomAt(input.MousePosition, factor);
        }

        // ── Per-mode dispatch ────────────────────────────────────────────────
        switch (mode)
        {
            case InteractionMode.Idle:
                HandleIdle(view, canProcess, isCanvasDirectlyFocused, input);
                break;

            case InteractionMode.Panning:
                HandlePanning(view, input);
                break;

            case InteractionMode.DraggingNodes:
                HandleDraggingNodes(view, input, spatialIndex);
                break;

            case InteractionMode.DraggingReroutes:
                HandleDraggingReroutes(view, input);
                break;

            case InteractionMode.DraggingComment:
                HandleDraggingComment(view, input);
                break;

            case InteractionMode.ResizingComment:
                HandleResizingComment(view, input);
                break;

            case InteractionMode.MarqueeSelecting:
                HandleMarquee(view, input);
                break;

            case InteractionMode.PendingWire:
                HandlePendingWire(view, input);
                break;
        }

        // ── Delete / Backspace ───────────────────────────────────────────────
        if (mode == InteractionMode.Idle && canProcess
            && (input.IsKeyPressed(EditorKey.Delete) || input.IsKeyPressed(EditorKey.Backspace)))
        {
            DeleteSelected(view);
        }
    }

    // ── Idle ──────────────────────────────────────────────────────────────────

    private static void HandleIdle(GraphView view, bool canProcess, bool isCanvasDirectlyFocused, IInputSource input)
    {
        var hover = view.Interaction.Hover;
        var modifiers = input.Modifiers;

        // Strict constraint: only open the picker if hovering empty canvas (HoverKind.None).
        // This prevents Tab/Space from opening the picker while navigating widgets or hovering nodes.
        if (canProcess && isCanvasDirectlyFocused && hover.Kind == HoverKind.None
            && (input.IsKeyPressed(EditorKey.Tab) || input.IsKeyPressed(EditorKey.Space)))
        {
            view.Interaction.Mode = InteractionMode.PickerOpen;
            var graphPos = view.Viewport.ScreenToGraph(input.MousePosition);
            view.Host.Pickers.Open(
                "nodes.all",
                input.MousePosition,
                pick =>
                {
                    if (pick is NodeCatalogEntry entry)
                    {
                        var cb = new CommandBuilder(view.Model);
                        var (fwd, inv) = cb.AddNode(entry.Kind, graphPos, null);
                        view.Execute(fwd, inv, "Add Node");
                    }
                    view.Interaction.ResetToIdle();
                },
                () => view.Interaction.ResetToIdle());
            return;
        }

        // Right-mouse → pan
        if (canProcess && input.IsMousePressed(MouseButton.Right))
        {
            view.Interaction.Mode = InteractionMode.Panning;
            view.Interaction.DragStartScreen = input.MousePosition;
            view.Interaction.DragThresholdCrossed = false;
            return;
        }

        // Left-mouse pressed
        if (canProcess && input.IsMousePressed(MouseButton.Left))
        {
            bool ctrl  = (modifiers & KeyModifiers.Ctrl)  != 0;
            bool shift = (modifiers & KeyModifiers.Shift) != 0;
            bool alt   = (modifiers & KeyModifiers.Alt)   != 0;

            switch (hover.Kind)
            {
                case HoverKind.Pin:
                    if (alt)
                    {
                        var linksToRemove = view.Model.Links
                            .Where(l => l.FromPin == hover.Pin || l.ToPin == hover.Pin)
                            .Select(l => l.Id)
                            .ToList();

                        if (linksToRemove.Count > 0)
                            view.Commands.Apply(new GraphCommand.RemoveLinks(linksToRemove));
                        return;
                    }

                    var pinModel = view.Model.FindPin(hover.Pin);
                    if (ctrl && pinModel?.Direction == PinDirection.Input)
                    {
                        var existingLink = view.Model.Links.FirstOrDefault(l => l.ToPin == hover.Pin);
                        if (existingLink != null)
                        {
                            view.Commands.Apply(new GraphCommand.RemoveLinks(new[] { existingLink.Id }));
                            view.Interaction.DragStartScreen = input.MousePosition;
                            view.Interaction.DragThresholdCrossed = false;
                            view.Interaction.Mode = InteractionMode.PendingWire;
                            view.Interaction.PendingWire = new PendingWire
                            {
                                SourcePin = existingLink.FromPin,
                                CursorGraph = view.Viewport.ScreenToGraph(input.MousePosition),
                            };
                            return;
                        }
                    }

                    // Start wire drag from pin
                    view.Interaction.DragStartScreen = input.MousePosition;
                    view.Interaction.DragThresholdCrossed = false;
                    view.Interaction.Mode = InteractionMode.PendingWire;
                    view.Interaction.PendingWire = new PendingWire
                    {
                        SourcePin = hover.Pin,
                        CursorGraph = view.Viewport.ScreenToGraph(input.MousePosition),
                    };
                    // Optionally pre-select the source node
                    break;

                case HoverKind.Node:
                    if (!ctrl && !shift && !view.Selection.Contains(SelectionEntry.OfNode(hover.Node)))
                        view.Selection.ReplaceWith(SelectionEntry.OfNode(hover.Node));
                    else if (ctrl)
                        view.Selection.Toggle(SelectionEntry.OfNode(hover.Node));
                    else if (shift)
                        view.Selection.Add(SelectionEntry.OfNode(hover.Node));

                    // Begin node drag
                    view.Interaction.Mode = InteractionMode.DraggingNodes;
                    view.Interaction.DragStartScreen = input.MousePosition;
                    view.Interaction.DragStartGraph  = view.Viewport.ScreenToGraph(input.MousePosition);
                    view.Interaction.DragThresholdCrossed = false;
                    // Snapshot positions
                    foreach (var nid in view.Selection.Nodes)
                    {
                        var n = view.Model.FindNode(nid);
                        if (n != null)
                            view.Interaction.DragOverridePositions[nid] = n.Position;
                    }
                    break;

                case HoverKind.Reroute:
                    view.Interaction.Mode = InteractionMode.DraggingReroutes;
                    view.Interaction.DragStartScreen = input.MousePosition;
                    view.Selection.ReplaceWith(SelectionEntry.OfReroute(hover.Reroute));
                    break;

                case HoverKind.Link:
                    if (alt)
                    {
                        view.Commands.Apply(new GraphCommand.RemoveLinks(new[] { hover.Link }));
                        return;
                    }

                    if (ctrl)
                    {
                        // Ctrl+click wire → insert reroute
                        var graphPos = view.Viewport.ScreenToGraph(input.MousePosition);
                        view.Commands.Apply(new GraphCommand.InsertReroute(hover.Link, graphPos));
                    }
                    else
                    {
                        view.Selection.ReplaceWith(SelectionEntry.OfLink(hover.Link));
                    }
                    break;

                case HoverKind.Comment:
                    if (hover.CommentZone == CommentHoverZone.ResizeHandle)
                    {
                        view.Interaction.Mode = InteractionMode.ResizingComment;
                        view.Interaction.DragStartScreen = input.MousePosition;
                        view.Selection.ReplaceWith(SelectionEntry.OfComment(hover.Comment));
                    }
                    else if (hover.CommentZone == CommentHoverZone.Header)
                    {
                        view.Interaction.Mode = InteractionMode.DraggingComment;
                        view.Interaction.DragStartScreen = input.MousePosition;
                        view.Selection.ReplaceWith(SelectionEntry.OfComment(hover.Comment));
                    }
                    break;

                case HoverKind.CustomElement:
                {
                    var ceRef = hover.CustomElement;
                    var entry = SelectionEntry.OfCustomElement(ceRef);
                    if (!ctrl && !shift)
                        view.Selection.ReplaceWith(entry);
                    else if (ctrl)
                        view.Selection.Toggle(entry);
                    else if (shift)
                        view.Selection.Add(entry);
                    // Notify the renderer if it implements ICustomCanvasSelectable.
                    foreach (var renderer in view.Host.CustomCanvasRenderers)
                    {
                        if (renderer.Id == ceRef.RendererId && renderer is ICustomCanvasSelectable sel)
                        {
                            // Re-run hit test to get the full CustomElementHit for the callback.
                            // (We only stored the ref in HoverInfo; the full hit is not cached.)
                            // Since the click-to-select already happened, notify with a minimal hit.
                            sel.OnElementSelected(ceRef.ElementKey, new CustomElementHit(ceRef.ElementKey, CustomElementKind.Standalone, default));
                            break;
                        }
                    }
                    break;
                }

                case HoverKind.None:
                    // Click on empty canvas → clear selection, start marquee
                    if (!ctrl && !shift)
                        view.Selection.Clear();
                    view.Interaction.Mode = InteractionMode.MarqueeSelecting;
                    view.Interaction.DragStartScreen = input.MousePosition;
                    view.Interaction.DragStartGraph  = view.Viewport.ScreenToGraph(input.MousePosition);
                    view.Interaction.MarqueeTouchMode = (modifiers & KeyModifiers.Alt) != 0;
                    break;
            }
        }
    }

    // ── Panning ───────────────────────────────────────────────────────────────

    private static void HandlePanning(GraphView view, IInputSource input)
    {
        var delta = input.MousePosition - view.Interaction.DragStartScreen;
        if (!view.Interaction.DragThresholdCrossed
            && delta.Length() > TimingConstants.DragThresholdPixels)
        {
            view.Interaction.DragThresholdCrossed = true;
        }

        if (input.MouseDelta != Vector2.Zero)
            view.Viewport.PanScreen(-input.MouseDelta);

        if (input.IsMouseReleased(MouseButton.Right))
        {
            bool wasDrag = view.Interaction.DragThresholdCrossed;
            var menuTarget = view.Interaction.Hover;
            view.Interaction.ResetToIdle();
            if (!wasDrag)
            {
                view.Interaction.ContextMenuScreen = input.MousePosition;
                view.Interaction.ContextMenuTarget = menuTarget;
            }
        }
    }

    // ── Dragging nodes ────────────────────────────────────────────────────────

    private static void HandleDraggingNodes(GraphView view, IInputSource input, SpatialIndex spatialIndex)
    {
        var delta = input.MousePosition - view.Interaction.DragStartScreen;

        if (!view.Interaction.DragThresholdCrossed
            && delta.Length() > TimingConstants.DragThresholdPixels)
        {
            view.Interaction.DragThresholdCrossed = true;
        }

        if (view.Interaction.DragThresholdCrossed)
        {
            var deltaGraph = delta / view.Viewport.Zoom;
            foreach (var nid in view.Selection.Nodes)
            {
                var n = view.Model.FindNode(nid);
                if (n == null) continue;
                // Use canvas-absolute position as the override so the renderer can
                // place dragged container-children correctly regardless of parent.
                var canvasPos = view.NodeCanvasPosition(nid);
                view.Interaction.DragOverridePositions[nid] = canvasPos + deltaGraph;
            }

            // Compute which container the cursor is over (drop target for reparenting).
            UpdateContainerDropTarget(view, input, spatialIndex);
        }

        if (input.IsMouseReleased(MouseButton.Left))
        {
            if (view.Interaction.DragThresholdCrossed && view.Interaction.DragOverridePositions.Count > 0)
                CommitNodeDrop(view, input);
            view.Interaction.ResetToIdle();
        }
    }

    // Determines the container drop target and stores it in InteractionState.
    private static void UpdateContainerDropTarget(GraphView view, IInputSource input, SpatialIndex spatialIndex)
    {
        var mouseGraph = view.Viewport.ScreenToGraph(input.MousePosition);
        var draggedSet = view.Selection.Nodes.ToHashSet();

        // Find the smallest-area (innermost) container that contains the mouse
        // and is not being dragged itself.
        NodeId? best = null;
        float  bestArea = float.MaxValue;
        foreach (var node in view.Model.Nodes)
        {
            if (node.AsContainer() is null) continue;
            if (draggedSet.Contains(node.Id)) continue;

            var bounds = spatialIndex.GetBounds(node.Id);
            if (!bounds.HasValue) continue;
            if (!bounds.Value.Contains(mouseGraph)) continue;
            // Exclude clicks on the header zone (dropping onto the header selects the container, not its interior).
            if (mouseGraph.Y < bounds.Value.Min.Y + view.Host.Theme.NodeHeaderHeight) continue;

            float area = bounds.Value.Size.X * bounds.Value.Size.Y;
            if (area < bestArea)
            {
                bestArea = area;
                best = node.Id;
            }
        }

        // Cycle check: reject if any dragged node is an ancestor of the target.
        bool cycleDetected = best.HasValue
            && ContainerCycleDetector.WouldCreateCycleAny(draggedSet, best.Value, view.Model);

        view.Interaction.DropTargetContainerId  = cycleDetected ? null : best;
        view.Interaction.DropTargetCycleDetected = cycleDetected;
    }

    // Commits the drop: emits ChangeParent if reparenting occurred, else MoveNodes.
    private static void CommitNodeDrop(GraphView view, IInputSource input)
    {
        var newParentId = view.Interaction.DropTargetContainerId;
        var moves = new List<(NodeId Id, Vector2 NewPos)>();
        var changeParents = new List<ChangeParentMove>();
        var inverseChangeParents = new List<ChangeParentMove>();

        foreach (var nid in view.Selection.Nodes)
        {
            var n = view.Model.FindNode(nid);
            if (n == null) continue;

            bool reparenting = n.ParentContainerId != newParentId;
            var  canvasPos   = view.Interaction.DragOverridePositions.TryGetValue(nid, out var ovr)
                ? ovr
                : view.NodeCanvasPosition(nid);

            if (reparenting)
            {
                // Compute position relative to the new parent's interior origin.
                Vector2 newLocalPos;
                if (newParentId.HasValue)
                {
                    var container = view.Model.FindNode(newParentId.Value)?.AsContainer();
                    if (container != null)
                    {
                        var containerCanvas = view.NodeCanvasPosition(newParentId.Value);
                        var interiorOrigin  = containerCanvas + new Vector2(
                            container.Padding.Left,
                            view.Host.Theme.NodeHeaderHeight + container.Padding.Top);
                        newLocalPos = canvasPos - interiorOrigin;
                    }
                    else
                    {
                        newLocalPos = canvasPos;
                    }
                }
                else
                {
                    newLocalPos = canvasPos; // dropping to root: position is canvas-absolute
                }
                changeParents.Add(new ChangeParentMove(nid, newParentId, null, newLocalPos));
                inverseChangeParents.Add(new ChangeParentMove(nid, n.ParentContainerId, null, n.Position));
            }
            else if (n.ParentContainerId == null)
            {
                // Root-level node staying at root: regular move.
                moves.Add((nid, canvasPos));
            }
            else
            {
                // Container child staying in same parent: move within parent.
                // Compute parent-local position from canvas-absolute override.
                var container = view.Model.FindNode(n.ParentContainerId.Value)?.AsContainer();
                if (container != null)
                {
                    var containerCanvas = view.NodeCanvasPosition(n.ParentContainerId.Value);
                    var interiorOrigin  = containerCanvas + new Vector2(
                        container.Padding.Left,
                        view.Host.Theme.NodeHeaderHeight + container.Padding.Top);
                    var localPos = canvasPos - interiorOrigin;
                    changeParents.Add(new ChangeParentMove(nid, n.ParentContainerId, null, localPos));
                    inverseChangeParents.Add(new ChangeParentMove(nid, n.ParentContainerId, null, n.Position));
                }
            }
        }

        var forwards = new List<GraphCommand>();
        var inverses = new List<GraphCommand>();

        if (moves.Count > 0)
        {
            var cb = new CommandBuilder(view.Model);
            var (f, i) = cb.MoveNodes(moves);
            forwards.Add(f);
            inverses.Add(i);
        }

        if (changeParents.Count > 0)
        {
            forwards.Add(new GraphCommand.ChangeParentMultiple(changeParents));
            inverses.Add(new GraphCommand.ChangeParentMultiple(inverseChangeParents));
        }

        if (forwards.Count == 1)
        {
            view.Execute(forwards[0], inverses[0], "Move Nodes");
        }
        else if (forwards.Count > 1)
        {
            inverses.Reverse();
            view.Execute(
                new GraphCommand.Batch("Move Nodes", forwards),
                new GraphCommand.Batch("Move Nodes", inverses),
                "Move Nodes");
        }
    }

    // ── Dragging reroutes ─────────────────────────────────────────────────────

    private static void HandleDraggingReroutes(GraphView view, IInputSource input)
    {
        if (input.IsMouseReleased(MouseButton.Left))
        {
            // Commit reroute move
            foreach (var reroute in view.Selection.Reroutes)
            {
                var graphPos = view.Viewport.ScreenToGraph(input.MousePosition);
                view.Commands.Apply(new GraphCommand.MoveReroute(reroute.LinkId, reroute.WaypointIndex, graphPos));
            }
            view.Interaction.ResetToIdle();
        }
        else if (view.Interaction.DragThresholdCrossed || input.MouseDelta.Length() > 0)
        {
            view.Interaction.DragThresholdCrossed = true;
        }
    }

    // ── Dragging comment ──────────────────────────────────────────────────────

    private static void HandleDraggingComment(GraphView view, IInputSource input)
    {
        if (input.IsMouseReleased(MouseButton.Left))
        {
            var delta = view.Viewport.ScreenToGraph(input.MousePosition)
                      - view.Viewport.ScreenToGraph(view.Interaction.DragStartScreen);

            foreach (var cid in view.Selection.Comments)
            {
                var comment = view.Model.Comments.FirstOrDefault(c => c.Id == cid);
                if (comment == null) continue;
                var newPos = comment.Position + delta;
                view.Commands.Apply(new GraphCommand.UpdateComment(cid, null, newPos, null, null, null, null));
            }
            view.Interaction.ResetToIdle();
        }
    }

    // ── Resizing comment ──────────────────────────────────────────────────────

    private static void HandleResizingComment(GraphView view, IInputSource input)
    {
        if (input.IsMouseReleased(MouseButton.Left))
        {
            var graphPos = view.Viewport.ScreenToGraph(input.MousePosition);
            foreach (var cid in view.Selection.Comments)
            {
                var comment = view.Model.Comments.FirstOrDefault(c => c.Id == cid);
                if (comment == null) continue;
                var newSize = graphPos - comment.Position;
                newSize = Vector2.Max(newSize, new Vector2(80, 40));
                view.Commands.Apply(new GraphCommand.UpdateComment(cid, null, null, newSize, null, null, null));
            }
            view.Interaction.ResetToIdle();
        }
    }

    // ── Marquee ───────────────────────────────────────────────────────────────

    private static void HandleMarquee(GraphView view, IInputSource input)
    {
        var startGraph = view.Interaction.DragStartGraph;
        var currentGraph = view.Viewport.ScreenToGraph(input.MousePosition);
        var marquee = RectF.FromMinMax(
            Vector2.Min(startGraph, currentGraph),
            Vector2.Max(startGraph, currentGraph));
        view.Interaction.MarqueeGraph = marquee;

        if (input.IsMouseReleased(MouseButton.Left))
        {
            // Select nodes inside marquee
            if (view.Interaction.MarqueeTouchMode)
            {
                // Touch mode: any intersection
                var hits = view.Model.Nodes
                    .Where(n => marquee.Intersects(new RectF(n.Position, n.SizeOverride ?? new Vector2(160, 80))))
                    .Select(n => SelectionEntry.OfNode(n.Id));
                view.Selection.ReplaceWith(hits);
            }
            else
            {
                // Enclosed mode
                var hits = view.Model.Nodes
                    .Where(n => marquee.FullyContains(new RectF(n.Position, n.SizeOverride ?? new Vector2(160, 80))))
                    .Select(n => SelectionEntry.OfNode(n.Id));
                view.Selection.ReplaceWith(hits);
            }
            view.Interaction.ResetToIdle();
        }
    }

    // ── Pending wire ──────────────────────────────────────────────────────────

    private static void HandlePendingWire(GraphView view, IInputSource input)
    {
        var pw = view.Interaction.PendingWire;
        if (pw == null) { view.Interaction.ResetToIdle(); return; }

        var delta = input.MousePosition - view.Interaction.DragStartScreen;
        if (!view.Interaction.DragThresholdCrossed
            && delta.Length() > TimingConstants.DragThresholdPixels)
        {
            view.Interaction.DragThresholdCrossed = true;
        }

        pw.CursorGraph = view.Viewport.ScreenToGraph(input.MousePosition);

        // Check for candidate pin under cursor
        pw.CandidateTarget = null;
        pw.CandidateValid = false;
        pw.CandidateNeedsCast = false;

        var hover = view.Interaction.Hover;
        if (hover.Kind == HoverKind.Pin && hover.Pin != pw.SourcePin)
        {
            var result = view.Validator.Validate(pw.SourcePin, hover.Pin);
            if (result.Verdict != LinkValidity.Invalid)
            {
                pw.CandidateTarget = hover.Pin;
                pw.CandidateValid = true;
                pw.CandidateNeedsCast = result.Verdict == LinkValidity.ValidWithCast;
            }
        }

        if (input.IsMouseReleased(MouseButton.Left))
        {
            var dropHover = view.Interaction.Hover;
            var cb = new CommandBuilder(view.Model);

            if (pw.CandidateTarget.HasValue && pw.CandidateValid)
            {
                var (fwd, inv) = cb.AddLink(pw.SourcePin, pw.CandidateTarget.Value);
                view.Execute(fwd, inv, "Connect Pins");
                view.Interaction.ResetToIdle();
            }
            else if (dropHover.Kind == HoverKind.Pin)
            {
                // Dropped on an invalid pin (including source pin): silent abort.
                view.Interaction.ResetToIdle();
            }
            else if (dropHover.Kind == HoverKind.Node)
            {
                var node = view.Model.FindNode(dropHover.Node);
                var srcPin = view.Model.FindPin(pw.SourcePin);

                if (node != null && srcPin != null)
                {
                    var compatiblePin = node.Pins.FirstOrDefault(p =>
                        p.Id != pw.SourcePin
                        && view.Validator.Validate(pw.SourcePin, p.Id).Verdict != LinkValidity.Invalid);

                    if (compatiblePin != null)
                    {
                        var fromId = srcPin.Direction == PinDirection.Output ? srcPin.Id : compatiblePin.Id;
                        var toId = srcPin.Direction == PinDirection.Output ? compatiblePin.Id : srcPin.Id;
                        var (fwd, inv) = cb.AddLink(fromId, toId);
                        view.Execute(fwd, inv, "Connect Pins");
                    }
                }

                view.Interaction.ResetToIdle();
            }
            else if (dropHover.Kind == HoverKind.None && view.Interaction.DragThresholdCrossed)
            {
                // Dropped on empty canvas: suspend canvas input and open contextual picker.
                view.Interaction.Mode = InteractionMode.PickerOpen;
                var srcPin = view.Model.FindPin(pw.SourcePin);

                var context = new Dictionary<string, object?>
                {
                    ["sourcePinId"] = pw.SourcePin,
                    ["cursorGraph"] = pw.CursorGraph,
                    ["sourceDirection"] = srcPin?.Direction,
                    ["sourceKind"] = srcPin?.Kind,
                    ["sourceType"] = srcPin?.Type
                };

                view.Host.Pickers.Open(
                    "nodes.by-pin",
                    input.MousePosition,
                    pick =>
                    {
                        if (pick is NodeCatalogEntry entry)
                        {
                            var srcPinModel = view.Model.FindPin(pw.SourcePin);
                            if (srcPinModel != null)
                            {
                                // 1. Pre-generate Pin IDs so they remain stable across Undo/Redo
                                var pinIds = new List<PinId>();
                                int totalPins = entry.Inputs.Count + entry.Outputs.Count;
                                for (int i = 0; i < totalPins; i++)
                                {
                                    pinIds.Add(IdGenerator.NewPinId());
                                }

                                var props = new Dictionary<string, object?> { ["PinIds"] = pinIds };
                                var newNodeId = IdGenerator.NewNodeId();

                                var nodeFwd = new GraphCommand.AddNode(newNodeId, entry.Kind, pw.CursorGraph, props);
                                var nodeInv = new GraphCommand.RemoveNodes(new[] { newNodeId });

                                var fwds = new List<GraphCommand> { nodeFwd };
                                var invs = new List<GraphCommand> { nodeInv };

                                // 2. Find a compatible pin using the catalog entry signatures
                                var targetDir = srcPinModel.Direction == PinDirection.Output ? PinDirection.Input : PinDirection.Output;
                                PinId? compatiblePinId = null;
                                int pinIdx = 0;

                                foreach (var sig in entry.Inputs)
                                {
                                    if (targetDir == PinDirection.Input && sig.Kind == srcPinModel.Kind &&
                                        (srcPinModel.Kind == PinKind.Exec || sig.Type == srcPinModel.Type))
                                    {
                                        compatiblePinId = pinIds[pinIdx];
                                        break;
                                    }
                                    pinIdx++;
                                }

                                if (compatiblePinId == null)
                                {
                                    foreach (var sig in entry.Outputs)
                                    {
                                        if (targetDir == PinDirection.Output && sig.Kind == srcPinModel.Kind &&
                                            (srcPinModel.Kind == PinKind.Exec || sig.Type == srcPinModel.Type))
                                        {
                                            compatiblePinId = pinIds[pinIdx];
                                            break;
                                        }
                                        pinIdx++;
                                    }
                                }

                                // 3. Form the link command targeting the deterministic PinId
                                if (compatiblePinId.HasValue)
                                {
                                    var linkId = IdGenerator.NewLinkId();
                                    var fromId = srcPinModel.Direction == PinDirection.Output ? srcPinModel.Id : compatiblePinId.Value;
                                    var toId   = srcPinModel.Direction == PinDirection.Output ? compatiblePinId.Value : srcPinModel.Id;

                                    fwds.Add(new GraphCommand.AddLink(linkId, fromId, toId));
                                    invs.Add(new GraphCommand.RemoveLinks(new[] { linkId }));
                                }

                                // 4. Execute as a single atomic batch (inverses must be reversed)
                                invs.Reverse();
                                var batchFwd = new GraphCommand.Batch("Add Node", fwds);
                                var batchInv = new GraphCommand.Batch("Add Node", invs);

                                view.Execute(batchFwd, batchInv, "Add Node");
                            }
                        }
                        view.Interaction.ResetToIdle();
                    },
                    () => view.Interaction.ResetToIdle(),
                    context);
            }
            else
            {
                // Empty-canvas click without drag, or drop on wire/reroute/comment: silent abort.
                view.Interaction.ResetToIdle();
            }
        }
        else if (input.IsMouseReleased(MouseButton.Right))
        {
            view.Interaction.ResetToIdle();
        }
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    private static void DeleteSelected(GraphView view)
    {
        var sel = view.Selection;
        if (sel.IsEmpty) return;

        var cmds = new List<GraphCommand>();

        var links = sel.Links.ToList();
        if (links.Count > 0) cmds.Add(new GraphCommand.RemoveLinks(links));

        var nodes = sel.Nodes.ToList();
        if (nodes.Count > 0) cmds.Add(new GraphCommand.RemoveNodes(nodes));

        var comments = sel.Comments.ToList();
        foreach (var c in comments) cmds.Add(new GraphCommand.RemoveComment(c));

        var reroutes = sel.Reroutes.ToList();
        foreach (var r in reroutes) cmds.Add(new GraphCommand.RemoveReroute(r.LinkId, r.WaypointIndex));

        if (cmds.Count > 0)
        {
            var batch = new GraphCommand.Batch("Delete", cmds);
            view.Commands.Apply(batch);
        }

        sel.Clear();
    }
}
