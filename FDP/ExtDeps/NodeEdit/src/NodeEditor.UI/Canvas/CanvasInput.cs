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
    public void Handle(GraphView view, bool isCanvasHovered, bool isCanvasBgActive, bool isNodeBgActive, bool isCanvasDirectlyFocused, SpatialIndex spatialIndex)
    {
        // Allow canvas processing if either the canvas itself OR a node background was clicked.
        bool canProcess = isCanvasHovered && (!ImGui.IsAnyItemActive() || isCanvasBgActive || isNodeBgActive);

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
                            .ToList();

                        if (linksToRemove.Count > 0)
                        {
                            var fwds = new List<GraphCommand>();
                            var invs = new List<GraphCommand>();
                            fwds.Add(new GraphCommand.RemoveLinks(linksToRemove.Select(l => l.Id).ToList()));
                            foreach (var l in linksToRemove)
                            {
                                invs.Add(new GraphCommand.AddLink(l.Id, l.FromPin, l.ToPin));
                            }
                            invs.Reverse();

                            view.Execute(
                                new GraphCommand.Batch("Break Links", fwds),
                                new GraphCommand.Batch("Break Links", invs),
                                "Break Links");
                        }
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
                            view.Interaction.DragOverridePositions[nid] = view.NodeCanvasPosition(nid);
                    }
                    break;

                case HoverKind.Reroute:
                    view.Interaction.Mode = InteractionMode.DraggingReroutes;
                    view.Interaction.DragStartScreen = input.MousePosition;
                    view.Selection.ReplaceWith(SelectionEntry.OfReroute(hover.Reroute));
                    break;

                case HoverKind.Container:
                    if (hover.ContainerZone == ContainerHoverZone.CollapseArrow)
                    {
                        var containerNode = view.Model.FindNode(hover.Node);
                        if (containerNode != null)
                        {
                            var fwd = new GraphCommand.SetContainerCollapsed(hover.Node, !containerNode.IsCollapsed);
                            var inv = new GraphCommand.SetContainerCollapsed(hover.Node, containerNode.IsCollapsed);
                            view.Execute(fwd, inv, "Toggle Container Collapse");
                        }
                    }
                    else if (hover.ContainerZone == ContainerHoverZone.Header)
                    {
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
                        foreach (var nid in view.Selection.Nodes)
                        {
                            var n = view.Model.FindNode(nid);
                            if (n != null)
                                view.Interaction.DragOverridePositions[nid] = view.NodeCanvasPosition(nid);
                        }
                    }
                    else if (hover.ContainerZone == ContainerHoverZone.Interior)
                    {
                        // Treat clicking the interior like empty canvas to allow marquee selection.
                        if (!ctrl && !shift)
                            view.Selection.Clear();
                        view.Interaction.Mode = InteractionMode.MarqueeSelecting;
                        view.Interaction.DragStartScreen = input.MousePosition;
                        view.Interaction.DragStartGraph  = view.Viewport.ScreenToGraph(input.MousePosition);
                        view.Interaction.MarqueeTouchMode = alt;
                    }
                    break;

                case HoverKind.Link:
                    if (alt)
                    {
                        var link = view.Model.FindLink(hover.Link);
                        if (link != null)
                        {
                            var fwd = new GraphCommand.RemoveLinks(new[] { hover.Link });
                            var inv = new GraphCommand.AddLink(link.Id, link.FromPin, link.ToPin);
                            view.Execute(fwd, inv, "Break Link");
                        }
                        return;
                    }

                    if (ctrl || input.IsMouseDoubleClicked(MouseButton.Left))
                    {
                        // Ctrl+click or double-click wire → insert reroute
                        var graphPos = view.Viewport.ScreenToGraph(input.MousePosition);
                        var link = view.Model.FindLink(hover.Link);
                        if (link != null)
                        {
                            var fwd = new GraphCommand.InsertReroute(hover.Link, graphPos);
                            var inv = new GraphCommand.RemoveReroute(hover.Link, link.Waypoints.Count);
                            view.Execute(fwd, inv, "Insert Reroute");
                        }
                    }
                    else
                    {
                        view.Selection.ReplaceWith(SelectionEntry.OfLink(hover.Link));
                    }
                    break;

                case HoverKind.Comment:
                    if (input.IsMouseDoubleClicked(MouseButton.Left) && hover.CommentZone == CommentHoverZone.Header)
                    {
                        view.Interaction.RenamingComment = hover.Comment;
                        view.Selection.ReplaceWith(SelectionEntry.OfComment(hover.Comment));
                    }
                    else if (hover.CommentZone == CommentHoverZone.ResizeHandle)
                    {
                        view.Interaction.Mode = InteractionMode.ResizingComment;
                        view.Interaction.DragStartScreen = input.MousePosition;
                        view.Interaction.ActiveCommentResizeHandle = hover.CommentResizeHandle;
                        view.Selection.ReplaceWith(SelectionEntry.OfComment(hover.Comment));
                    }
                    else if (hover.CommentZone == CommentHoverZone.Header)
                    {
                        // Prevent drag interception when the active rename box is being clicked.
                        if (view.Interaction.RenamingComment != hover.Comment)
                        {
                            view.Interaction.Mode = InteractionMode.DraggingComment;
                            view.Interaction.DragStartScreen = input.MousePosition;
                            view.Selection.ReplaceWith(SelectionEntry.OfComment(hover.Comment));

                            // Snapshot fully enclosed nodes for the "Move with contents" behavior
                            view.Interaction.CommentDragContents.Clear();
                            var comment = view.Model.Comments.FirstOrDefault(c => c.Id == hover.Comment);
                            if (comment != null && comment.MoveWithContents)
                            {
                                var commentRect = new RectF(comment.Position, comment.Size);
                                foreach (var node in view.Model.Nodes)
                                {
                                    var nodeBounds = new RectF(node.Position, node.SizeOverride ?? new Vector2(160, 64));
                                    if (commentRect.FullyContains(nodeBounds))
                                    {
                                        view.Interaction.CommentDragContents.Add(node.Id);
                                    }
                                }
                            }
                        }
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
        var finalLocalPositions = new Dictionary<NodeId, Vector2>();
        var targetParents = new Dictionary<NodeId, NodeId?>();
        var affectedContainers = new HashSet<NodeId>();

        foreach (var nid in view.Selection.Nodes)
        {
            var n = view.Model.FindNode(nid);
            if (n == null) continue;

            if (n.ParentContainerId.HasValue) affectedContainers.Add(n.ParentContainerId.Value);
            if (newParentId.HasValue) affectedContainers.Add(newParentId.Value);

            var canvasPos = view.Interaction.DragOverridePositions.TryGetValue(nid, out var ovr)
                ? ovr
                : view.NodeCanvasPosition(nid);

            bool reparenting = n.ParentContainerId != newParentId;
            NodeId? targetContainer = reparenting ? newParentId : n.ParentContainerId;
            targetParents[nid] = targetContainer;

            Vector2 rawLocalPos = canvasPos;
            if (targetContainer.HasValue)
            {
                var container = view.Model.FindNode(targetContainer.Value)?.AsContainer();
                if (container != null)
                {
                    var containerCanvas = view.Interaction.DragOverridePositions.TryGetValue(targetContainer.Value, out var cOvr)
                        ? cOvr
                        : view.NodeCanvasPosition(targetContainer.Value);

                    var interiorOrigin  = containerCanvas + new Vector2(
                        container.Padding.Left,
                        view.Host.Theme.NodeHeaderHeight + container.Padding.Top);
                    rawLocalPos = canvasPos - interiorOrigin;
                }
            }
            finalLocalPositions[nid] = rawLocalPos;
        }

        foreach (var cid in affectedContainers)
        {
            var containerNode = view.Model.FindNode(cid);
            if (containerNode?.AsContainer() is not { } containerModel) continue;

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            bool hasChildren = false;

            var futureChildren = new Dictionary<NodeId, Vector2>();

            foreach (var childId in containerModel.ChildNodeIds)
            {
                if (view.Selection.Nodes.Contains(childId)) continue;
                var childNode = view.Model.FindNode(childId);
                if (childNode != null)
                {
                    var pos = finalLocalPositions.TryGetValue(childId, out var fPos) ? fPos : childNode.Position;
                    futureChildren[childId] = pos;
                    minX = Math.Min(minX, pos.X);
                    minY = Math.Min(minY, pos.Y);
                    hasChildren = true;
                }
            }

            foreach (var nid in view.Selection.Nodes)
            {
                if (targetParents[nid] == cid)
                {
                    var pos = finalLocalPositions[nid];
                    futureChildren[nid] = pos;
                    minX = Math.Min(minX, pos.X);
                    minY = Math.Min(minY, pos.Y);
                    hasChildren = true;
                }
            }

            if (hasChildren && (Math.Abs(minX) > 0.01f || Math.Abs(minY) > 0.01f))
            {
                var shift = new Vector2(minX, minY);

                foreach (var childKvp in futureChildren)
                {
                    finalLocalPositions[childKvp.Key] = childKvp.Value - shift;
                    if (!targetParents.ContainsKey(childKvp.Key))
                        targetParents[childKvp.Key] = cid;
                }

                var currentContainerPos = finalLocalPositions.TryGetValue(cid, out var cPos)
                    ? cPos
                    : containerNode.Position;
                finalLocalPositions[cid] = currentContainerPos + shift;

                if (!targetParents.ContainsKey(cid))
                    targetParents[cid] = containerNode.ParentContainerId;
            }
        }

        var moves = new List<(NodeId Id, Vector2 NewPos)>();
        var changeParents = new List<ChangeParentMove>();
        var inverseChangeParents = new List<ChangeParentMove>();

        foreach (var kvp in finalLocalPositions)
        {
            var nid = kvp.Key;
            var newLocalPos = kvp.Value;

            newLocalPos.X = (float)Math.Round(newLocalPos.X, 2);
            newLocalPos.Y = (float)Math.Round(newLocalPos.Y, 2);

            var n = view.Model.FindNode(nid);
            if (n == null) continue;

            var newParent = targetParents[nid];
            bool reparenting = n.ParentContainerId != newParent;

            if (!reparenting && Vector2.DistanceSquared(n.Position, newLocalPos) < 0.01f)
                continue;

            if (reparenting)
            {
                changeParents.Add(new ChangeParentMove(nid, newParent, null, newLocalPos));
                inverseChangeParents.Add(new ChangeParentMove(nid, n.ParentContainerId, null, n.Position));
            }
            else if (n.ParentContainerId == null)
            {
                moves.Add((nid, newLocalPos));
            }
            else
            {
                changeParents.Add(new ChangeParentMove(nid, n.ParentContainerId, null, newLocalPos));
                inverseChangeParents.Add(new ChangeParentMove(nid, n.ParentContainerId, null, n.Position));
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
        var delta = input.MousePosition - view.Interaction.DragStartScreen;

        if (!view.Interaction.DragThresholdCrossed && delta.Length() > TimingConstants.DragThresholdPixels)
        {
            view.Interaction.DragThresholdCrossed = true;
        }

        if (view.Interaction.DragThresholdCrossed)
        {
            var deltaGraph = delta / view.Viewport.Zoom;
            foreach (var reroute in view.Selection.Reroutes)
            {
                var link = view.Model.FindLink(reroute.LinkId);
                if (link != null && reroute.WaypointIndex >= 0 && reroute.WaypointIndex < link.Waypoints.Count)
                {
                    var startPos = link.Waypoints[reroute.WaypointIndex];
                    view.Interaction.RerouteDragOverridePositions[reroute] = startPos + deltaGraph;
                }
            }
        }

        if (input.IsMouseReleased(MouseButton.Left))
        {
            if (view.Interaction.DragThresholdCrossed && view.Interaction.RerouteDragOverridePositions.Count > 0)
            {
                var fwds = new List<GraphCommand>();
                var invs = new List<GraphCommand>();

                foreach (var kvp in view.Interaction.RerouteDragOverridePositions)
                {
                    var link = view.Model.FindLink(kvp.Key.LinkId);
                    if (link != null && kvp.Key.WaypointIndex >= 0 && kvp.Key.WaypointIndex < link.Waypoints.Count)
                    {
                        var oldPos = link.Waypoints[kvp.Key.WaypointIndex];
                        fwds.Add(new GraphCommand.MoveReroute(kvp.Key.LinkId, kvp.Key.WaypointIndex, kvp.Value));
                        invs.Add(new GraphCommand.MoveReroute(kvp.Key.LinkId, kvp.Key.WaypointIndex, oldPos));
                    }
                }

                if (fwds.Count == 1)
                {
                    view.Execute(fwds[0], invs[0], "Move Reroute");
                }
                else if (fwds.Count > 1)
                {
                    invs.Reverse();
                    view.Execute(
                        new GraphCommand.Batch("Move Reroutes", fwds),
                        new GraphCommand.Batch("Move Reroutes", invs),
                        "Move Reroutes");
                }
            }

            view.Interaction.ResetToIdle();
        }
    }

    // ── Dragging comment ──────────────────────────────────────────────────────

    private static void HandleDraggingComment(GraphView view, IInputSource input)
    {
        var delta = input.MousePosition - view.Interaction.DragStartScreen;

        if (!view.Interaction.DragThresholdCrossed && delta.Length() > TimingConstants.DragThresholdPixels)
        {
            view.Interaction.DragThresholdCrossed = true;
        }

        if (view.Interaction.DragThresholdCrossed)
        {
            var deltaGraph = delta / view.Viewport.Zoom;
            bool moveComment = (input.Modifiers & KeyModifiers.Alt) == 0;
            bool moveContents = (input.Modifiers & KeyModifiers.Shift) == 0;

            foreach (var cid in view.Selection.Comments)
            {
                var comment = view.Model.Comments.FirstOrDefault(c => c.Id == cid);
                if (comment == null) continue;

                if (moveComment)
                {
                    view.Interaction.CommentDragOverridePositions[cid] = comment.Position + deltaGraph;
                }

                if (moveContents && comment.MoveWithContents)
                {
                    foreach (var nid in view.Interaction.CommentDragContents)
                    {
                        var node = view.Model.FindNode(nid);
                        if (node != null)
                        {
                            view.Interaction.DragOverridePositions[nid] = node.Position + deltaGraph;
                        }
                    }
                }
            }
        }

        if (input.IsMouseReleased(MouseButton.Left))
        {
            if (view.Interaction.DragThresholdCrossed)
            {
                var deltaGraph = delta / view.Viewport.Zoom;
                bool moveComment = (input.Modifiers & KeyModifiers.Alt) == 0;
                bool moveContents = (input.Modifiers & KeyModifiers.Shift) == 0;

                var fwds = new List<GraphCommand>();
                var invs = new List<GraphCommand>();

                foreach (var cid in view.Selection.Comments)
                {
                    var comment = view.Model.Comments.FirstOrDefault(c => c.Id == cid);
                    if (comment == null) continue;

                    if (moveComment)
                    {
                        var newPos = comment.Position + deltaGraph;
                        fwds.Add(new GraphCommand.UpdateComment(cid, null, newPos, null, null, null, null));
                        invs.Add(new GraphCommand.UpdateComment(cid, null, comment.Position, null, null, null, null));
                    }

                    if (moveContents && comment.MoveWithContents && view.Interaction.CommentDragContents.Count > 0)
                    {
                        var nodeMovesFwd = new List<NodeMove>();
                        var nodeMovesInv = new List<NodeMove>();
                        foreach (var nid in view.Interaction.CommentDragContents)
                        {
                            var node = view.Model.FindNode(nid);
                            if (node != null)
                            {
                                nodeMovesFwd.Add(new NodeMove(nid, node.Position + deltaGraph));
                                nodeMovesInv.Add(new NodeMove(nid, node.Position));
                            }
                        }
                        if (nodeMovesFwd.Count > 0)
                        {
                            fwds.Add(new GraphCommand.MoveNodes(nodeMovesFwd));
                            invs.Add(new GraphCommand.MoveNodes(nodeMovesInv));
                        }
                    }
                }

                if (fwds.Count > 0)
                {
                    invs.Reverse();
                    view.Execute(
                        new GraphCommand.Batch("Move Comment", fwds),
                        new GraphCommand.Batch("Move Comment", invs),
                        "Move Comment");
                }
            }

            view.Interaction.ResetToIdle();
        }
    }

    // ── Resizing comment ──────────────────────────────────────────────────────

    private static void HandleResizingComment(GraphView view, IInputSource input)
    {
        int handle = view.Interaction.ActiveCommentResizeHandle;
        if (handle < 0) return;

        var deltaGraph = (input.MousePosition - view.Interaction.DragStartScreen) / view.Viewport.Zoom;

        bool modLeft   = handle == 0 || handle == 3 || handle == 5;
        bool modRight  = handle == 2 || handle == 4 || handle == 7;
        bool modTop    = handle == 0 || handle == 1 || handle == 2;
        bool modBottom = handle == 5 || handle == 6 || handle == 7;

        foreach (var cid in view.Selection.Comments)
        {
            var comment = view.Model.Comments.FirstOrDefault(c => c.Id == cid);
            if (comment == null) continue;

            var newPos = comment.Position;
            var newSize = comment.Size;

            if (modLeft)
            {
                newPos.X += deltaGraph.X;
                newSize.X -= deltaGraph.X;
                if (newSize.X < 80f) { newPos.X -= (80f - newSize.X); newSize.X = 80f; }
            }
            else if (modRight)
            {
                newSize.X += deltaGraph.X;
                if (newSize.X < 80f) newSize.X = 80f;
            }

            if (modTop)
            {
                newPos.Y += deltaGraph.Y;
                newSize.Y -= deltaGraph.Y;
                if (newSize.Y < 40f) { newPos.Y -= (40f - newSize.Y); newSize.Y = 40f; }
            }
            else if (modBottom)
            {
                newSize.Y += deltaGraph.Y;
                if (newSize.Y < 40f) newSize.Y = 40f;
            }

            view.Interaction.CommentDragOverridePositions[cid] = newPos;
            view.Interaction.CommentSizeOverrides[cid] = newSize;
        }

        if (input.IsMouseReleased(MouseButton.Left))
        {
            var fwds = new List<GraphCommand>();
            var invs = new List<GraphCommand>();

            foreach (var cid in view.Selection.Comments)
            {
                var comment = view.Model.Comments.FirstOrDefault(c => c.Id == cid);
                if (comment == null) continue;

                if (view.Interaction.CommentSizeOverrides.TryGetValue(cid, out var finalSize) &&
                    view.Interaction.CommentDragOverridePositions.TryGetValue(cid, out var finalPos))
                {
                    fwds.Add(new GraphCommand.UpdateComment(cid, null, finalPos, finalSize, null, null, null));
                    invs.Add(new GraphCommand.UpdateComment(cid, null, comment.Position, comment.Size, null, null, null));
                }
            }

            if (fwds.Count > 0)
            {
                invs.Reverse();
                view.Execute(
                    new GraphCommand.Batch("Resize Comment", fwds),
                    new GraphCommand.Batch("Resize Comment", invs),
                    "Resize Comment");
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
