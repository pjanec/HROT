using System.Collections.Generic;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace NodeEditor.Core.Spatial;

/// <summary>
/// Pure utility for detecting reparent cycles in the container hierarchy.
/// A cycle would occur if a container were placed inside one of its own descendants.
/// </summary>
public static class ContainerCycleDetector
{
    /// <summary>
    /// Returns true if moving <paramref name="nodeToMove"/> to become a child of
    /// <paramref name="targetContainerId"/> would create an ancestor cycle.
    ///
    /// A cycle occurs when <paramref name="nodeToMove"/> is an ancestor of
    /// <paramref name="targetContainerId"/> (i.e. the target is already a
    /// descendant of the node being moved).
    /// </summary>
    public static bool WouldCreateCycle(NodeId nodeToMove, NodeId targetContainerId, IGraphModel model)
    {
        // Walk up the target's ancestor chain. If we encounter nodeToMove, it's a cycle.
        NodeId current = targetContainerId;
        // Guard against infinite loops caused by a corrupt model (limit to reasonable depth).
        int limit = 128;
        while (limit-- > 0)
        {
            if (current == nodeToMove) return true;
            var node = model.FindNode(current);
            if (node?.ParentContainerId == null) return false;
            current = node.ParentContainerId.Value;
        }
        // Exceeded depth limit — treat as cycle to be safe.
        return true;
    }

    /// <summary>
    /// Returns true if moving any node in <paramref name="nodesToMove"/> to become a child
    /// of <paramref name="targetContainerId"/> would create an ancestor cycle.
    /// </summary>
    public static bool WouldCreateCycleAny(IEnumerable<NodeId> nodesToMove, NodeId targetContainerId, IGraphModel model)
    {
        foreach (var node in nodesToMove)
            if (WouldCreateCycle(node, targetContainerId, model))
                return true;
        return false;
    }
}
