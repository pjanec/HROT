namespace NodeEditor.Core.Canvas;

/// <summary>
/// Named render passes in the canvas's per-frame paint sequence.
/// Custom renderers select the pass they need via their Pass property.
/// </summary>
public enum CanvasRenderPass
{
    /// <summary>
    /// After background and grid; before any graph content.
    /// Use for canvas-wide overlays that should sit behind everything
    /// (faint region tinting, large-scale debug heatmaps).
    /// </summary>
    BeforeContent,

    /// <summary>
    /// After wires; before regular/child nodes.
    /// Use for content that should sit on top of wires but below nodes
    /// (transition labels at wire midpoints, link-decoration badges).
    /// </summary>
    AfterWires,

    /// <summary>
    /// After all nodes, attachments, and reroutes; before selection outlines.
    /// Use for overlays on top of the rendered graph but below selection
    /// feedback (region-conflict lines, initial-state arrows, subtree indicators).
    /// </summary>
    AfterNodes,

    /// <summary>
    /// After selection outlines, hover effects, and drag previews.
    /// Use for tooltips, floating annotations, or anything that must overlay
    /// the entire canvas including selection feedback.
    /// </summary>
    TopMost,
}
