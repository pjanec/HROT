using Fbt;
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

    // Encoded leaf kind prefixes (D-02)
    public const string ActionPrefix    = "bt.leaf.action::";
    public const string ConditionPrefix = "bt.leaf.condition::";

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
               id == Subtree   ||
               id.StartsWith(ActionPrefix) ||
               id.StartsWith(ConditionPrefix);
    }

    /// <summary>Returns true when the given kind key identifies a decorator node.</summary>
    public static bool IsDecorator(NodeKindKey key)
    {
        var id = key.Id;
        return id == Inverter     ||
               id == Repeater     ||
               id == Cooldown     ||
               id == ForceSuccess ||
               id == ForceFailure ||
               id == UntilSuccess ||
               id == UntilFailure;
    }

    /// <summary>
    /// Tries to parse an encoded leaf action/condition kind id.
    /// Returns true and strips the prefix for encoded ids; false otherwise.
    /// </summary>
    public static bool TryParseLeafActionKind(string kindId, out string fqn, out bool isCondition)
    {
        if (kindId.StartsWith(ActionPrefix))
        {
            fqn = kindId.Substring(ActionPrefix.Length);
            isCondition = false;
            return true;
        }
        if (kindId.StartsWith(ConditionPrefix))
        {
            fqn = kindId.Substring(ConditionPrefix.Length);
            isCondition = true;
            return true;
        }
        fqn = "";
        isCondition = false;
        return false;
    }

    /// <summary>Maps a catalog kind-ID string to the corresponding kernel NodeType.</summary>
    public static NodeType KindIdToNodeType(string kindId)
    {
        if (kindId.StartsWith(ActionPrefix))
            return NodeType.Action;
        if (kindId.StartsWith(ConditionPrefix))
            return NodeType.Condition;

        return kindId switch
        {
            Root             => NodeType.Root,
            Sequence         => NodeType.Sequence,
            Selector         => NodeType.Selector,
            ObserverSelector => NodeType.ObserverSelector,
            Parallel         => NodeType.Parallel,
            Action           => NodeType.Action,
            Condition        => NodeType.Condition,
            Wait             => NodeType.Wait,
            Subtree          => NodeType.Subtree,
            Inverter         => NodeType.Inverter,
            Repeater         => NodeType.Repeater,
            Cooldown         => NodeType.Cooldown,
            ForceSuccess     => NodeType.ForceSuccess,
            ForceFailure     => NodeType.ForceFailure,
            UntilSuccess     => NodeType.UntilSuccess,
            UntilFailure     => NodeType.UntilFailure,
            _                => NodeType.Action,
        };
    }
}
