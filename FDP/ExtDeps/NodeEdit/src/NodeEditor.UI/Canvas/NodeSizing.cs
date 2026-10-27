using System.Numerics;
using NodeEditor.Core;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.Spatial;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Canvas;

/// <summary>
/// ⭐ <c>CE-1367</c> — the ONE implementation of "how big is this node when the canvas draws it": its body (width from the title and the pins,
/// height from the header and the pin rows) AND the attachment pills drawn above it. <see cref="CanvasLayoutBuilder"/> sizes every node
/// through it, and a host that places nodes itself (the BTree auto-layout) asks it too — so the two cannot disagree.
/// Pure: the caller supplies the text measure (ImGui's <c>CalcTextSize</c>) and the inline-editor width of a pin.
/// </summary>
public static class NodeSizing
{
    /// <summary>What the canvas draws for one node, in graph units. The body is at the node's position; the pills extend ABOVE it.</summary>
    public readonly record struct Drawn(
        float Width, float Height, float PillsAbove, float PillsWidth, AttachmentLayout Pills, IReadOnlyList<IPinModel> InputPins, IReadOnlyList<IPinModel> OutputPins)
    {
        /// <summary>The horizontal extent of the node with its pills (a lone pill can be wider than the body).</summary>
        public float TotalWidth => Math.Max(Width, PillsWidth);
    }

    /// <param name="node">The node.</param>
    /// <param name="attachments">The node's attachments (pills), any order.</param>
    /// <param name="orientation">The graph kind's pin orientation.</param>
    /// <param name="headerHeight">The theme's <c>NodeHeaderHeight</c>.</param>
    /// <param name="measureText">Text width in graph units (ImGui's <c>CalcTextSize(s).X</c>).</param>
    /// <param name="inlineEditorWidth">For an input pin: the width of its inline editor, 0 when it shows none (connected, no default, no editor).</param>
    public static Drawn Measure(
        INodeModel node,
        IReadOnlyList<IAttachmentModel> attachments,
        PinOrientation orientation,
        float headerHeight,
        Func<string, float> measureText,
        Func<IPinModel, float> inlineEditorWidth)
    {
        var inputPins = new List<IPinModel>();
        var outputPins = new List<IPinModel>();
        float maxInputWidthGu = 0f;
        float maxOutputWidthGu = 0f;

        foreach (var p in node.Pins)
        {
            if (p.IsAdvanced && !node.ShowAdvancedPins) continue;

            float labelWidthGu = string.IsNullOrEmpty(p.Label) ? 0f : measureText(p.Label);
            float pinWidthGu = 20f + labelWidthGu; // glyph + label spacing budget

            if (p.Direction == PinDirection.Input)
            {
                float editor = inlineEditorWidth(p);
                if (editor > 0f) pinWidthGu += editor + (CanvasLayoutBuilder.EditorHorizPadGu * 2f);
                maxInputWidthGu = Math.Max(maxInputWidthGu, pinWidthGu);
                inputPins.Add(p);
            }
            else
            {
                maxOutputWidthGu = Math.Max(maxOutputWidthGu, pinWidthGu);
                outputPins.Add(p);
            }
        }

        float titleWidthGu = measureText(node.Title) + 48f;
        float requiredWidthGu = (CanvasLayoutBuilder.NodeHorizPadGu * 2f) + maxInputWidthGu + maxOutputWidthGu + 24f;
        float width = node.SizeOverride?.X ?? Math.Max(CanvasLayoutBuilder.NodeMinWidthGu, Math.Max(titleWidthGu, requiredWidthGu));

        int rowCount = Math.Max(inputPins.Count, outputPins.Count);
        float height = orientation == PinOrientation.Vertical
            ? headerHeight + CanvasLayoutBuilder.PinTopPadGu + CanvasLayoutBuilder.PinBottomPadGu
            : headerHeight + CanvasLayoutBuilder.PinTopPadGu + rowCount * CanvasLayoutBuilder.PinRowHeightGu + CanvasLayoutBuilder.PinBottomPadGu;

        var pills = PillsOf(attachments, width, measureText);
        float pillsWidth = 0f;
        foreach (var pl in pills.Placements.Values) pillsWidth = Math.Max(pillsWidth, pl.TopLeft.X + pl.Size.X);

        return new Drawn(width, height, pills.TotalHeightAboveHost, pillsWidth, pills, inputPins, outputPins);
    }

    /// <summary>The pills' layout above a host of <paramref name="hostWidth"/> — the same call the canvas draws from.</summary>
    public static AttachmentLayout PillsOf(IReadOnlyList<IAttachmentModel> attachments, float hostWidth, Func<string, float> measureText)
        => AttachmentLayoutEngine.Compute(attachments, hostWidth, a =>
        {
            float w = 0f;
            if (!string.IsNullOrEmpty(a.Glyph)) w += measureText(a.Glyph);
            if (!string.IsNullOrEmpty(a.Label))
            {
                if (w > 0f) w += 4f;
                w += measureText(a.Label);
            }
            return w;
        });
}
