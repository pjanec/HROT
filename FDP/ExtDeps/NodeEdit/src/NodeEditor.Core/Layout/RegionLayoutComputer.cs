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
    /// Convenience overload that computes region strips without child-node size data.
    /// Child-driven region sizing is skipped; all regions are equally distributed.
    /// </summary>
    public static IReadOnlyList<RegionStrip> Compute(
        IContainerNodeModel container,
        RectF outerBounds,
        float headerHeight,
        float outlineWidth,
        float paddingScale = 1f)
        => Compute(container, null!, static _ => null, outerBounds, headerHeight, outlineWidth, paddingScale);

    /// <summary>
    /// Compute the layout strips for a container with one or more regions.
    /// Returns an empty list if the container has no regions.
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

        bool isHorizontal = container.RegionOrientation == RegionLayoutOrientation.HorizontalStack;
        int count = container.Regions.Count;
        float[] regionSizes = new float[count];
        float minSize = 60f * paddingScale;
        for (int i = 0; i < count; i++) regionSizes[i] = minSize;

        foreach (var childId in container.ChildNodeIds)
        {
            var childNode = model.FindNode(childId);
            var childSize = getChildGraphSize(childId);
            if (childNode == null || !childSize.HasValue) continue;

            int rIdx = container.GetRegionIndexForChild(childId);
            if (rIdx >= 0 && rIdx < count)
            {
                float extent = isHorizontal
                    ? (childNode.Position.X + childSize.Value.X) * paddingScale
                    : (childNode.Position.Y + childSize.Value.Y) * paddingScale;
                regionSizes[rIdx] = Math.Max(regionSizes[rIdx], extent);
            }
        }

        float sumSize = 0f;
        foreach (var s in regionSizes) sumSize += s;
        float availableSize = isHorizontal ? innerW : innerH;

        if (availableSize > sumSize + 0.1f)
        {
            float extra = (availableSize - sumSize) / count;
            for (int i = 0; i < count; i++) regionSizes[i] += extra;
        }
        else if (sumSize > availableSize + 0.1f && sumSize > 0f)
        {
            float scale = availableSize / sumSize;
            for (int i = 0; i < count; i++) regionSizes[i] *= scale;
        }

        var result = new List<RegionStrip>(count);
        float currentOffset = 0f;
        for (int i = 0; i < count; i++)
        {
            Vector2 min = isHorizontal
                ? interiorMin + new Vector2(currentOffset, 0f)
                : interiorMin + new Vector2(0f, currentOffset);
            Vector2 size = isHorizontal
                ? new Vector2(regionSizes[i], innerH)
                : new Vector2(innerW, regionSizes[i]);

            result.Add(new RegionStrip(
                Min:         min,
                Size:        size,
                Descriptor:  container.Regions[i],
                RegionIndex: i));
            currentOffset += regionSizes[i];
        }
        return result;
    }
}
