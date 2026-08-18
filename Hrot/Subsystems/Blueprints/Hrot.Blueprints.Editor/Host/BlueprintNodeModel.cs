using System.Numerics;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// <see cref="INodeModel"/> adapter projecting a <see cref="Hrot.Blueprints.Core.Assets.Node"/>
/// onto the NodeEdit canvas contract.
/// <para>
/// <see cref="Position"/> reads live from <see cref="Hrot.Blueprints.Core.Assets.NodeMetadata"/>
/// so it is always in sync with the asset after any mutation (no stale snapshot).
/// </para>
/// </summary>
internal sealed class BlueprintNodeModel : INodeModel
{
    private readonly List<IPinModel> _pins;
    // Keep the asset node reference so Position reads live from EditorMetadata.
    private readonly Hrot.Blueprints.Core.Assets.Node _node;

    public NodeId      Id               { get; }
    public NodeKindKey Kind             { get; }

    /// <summary>
    /// BP-17: an author-supplied title wins over the generated one. Read live from
    /// <c>EditorMetadata</c> so a rename shows up on the next frame without rebuilding the model.
    /// </summary>
    public string      Title            => string.IsNullOrWhiteSpace(_node.EditorMetadata.CustomTitle)
        ? GeneratedTitle
        : _node.EditorMetadata.CustomTitle!;

    /// <summary>
    /// BP-17: when a node carries a custom title, the generated one becomes the subtitle — a
    /// renamed node must not lose the only indication of what it actually is.
    /// </summary>
    public string?     Subtitle         => string.IsNullOrWhiteSpace(_node.EditorMetadata.CustomTitle)
        ? null
        : GeneratedTitle;

    /// <summary>The title derived from the node's kind and configuration (BP-17's fallback).</summary>
    internal string    GeneratedTitle   { get; }
    public NodeCategory Category        { get; }
    /// <summary>
    /// Reads live from the asset's <see cref="Hrot.Blueprints.Core.Assets.NodeMetadata"/>
    /// so it cannot go stale after a move or a <c>ChangeParentMultiple</c> command.
    /// </summary>
    public Vector2     Position         => new(_node.EditorMetadata.X, _node.EditorMetadata.Y);
    public Vector2?    SizeOverride     => null;
    /// <summary>
    /// <see cref="NodeState.Error"/> when this is a CLR <see cref="Hrot.Blueprints.Core.Assets.FunctionCallNode"/>
    /// whose target method can no longer be resolved (renamed/removed from C#) — the canvas then draws the
    /// red error outline and the reason shows in <see cref="StatusTooltip"/>.
    /// </summary>
    public NodeState   State            { get; }
    /// <summary>
    /// Punch-list #4: for a <see cref="Hrot.Blueprints.Core.Assets.FunctionCallNode"/> this carries the
    /// resolved signature + XML-doc summary shown on node hover (see <see cref="FunctionCallTooltip"/>);
    /// null for every other node kind.
    /// </summary>
    public string?     StatusTooltip    { get; }
    /// <summary>BP-18: editor-only view state, read live so the collapse glyph responds at once.</summary>
    public bool        IsCollapsed      => _node.EditorMetadata.Collapsed;
    public bool        ShowAdvancedPins => false;
    public NodeId?     ParentContainerId => null;
    /// <summary>
    /// UE-style: function-call nodes are marked with an italic <c>ƒ</c> in the header corner
    /// (native and Blueprint calls alike — they are told apart by workflow, not paint). Null for
    /// every other node kind.
    /// </summary>
    public string?     HeaderGlyph      { get; }
    public IReadOnlyList<IPinModel> Pins => _pins;

