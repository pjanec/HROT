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

    /// <summary>The floor of a band along the stack axis (graph units).</summary>
    public const float MinRegionSize = 60f;

    /// <summary>
    /// ⭐⭐ CE-1004 (A) — THE band sizes of <paramref name="container"/>, along its stack axis, in graph units: each band is
    /// max(<see cref="MinRegionSize"/>, its author-set <see cref="RegionDescriptor.PreferredSize"/>, the far edge of the
    /// furthest child in it). The band drawing, the child offset, the drop and the container bounds all read this — they
    /// were four copies that disagreed (spare-space sharing; default vs measured child sizes).
    /// </summary>
    /// <param name="container">The container whose bands are sized.</param>
    /// <param name="model">Graph model used to look up children; null skips content sizing.</param>
    /// <param name="getChildGraphSize">A child's measured graph size, or null when unknown.</param>
    /// <param name="skipChild">Children to leave out (e.g. the ones being dragged), or null.</param>
    /// <param name="preferredOverride">A live preferred size per band (a divider drag in progress), or null.</param>
    public static float[] ComputeRegionSizes(
        IContainerNodeModel container,
        IGraphModel model,
        Func<NodeId, Vector2?> getChildGraphSize,
        Func<NodeId, bool>? skipChild = null,
        Func<int, float?>? preferredOverride = null)
    {
        int count = container.Regions.Count;
        var sizes = new float[count];
        bool isHorizontal = container.RegionOrientation == RegionLayoutOrientation.HorizontalStack;
        for (int i = 0; i < count; i++)
            sizes[i] = Math.Max(MinRegionSize, preferredOverride?.Invoke(i) ?? container.Regions[i].PreferredSize ?? 0f);

        if (model is null) return sizes;
        foreach (var childId in container.ChildNodeIds)
        {
            if (skipChild?.Invoke(childId) == true) continue;
            var childNode = model.FindNode(childId);
            var childSize = getChildGraphSize(childId);
            if (childNode == null || !childSize.HasValue) continue;
            int rIdx = container.GetRegionIndexForChild(childId);
            if (rIdx < 0 || rIdx >= count) continue;
            float extent = isHorizontal
                ? childNode.Position.X + childSize.Value.X
                : childNode.Position.Y + childSize.Value.Y;
            sizes[rIdx] = Math.Max(sizes[rIdx], extent);
        }
        return sizes;
    }

    /// <summary>CE-1004: the offset of band <paramref name="regionIndex"/>'s start from the interior origin (sum of the bands before it).</summary>
    public static float RegionOffset(float[] sizes, int regionIndex)
    {
        float o = 0f;
        for (int i = 0; i < regionIndex && i < sizes.Length; i++) o += sizes[i];
        return o;
    }

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
    /// <param name="preferredOverride">CE-1004: a live preferred size per band (divider drag preview), or null.</param>
    public static IReadOnlyList<RegionStrip> Compute(
        IContainerNodeModel container,
        IGraphModel model,
        Func<NodeId, Vector2?> getChildGraphSize,
        RectF outerBounds,
        float headerHeight,
        float outlineWidth,
        float paddingScale = 1f,
        Func<int, float?>? preferredOverride = null)
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
        float[] regionSizes = ComputeRegionSizes(container, model, getChildGraphSize, preferredOverride: preferredOverride);
        for (int i = 0; i < count; i++) regionSizes[i] *= paddingScale;

        float sumSize = 0f;
        foreach (var s in regionSizes) sumSize += s;
        float availableSize = isHorizontal ? innerW : innerH;

        if (availableSize > sumSize + 0.1f)
        {
            // ⭐ CE-1004: spare space goes to the LAST band, so every band starts where the child offset and the drop
            //    put it (they sum the bands before it). It used to be shared equally — the dividers then moved away
            //    from the children they bound.
            regionSizes[count - 1] += availableSize - sumSize;
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
