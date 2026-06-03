using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.NodeDrawers;

/// <summary>
/// Palette <see cref="NodeKindDescriptor"/> factories for the full set of built-in
/// Blueprint <see cref="Node"/> subtypes (the kinds declared in
/// <c>Hrot.Blueprints.Compiler/Assets/Nodes.cs</c>), so the TAB / wire-drop node
/// picker offers the complete blueprint vocabulary rather than just the
/// When/EQS reactive trio.
/// <para>
/// Pins are <b>not</b> hand-authored here: each <see cref="NodeKindDescriptor.CreateInstance"/>
/// returns a default-constructed typed node with empty <see cref="Node.Pins"/>, and the
/// editor projection hydrates the canonical pin list via
/// <see cref="Host.NodePinSchema"/> at render time (projection-only — nothing is persisted).
/// The When/ReadEqsResult/SpawnEqsSensor kinds keep their hand-authored pins via
/// <see cref="WhenNodePaletteEntries"/>; they are registered separately.
/// </para>
/// </summary>
public static class BlueprintNodePaletteEntries
{
    /// <summary>Category names used for picker grouping (mirrors the demo's FakeNodeCatalog).</summary>
    public static class Categories
    {
        public const string FlowControl = "Flow Control";
        public const string Variables   = "Variables";
        public const string Function    = "Function";
        public const string Event       = "Events";
        public const string Array       = "Array";
        public const string Latent      = "Latent";
        public const string Channel     = "Channel";
        public const string Utility     = "Utility";
        public const string Squad       = "Squad";
        public const string Decision    = "Decision";
    }

    /// <summary>
    /// Returns the full set of descriptors for the core Blueprint node kinds.
    /// Does NOT include When/ReadEqsResult/SpawnEqsSensor (those come from
    /// <see cref="WhenNodePaletteEntries"/>).
    /// </summary>
    public static IEnumerable<NodeKindDescriptor> All()
    {
        // ── Flow control ───────────────────────────────────────────────────
        yield return Make<BranchNode>(
            "Branch", "Branch", Categories.FlowControl,
            "Branch execution on a boolean condition (True / False).");
        yield return Make<SequenceNode>(
            "Sequence", "Sequence", Categories.FlowControl,
            "Fire each output pin in order.");
        yield return Make<ReturnNode>(
            "Return", "Return", Categories.FlowControl,
            "Return from the current function / event graph.");

        // ── Events ─────────────────────────────────────────────────────────
        yield return Make<EventEntryNode>(
            "EventEntry", "Event Entry", Categories.Event,
            "Entry point fired by an engine event.");
        yield return Make<CallCustomEventNode>(
            "CallCustomEvent", "Call Custom Event", Categories.Event,
            "Invoke a custom event declared on this blueprint.");
        yield return Make<CallEventDispatcherNode>(
            "CallDispatcher", "Call Event Dispatcher", Categories.Event,
            "Broadcast an event dispatcher to all bound listeners.");
        yield return Make<BindEventDispatcherNode>(
            "BindDispatcher", "Bind Event Dispatcher", Categories.Event,
            "Bind a handler to an event dispatcher.");
        yield return Make<WaitForEventNode>(
            "WaitForEvent", "Wait For Event", Categories.Event,
            "Latent: suspend until a matching event fires.");

        // ── Variables ──────────────────────────────────────────────────────
        yield return Make<GetVariableNode>(
            "GetVariable", "Get Variable", Categories.Variables,
            "Read a blueprint variable's value (pure).");
        yield return Make<SetVariableNode>(
            "SetVariable", "Set Variable", Categories.Variables,
            "Write a blueprint variable's value.");

        // ── Function / data ────────────────────────────────────────────────
        yield return Make<FunctionCallNode>(
            "FunctionCall", "Function Call", Categories.Function,
            "Call a method on a target type.");
        yield return Make<LiteralNode>(
            "Literal", "Literal", Categories.Function,
            "A constant value of a chosen type.");
        yield return Make<CastNode>(
            "Cast", "Cast", Categories.Function,
            "Cast a value to a target type.");

        // ── Array ──────────────────────────────────────────────────────────
        yield return Make<ArrayMakeNode>(
            "ArrayMake", "Make Array", Categories.Array,
            "Construct an array from element inputs.");
        yield return Make<ArrayGetNode>(
            "ArrayGet", "Get Array Element", Categories.Array,
            "Read an element from an array by index.");

        // ── Latent ─────────────────────────────────────────────────────────
        yield return Make<LatentDelayNode>(
            "Delay", "Delay", Categories.Latent,
            "Latent: pause execution for a duration.");

        // ── Peers / channels ───────────────────────────────────────────────
        yield return Make<CallPeerBlueprintNode>(
            "CallPeerBlueprint", "Call Peer Blueprint", Categories.Function,
            "Call a function on a peer blueprint instance.");
        yield return Make<ChannelCommandNode>(
            "ChannelCommand", "Channel Command", Categories.Channel,
            "Issue a command on an actuator channel.");
        yield return Make<WaitForChannelNode>(
            "WaitForChannel", "Wait For Channel", Categories.Channel,
            "Latent: suspend until a channel completes / reports.");

        // ── Decision (utility AI) ──────────────────────────────────────────
        yield return Make<ScoreDecisionNode>(
            "ScoreDecision", "Score Decision", Categories.Decision,
            "Evaluate a UtilityDecisionDef and output the winning option.");
        yield return Make<ReadRankedResultNode>(
            "ReadRankedResult", "Read Ranked Result", Categories.Decision,
            "Read the rank-i entry from the utility result buffer.");

        // ── Squad coordination primitives ──────────────────────────────────
        yield return Make<PartitionElementsNode>(
            "PartitionElements", "Partition Elements", Categories.Squad,
            "Partition squad members into N elements.");
        yield return Make<AssignRolesNode>(
            "AssignRoles", "Assign Roles", Categories.Squad,
            "Assign roles to squad members via greedy matrix.");
        yield return Make<AdvancePhaseNode>(
            "AdvancePhase", "Advance Phase", Categories.Squad,
            "Advance the squad phase sequencer one step.");
        yield return Make<AcquireSlotNode>(
            "AcquireSlot", "Acquire Slot", Categories.Squad,
            "Acquire the next available slot from the rotation ring.");
    }

    /// <summary>
    /// Factory for a descriptor whose <c>CreateInstance</c> returns a fresh, default-constructed
    /// node of type <typeparamref name="TNode"/> with a new <see cref="Node.Id"/> and empty pins
    /// (the projection hydrates pins).
    /// </summary>
    private static NodeKindDescriptor Make<TNode>(
        string kind, string displayName, string category, string tooltip)
        where TNode : Node, new()
        => new()
        {
            Kind        = kind,
            DisplayName = displayName,
            Category    = category,
            Tooltip     = tooltip,
            Icon        = "",
            CreateInstance = () => new TNode { Id = Guid.NewGuid() },
        };
}
