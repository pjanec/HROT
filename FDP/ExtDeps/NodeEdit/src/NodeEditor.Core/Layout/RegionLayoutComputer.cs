using System.Collections.Generic;
using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Layout;

/// <summary>
/// Describes the screen-space (or graph-space) geometry of one region strip
/// within a parallel-region container.
/// </summary>
public readonly record struct RegionStrip(
    Vector2 Min,
    Vector2 Size,
    RegionDescriptor Descriptor,
    int RegionIndex);

/// <summary>
/// Computes equal-height region strips for a parallel-region container.
/// All input and output values use the same coordinate space (graph units or
/// screen pixels — caller decides by passing the appropriate bounds).
/// </summary>
public static class RegionLayoutComputer
{
    /// <summary>
    /// Compute the layout strips for a container with one or more regions.
    /// Returns an empty list if the container has fewer than 2 regions (no dividers needed).
    /// </summary>
    /// <param name="container">The container whose regions are being laid out.</param>
    /// <param name="model">Graph model used to look up child nodes.</param>
    /// <param name="getChildGraphSize">Returns a child node's graph-space size by ID.</param>
    /// <param name="outerBounds">The container's outer bounding rect in the target coordinate space.</param>
    /// <param name="headerHeight">Header height in the target coordinate space.</param>
    /// <param name="outlineWidth">Outline half-width in the target coordinate space.</param>
    /// <param name="paddingScale">
    /// Scale factor applied to the container's <see cref="ContainerPadding"/> values.
    /// Use 1.0 for graph units, or the canvas zoom for screen pixels.
    /// </param>
    public static IReadOnlyList<RegionStrip> Compute(
        IContainerNodeModel container,
        IGraphModel model,
        Func<NodeId, Vector2?> getChildGraphSize,
        RectF outerBounds,
        float headerHeight,
        float outlineWidth,
        float paddingScale = 1f)
    {
        if (container.Regions.Count < 1)
            return System.Array.Empty<RegionStrip>();

        var pad = container.Padding;
        var interiorMin = new Vector2(
            outerBounds.Min.X + outlineWidth + pad.Left * paddingScale,
            outerBounds.Min.Y + outlineWidth + headerHeight + pad.Top * paddingScale);
        float innerW = outerBounds.Size.X - 2f * outlineWidth - (pad.Left + pad.Right)  * paddingScale;
        float innerH = outerBounds.Size.Y - 2f * outlineWidth - headerHeight - (pad.Top  + pad.Bottom) * paddingScale;

        if (innerW <= 0 || innerH <= 0)
            return System.Array.Empty<RegionStrip>();

        int count = container.Regions.Count;
        float[] regionH = new float[count];
        float minH = 60f * paddingScale;
        for (int i = 0; i < count; i++) regionH[i] = minH;

        foreach (var childId in container.ChildNodeIds)
        {
            var childNode = model.FindNode(childId);
            var childSize = getChildGraphSize(childId);
            if (childNode == null || !childSize.HasValue) continue;

            int rIdx = container.GetRegionIndexForChild(childId);
            if (rIdx >= 0 && rIdx < count)
            {
                float extentY = (childNode.Position.Y + childSize.Value.Y) * paddingScale;
                regionH[rIdx] = Math.Max(regionH[rIdx], extentY);
            }
        }

        float sumH = 0f;
        foreach (var h in regionH) sumH += h;

        if (innerH > sumH + 0.1f)
        {
            float extra = (innerH - sumH) / count;
            for (int i = 0; i < count; i++) regionH[i] += extra;
        }
        else if (sumH > innerH + 0.1f && sumH > 0f)
        {
            float scale = innerH / sumH;
            for (int i = 0; i < count; i++) regionH[i] *= scale;
        }

        var result = new List<RegionStrip>(count);
        float currentY = 0f;
        for (int i = 0; i < count; i++)
        {
            result.Add(new RegionStrip(
                Min:         interiorMin + new Vector2(0f, currentY),
                Size:        new Vector2(innerW, regionH[i]),
                Descriptor:  container.Regions[i],
                RegionIndex: i));
            currentY += regionH[i];
        }
        return result;
    }
}
