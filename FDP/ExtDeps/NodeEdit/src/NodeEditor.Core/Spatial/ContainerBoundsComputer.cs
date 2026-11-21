using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Spatial;

/// <summary>
/// Pure utility for computing the outer size of a container node from its children.
/// All values in graph units at zoom 1.0.
/// Used by the canvas layout builder to auto-size container nodes.
/// </summary>
public static class ContainerBoundsComputer
{
    /// <summary>Outline stroke half-width added to each side (total 2 * OutlineWidth) in graph units.</summary>
    public const float OutlineWidth = 1f;

    /// <summary>
    /// Computes the outer size (width and height) of a container node.
    /// The returned size is in the same coordinate space as the children's positions.
    /// </summary>
    /// <param name="container">The container whose bounds are being computed.</param>
    /// <param name="model">Graph model used to look up child node positions.</param>
    /// <param name="getChildGraphSize">
    /// Returns the graph-unit size of a child node by its ID, or null if unknown.
    /// For nested containers, the child's own size should already be computed before calling this.
    /// </param>
    /// <param name="headerHeight">Header height in graph units (from IEditorTheme.NodeHeaderHeight).</param>
    /// <param name="regionPreferredOverride">CE-1004: a live band size per region (divider drag preview), or null.</param>
    /// <param name="sizeOverride">CE-1004: a live outer size (grip drag preview); else <see cref="INodeModel.SizeOverride"/> is the minimum.</param>
    /// <returns>The outer size (width, height) of the container in graph units.</returns>
    public static Vector2 ComputeOuterSize(
        IContainerNodeModel container,
        IGraphModel model,
        Func<NodeId, Vector2?> getChildGraphSize,
        float headerHeight,
        Func<int, float?>? regionPreferredOverride = null,
        Vector2? sizeOverride = null)
    {
        float maxX = container.MinimumInteriorSize.X;
        float maxY = container.MinimumInteriorSize.Y;

        if (container.Regions.Count > 0)
        {
            bool isHorizontal = container.RegionOrientation == RegionLayoutOrientation.HorizontalStack;
            // ⭐ CE-1004 (A): the ONE band-size function — the same numbers the band drawing and child offsets use.
            float[] regionSizes = Layout.RegionLayoutComputer.ComputeRegionSizes(
                container, model, getChildGraphSize, preferredOverride: regionPreferredOverride);

            foreach (var childId in container.ChildNodeIds)
            {
                var childNode = model.FindNode(childId);
                var childSize = getChildGraphSize(childId);
                if (childNode == null || !childSize.HasValue) continue;
                if (isHorizontal) maxY = Math.Max(maxY, childNode.Position.Y + childSize.Value.Y);
                else              maxX = Math.Max(maxX, childNode.Position.X + childSize.Value.X);
            }

            float totalRegionSize = 0f;
            foreach (var s in regionSizes) totalRegionSize += s;
            if (isHorizontal) maxX = Math.Max(maxX, totalRegionSize);
            else maxY = Math.Max(maxY, totalRegionSize);
        }
        else
        {
            foreach (var childId in container.ChildNodeIds)
            {
                var childNode = model.FindNode(childId);
                var childSize = getChildGraphSize(childId);
                if (childNode == null || !childSize.HasValue) continue;

                float extentX = childNode.Position.X + childSize.Value.X;
                float extentY = childNode.Position.Y + childSize.Value.Y;
                maxX = Math.Max(maxX, extentX);
                maxY = Math.Max(maxY, extentY);
            }
        }

        float outerWidth  = maxX + container.Padding.Left + container.Padding.Right  + 2f * OutlineWidth;
        float outerHeight = headerHeight + maxY + container.Padding.Top + container.Padding.Bottom + 2f * OutlineWidth;

        // ⭐ CE-1004 (R1): the author-set outer size (INodeModel.SizeOverride, or a live edge drag) is a MINIMUM —
        //    a container is never smaller than what it holds.
        var preferred = sizeOverride ?? container.SizeOverride;
        if (preferred is { } p)
        {
            outerWidth  = Math.Max(outerWidth,  p.X);
            outerHeight = Math.Max(outerHeight, p.Y);
        }
        return new Vector2(outerWidth, outerHeight);
    }
}