    /// <summary>
    /// Constructs a node model from a raw asset node, using the pre-resolved
    /// <paramref name="resolvedPins"/> list built by the two-pass GUID-binding algorithm.
    /// </summary>
    /// <param name="node">The asset node to project.</param>
    /// <param name="resolvedPins">The GUID-bound pin list built by the graph model.</param>
    /// <param name="asset">
    /// The owning asset, threaded so Get/Set variable titles can resolve the variable's
    /// declared NAME (instead of showing the raw <c>var:&lt;guid&gt;</c> id). May be null
    /// in unit tests that don't exercise variable nodes.
    /// </param>
    /// <param name="collectionPinWired">
    /// CA-07c/CA-07d-1: true when this node is one of the five component-collection CONSUMER kinds
    /// (<see cref="Hrot.Blueprints.Core.Assets.ComponentForEachNode"/>/
    /// <see cref="Hrot.Blueprints.Core.Assets.ComponentItemGetNode"/>/
    /// <see cref="Hrot.Blueprints.Core.Assets.ComponentItemCountNode"/>/
    /// <see cref="Hrot.Blueprints.Core.Assets.ComponentContainsNode"/>/
    /// <see cref="Hrot.Blueprints.Core.Assets.ComponentFindNode"/>) AND its "Collection" data-IN
    /// pin has an incoming link right now. Computed by the caller (<see cref="BlueprintGraphModel"/>,
    /// which has the graph's <c>Links</c> in scope) since this constructor otherwise has no
    /// connectivity signal. Drives the BP2066-mirroring stale-bake error check below; ignored for
    /// every other node kind. Defaults false so every other call site (including headless tests) is
    /// unaffected.
    /// </param>
    public BlueprintNodeModel(
        Hrot.Blueprints.Core.Assets.Node node,
        IReadOnlyList<IPinModel> resolvedPins,
        Hrot.Blueprints.Core.Assets.BlueprintAsset? asset = null,
        bool collectionPinWired = false)
    {
        _node     = node;
        Id        = new NodeId(node.Id);
        Kind      = new NodeKindKey(node.GetType().Name);
        GeneratedTitle = BuildTitle(node, asset);
        Category  = BuildCategory(node);
        _pins     = new List<IPinModel>(resolvedPins);
        StatusTooltip = node is Hrot.Blueprints.Core.Assets.FunctionCallNode fc
            ? FunctionCallTooltip.Build(fc, _pins)
            : null;
        // "ƒ" (U+0192) reads as UE's italic function mark and is in the canvas font range.
        HeaderGlyph = node is Hrot.Blueprints.Core.Assets.FunctionCallNode ? "ƒ" : null;

        // Validation: a CLR FunctionCall whose method no longer resolves (renamed/removed from C#)
        // is flagged as an error so it's obvious on the canvas, not silently mis-wired.
        if (node is Hrot.Blueprints.Core.Assets.FunctionCallNode fcErr && IsUnresolvedClrCall(fcErr))
        {
            State = NodeState.Error;
            StatusTooltip = $"⚠ Unresolved CLR method: {fcErr.TargetTypeId}.{fcErr.MethodName}\n"
                          + "It may have been renamed or removed from C#. Re-pick the function (add a new node).\n\n"
                          + (StatusTooltip ?? string.Empty);
        }
        // CA-02: reuses the same red-node pattern for a GetComponent node whose baked
        // ComponentTypeFqn no longer resolves (component renamed/removed from C#).
        else if (node is Hrot.Blueprints.Core.Assets.GetComponentNode gcnErr && IsUnresolvedComponent(gcnErr.ComponentTypeFqn))
        {
            State = NodeState.Error;
            StatusTooltip = $"⚠ Unresolved ECS component: {gcnErr.ComponentTypeFqn}\n"
                          + "It may have been renamed or removed from C#. Re-pick the component (add a new node).";
        }
        // CA-04: same stale-ref pattern for a SetComponent node whose baked ComponentTypeFqn no
        // longer resolves.
        else if (node is Hrot.Blueprints.Core.Assets.SetComponentNode scnErr && IsUnresolvedComponent(scnErr.ComponentTypeFqn))
        {
            State = NodeState.Error;
            StatusTooltip = $"⚠ Unresolved ECS component: {scnErr.ComponentTypeFqn}\n"
                          + "It may have been renamed or removed from C#. Re-pick the component (add a new node).";
        }
        // CA-07c/CA-07d-1: same stale-ref pattern for the five collection CONSUMER nodes -- their
        // ComponentTypeFqn is baked on WIRE (BlueprintCommandSink.TryBakeCollectionConsumer), not
        // picked from a dropdown, but a renamed/removed component still needs the same red-node signal.
        else if (node is (Hrot.Blueprints.Core.Assets.ComponentForEachNode
                       or Hrot.Blueprints.Core.Assets.ComponentItemGetNode
                       or Hrot.Blueprints.Core.Assets.ComponentItemCountNode
                       or Hrot.Blueprints.Core.Assets.ComponentContainsNode
                       or Hrot.Blueprints.Core.Assets.ComponentFindNode
                       or Hrot.Blueprints.Core.Assets.CollectionWriteNode)
                 && IsUnresolvedComponent(CollectionConsumerComponentTypeFqn(node)))
        {
            State = NodeState.Error;
            StatusTooltip = $"⚠ Unresolved ECS component: {CollectionConsumerComponentTypeFqn(node)}\n"
                          + "It may have been renamed or removed from C#. Re-wire the Collection pin from a GetComponent node.";
        }
        // CA-07c: mirrors Stage2's BP2066 (wired Collection pin + empty baked accessors is
        // structurally invalid) -- catches a "Collection" wired to something OTHER than a real
        // GetComponent collection pin (TryBakeCollectionConsumer left the bake empty because
        // detection failed), so the designer sees it on the canvas instead of only at compile time.
        else if (collectionPinWired && IsCollectionConsumerBakeIncomplete(node))
        {
            State = NodeState.Error;
            StatusTooltip = "⚠ \"Collection\" is wired but no component-collection accessor metadata "
                          + "was baked. Wire it FROM a GetComponent node's collection out-pin.";
        }
        else
        {
            State = NodeState.Normal;
        }
    }

