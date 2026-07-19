using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor.ActionCatalog;
using Hrot.Blueprints.Editor.Host;

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
/// <para>
/// <b>AN4 — Per-action palette (D-B):</b>
/// <see cref="ChannelCommandNode"/> entries are <b>not</b> emitted by <see cref="All()"/>; the
/// caller must instead call <see cref="ChannelCommandEntries"/> with an
/// <see cref="IChannelCommandCatalog"/> to get one concrete entry per channel-command action.
/// Each entry bakes <see cref="ChannelCommandNode.ChannelType"/> (short class name) and
/// <see cref="ChannelCommandNode.ActionId"/> directly in <c>CreateInstance</c>, so a
/// placed node is immutably pre-configured.  The generic "pick action later" entry has
/// been removed (D-B: no chameleon hazard).
/// </para>
/// </summary>
public static class BlueprintNodePaletteEntries
{
    /// <summary>Category names used for picker grouping (mirrors the demo's FakeNodeCatalog).</summary>
    public static class Categories
    {
        public const string FlowControl = "Flow Control";
        public const string Variables   = "Variables";
        /// <summary>
        /// Slice 2a-3 — entity-scoped Blueprint shared state (<c>GetSharedNode</c>/
        /// <c>SetSharedNode</c>). Kept distinct from <see cref="Variables"/> because a shared
        /// slot is a foreign Category-1 struct keyed by a manifest-provisioned name, not a
        /// blueprint-local <c>VariableDecl</c> — grouping them together would blur that
        /// distinction in the picker.
        /// </summary>
        public const string SharedState = "Shared State";
        public const string Function    = "Function";
        /// <summary>Constant/literal value nodes — kept out of Function so designers see them distinctly.</summary>
        public const string Literal     = "Literal";
        public const string Event       = "Events";
        public const string Array       = "Array";
        public const string Latent      = "Latent";
        public const string Channel     = "Channel";
        public const string Utility     = "Utility";
        public const string Squad       = "Squad";
        public const string Decision    = "Decision";
        /// <summary>
        /// AN7 — non-channel behavior actions (SharedAiAction / AiPrimitive BlueprintCall),
        /// grouped by the declaring-type short name within this top-level category.
        /// E.g. <c>"Action/SomeActionsClass"</c>.
        /// </summary>
        public const string Action      = "Action";
    }

    /// <summary>
    /// Returns the full set of descriptors for the core Blueprint node kinds.
    /// Does NOT include When/ReadEqsResult/SpawnEqsSensor (those come from
    /// <see cref="WhenNodePaletteEntries"/>).
    /// Does NOT include ChannelCommandNode entries — use <see cref="ChannelCommandEntries"/>
    /// instead (AN4: one entry per action, action baked at creation).
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

        // ── Shared State (Slice 2a-3) ──────────────────────────────────────
        // GetSharedNode/SetSharedNode default-construct with empty VariableId/SharedTypeId;
        // both are editable post-placement via GetSharedNodeDrawer/SetSharedNodeDrawer
        // (Hrot.Blueprints.Editor.NodeDrawers.SharedNodeDrawers), the same
        // IBlueprintNodeDrawer/INodeEditSession Details-panel mechanism used by
        // FunctionCallNode/LiteralNode — see BlueprintEditorBootstrap.CreateNodeDrawerRegistry.
        yield return Make<GetSharedNode>(
            "GetShared", "Get Shared", Categories.SharedState,
            "Read an entity-scoped shared struct slot (pure).");
        yield return Make<SetSharedNode>(
            "SetShared", "Set Shared", Categories.SharedState,
            "Write an entity-scoped shared struct slot.");

        // ── Function / data ────────────────────────────────────────────────
        yield return Make<FunctionCallNode>(
            "FunctionCall", "Function Call", Categories.Function,
            "Call a method on a target type.");

