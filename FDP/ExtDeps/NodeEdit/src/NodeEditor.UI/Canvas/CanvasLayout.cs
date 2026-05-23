using System.Numerics;
using ImGuiNET;
using NodeEditor.Core;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Spatial;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Canvas;

/// <summary>
/// Per-frame layout data computed before any drawing begins.
/// Stores screen-space geometry for all visible nodes and pins.
/// </summary>
internal sealed class CanvasLayout
{
    /// <summary>Bounding rects of all nodes in screen coordinates.</summary>
    public Dictionary<NodeId, RectF> NodeScreenRects { get; } = [];

    /// <summary>Pin attachment points in screen coordinates.</summary>
    public Dictionary<PinId, Vector2> PinScreenPositions { get; } = [];

    /// <summary>Pre-computed set of input pins that have at least one wire connected.</summary>
    public HashSet<PinId> ConnectedInputPins { get; } = [];

    /// <summary>Attachment layouts (screen-pixel coords) for nodes that have attachments.</summary>
    public Dictionary<NodeId, AttachmentLayout> AttachmentLayouts { get; } = [];

    /// <summary>Screen-space bounding rects for each attachment, keyed by AttachmentId.</summary>
    public Dictionary<AttachmentId, RectF> AttachmentScreenRects { get; } = [];

    /// <summary>Graph-unit sizes for all nodes, including containers after auto-resize.</summary>
    public Dictionary<NodeId, Vector2> NodeGraphSizes { get; } = [];

    public void Clear()
    {
        NodeScreenRects.Clear();
        PinScreenPositions.Clear();
        ConnectedInputPins.Clear();
        AttachmentLayouts.Clear();
        AttachmentScreenRects.Clear();
        NodeGraphSizes.Clear();
    }
}

/// <summary>
/// Computes the screen-space bounding rect and pin positions for every node
/// in the graph, fills the <see cref="SpatialIndex"/> used by hit-testing, and
/// records which input pins are covered by at least one wire.
/// </summary>
internal sealed class CanvasLayoutBuilder
{
    // Layout constants — all in graph units.
    public const float NodeMinWidthGu   = 160f;
    public const float NodeHorizPadGu   = 12f;
    public const float PinRowHeightGu   = 22f;
    public const float PinTopPadGu      = 6f;
    public const float PinBottomPadGu   = 8f;
    public const float EditorWidthGu    = 80f;
    public const float EditorHorizPadGu = 4f;