    /// <summary>
    /// The baked <c>ComponentTypeFqn</c> off any of the five collection CONSUMER node kinds, or
    /// <c>""</c> for any other node. Shared by the stale-ref check above.
    /// </summary>
    private static string CollectionConsumerComponentTypeFqn(Hrot.Blueprints.Core.Assets.Node node) => node switch
    {
        Hrot.Blueprints.Core.Assets.ComponentForEachNode cfe   => cfe.ComponentTypeFqn,
        Hrot.Blueprints.Core.Assets.ComponentItemGetNode cig   => cig.ComponentTypeFqn,
        Hrot.Blueprints.Core.Assets.ComponentItemCountNode cic => cic.ComponentTypeFqn,
        Hrot.Blueprints.Core.Assets.ComponentContainsNode ccn  => ccn.ComponentTypeFqn,
        Hrot.Blueprints.Core.Assets.ComponentFindNode cfn      => cfn.ComponentTypeFqn,
        Hrot.Blueprints.Core.Assets.CollectionWriteNode cwn    => cwn.ComponentTypeFqn,
        _ => "",
    };

    /// <summary>
    /// True when a collection CONSUMER node's REQUIRED baked accessor props (per its own field set --
    /// see <c>Nodes.cs</c>) are missing. <c>ComponentForEachNode</c> needs
    /// ComponentTypeFqn+CountAccessorFqn+ItemAccessorFqn; <c>ComponentItemGetNode</c> needs
    /// ComponentTypeFqn+ItemAccessorFqn (no Count); <c>ComponentItemCountNode</c> needs
    /// ComponentTypeFqn+CountAccessorFqn (no Item). <c>ComponentContainsNode</c>/<c>ComponentFindNode</c>
    /// (CA-07d-1) are search nodes that both loop (Count) and compare (Item), so they need the same
    /// ComponentTypeFqn+CountAccessorFqn+ItemAccessorFqn set as ForEach. False for every other node kind.
    /// </summary>
    private static bool IsCollectionConsumerBakeIncomplete(Hrot.Blueprints.Core.Assets.Node node) => node switch
    {
        // FC-2/LV-2: a BlackboardFixedList consumer bakes only the variable name (component FQN /
        // accessor FQNs legitimately empty) -- checked FIRST, before the per-kind curated patterns.
        Hrot.Blueprints.Core.Assets.ComponentForEachNode { CollectionKind: Hrot.Blueprints.Core.Assets.CollectionKind.BlackboardFixedList } lf =>
            string.IsNullOrEmpty(lf.CollectionFieldName),
        Hrot.Blueprints.Core.Assets.ComponentItemGetNode { CollectionKind: Hrot.Blueprints.Core.Assets.CollectionKind.BlackboardFixedList } lg =>
            string.IsNullOrEmpty(lg.CollectionFieldName),
        Hrot.Blueprints.Core.Assets.ComponentItemCountNode { CollectionKind: Hrot.Blueprints.Core.Assets.CollectionKind.BlackboardFixedList } lc =>
            string.IsNullOrEmpty(lc.CollectionFieldName),
        Hrot.Blueprints.Core.Assets.ComponentContainsNode { CollectionKind: Hrot.Blueprints.Core.Assets.CollectionKind.BlackboardFixedList } lcn =>
            string.IsNullOrEmpty(lcn.CollectionFieldName),
        Hrot.Blueprints.Core.Assets.ComponentFindNode { CollectionKind: Hrot.Blueprints.Core.Assets.CollectionKind.BlackboardFixedList } lfn =>
            string.IsNullOrEmpty(lfn.CollectionFieldName),
        Hrot.Blueprints.Core.Assets.ComponentForEachNode cfe =>
            string.IsNullOrEmpty(cfe.ComponentTypeFqn) || string.IsNullOrEmpty(cfe.CountAccessorFqn) || string.IsNullOrEmpty(cfe.ItemAccessorFqn),
        Hrot.Blueprints.Core.Assets.ComponentItemGetNode cig =>
            string.IsNullOrEmpty(cig.ComponentTypeFqn) || string.IsNullOrEmpty(cig.ItemAccessorFqn),
        Hrot.Blueprints.Core.Assets.ComponentItemCountNode cic =>
            string.IsNullOrEmpty(cic.ComponentTypeFqn) || string.IsNullOrEmpty(cic.CountAccessorFqn),
        Hrot.Blueprints.Core.Assets.ComponentContainsNode ccn =>
            string.IsNullOrEmpty(ccn.ComponentTypeFqn) || string.IsNullOrEmpty(ccn.CountAccessorFqn) || string.IsNullOrEmpty(ccn.ItemAccessorFqn),
        Hrot.Blueprints.Core.Assets.ComponentFindNode cfn =>
            string.IsNullOrEmpty(cfn.ComponentTypeFqn) || string.IsNullOrEmpty(cfn.CountAccessorFqn) || string.IsNullOrEmpty(cfn.ItemAccessorFqn),
        // FC-1 (Q#20): the WRITE node needs ComponentTypeFqn + its op's WriteAccessorFqn. Also the
        // canvas-visible signal when the wire-bake REFUSED a non-writable/managed/accessor-less
        // target (TryBakeCollectionConsumer's two gates) -- the wire exists, the bake doesn't.
        Hrot.Blueprints.Core.Assets.CollectionWriteNode cwn =>
            string.IsNullOrEmpty(cwn.ComponentTypeFqn) || string.IsNullOrEmpty(cwn.WriteAccessorFqn),
        _ => false,
    };