        // Typed literal nodes — strongly-typed so the wire-drop picker's
        // nodes.by-pin filter (p.Type == q.SourceType) correctly matches
        // them to a dragged data pin.
        yield return new NodeKindDescriptor
        {
            Kind           = "LiteralBool",
            DisplayName    = "Literal (Boolean)",
            Category       = Categories.Literal,
            Tooltip        = "A boolean constant (true/false).",
            Icon           = "bp/pure",
            CreateInstance = () => new LiteralNode { Id = Guid.NewGuid(), TypeId = BlueprintTypeSystem.Bool },
        };
        yield return new NodeKindDescriptor
        {
            Kind           = "LiteralInt",
            DisplayName    = "Literal (Integer)",
            Category       = Categories.Literal,
            Tooltip        = "An integer constant.",
            Icon           = "bp/pure",
            CreateInstance = () => new LiteralNode { Id = Guid.NewGuid(), TypeId = BlueprintTypeSystem.Int32 },
        };
        yield return new NodeKindDescriptor
        {
            Kind           = "LiteralFloat",
            DisplayName    = "Literal (Float)",
            Category       = Categories.Literal,
            Tooltip        = "A floating-point constant.",
            Icon           = "bp/pure",
            CreateInstance = () => new LiteralNode { Id = Guid.NewGuid(), TypeId = BlueprintTypeSystem.Single },
        };
        yield return new NodeKindDescriptor
        {
            Kind           = "LiteralDouble",
            DisplayName    = "Literal (Double)",
            Category       = Categories.Literal,
            Tooltip        = "A double-precision constant.",
            Icon           = "bp/pure",
            CreateInstance = () => new LiteralNode { Id = Guid.NewGuid(), TypeId = BlueprintTypeSystem.Float64 },
        };
        yield return new NodeKindDescriptor
        {
            Kind           = "LiteralString",
            DisplayName    = "Literal (String)",
            Category       = Categories.Literal,
            Tooltip        = "A string constant.",
            Icon           = "bp/pure",
            CreateInstance = () => new LiteralNode { Id = Guid.NewGuid(), TypeId = BlueprintTypeSystem.String },
        };
        yield return new NodeKindDescriptor
        {
            Kind           = "LiteralByte",
            DisplayName    = "Literal (Byte)",
            Category       = Categories.Literal,
            Tooltip        = "A byte constant.",
            Icon           = "bp/pure",
            CreateInstance = () => new LiteralNode { Id = Guid.NewGuid(), TypeId = BlueprintTypeSystem.Byte },
        };

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
        // NOTE: ChannelCommandNode is NOT emitted here (AN4).
        // Use ChannelCommandEntries(catalog) for per-action entries.
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
    /// <summary>
    /// AN4 — Generates one palette entry per channel-command action from
    /// <paramref name="catalog"/>, over the single <see cref="ChannelCommandNode"/> kind.
    /// Each entry's <c>CreateInstance</c> bakes the action's
    /// <see cref="ChannelCommandNode.ChannelType"/> (short class name, e.g.
    /// <c>"LocomotionChannel"</c>) and <see cref="ChannelCommandNode.ActionId"/>
    /// (action name, e.g. <c>"MoveTo"</c>) directly on the new node.
    /// <para>
    /// This replaces the single generic <c>"ChannelCommand"</c> entry that was previously
    /// in <see cref="All()"/> (D-B decision: action is baked at create time, no chameleon
    /// hazard, no mutable dropdown on an existing node).
    /// </para>
    /// <para>
    /// <b>Kind:</b> <c>"ChannelCommand:{ChannelShortName}:{ActionId}"</c> — unique per entry so
    /// each descriptor occupies its own <see cref="NodeKindRegistry"/> slot and the canvas
    /// can round-trip the placement via the correct baked-in factory.<br/>
    /// <b>DisplayName:</b> <c>"{ChannelFriendlyName} / {ActionId}"</c> (e.g. "Locomotion / MoveTo").<br/>
    /// <b>Category:</b> <c>"Channel/{ChannelFriendlyName}"</c> (e.g. "Channel/Locomotion")
    /// — groups actions by channel in the picker.
    /// </para>
    /// <para>
    /// If <paramref name="catalog"/> is null or empty, returns an empty sequence.
    /// </para>
    /// </summary>
    /// <param name="catalog">Source of channel-command entries.</param>
    public static IEnumerable<NodeKindDescriptor> ChannelCommandEntries(
        IChannelCommandCatalog? catalog)
    {
        if (catalog == null)
            yield break;

        foreach (var entry in catalog.GetEntries())
        {
            // Short class name used both in the node field and in the category / display name.
            var channelShortName = LastSegment(entry.ChannelTypeFqn);

            // Friendly prefix: strip the "Channel" suffix if present
            // (e.g. "LocomotionChannel" → "Locomotion") for cleaner display.
            var channelFriendly  = StripChannelSuffix(channelShortName);

            // Unique kind id: "ChannelCommand:{ChannelShortName}:{ActionId}"
            // Each per-action entry gets a distinct registry slot.
            var kind        = $"ChannelCommand:{channelShortName}:{entry.Name}";
            var displayName = $"{channelFriendly} / {entry.Name}";
            var category    = $"{Categories.Channel}/{channelFriendly}";
            var tooltip     = $"Issue the {entry.Name} command on the {channelFriendly} channel.";

            // Capture loop variables for the closure.
            var bakedChannelType = channelShortName; // short name matched by NodePinSchema + compiler
            var bakedActionId    = entry.Name;        // action name matched by catalog

            yield return new NodeKindDescriptor
            {
                Kind        = kind,
                DisplayName = displayName,
                Category    = category,
                Tooltip     = tooltip,
                Icon        = "",
                // AN4: bake ChannelType + ActionId at create-time — node is immutably pre-configured.
                CreateInstance = () => new ChannelCommandNode
                {
                    Id          = Guid.NewGuid(),
                    ChannelType = bakedChannelType,
                    ActionId    = bakedActionId,
                },
            };
        }
    }

