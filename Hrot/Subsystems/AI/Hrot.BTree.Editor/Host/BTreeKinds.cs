using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Well-known NodeKindKey strings used by the BTree editor.
/// The BTreeNodeCatalog registers entries under these IDs.
/// </summary>
internal static class BTreeKinds
{
    // Root
    public const string Root             = "bt.composite.root";

    // Composites
    public const string Sequence         = "bt.composite.sequence";
    public const string Selector         = "bt.composite.selector";
    public const string Parallel         = "bt.composite.parallel";
    public const string ObserverSelector = "bt.composite.observerSelector";

    // Leaves
    public const string Action           = "bt.leaf.action";
    public const string Condition        = "bt.leaf.condition";
    public const string Wait             = "bt.leaf.wait";
    public const string Subtree          = "bt.leaf.subtree";

    // Decorators (collapsed to pills; should not appear as standalone nodes)
    public const string Inverter         = "bt.decorator.inverter";
    public const string Repeater         = "bt.decorator.repeater";
    public const string Cooldown         = "bt.decorator.cooldown";
    public const string ForceSuccess     = "bt.decorator.forceSuccess";
    public const string ForceFailure     = "bt.decorator.forceFailure";
    public const string UntilSuccess     = "bt.decorator.untilSuccess";
    public const string UntilFailure     = "bt.decorator.untilFailure";

    /// <summary>Returns true when the given kind key identifies a leaf node.</summary>
    public static bool IsLeaf(NodeKindKey key)
    {
        var id = key.Id;
        return id == Action    ||
               id == Condition ||
               id == Wait      ||
               id == Subtree;
    }
}