    /// <summary>FC-1 (Q#20): the designer-facing verb for a collection write op (title use).</summary>
    private static string CollectionWriteVerb(Hrot.Blueprints.Core.Assets.CollectionWriteOp op) => op switch
    {
        Hrot.Blueprints.Core.Assets.CollectionWriteOp.Add      => "Add",
        Hrot.Blueprints.Core.Assets.CollectionWriteOp.SetAt    => "Set At",
        Hrot.Blueprints.Core.Assets.CollectionWriteOp.InsertAt => "Insert At",
        Hrot.Blueprints.Core.Assets.CollectionWriteOp.RemoveAt => "Remove At",
        Hrot.Blueprints.Core.Assets.CollectionWriteOp.Clear    => "Clear",
        Hrot.Blueprints.Core.Assets.CollectionWriteOp.Resize   => "Resize",
        _ => "Collection Write",
    };

    /// <summary>
    /// True when <paramref name="fc"/> targets a CLR method (has a TargetTypeId, not an in-blueprint graph)
    /// that reflection can no longer resolve — i.e. the C# method was renamed or deleted.
    /// </summary>
    private static bool IsUnresolvedClrCall(Hrot.Blueprints.Core.Assets.FunctionCallNode fc)
        => !string.IsNullOrEmpty(fc.TargetTypeId)
           && string.IsNullOrEmpty(fc.TargetGraphId)
           && NodePinSchema.ResolveClrMethod(fc) == null;

    /// <summary>
    /// True when <paramref name="componentTypeFqn"/> is non-empty but reflection can no longer
    /// resolve it to a loaded CLR type — i.e. the ECS component struct/class was renamed or
    /// deleted. Shared by <see cref="Hrot.Blueprints.Core.Assets.GetComponentNode"/> (CA-02) and
    /// <see cref="Hrot.Blueprints.Core.Assets.SetComponentNode"/> (CA-04) stale-ref checks. Uses
    /// <see cref="NodeDrawers.ComponentFieldReflector.ResolveType"/> (existence-only check) rather
    /// than <c>TryReflect</c>, so a resolvable zero-field ("tag") component is never misreported as
    /// unresolved.
    /// </summary>
    private static bool IsUnresolvedComponent(string? componentTypeFqn)
        => !string.IsNullOrEmpty(componentTypeFqn)
           && NodeDrawers.ComponentFieldReflector.ResolveType(componentTypeFqn) == null;