    /// <summary>
    /// AN7 — Generates one palette entry per Blueprint-valid NON-channel action from
    /// <paramref name="catalog"/> (i.e. entries where <see cref="BehaviorActionHosts.Blueprint"/>
    /// is set AND <see cref="BehaviorActionSource"/> is NOT <see cref="BehaviorActionSource.ChannelCommand"/>).
    /// <para>
    /// Each entry's <c>CreateInstance</c> bakes the action's FQN into
    /// <see cref="ChannelCommandNode.ActionFqn"/> and leaves
    /// <see cref="ChannelCommandNode.ChannelType"/> / <see cref="ChannelCommandNode.ActionId"/>
    /// empty, signalling a non-channel invocation.  The placed node is immutably pre-configured
    /// (D-B: action baked at creation).
    /// </para>
    /// <para>
    /// <b>Kind:</b> <c>"Action:{FQN}"</c> — unique per entry.<br/>
    /// <b>DisplayName:</b> <c>"{Category} / {MethodName}"</c>
    ///   (e.g. <c>"SomeActionsClass / DoThing"</c>).<br/>
    /// <b>Category:</b> <c>"Action/{DeclaringTypeShortName}"</c>
    ///   — groups actions by declaring type in the picker.
    /// </para>
    /// <para>
    /// If <paramref name="catalog"/> is null or has no Blueprint-valid non-channel entries,
    /// returns an empty sequence.
    /// </para>
    /// <para>
    /// <b>Compile note (AN8):</b> nodes created from these entries will NOT compile until
    /// AN8 implements the non-channel lowering path in Stage5.  Placing such a node in a
    /// canvas-authored asset is headless-safe; do not include them in any asset that passes
    /// through the generator in committed tests.
    /// </para>
    /// </summary>
    /// <param name="catalog">Source of unified behavior-action entries (AN3).</param>
    public static IEnumerable<NodeKindDescriptor> NonChannelActionEntries(
        IBehaviorActionCatalog? catalog)
    {
        if (catalog == null)
            yield break;

        foreach (var entry in catalog.GetActions(BehaviorActionHosts.Blueprint))
        {
            // Skip channel-command entries — those are handled by ChannelCommandEntries().
            if (entry.Source == BehaviorActionSource.ChannelCommand)
                continue;

            // Unique kind: "Action:{FQN}" — one slot per non-channel action.
            var kind        = $"Action:{entry.Id}";
            var category    = $"{Categories.Action}/{entry.Category ?? LastSegment(entry.Id)}";
            var displayName = $"{entry.Category ?? LastSegment(entry.Id)} / {entry.DisplayName}";
            var tooltip     = $"Invoke the {entry.DisplayName} non-channel behavior action. (AN8: compile lowering pending)";

            // Capture for closure.
            var bakedFqn        = entry.Id;           // FQN is the canonical identity (AQ2).
            var bakedParamsFqn  = entry.ParamsTypeFqn; // AN8: bake ParamsTypeFqn for compiler.

            yield return new NodeKindDescriptor
            {
                Kind        = kind,
                DisplayName = displayName,
                Category    = category,
                Tooltip     = tooltip,
                Icon        = "",
                // AN7/AN8: bake ActionFqn + ActionParamsTypeFqn at create-time.
                // ChannelType + ActionId remain empty (non-channel path).
                CreateInstance = () => new ChannelCommandNode
                {
                    Id                   = Guid.NewGuid(),
                    ActionFqn            = bakedFqn,
                    ActionParamsTypeFqn  = bakedParamsFqn,
                },
            };
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

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
            Icon        = CategoryIcon(category),
            CreateInstance = () => new TNode { Id = Guid.NewGuid() },
        };

    /// <summary>
    /// Maps a picker category to an icon-atlas key (so the Add-Node picker rows are scannable,
    /// especially under "All"). Keys resolve via the shared SilkIconProvider.
    /// </summary>
    internal static string CategoryIcon(string category) => category switch
    {
        Categories.FlowControl => "bp/flow",
        Categories.Event       => "bp/event",
        Categories.Variables   => "bp/variable_get",
        Categories.SharedState => "bp/variable_get",
        Categories.Function    => "bp/function",
        Categories.Literal     => "bp/pure",
        Categories.Array       => "bp/macro",
        Categories.Latent      => "bt/wait",
        Categories.Channel     => "bp/function",
        Categories.Decision    => "bp/function",
        Categories.Squad       => "bp/function",
        Categories.Utility     => "bp/function",
        Categories.Action      => "bt/action",
        _                      => "bp/function",
    };

    /// <summary>Returns the last dotted segment of a fully-qualified type name.</summary>
    private static string LastSegment(string fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return fqn;
        var idx = fqn.LastIndexOf('.');
        return idx >= 0 ? fqn[(idx + 1)..] : fqn;
    }

    /// <summary>
    /// Strips the trailing "Channel" suffix from a class name if present,
    /// returning a cleaner friendly name (e.g. "LocomotionChannel" → "Locomotion").
    /// </summary>
    private static string StripChannelSuffix(string name)
    {
        const string suffix = "Channel";
        return name.EndsWith(suffix, StringComparison.Ordinal) && name.Length > suffix.Length
            ? name[..^suffix.Length]
            : name;
    }
}
