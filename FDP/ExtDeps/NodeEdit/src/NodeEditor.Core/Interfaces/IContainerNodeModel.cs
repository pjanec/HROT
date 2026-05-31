using System.Collections.Generic;
using System.Numerics;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Interfaces;

/// <summary>
/// Extended node model interface for container nodes.
/// A container visually and logically encloses child nodes. Its bounds
/// auto-resize to fit its children. Child positions are expressed in the
/// container's interior coordinate space, not in canvas space.
/// </summary>
public interface IContainerNodeModel : INodeModel
{
    /// <summary>
    /// True when this node acts as a container. Implementing classes that set
    /// IsContainer = false behave as regular nodes regardless of the interface.
    /// </summary>
    bool IsContainer { get; }

    /// <summary>
    /// Ordered list of child node IDs. Order determines sibling z-order (later
    /// indices render on top) and serialization order determinism.
    /// </summary>
    IReadOnlyList<NodeId> ChildNodeIds { get; }

    /// <summary>
    /// Region descriptors for parallel-region containers.
    /// Empty list for simple (non-region) containers.
    /// </summary>
    IReadOnlyList<RegionDescriptor> Regions { get; }

    /// <summary>Returns the zero-based region index for the given child, or -1 if not applicable.</summary>
    int GetRegionIndexForChild(NodeId childId);

    /// <summary>Interior padding from the container edge to the child layout area.</summary>
    ContainerPadding Padding { get; }
    /// <summary>The layout orientation of regions within the container.</summary>
    RegionLayoutOrientation RegionOrientation { get; }

    /// <summary>
    /// Minimum interior size in graph units. Container auto-resize never
    /// shrinks the interior below this value.
    /// </summary>
    Vector2 MinimumInteriorSize { get; }
}

/// <summary>Describes one region within a parallel-region container.</summary>
public sealed record RegionDescriptor(
    int Index,
    string Name,
    int Priority,
    Vector4? CustomColor);

/// <summary>Interior padding (all values in graph units at zoom 1.0).</summary>
public sealed record ContainerPadding(
    float Top,
    float Right,
    float Bottom,
    float Left)
{
    /// <summary>Default padding: 8 px top, 12 px on each other side.</summary>
    public static ContainerPadding Default { get; } = new(8f, 12f, 12f, 12f);
}

/// <summary>Orientation of parallel regions inside a container.</summary>
public enum RegionLayoutOrientation
{
    VerticalStack,
    HorizontalStack,
}

/// <summary>Extension methods on INodeModel for container-related queries.</summary>
public static class INodeModelExtensions
{
    /// <summary>Returns true if this node is an active container (IsContainer = true).</summary>
    public static bool IsContainerNode(this INodeModel node) =>
        node is IContainerNodeModel { IsContainer: true };

    /// <summary>
    /// Returns the node cast to IContainerNodeModel if it is an active container,
    /// or null if it is a regular node or a non-active container.
    /// </summary>
    public static IContainerNodeModel? AsContainer(this INodeModel node) =>
        node is IContainerNodeModel c && c.IsContainer ? c : null;
}