    /// <summary>
    /// Updates the asset's <see cref="Hrot.Blueprints.Core.Assets.NodeMetadata"/> position in place.
    /// Because <see cref="Position"/> reads live from the asset, the canvas sees the new value
    /// immediately without a full model rebuild.
    /// </summary>
    internal void SetPosition(Vector2 pos)
    {
        _node.EditorMetadata.X = pos.X;
        _node.EditorMetadata.Y = pos.Y;
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Short display name for an event identity on a node title: the last segment of a fully-qualified
    /// type name (e.g. <c>Hrot.AI.Behaviors.PingEvent</c> → <c>PingEvent</c>), so a subscriber/publisher
    /// node stays readable instead of showing the whole namespace. A plain catalog event name (no dots)
    /// is returned unchanged; the full FQN remains visible in the Details panel. Also trims a nested-type
    /// <c>+</c> segment defensively.
    /// </summary>
    private static string ShortEventName(string? eventId)
    {
        if (string.IsNullOrEmpty(eventId)) return "(none)";
        int cut = eventId!.LastIndexOfAny(new[] { '.', '+' });
        return cut >= 0 && cut < eventId.Length - 1 ? eventId[(cut + 1)..] : eventId;
    }

    private static string BuildTitle(
        Hrot.Blueprints.Core.Assets.Node node,
        Hrot.Blueprints.Core.Assets.BlueprintAsset? asset) => node switch
    {
        Hrot.Blueprints.Core.Assets.FunctionCallNode fc   => string.IsNullOrEmpty(fc.MethodName) ? "Function Call" : fc.MethodName,
        // Identifier titles wrap the identifier in [brackets] so the variable/slot/struct name is
        // instantly distinguishable from the verb ("Set Members [StructDemoData]", not the ambiguous
        // 3-word "Set Members StructDemoData").
        Hrot.Blueprints.Core.Assets.GetVariableNode gv    => $"Get [{ResolveVariableName(gv.VariableId, asset)}]",
        Hrot.Blueprints.Core.Assets.SetVariableNode sv    => $"Set [{ResolveVariableName(sv.VariableId, asset)}]",
        // Slice 2a-3: GetShared/SetShared — VariableId is a raw manifest-provisioned slot name
        // (not a blueprint VariableDecl GUID), so no ResolveVariableName lookup is needed. The slot
        // name is bracketed into the title for fast identification (also shown on the collapsed Value pin).
        Hrot.Blueprints.Core.Assets.GetSharedNode gsn      => string.IsNullOrEmpty(gsn.VariableId) ? "Get Shared" : $"Get Shared [{gsn.VariableId}]",
        Hrot.Blueprints.Core.Assets.SetSharedNode ssn      => string.IsNullOrEmpty(ssn.VariableId) ? "Set Shared" : $"Set Shared [{ssn.VariableId}]",
        // CA-02: bracket the short component type name, mirroring Make/Break/SetMembers's
        // "[ShortTypeName]" convention -- the component identity is the interesting bit, not the
        // generic "GetComponentNode" class name.
        Hrot.Blueprints.Core.Assets.GetComponentNode gcn   => string.IsNullOrEmpty(gcn.ComponentTypeFqn) ? "Get Component" : $"Get Component [{ShortTypeName(gcn.ComponentTypeFqn)}]",
        // CA-04: same "[ShortTypeName]" convention for the write node.
        Hrot.Blueprints.Core.Assets.SetComponentNode scn   => string.IsNullOrEmpty(scn.ComponentTypeFqn) ? "Set Component" : $"Set Component [{ShortTypeName(scn.ComponentTypeFqn)}]",
        // CA-07c: the three collection CONSUMER nodes bake ComponentTypeFqn on WIRE (no picker), so
        // an unwired/freshly-placed instance shows a generic label; once wired, the same
        // "[ShortTypeName]" bracket convention as Get/SetComponent kicks in.
        Hrot.Blueprints.Core.Assets.ComponentForEachNode cfe    => string.IsNullOrEmpty(cfe.ComponentTypeFqn) ? "For Each Component Item" : $"For Each [{ShortTypeName(cfe.ComponentTypeFqn)}]",
        Hrot.Blueprints.Core.Assets.ComponentItemGetNode cig    => string.IsNullOrEmpty(cig.ComponentTypeFqn) ? "Get Item"                 : $"Get Item [{ShortTypeName(cig.ComponentTypeFqn)}]",
        Hrot.Blueprints.Core.Assets.ComponentItemCountNode cic  => string.IsNullOrEmpty(cic.ComponentTypeFqn) ? "Item Count"               : $"Item Count [{ShortTypeName(cic.ComponentTypeFqn)}]",
        // CA-07d-1: same wire-baked "generic label until wired" convention as the other collection
        // CONSUMER nodes above.
        Hrot.Blueprints.Core.Assets.ComponentContainsNode ccn   => string.IsNullOrEmpty(ccn.ComponentTypeFqn) ? "Contains"                 : $"Contains [{ShortTypeName(ccn.ComponentTypeFqn)}]",
        Hrot.Blueprints.Core.Assets.ComponentFindNode cfn       => string.IsNullOrEmpty(cfn.ComponentTypeFqn) ? "Find"                     : $"Find [{ShortTypeName(cfn.ComponentTypeFqn)}]",
        // FC-1 (Q#20): same wire-baked "generic verb until wired" convention -- the op is the verb,
        // the component identity lands in brackets once the wire bakes it.
        Hrot.Blueprints.Core.Assets.CollectionWriteNode cwn     => string.IsNullOrEmpty(cwn.ComponentTypeFqn)
            ? $"{CollectionWriteVerb(cwn.Op)} (Collection)"
            : $"{CollectionWriteVerb(cwn.Op)} [{ShortTypeName(cwn.ComponentTypeFqn)}]",
        // FC-2/LV-3: same verb convention, the bound LIST VARIABLE's name in brackets (mirrors
        // Get/SetVariable's "[name]" identifier convention rather than a component type).
        Hrot.Blueprints.Core.Assets.ListWriteNode lwn           => string.IsNullOrEmpty(lwn.VariableId)
            ? $"{CollectionWriteVerb(lwn.Op)} (List)"
            : $"{CollectionWriteVerb(lwn.Op)} [{ResolveVariableName(lwn.VariableId, asset)}]",
        // Punch-list #1/#5/#8: show the node's own DATA in the body instead of the generic "Value"
        // pin label — the literal's value, the parameter's name, the compare/arith/bool operator.
        // Punch-list: the parameter NAME is shown on the output pin (render-only display label in
        // BlueprintGraphModel), so the title stays clean/uncluttered.
        Hrot.Blueprints.Core.Assets.GetParameterNode      => "Get Parameter",
        Hrot.Blueprints.Core.Assets.GetAllParametersNode  => "Get All Parameters",
        // Inline-editable Literals show their value in the body editor, so the title stays the type
        // ("Literal (Int32)"). Rarer types (no inline editor) keep the value in the title.
        Hrot.Blueprints.Core.Assets.LiteralNode lt        => LiteralValueJson.HasInlineEditor(lt.TypeId)
            ? $"Literal ({ShortTypeName(lt.TypeId)})"
            : FormatLiteral(lt),
        Hrot.Blueprints.Core.Assets.CompareNode cmp       => $"Compare {OperatorSymbol(cmp.Operator)}",
        Hrot.Blueprints.Core.Assets.BinaryOpNode bin      => $"Math {OperatorSymbol(bin.Operator)}",
        Hrot.Blueprints.Core.Assets.BooleanOpNode boo     => $"Logic {OperatorSymbol(boo.Operator)}",
        Hrot.Blueprints.Core.Assets.NotNode               => "Not (!)",
        Hrot.Blueprints.Core.Assets.EventEntryNode ee     => string.IsNullOrEmpty(ee.EventTypeId) ? "Event" : $"Event: {ShortEventName(ee.EventTypeId)}",
        Hrot.Blueprints.Core.Assets.CallPeerBlueprintNode cp => string.IsNullOrEmpty(cp.FunctionRef)
            ? "Call Peer"
            : $"Call Peer: {cp.FunctionRef}",
        // BP-68: EventId is the declaration's GUID (what the picker and pin projection use), so the
        // raw field made the header read "Call 3f2a…". Resolve it to the declared name, exactly as
        // Get/SetVariable resolve theirs.
        Hrot.Blueprints.Core.Assets.CallCustomEventNode ce   => string.IsNullOrEmpty(ce.EventId)
            ? "Call Custom Event"
            : $"Call {ResolveCustomEventName(ce.EventId, asset)}",
        Hrot.Blueprints.Core.Assets.ChannelCommandNode cc    => $"Command: {cc.ActionId}",
        Hrot.Blueprints.Core.Assets.WhenNode                 => "When",
        Hrot.Blueprints.Core.Assets.ReturnNode               => "Return",
        Hrot.Blueprints.Core.Assets.BranchNode               => "Branch",
        Hrot.Blueprints.Core.Assets.SequenceNode             => "Sequence",
        Hrot.Blueprints.Core.Assets.CastNode ca              => $"Cast to {ca.TargetTypeId}",
        Hrot.Blueprints.Core.Assets.LatentDelayNode          => "Delay",
        Hrot.Blueprints.Core.Assets.ArrayMakeNode            => "Make Array",
        Hrot.Blueprints.Core.Assets.ArrayGetNode             => "Get Array",
        Hrot.Blueprints.Core.Assets.WaitForChannelNode wfc   => $"Wait: {wfc.ChannelType}",
        Hrot.Blueprints.Core.Assets.WaitForEventNode wfe     => $"Wait Event: {ShortEventName(wfe.EventTypeId)}",
        // Custom events bake the FQN in EventTypeFqn and leave EventId empty; show the short event name
        // either way so the node never reads "Publish:" with a blank identity.
        Hrot.Blueprints.Core.Assets.PublishEventNode pev     => $"Publish: {ShortEventName(!string.IsNullOrEmpty(pev.EventTypeFqn) ? pev.EventTypeFqn : pev.EventId)}",
        Hrot.Blueprints.Core.Assets.ReadEqsResultNode        => "Read EQS Result",
        Hrot.Blueprints.Core.Assets.SpawnEqsSensorNode       => "Spawn EQS Sensor",
        // Q#14 Option B struct-value nodes: show the short struct name (namespace/global:: stripped)
        // so the header reads "Make StructDemoData" instead of the raw class name "MakeStructNode".
        Hrot.Blueprints.Core.Assets.MakeStructNode mk        => string.IsNullOrEmpty(mk.StructTypeId) ? "Make Struct"        : $"Make [{ShortTypeName(mk.StructTypeId)}]",
        Hrot.Blueprints.Core.Assets.BreakStructNode br       => string.IsNullOrEmpty(br.StructTypeId) ? "Break Struct"       : $"Break [{ShortTypeName(br.StructTypeId)}]",
        Hrot.Blueprints.Core.Assets.SetMembersNode sm        => string.IsNullOrEmpty(sm.StructTypeId) ? "Set Members"        : $"Set Members [{ShortTypeName(sm.StructTypeId)}]",
        _ => node.GetType().Name,
    };

    private static NodeCategory BuildCategory(Hrot.Blueprints.Core.Assets.Node node) => node switch
    {
        Hrot.Blueprints.Core.Assets.EventEntryNode           => NodeCategory.Event,
        Hrot.Blueprints.Core.Assets.GetVariableNode          => NodeCategory.VariableGet,
        Hrot.Blueprints.Core.Assets.SetVariableNode          => NodeCategory.VariableSet,
        Hrot.Blueprints.Core.Assets.GetSharedNode            => NodeCategory.VariableGet,
        Hrot.Blueprints.Core.Assets.SetSharedNode            => NodeCategory.VariableSet,
        // CA-02: GetComponent is pure-data (no exec pins), the "get" analog of GetShared.
        Hrot.Blueprints.Core.Assets.GetComponentNode         => NodeCategory.VariableGet,
        // CA-04: SetComponent is an exec node, the "set" analog of SetShared.
        Hrot.Blueprints.Core.Assets.SetComponentNode         => NodeCategory.VariableSet,
        // CA-07c: ComponentForEach is an exec node (Body/Completed loop-control), the collection
        // analog of BranchNode/SequenceNode. ItemGet/ItemCount are pure data reads, like Compare/BinaryOp.
        Hrot.Blueprints.Core.Assets.ComponentForEachNode     => NodeCategory.FlowControl,
        Hrot.Blueprints.Core.Assets.ComponentItemGetNode     => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.ComponentItemCountNode   => NodeCategory.Pure,
        // CA-07d-1: Contains/Find are pure-data search reads, same category as ItemGet/ItemCount.
        Hrot.Blueprints.Core.Assets.ComponentContainsNode    => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.ComponentFindNode        => NodeCategory.Pure,
        // FC-1 (Q#20): CollectionWrite is an exec write node, the collection analog of SetComponent.
        Hrot.Blueprints.Core.Assets.CollectionWriteNode      => NodeCategory.VariableSet,
        // FC-2/LV-3: ListWrite is the exec write node for fixed-list VARIABLES (SetVariable analog).
        Hrot.Blueprints.Core.Assets.ListWriteNode            => NodeCategory.VariableSet,
        Hrot.Blueprints.Core.Assets.LiteralNode              => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.GetParameterNode         => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.GetAllParametersNode     => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.CompareNode              => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.BinaryOpNode             => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.BooleanOpNode            => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.NotNode                  => NodeCategory.Pure,
        // Q#14 Option B struct-value nodes are pure data (construct/deconstruct/copy-modify).
        Hrot.Blueprints.Core.Assets.MakeStructNode           => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.BreakStructNode          => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.SetMembersNode           => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.FunctionCallNode fc when fc.IsPure => NodeCategory.Pure,
        Hrot.Blueprints.Core.Assets.BranchNode               => NodeCategory.FlowControl,
        Hrot.Blueprints.Core.Assets.SequenceNode             => NodeCategory.FlowControl,
        _                                                    => NodeCategory.Function,
    };

    /// <summary>
    /// Resolves the display NAME of a variable from the owning asset's variable list.
    /// Accepts both the raw GUID string and the My-Blueprint <c>var:&lt;guid&gt;</c> item-id
    /// form. Falls back to the original id string when the asset is null or the variable
    /// is not found.
    /// </summary>
    private static string ResolveVariableName(
        string variableId,
        Hrot.Blueprints.Core.Assets.BlueprintAsset? asset)
    {
        if (asset == null || string.IsNullOrEmpty(variableId))
            return variableId;

        var idStr = variableId.StartsWith("var:", StringComparison.OrdinalIgnoreCase)
            ? variableId[4..]
            : variableId;

        if (Guid.TryParse(idStr, out var guid))
        {
            // Get/SetVariable can target either a blueprint Variable OR a WorkingState slot — both
            // are VariableDecl lists (the cross-entity / shared-state demos declare their mirrored
            // slots in WorkingState). Without the WorkingState fallback the title showed the raw GUID.
            var decl = asset.Variables.FirstOrDefault(v => v.Id == guid)
                    ?? asset.WorkingState.FirstOrDefault(v => v.Id == guid);
            if (decl != null && !string.IsNullOrEmpty(decl.Name))
                return decl.Name;
        }

        return variableId;
    }

    /// <summary>
    /// BP-68 — the declared name behind a <c>CallCustomEvent.EventId</c>.
    /// <para>
    /// Accepts both forms the rest of the pipeline accepts: the declaration's GUID (what the BP-07
    /// picker writes and <c>NodePinSchema</c> parses) and a bare name (hand-authored assets, which
    /// Stage5's <c>FindCustomEventIndex</c> resolves too). An id that matches no declaration is
    /// returned as-is so a dangling reference stays visible on the node rather than reading as a
    /// valid event.
    /// </para>
    /// </summary>
    private static string ResolveCustomEventName(
        string eventId,
        Hrot.Blueprints.Core.Assets.BlueprintAsset? asset)
    {
        if (asset == null || string.IsNullOrEmpty(eventId))
            return eventId;

        var idStr = eventId.StartsWith("evt:", StringComparison.OrdinalIgnoreCase)
            ? eventId[4..]
            : eventId;

        if (Guid.TryParse(idStr, out var guid))
        {
            var decl = asset.CustomEvents.FirstOrDefault(e => e.Id == guid);
            if (decl != null && !string.IsNullOrEmpty(decl.Name))
                return decl.Name;
        }

        return eventId;
    }

    /// <summary>
    /// Resolves the display NAME of a blueprint parameter from the owning asset's parameter list.
    /// Mirrors <see cref="ResolveVariableName"/> but over <c>asset.Parameters</c> and also accepts the
    /// <c>param:&lt;guid&gt;</c> item-id form (Stage5 strips both <c>var:</c>/<c>param:</c> prefixes).
    /// Falls back to the raw id when the asset is null or the parameter is not found.
    /// </summary>
    private static string ResolveParameterName(
        string parameterId,
        Hrot.Blueprints.Core.Assets.BlueprintAsset? asset)
    {
        if (asset == null || string.IsNullOrEmpty(parameterId))
            return string.IsNullOrEmpty(parameterId) ? "(unset)" : parameterId;

        var idStr = parameterId;
        if (idStr.StartsWith("param:", StringComparison.OrdinalIgnoreCase)) idStr = idStr[6..];
        else if (idStr.StartsWith("var:", StringComparison.OrdinalIgnoreCase)) idStr = idStr[4..];

        if (Guid.TryParse(idStr, out var guid))
        {
            var decl = asset.Parameters.FirstOrDefault(p => p.Id == guid);
            if (decl != null && !string.IsNullOrEmpty(decl.Name))
                return decl.Name;
        }

        return parameterId;
    }

    /// <summary>
    /// Renders a <see cref="Hrot.Blueprints.Core.Assets.LiteralNode"/>'s title as its actual value
    /// (punch-list #5) — strings shown unquoted, floats without the <c>f</c> suffix. Falls back to
    /// <c>Literal (Type)</c> when no value has been entered yet.
    /// </summary>
    private static string FormatLiteral(Hrot.Blueprints.Core.Assets.LiteralNode lt)
    {
        var raw = lt.ValueJson ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw))
            return $"Literal ({ShortTypeName(lt.TypeId)})";

