using System.Numerics;
using ImGuiNET;
using NodeEditor.Core;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
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
    public void Handle(GraphView view)
    {
        // Don't process canvas input when an ImGui widget has keyboard/mouse focus.
        bool canvasHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.None)
                          && !ImGui.IsAnyItemActive();

        var input = view.Host.Input;
        var mode  = view.Interaction.Mode;

        // ── Zoom ────────────────────────────────────────────────────────────
        if (canvasHovered && input.WheelDelta != 0f)
        {
            float factor = 1f + input.WheelDelta * 0.1f;
            view.Viewport.ZoomAt(input.MousePosition, factor);
        }

        // ── Per-mode dispatch ────────────────────────────────────────────────
        switch (mode)
        {
            case InteractionMode.Idle:
                HandleIdle(view, canvasHovered, input);
                break;

            case InteractionMode.Panning:
                HandlePanning(view, input);
                break;

            case InteractionMode.DraggingNodes:
                HandleDraggingNodes(view, input);
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
        if (mode == InteractionMode.Idle && canvasHovered
            && (input.IsKeyPressed(EditorKey.Delete) || input.IsKeyPressed(EditorKey.Backspace)))
        {
            DeleteSelected(view);
        }
    }

    // ── Idle ──────────────────────────────────────────────────────────────────

    private static void HandleIdle(GraphView view, bool canvasHovered, IInputSource input)
    {
        var hover = view.Interaction.Hover;
        var modifiers = input.Modifiers;

        // Right-mouse → pan
        if (canvasHovered && input.IsMousePressed(MouseButton.Right))
        {
            view.Interaction.Mode = InteractionMode.Panning;
            view.Interaction.DragStartScreen = input.MousePosition;
            return;
        }

        // Left-mouse pressed
        if (canvasHovered && input.IsMousePressed(MouseButton.Left))
        {
            bool ctrl  = (modifiers & KeyModifiers.Ctrl)  != 0;
            bool shift = (modifiers & KeyModifiers.Shift) != 0;

            switch (hover.Kind)
            {
                case HoverKind.Pin:
                    // Start wire drag from pin
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
        if (input.MouseDelta != Vector2.Zero)
            view.Viewport.PanScreen(input.MouseDelta);

        if (input.IsMouseReleased(MouseButton.Right))
            view.Interaction.ResetToIdle();
    }

    // ── Dragging nodes ────────────────────────────────────────────────────────

    private static void HandleDraggingNodes(GraphView view, IInputSource input)
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
                view.Interaction.DragOverridePositions[nid] = n.Position + deltaGraph;
            }
        }

        if (input.IsMouseReleased(MouseButton.Left))
        {
            if (view.Interaction.DragThresholdCrossed && view.Interaction.DragOverridePositions.Count > 0)
            {
                var moves = view.Interaction.DragOverridePositions
                    .Select(kv => new NodeMove(kv.Key, kv.Value))
                    .ToList();
                view.Commands.Apply(new GraphCommand.MoveNodes(moves));
            }
            view.Interaction.ResetToIdle();
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
            if (pw.CandidateTarget.HasValue && pw.CandidateValid)
            {
                var newId = LinkId.NewId();
                view.Commands.Apply(new GraphCommand.AddLink(newId, pw.SourcePin, pw.CandidateTarget.Value));
            }
            view.Interaction.ResetToIdle();
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
