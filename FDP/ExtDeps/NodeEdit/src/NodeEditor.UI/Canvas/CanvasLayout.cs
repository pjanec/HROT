using System.Numerics;
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

    public void Clear()
    {
        NodeScreenRects.Clear();
        PinScreenPositions.Clear();
        ConnectedInputPins.Clear();
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

    public void Build(GraphView view, CanvasLayout layout, SpatialIndex spatialIndex)
    {
        layout.Clear();

        // Pre-compute connected input pins.
        foreach (var link in view.Model.Links)
            layout.ConnectedInputPins.Add(link.ToPin);

        float headerHt = view.Host.Theme.NodeHeaderHeight;
        float zoom = view.Viewport.Zoom;

        var entries = new List<(NodeId, RectF)>(view.Model.Nodes.Count);

        foreach (var node in view.Model.Nodes)
        {
            var graphPos = view.Interaction.DragOverridePositions.TryGetValue(node.Id, out var over)
                ? over
                : node.Position;

            // Separate input and output pins (visible ones).
            var inputPins  = new List<IPinModel>();
            var outputPins = new List<IPinModel>();
            foreach (var p in node.Pins)
            {
                if (p.IsAdvanced && !node.ShowAdvancedPins) continue;
                if (p.Direction == PinDirection.Input)  inputPins.Add(p);
                else                                     outputPins.Add(p);
            }

            int rowCount = Math.Max(inputPins.Count, outputPins.Count);
            float nodeHGu = headerHt + PinTopPadGu + rowCount * PinRowHeightGu + PinBottomPadGu;
            float nodeWGu = node.SizeOverride?.X ?? NodeMinWidthGu;

            var screenPos = view.Viewport.GraphToScreen(graphPos);
            float sw = nodeWGu * zoom;
            float sh = nodeHGu * zoom;
            var rect = new RectF(screenPos, new Vector2(sw, sh));
            layout.NodeScreenRects[node.Id] = rect;
            entries.Add((node.Id, new RectF(graphPos, new Vector2(nodeWGu, nodeHGu))));

            // Input pin screen positions (left edge).
            for (int i = 0; i < inputPins.Count; i++)
            {
                float offsetYGu = headerHt + PinTopPadGu + i * PinRowHeightGu + PinRowHeightGu * 0.5f;
                var pinGraphPos = graphPos + new Vector2(0f, offsetYGu);
                layout.PinScreenPositions[inputPins[i].Id] = view.Viewport.GraphToScreen(pinGraphPos);
            }

            // Output pin screen positions (right edge).
            for (int i = 0; i < outputPins.Count; i++)
            {
                float offsetYGu = headerHt + PinTopPadGu + i * PinRowHeightGu + PinRowHeightGu * 0.5f;
                var pinGraphPos = graphPos + new Vector2(nodeWGu, offsetYGu);
                layout.PinScreenPositions[outputPins[i].Id] = view.Viewport.GraphToScreen(pinGraphPos);
            }
        }

        spatialIndex.Rebuild(entries);
    }
}
