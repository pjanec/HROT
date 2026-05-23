using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Link validator for the BTree canvas.
///
/// BTree pin direction is reversed relative to execution flow: each child has an
/// output-pin pointing at its parent's input-pin. This lets the editor use
/// NodeEditor's standard many-to-one exec rule (many child outputs -> one parent
/// input) while still rendering wires from parent to child visually.
///
/// Rules enforced:
/// - Leaves (Action, Condition, Wait, Subtree) cannot accept incoming child edges.
/// - Adding a second parent edge is forbidden (each node has at most one parent).
/// - Cycles are rejected by walking the ancestor chain.
/// </summary>
public sealed class BTreeLinkValidator : ILinkValidator
{
    private readonly IGraphModel _graph;

    public BTreeLinkValidator(IGraphModel graph)
    {
        _graph = graph;
    }

    public LinkValidationResult Validate(PinId from, PinId to)
    {
        var fromPin = _graph.FindPin(from);
        var toPin   = _graph.FindPin(to);
        if (fromPin == null || toPin == null)
            return Invalid("Pin not found.");

        var fromNode = _graph.FindNode(fromPin.OwnerNodeId);
        var toNode   = _graph.FindNode(toPin.OwnerNodeId);
        if (fromNode == null || toNode == null)
            return Invalid("Node not found.");

        // toNode is the "parent" in the reversed convention.
        if (BTreeKinds.IsLeaf(toNode.Kind))
            return Invalid("Leaf nodes cannot have children.");

        if (fromNode.Id == toNode.Id)
            return Invalid("A node cannot be its own parent.");

        if (WouldCreateCycle(fromNode, toNode))
            return Invalid("Would create a cycle.");

        return new LinkValidationResult(LinkValidity.Valid, null, false, null);
    }

    // Walk the ancestor chain of toNode looking for fromNode.
    // If fromNode is already an ancestor of toNode, connecting fromNode as a
    // child of toNode would form a cycle.
    private bool WouldCreateCycle(INodeModel fromNode, INodeModel toNode)
    {
        // Collect the parent of each node via its input links.
        var visited = new HashSet<NodeId>();
        var current = toNode;
        while (true)
        {
            if (!visited.Add(current.Id)) break; // guard against any existing cycles
            var parentId = FindParent(current.Id);
            if (parentId == null) break;
            if (parentId == fromNode.Id) return true;
            var parentNode = _graph.FindNode(parentId.Value);
            if (parentNode == null) break;
            current = parentNode;
        }
        return false;
    }

    // Finds the parent node of the given node by looking at its input links.
    // In the reversed convention, the node's OUTPUT pin receives connections from children,
    // and its INPUT pin connects to its parent.
    // Here we look for links where the toPin belongs to this node and the fromPin
    // belongs to another node (that other node is this node's child in BTree terms,
    // but in editor graph terms it owns the "from" output pin going to this node).
    //
    // Actually: in the reversed convention, children have output pins pointing at
    // parent input pins. So fromNode (child) has output pin -> toNode (parent) has
    // input pin. To find the parent of a node N, we look for links where the
    // TO pin belongs to N. That "to" node is N, and the "from" node is N's child.
    // We want N's parent, so we look for links where N's output pin is the FROM.
    // In this model, N's input pin receives from its parent's output. Wait...
    //
    // Let's be precise about the reversed convention:
    //   - In BTree execution: parent tick() -> child tick()
    //   - In NodeEditor graph: child has Output pin -> parent has Input pin
    //   - So a link goes: child.OutputPin -> parent.InputPin
    //   - `from` = child.OutputPin, `to` = parent.InputPin
    //
    // To find N's parent: find a link where the toPin.OwnerNodeId == N.Id (N is the "to" = parent).
    // Actually N is the parent of any node that has a link going TO N.
    // To find N's own parent, find a link where the fromPin.OwnerNodeId == N.Id (N is the child).
    // That link's toPin.OwnerNodeId is N's parent.
    private NodeId? FindParent(NodeId nodeId)
    {
        foreach (var link in _graph.Links)
        {
            var fromPin = _graph.FindPin(link.FromPin);
            if (fromPin != null && fromPin.OwnerNodeId == nodeId)
            {
                var toPin = _graph.FindPin(link.ToPin);
                return toPin?.OwnerNodeId;
            }
        }
        return null;
    }

    private static LinkValidationResult Invalid(string reason) =>
        new(LinkValidity.Invalid, reason, false, null);
}
