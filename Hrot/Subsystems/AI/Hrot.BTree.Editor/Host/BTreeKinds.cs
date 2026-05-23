using NodeEditor.Primitives;

namespace Hrot.BTree.Editor.Host;

/// <summary>
/// Well-known NodeKindKey strings used by the BTree editor.
/// The BTreeNodeCatalog registers entries under these IDs.
/// </summary>
internal static class BTreeKinds
{
    // Composites
    public const string Root             = "bt.root";
    public const string Sequence         = "bt.sequence";
    public const string Selector         = "bt.selector";
    public const string Parallel         = "bt.parallel";
    public const string ObserverSelector = "bt.observer_selector";

    // Leaves
    public const string Action           = "bt.action";
    public const string Condition        = "bt.condition";
    public const string Wait             = "bt.wait";
    public const string Subtree          = "bt.subtree";

    // Decorators (collapsed to pills; should not appear as standalone nodes)
    public const string Inverter         = "bt.inverter";
    public const string Repeater         = "bt.repeater";
    public const string Cooldown         = "bt.cooldown";
    public const string ForceSuccess     = "bt.force_success";
    public const string ForceFailure     = "bt.force_failure";
    public const string UntilSuccess     = "bt.until_success";
    public const string UntilFailure     = "bt.until_failure";

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