    public void Build(GraphView view, CanvasLayout layout, SpatialIndex spatialIndex, bool rebuildSpatial)
    {
        layout.Clear();

        // Pre-compute connected input pins.
        foreach (var link in view.Model.Links)
            layout.ConnectedInputPins.Add(link.ToPin);

        float headerHt = view.Host.Theme.NodeHeaderHeight;
        float zoom = view.Viewport.Zoom;

        // Only allocate the entries list when we actually need to rebuild the spatial index.
        List<(NodeId, RectF)>? entries = rebuildSpatial
            ? new List<(NodeId, RectF)>(view.Model.Nodes.Count)
            : null;

        foreach (var node in view.Model.Nodes)
        {
            // Use canvas-absolute position: container children store Position as parent-local,
            // so we resolve the full chain via NodeCanvasPosition.
            Vector2 graphPos;
            if (view.Interaction.DragOverridePositions.TryGetValue(node.Id, out var over))
                graphPos = over;
            else if (node.ParentContainerId == null)
                graphPos = node.Position;
            else
                graphPos = view.NodeCanvasPosition(node.Id);

            var inputPins = new List<IPinModel>();
            var outputPins = new List<IPinModel>();
            float maxInputWidthGu = 0f;
            float maxOutputWidthGu = 0f;

            foreach (var p in node.Pins)
            {
                if (p.IsAdvanced && !node.ShowAdvancedPins) continue;

                float labelWidthGu = string.IsNullOrEmpty(p.Label) ? 0f : ImGui.CalcTextSize(p.Label).X;
                float pinWidthGu = 18f + labelWidthGu; // glyph + label spacing budget

                if (p.Direction == PinDirection.Input)
                {
                    bool hasInlineEditor = p.Kind == PinKind.Data
                                           && p.Default != null
                                           && p.Type.HasValue
                                           && !layout.ConnectedInputPins.Contains(p.Id)
                                           && view.TypeSystem.GetDefaultEditor(p.Type.Value) != null;

                    if (hasInlineEditor)
                        pinWidthGu += EditorWidthGu + (EditorHorizPadGu * 2f);

                    maxInputWidthGu = Math.Max(maxInputWidthGu, pinWidthGu);
                    inputPins.Add(p);
                }
                else
                {
                    maxOutputWidthGu = Math.Max(maxOutputWidthGu, pinWidthGu);
                    outputPins.Add(p);
                }
            }

            float titleWidthGu = ImGui.CalcTextSize(node.Title).X + 48f;
            float requiredWidthGu = (NodeHorizPadGu * 2f) + maxInputWidthGu + maxOutputWidthGu + 24f;
            float nodeWGu = node.SizeOverride?.X ?? Math.Max(NodeMinWidthGu, Math.Max(titleWidthGu, requiredWidthGu));

            int rowCount = Math.Max(inputPins.Count, outputPins.Count);
            float nodeHGu = headerHt + PinTopPadGu + rowCount * PinRowHeightGu + PinBottomPadGu;

            var screenPos = view.Viewport.GraphToScreen(graphPos);
            float sw = nodeWGu * zoom;
            float sh = nodeHGu * zoom;
            var rect = new RectF(screenPos, new Vector2(sw, sh));
            layout.NodeScreenRects[node.Id] = rect;
            layout.NodeGraphSizes[node.Id]  = new Vector2(nodeWGu, nodeHGu);
            entries?.Add((node.Id, new RectF(graphPos, new Vector2(nodeWGu, nodeHGu))));

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

            for (int i = 0; i < inputPins.Count; i++)
            {
                float offsetYGu = headerHt + PinTopPadGu + i * PinRowHeightGu + PinRowHeightGu * 0.5f;
                var pinGraphPos = graphPos + new Vector2(NodeHorizPadGu, offsetYGu);
                layout.PinScreenPositions[inputPins[i].Id] = view.Viewport.GraphToScreen(pinGraphPos);
            }

            for (int i = 0; i < outputPins.Count; i++)
            {
                float offsetYGu = headerHt + PinTopPadGu + i * PinRowHeightGu + PinRowHeightGu * 0.5f;
                var pinGraphPos = graphPos + new Vector2(nodeWGu - NodeHorizPadGu, offsetYGu);
                layout.PinScreenPositions[outputPins[i].Id] = view.Viewport.GraphToScreen(pinGraphPos);
            }
        }

        // Second pass: compute container bounds recursively (innermost-first).
        // We only visit root containers; the recursion handles nested ones first.
        foreach (var node in view.Model.Nodes)
        {
            if (node.ParentContainerId == null && node.AsContainer() is { } rootContainer)
                FillContainerBoundsRecursive(rootContainer, view, layout, headerHt, zoom);
        }

        // Third pass: update screen rects for all container nodes using the
        // now-correct graph-unit sizes from the second pass.
        foreach (var node in view.Model.Nodes)
        {
            if (node.AsContainer() is not { } container) continue;
            if (!layout.NodeGraphSizes.TryGetValue(node.Id, out var graphSize)) continue;
            var canvasPos = view.NodeCanvasPosition(node.Id);
            var screenPos = view.Viewport.GraphToScreen(canvasPos);
            layout.NodeScreenRects[node.Id] = new RectF(screenPos, graphSize * zoom);
        }

        if (rebuildSpatial)
        {
            spatialIndex.Rebuild(entries!);
            // Overwrite container entries with correct sizes computed in the second pass.
            foreach (var node in view.Model.Nodes)
            {
                if (node.AsContainer() is not { } container) continue;
                if (!layout.NodeGraphSizes.TryGetValue(node.Id, out var graphSize)) continue;
                var canvasPos = view.NodeCanvasPosition(node.Id);
                spatialIndex.Insert(node.Id, new RectF(canvasPos, graphSize));
            }
        }
    }

    // Recursively computes container bounds, processing deepest nesting first.
    private static void FillContainerBoundsRecursive(
        IContainerNodeModel container,
        GraphView view,
        CanvasLayout layout,
        float headerHt,
        float zoom)
    {
        // Recurse into child containers before this one.
        foreach (var childId in container.ChildNodeIds)
        {
            var child = view.Model.FindNode(childId);
            if (child?.AsContainer() is { } childContainer)
                FillContainerBoundsRecursive(childContainer, view, layout, headerHt, zoom);
        }

        var outerSize = ContainerBoundsComputer.ComputeOuterSize(
            container,
            view.Model,
            id => layout.NodeGraphSizes.TryGetValue(id, out var s) ? s : (Vector2?)null,
            headerHt);

        layout.NodeGraphSizes[container.Id] = outerSize;
    }
}
