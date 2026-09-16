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
    /// <summary>
    /// Alias for the math picker groups (BP-04). The native value ops live alongside the
    /// <c>BlueprintMath</c> helper entries rather than in a separate group, so a designer looking
    /// for "less than" finds it in one place.
    /// </summary>
    private static class MathCategories
    {
        public const string Math        = BlueprintMathPaletteEntries.Categories.Math;
        public const string MathCompare = BlueprintMathPaletteEntries.Categories.MathCompare;
        public const string MathBool    = BlueprintMathPaletteEntries.Categories.MathBool;
    }

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
        /// <summary>
        /// CA-02 — real ECS component reads (<c>GetComponentNode</c>), distinct from
        /// <see cref="SharedState"/> (a Blueprint-authored slot) and <see cref="Variables"/> (a
        /// blueprint-local declaration): a component is discovered by reflecting the engine's live
        /// <c>[ComponentId]</c>-marked ECS component types, not authored/declared in this asset.
        /// </summary>
        public const string Component   = "Component";
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
        // BP-09: CallDispatcher / BindDispatcher palette entries REMOVED. Both node kinds have no
        // Stage5_Schedule lowering -- they fall to the generic `default:` branch, emit a BP4004
        // warning and no IR, so a graph using them compiles "successfully" and does nothing at
        // runtime. The dispatcher model is superseded by PublishEvent + EventEntry subscribe.
        // The node classes remain (assets may still deserialize them); only the front door is gone.
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
        yield return Make<GetAllParametersNode>(
            "GetAllParameters", "Get All Parameters", Categories.Variables,
            "Read all of this blueprint's declared Parameters at once (pure) -- one output pin per Parameter.");

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

        // ── Native value ops (BP-04) ───────────────────────────────────────
        // Compare / BinaryOp / BooleanOp / Not are fully lowered and compile-tested but previously
        // had NO palette entry at all, so they were reachable only from hand-authored JSON. One
        // baked entry per operator enum value -- the operator is a fixed property, so a picker row
        // per value is friendlier than one row plus a drawer, and needs no drawer at all.
        //
        // Baking works because BlueprintCommandSink.CreateAssetNode builds the node via the
        // descriptor's CreateInstance and only *overlays* caller-supplied props afterwards; it does
        // NOT round-trip through ApplyInitialProperties' 8-of-50 whitelist. Same recipe as
        // MakeMath / WhenNodePaletteEntries.
        //
        // Pins are intentionally left empty: these are asset-authored-pin kinds, and Stage0_Rehydrate
        // deterministically reconstructs A/B/Result for a pin-less instance (proven by
        // DeterministicPinReconstructionTests).

        // Compare -- 6 operators, boolean result.
        yield return MakeBaked<CompareNode>("Compare.Equal", "A == B",
            MathCategories.MathCompare, "True when A equals B.",
            n => n.Operator = ComparisonOperator.Equal);
        yield return MakeBaked<CompareNode>("Compare.NotEqual", "A != B",
            MathCategories.MathCompare, "True when A does not equal B.",
            n => n.Operator = ComparisonOperator.NotEqual);
        yield return MakeBaked<CompareNode>("Compare.LessThan", "A < B",
            MathCategories.MathCompare, "True when A is less than B.",
            n => n.Operator = ComparisonOperator.LessThan);
        yield return MakeBaked<CompareNode>("Compare.LessThanOrEqual", "A <= B",
            MathCategories.MathCompare, "True when A is less than or equal to B.",
            n => n.Operator = ComparisonOperator.LessThanOrEqual);
        yield return MakeBaked<CompareNode>("Compare.GreaterThan", "A > B",
            MathCategories.MathCompare, "True when A is greater than B.",
            n => n.Operator = ComparisonOperator.GreaterThan);
        yield return MakeBaked<CompareNode>("Compare.GreaterThanOrEqual", "A >= B",
            MathCategories.MathCompare, "True when A is greater than or equal to B.",
            n => n.Operator = ComparisonOperator.GreaterThanOrEqual);

        // BinaryOp -- 5 operators; result type is the OPERAND type, not bool.
        yield return MakeBaked<BinaryOpNode>("BinaryOp.Add", "A + B",
            MathCategories.Math, "Add two operands; the result has the operand type.",
            n => n.Operator = ArithmeticOperator.Add);
        yield return MakeBaked<BinaryOpNode>("BinaryOp.Subtract", "A - B",
            MathCategories.Math, "Subtract B from A; the result has the operand type.",
            n => n.Operator = ArithmeticOperator.Subtract);
        yield return MakeBaked<BinaryOpNode>("BinaryOp.Multiply", "A × B",
            MathCategories.Math, "Multiply two operands; the result has the operand type.",
            n => n.Operator = ArithmeticOperator.Multiply);
        yield return MakeBaked<BinaryOpNode>("BinaryOp.Divide", "A ÷ B",
            MathCategories.Math, "Divide A by B; the result has the operand type.",
            n => n.Operator = ArithmeticOperator.Divide);
        yield return MakeBaked<BinaryOpNode>("BinaryOp.Modulo", "A % B",
            MathCategories.Math, "Remainder of A divided by B; the result has the operand type.",
            n => n.Operator = ArithmeticOperator.Modulo);

        // BooleanOp / Not -- boolean result. Data-flow, so NOT short-circuiting: both operands are
        // resolved before combining. Use nested Branch when short-circuit matters.
        yield return MakeBaked<BooleanOpNode>("BooleanOp.And", "A && B",
            MathCategories.MathBool, "True when both operands are true (no short-circuit).",
            n => n.Operator = BooleanOperator.And);
        yield return MakeBaked<BooleanOpNode>("BooleanOp.Or", "A || B",
            MathCategories.MathBool, "True when either operand is true (no short-circuit).",
            n => n.Operator = BooleanOperator.Or);
        yield return Make<NotNode>("Not", "!A",
            MathCategories.MathBool, "Logical negation of a boolean operand.");

        // ── Array ──────────────────────────────────────────────────────────
        // BP-09/BP-16: ArrayMake / ArrayGet palette entries REMOVED. Since BP-16 they are rejected
        // at Stage2 with a BP1420 error, so offering them in the picker would let a designer place
        // a node that is guaranteed to break the build. The node classes remain so existing assets
        // still deserialize (and then fail loudly). Use a fixed-capacity list variable instead.

        // ── Latent ─────────────────────────────────────────────────────────
        yield return Make<LatentDelayNode>(
            "Delay", "Delay", Categories.Latent,
            "Latent: pause execution for a duration.");

        // ── Utility (BP-108: Print String / Format String) ─────────────────
        // Both default-construct with an empty Format (⭐ Pins EMPTY -- NodePinSchema.GetCanonicalPins
        // would otherwise shadow the format-derived pins with this default, pin-less instance).
        // Post-placement editing (Format/Level/ResultTypeId) is via PrintStringNodeDrawer /
        // FormatStringNodeDrawer (registered in BlueprintEditorBootstrap).
        yield return Make<PrintStringNode>(
            "PrintString", "Print String", Categories.Utility,
            "Format a message and write it to the AI Behaviors log at the chosen level.");
        yield return Make<FormatStringNode>(
            "FormatString", "Format String", Categories.Utility,
            "Format a message into a FixedString result (pure) -- Unreal's Format Text.");

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
        // BP-09: PartitionElements / AssignRoles / AdvancePhase / AcquireSlot palette entries
        // REMOVED. Each one's doc comment claims it "wraps" a real FDP primitive, but that wiring
        // was never implemented: none has a Stage5_Schedule case, so all four fall to the generic
        // `default:` branch (BP4004 warning, no IR) and are silent no-ops at runtime. The quartet is
        // superseded by MemberSlotList / SlotRotation. Inviting descriptions on nodes that do
        // nothing are worse than no entry at all.
        // The node classes remain (assets may still deserialize them); only the front door is gone.
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
    /// <see cref="Make{TNode}"/> plus a <paramref name="configure"/> step run on each created
    /// instance, so a palette row can bake a fixed property value (BP-04). Used for operator-keyed
    /// kinds where one picker row per enum value beats one row plus a drawer.
    /// </summary>
    private static NodeKindDescriptor MakeBaked<TNode>(
        string kind, string displayName, string category, string tooltip, Action<TNode> configure)
        where TNode : Node, new()
        => new()
        {
            Kind        = kind,
            DisplayName = displayName,
            Category    = category,
            Tooltip     = tooltip,
            Icon        = CategoryIcon(category),
            CreateInstance = () =>
            {
                var node = new TNode { Id = Guid.NewGuid() };
                configure(node);
                return node;
            },
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
        Categories.Component   => "bp/variable_get",
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
