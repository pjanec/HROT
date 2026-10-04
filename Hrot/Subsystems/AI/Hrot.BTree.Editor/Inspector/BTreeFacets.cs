using Hrot.Editor.AiShared.Inspector.ActionBinding;
using StructEdit.Core.Attributes;

namespace Hrot.BTree.Editor.Inspector;

// ---- Leaf node facets -------------------------------------------------------

/// <summary>Inspector facet for Action leaf nodes.</summary>
public struct BTreeActionFacet
{
    /// <summary>⭐ <c>CE-417</c> slice 4b — the node's binding: method OR blueprint (slice 4c, B-1) + target variable,
    /// drawn by ONE <c>ActionBindingDrawer</c>.</summary>
    [EditDisplayName("Action")]
    [ActionBinding(BindingSlotKind.Action, allowsBlueprint: true)]
    public BehaviorActionBindingFacet Action;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;

    [EditReadOnly]
    public string LastResult;

    [EditReadOnly]
    public int TickCount;
}

/// <summary>Inspector facet for Condition leaf nodes.</summary>
public struct BTreeConditionFacet
{
    /// <summary>⭐ <c>CE-417</c> slice 4b — the node's binding: method OR blueprint (slice 4c, B-1) + target variable,
    /// drawn by ONE <c>ActionBindingDrawer</c>.</summary>
    [EditDisplayName("Condition")]
    [ActionBinding(BindingSlotKind.Guard, allowsBlueprint: true)]
    public BehaviorActionBindingFacet Condition;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;

    [EditReadOnly]
    public string LastResult;

    [EditReadOnly]
    public int TickCount;
}

/// <summary>Inspector facet for Wait leaf nodes.</summary>
public struct BTreeWaitFacet
{
    [EditDisplayName("Duration (seconds)")]
    [EditUnit("seconds")]
    [EditRange(0.0, 600.0)]
    public float Duration;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;
}

// ---- Composite node facets --------------------------------------------------

/// <summary>Inspector facet for Sequence composite nodes.</summary>
public struct BTreeSequenceFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;

    [EditReadOnly]
    public int ChildCount;
}

/// <summary>Inspector facet for Selector composite nodes.</summary>
public struct BTreeSelectorFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;

    [EditReadOnly]
    public int ChildCount;
}

/// <summary>Inspector facet for ObserverSelector composite nodes.</summary>
public struct BTreeObserverSelectorFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;

    [EditReadOnly]
    public int ChildCount;
}

/// <summary>Inspector facet for Parallel composite nodes.</summary>
public struct BTreeParallelFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;

    [EditReadOnly]
    public int ChildCount;
}

/// <summary>Inspector facet for the Root node.</summary>
public struct BTreeRootFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}

/// <summary>⭐ <c>CE-2079</c> — inspector facet for an SOP order ("Do when idle" / "React"): the behaviour it starts, the
/// variable holding that behaviour's params (composed on pick), and — for React — how urgent it is.</summary>
public struct BTreeSopOrderFacet
{
    [EditReadOnly]
    [EditDisplayName("Order")]
    public string Kind;

    [EditDisplayName("Behaviour")]
    [Hrot.Editor.AiShared.Inspector.AiBehaviorPicker]
    public string BehaviorName;

    /// <summary>Composed when a behaviour is picked (a variable of its params type, edited in the blackboard); empty =
    /// the behaviour's authored defaults.</summary>
    [EditReadOnly]
    [EditDisplayName("Params variable")]
    public string ParamsVariable;

    [EditDisplayName("Urgency (React)")]
    public Hrot.AiEditor.Persistence.BTree.SopUrgencyDto Urgency;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;
}

/// <summary>Inspector facet for Subtree leaf nodes.</summary>
public struct BTreeSubtreeFacet
{
    // ⭐⭐ §S1 ① — PICKED, not typed. 🔒 User, 2026-09-26: "the tree asset must be pickable."
    // 🔴 This field was labelled "Referenced asset" and was plain free text: a designer typed an
    //    asset name by hand and learned it was wrong from a validator. The same picker now serves
    //    the HSM state facet, so the two editors cannot drift.
    [EditDisplayName("Referenced asset")]
    [Hrot.Editor.AiShared.Inspector.AiAssetPicker(Hrot.Editor.AiShared.AssetKind.BTree)]
    public string SubtreeName;

    [EditReadOnly]
    public string SubtreeAssetId;

    [EditReadOnly]
    public bool IsResolved;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditDisplayName("Breakpoint")]
    public bool IsBreakpoint;

    [EditReadOnly]
    public string VisualId;
}

// ---- Decorator pill facets --------------------------------------------------

/// <summary>Inspector facet for Repeater decorator pills.</summary>
public struct BTreeRepeaterFacet
{
    [EditDisplayName("Count")]
    [EditRange(1, 9999)]
    public int Count;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}

/// <summary>Inspector facet for Cooldown decorator pills.</summary>
public struct BTreeCooldownFacet
{
    [EditDisplayName("Duration (seconds)")]
    [EditUnit("seconds")]
    [EditRange(0.0, 600.0)]
    public float Duration;

    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}

/// <summary>Inspector facet for Inverter decorator pills.</summary>
public struct BTreeInverterFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}

/// <summary>Inspector facet for ForceSuccess decorator pills.</summary>
public struct BTreeForceSuccessFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}

/// <summary>Inspector facet for ForceFailure decorator pills.</summary>
public struct BTreeForceFailureFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}

/// <summary>Inspector facet for UntilSuccess decorator pills.</summary>
public struct BTreeUntilSuccessFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}

/// <summary>Inspector facet for UntilFailure decorator pills.</summary>
public struct BTreeUntilFailureFacet
{
    [EditDisplayName("Comment")]
    public string? Comment;

    [EditReadOnly]
    public string VisualId;
}