        // Strings persist as C# literals with surrounding quotes; floats carry an 'f' suffix.
        if (raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"')
            return raw[1..^1];
        if ((raw[^1] == 'f' || raw[^1] == 'F') && raw.Length > 1 && (char.IsDigit(raw[0]) || raw[0] == '-' || raw[0] == '.'))
            return raw[..^1];
        return raw;
    }

    private static string ShortTypeName(string typeId)
    {
        if (string.IsNullOrEmpty(typeId)) return "?";
        var dot = typeId.LastIndexOf('.');
        return dot >= 0 ? typeId[(dot + 1)..] : typeId;
    }

    private static string OperatorSymbol(Hrot.Blueprints.Core.Assets.ComparisonOperator op) => op switch
    {
        Hrot.Blueprints.Core.Assets.ComparisonOperator.Equal              => "==",
        Hrot.Blueprints.Core.Assets.ComparisonOperator.NotEqual           => "!=",
        Hrot.Blueprints.Core.Assets.ComparisonOperator.LessThan           => "<",
        Hrot.Blueprints.Core.Assets.ComparisonOperator.LessThanOrEqual    => "<=",
        Hrot.Blueprints.Core.Assets.ComparisonOperator.GreaterThan        => ">",
        Hrot.Blueprints.Core.Assets.ComparisonOperator.GreaterThanOrEqual => ">=",
        _                                                                 => op.ToString(),
    };

    private static string OperatorSymbol(Hrot.Blueprints.Core.Assets.ArithmeticOperator op) => op switch
    {
        Hrot.Blueprints.Core.Assets.ArithmeticOperator.Add      => "+",
        Hrot.Blueprints.Core.Assets.ArithmeticOperator.Subtract => "-",
        Hrot.Blueprints.Core.Assets.ArithmeticOperator.Multiply => "*",
        Hrot.Blueprints.Core.Assets.ArithmeticOperator.Divide   => "/",
        Hrot.Blueprints.Core.Assets.ArithmeticOperator.Modulo   => "%",
        _                                                       => op.ToString(),
    };

    private static string OperatorSymbol(Hrot.Blueprints.Core.Assets.BooleanOperator op) => op switch
    {
        Hrot.Blueprints.Core.Assets.BooleanOperator.And => "&&",
        Hrot.Blueprints.Core.Assets.BooleanOperator.Or  => "||",
        _                                               => op.ToString(),
    };
}
