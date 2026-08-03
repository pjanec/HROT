using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor.GraphEditor;
using Hrot.Blueprints.Editor.NodeDrawers;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Primitives;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// <see cref="IGraphCommandSink"/> that applies NodeEdit <see cref="GraphCommand"/>s to the
/// active <see cref="BlueprintAsset"/> graph.
///
/// <para>
/// Structural operations (add/remove node, add/remove link) are routed through the existing
/// <see cref="GraphCommands"/>/<see cref="CommandHistory"/> for undo/redo.  Position-only moves
/// bypass the history to avoid polluting the undo stack with continuous drag operations.
/// Property edits are routed through the real <see cref="EditService"/>.
/// </para>
///
/// <para>
/// After every mutation the model is rebuilt and <see cref="BlueprintGraphModel.RebuildAndNotify"/>
/// is called so the canvas reflects the change.  The asset is marked dirty via
/// <see cref="EditService.MarkDirty"/>.
/// </para>
/// </summary>
public sealed class BlueprintCommandSink : IGraphCommandSink
{
    private readonly BlueprintAsset      _asset;
    private readonly Graph               _graph;
    private readonly BlueprintGraphModel _model;
    private readonly BlueprintNodeCatalog _catalog;
    private readonly BlueprintLinkValidator _validator;
    private readonly CommandHistory      _history;
    private readonly EditService         _editService;
    private readonly Action<BlueprintAsset> _markDirty;
    // BF-UX1 FIX B: channel-command catalog so ApplyPinIds can pass it to
    // NodePinSchema.GetCanonicalPins — without it ChannelCommandNode projects exec-only pins.
    private readonly IChannelCommandCatalog? _channelCommands;
    // ENUM-NAME: provider used to convert a long enum value → member name at the persistence boundary.
    private readonly IEnumValueProvider? _enumProvider;
    // AN7: unified behavior-action catalog so ApplyPinIds re-stamps a non-channel ChannelCommandNode
    // (ActionFqn set) with its projected param data-IN pins instead of collapsing to exec-only.
    private readonly ActionCatalog.IBehaviorActionCatalog? _behaviorActions;

    /// <summary>
    /// Constructs a command sink bound to the given asset graph.
    /// </summary>
    /// <param name="asset">The owning Blueprint asset.</param>
    /// <param name="graph">The specific graph within the asset to mutate.</param>
    /// <param name="model">The <see cref="BlueprintGraphModel"/> to rebuild after every mutation.</param>
    /// <param name="catalog">Used to resolve <see cref="NodeKindKey"/> → descriptor when adding nodes.</param>
    /// <param name="validator">Used to enforce the single-data-input link replacement rule.</param>
    /// <param name="history">Command history for structural operations.</param>
    /// <param name="editService">Property-edit service for <see cref="GraphCommand.SetNodeProperty"/>.</param>
    /// <param name="markDirty">Callback invoked after every successful mutation to mark the asset dirty.</param>
    /// <param name="channelCommands">
    /// Optional channel-command catalog forwarded to <see cref="NodePinSchema.GetCanonicalPins"/>
    /// so that <see cref="ChannelCommandNode"/>s project their parameter data-IN pins rather than
    /// collapsing to exec-only when <see cref="ApplyPinIds"/> re-stamps canonical pins on create.
    /// </param>
    /// <param name="enumProvider">
    /// Optional enum-value provider used at the persistence boundary to convert the <c>long</c>
    /// selected by <see cref="NodeEditor.UI.MiniEditors.EnumPinEditor"/> into the member name
    /// string stored in <see cref="Node.PinDefaults"/> (ENUM-NAME).
    /// When null, the decimal integer string is stored instead (backward compat / headless tests).
    /// </param>
    /// <param name="behaviorActions">
    /// AN7 — optional unified behavior-action catalog forwarded to
    /// <see cref="NodePinSchema.GetCanonicalPins"/> so that a non-channel <see cref="ChannelCommandNode"/>
    /// (one whose <c>ActionFqn</c> is set) projects its parameter data-IN pins when
    /// <see cref="ApplyPinIds"/> re-stamps canonical pins on create.
    /// </param>
    public BlueprintCommandSink(
        BlueprintAsset       asset,
        Graph                graph,
        BlueprintGraphModel  model,
        BlueprintNodeCatalog catalog,
        BlueprintLinkValidator validator,
        CommandHistory       history,
        EditService          editService,
        Action<BlueprintAsset> markDirty,
        IChannelCommandCatalog? channelCommands = null,
        IEnumValueProvider?     enumProvider    = null,
        ActionCatalog.IBehaviorActionCatalog? behaviorActions = null)
    {
        _asset           = asset           ?? throw new ArgumentNullException(nameof(asset));
        _graph           = graph           ?? throw new ArgumentNullException(nameof(graph));
        _model           = model           ?? throw new ArgumentNullException(nameof(model));
        _catalog         = catalog         ?? throw new ArgumentNullException(nameof(catalog));
        _validator       = validator       ?? throw new ArgumentNullException(nameof(validator));
        _history         = history         ?? throw new ArgumentNullException(nameof(history));
        _editService     = editService     ?? throw new ArgumentNullException(nameof(editService));
        _markDirty       = markDirty       ?? throw new ArgumentNullException(nameof(markDirty));
        _channelCommands = channelCommands;
        _enumProvider    = enumProvider;
        _behaviorActions = behaviorActions;
    }

    // ── IGraphCommandSink ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public GraphCommandResult Apply(GraphCommand command)
    {
        switch (command)
        {
            case GraphCommand.AddNode add:
                return ApplyAddNode(add);

            case GraphCommand.RemoveNodes remove:
                return ApplyRemoveNodes(remove);

            case GraphCommand.AddLink link:
                return ApplyAddLink(link);

            case GraphCommand.RemoveLinks removeLinks:
                return ApplyRemoveLinks(removeLinks);

            case GraphCommand.MoveNodes move:
                return ApplyMoveNodes(move);

            case GraphCommand.ChangeParentMultiple cpm:
                return ApplyChangeParentMultiple(cpm);

            case GraphCommand.SetNodeProperty prop:
                return ApplySetNodeProperty(prop);

            case GraphCommand.SetPinDefault setPinDefault:
                return ApplySetPinDefault(setPinDefault);

            case GraphCommand.Batch batch:
                return ApplyBatch(batch);

            case GraphCommand.InsertReroute insertReroute:
                return ApplyInsertReroute(insertReroute);

            case GraphCommand.MoveReroute moveReroute:
                return ApplyMoveReroute(moveReroute);

            case GraphCommand.RemoveReroute removeReroute:
                return ApplyRemoveReroute(removeReroute);

            case GraphCommand.AddComment addComment:
                return ApplyAddComment(addComment);

            case GraphCommand.UpdateComment updateComment:
                return ApplyUpdateComment(updateComment);

            case GraphCommand.RemoveComment removeComment:
                return ApplyRemoveComment(removeComment);

            default:
                // Unknown commands are silently accepted (forward-compat).
                return new GraphCommandResult(true, null);
        }
    }

    // ── AddNode ──────────────────────────────────────────────────────────────

    private GraphCommandResult ApplyAddNode(GraphCommand.AddNode add)
    {
        // Create the asset-level node. Unknown kinds fall back to FunctionCallNode.
        var assetNode = CreateAssetNode(add.Kind, add.Position, add.InitialProperties);
        if (assetNode == null)
            return new GraphCommandResult(false, $"Could not create node for kind: {add.Kind.Id}");

        // Push through CommandHistory for undo/redo.
        var addCmd = new AddNodeCommand(_graph, assetNode);
        _history.Execute(addCmd);
        _markDirty(_asset);
        _model.RebuildAndNotify();

        return new GraphCommandResult(true, null);
    }

    private Node? CreateAssetNode(
        NodeKindKey kind,
        Vector2 position,
        IReadOnlyDictionary<string, object?>? props)
    {
        // BCP-BATCH-02-FIX Task 3: the My-Blueprint variable-drag create-path
        // (CanvasRenderer.PlaceVariableNode) emits the kind ids "Util.GetVar" / "Util.SetVar".
        // These are not in the Blueprint palette registry, so without this mapping they fell
        // through to a generic FunctionCallNode (exec in/out, no data pin). Create the real
        // GetVariableNode / SetVariableNode so NodePinSchema projects the correct pins:
        // Get = PURE (single data-out "Value"), Set = exec in/out + typed data "Value".
        if (IsGetVariableKind(kind.Id))
            return FinishVariableNode(new GetVariableNode(), position, props);
        if (IsSetVariableKind(kind.Id))
            return FinishVariableNode(new SetVariableNode(), position, props);

        // Map the NodeKindKey back to the appropriate asset Node subtype.
        // The NodeKindRegistry descriptor has a CreateInstance factory; use it
        // to create a properly-typed node (pins are not relevant here — they are
        // projected from the drawn descriptor, not from asset pin lists).
        var registryDescriptor = _catalog.KindRegistry.TryGet(kind.Id);
        Node assetNode;

        if (registryDescriptor != null)
        {
            try { assetNode = registryDescriptor.CreateInstance(); }
            catch { assetNode = new FunctionCallNode { MethodName = kind.Id }; }
        }
        else
        {
            // Dynamic kind (custom event, callable peer) — create a generic FunctionCallNode.
            assetNode = new FunctionCallNode { MethodName = kind.Id };
        }

        assetNode.Id = Guid.NewGuid();
        assetNode.EditorMetadata = new NodeMetadata { X = position.X, Y = position.Y };

        // Apply initial properties if provided.
        if (props != null)
            ApplyInitialProperties(assetNode, props);

        // BCP-BATCH-04 Task 1: honor caller-supplied pin GUIDs (wire-drop auto-connect).
        ApplyPinIds(assetNode, props);

        return assetNode;
    }

    // ── PinIds honoring (BCP-BATCH-04 Task 1) ─────────────────────────────────

    /// <summary>
    /// When <paramref name="props"/> carries a <c>"PinIds"</c> entry (a
    /// <see cref="IReadOnlyList{T}"/> / <see cref="List{T}"/> of <see cref="PinId"/>), populates
    /// <paramref name="node"/>.<see cref="Node.Pins"/> with the node's canonical pin schema and
    /// stamps the supplied GUIDs onto those pins in <b>inputs-then-outputs</b> order.
    /// <para>
    /// This is the server-side counterpart to NodeEdit's wire-drop create-path
    /// (<c>CanvasInput</c>): that path pre-generates a <c>List&lt;PinId&gt;</c> sized
    /// <c>entry.Inputs.Count + entry.Outputs.Count</c> (inputs first, then outputs — exactly how
    /// <see cref="BlueprintNodeCatalog.DescriptorToEntry"/> splits the canonical pins), forms the
    /// auto-connect <see cref="GraphCommand.AddLink"/> referencing <c>pinIds[pinIdx]</c>, and ships
    /// the list as <c>InitialProperties["PinIds"]</c>.  Without this step the new node's pins would
    /// carry fresh (different) GUIDs, so <see cref="ApplyAddLink"/>'s <c>FindPin</c> would return
    /// null and the link would be rejected.  By assigning the provided GUIDs to the canonical pins
    /// in the same inputs-then-outputs order the catalog used, the link resolves and the wire
    /// connects.
    /// </para>
    /// <para>
    /// The pins are populated <b>in memory only</b> (loaded assets still hydrate via projection;
    /// nothing is persisted here).  Count mismatches are guarded: only <c>min(supplied, canonical)</c>
    /// pins are re-stamped; any extra canonical pins keep their generated GUIDs.
    /// </para>
    /// </summary>
    private void ApplyPinIds(Node node, IReadOnlyDictionary<string, object?>? props)
    {
        if (props == null || !props.TryGetValue("PinIds", out var raw) || raw == null)
            return;

        // Accept the concrete List<PinId> the canvas ships, or any IReadOnlyList<PinId>.
        if (raw is not IReadOnlyList<PinId> pinIds || pinIds.Count == 0)
            return;

        // Build the canonical pin list the SAME way DescriptorToEntry did (registry-backed,
        // asset-aware for variable typing) so the count/order aligns with the catalog entry the
        // canvas walked when generating PinIds.
        // BF-UX1 FIX B: pass _channelCommands so ChannelCommandNode projects its param data-IN
        // pins instead of collapsing to exec-only (the root cause of BF-UX1 FIX B).
        var canonical = NodePinSchema.GetCanonicalPins(node, _catalog.KindRegistry, _asset,
            channelCommands: _channelCommands, containingGraph: _graph,
            behaviorActions: _behaviorActions);

        // Re-order into inputs-then-outputs, matching DescriptorToEntry (Inputs = Direction=="In",
        // Outputs = Direction=="Out") and CanvasInput's pinIdx walk (entry.Inputs then entry.Outputs).
        var ordered = new List<Pin>(canonical.Count);
        foreach (var p in canonical)
            if (p.Direction == "In") ordered.Add(p);
        foreach (var p in canonical)
            if (p.Direction == "Out") ordered.Add(p);

        // Stamp the supplied GUIDs onto the ordered pins (guard count mismatch: assign min).
        int count = Math.Min(ordered.Count, pinIds.Count);
        for (int i = 0; i < count; i++)
            ordered[i].Id = pinIds[i].Value;

        // Populate the node's pin list so ApplyAddLink.FindPin resolves the link-referenced GUID.
        node.Pins = ordered;
    }

    // ── Variable Get/Set create-path (BCP-BATCH-02-FIX Task 3) ────────────────

    /// <summary>
    /// True when <paramref name="kindId"/> denotes a "get variable" node create request
    /// (the My-Blueprint drag-to-canvas / context-menu "Get" path).
    /// </summary>
    private static bool IsGetVariableKind(string kindId) =>
        kindId is "Util.GetVar" or "Variable.Get" or "Blueprint.GetVariable" or "GetVariable";

    /// <summary>
    /// True when <paramref name="kindId"/> denotes a "set variable" node create request
    /// (the My-Blueprint drag-to-canvas / context-menu "Set" path).
    /// </summary>
    private static bool IsSetVariableKind(string kindId) =>
        kindId is "Util.SetVar" or "Variable.Set" or "Blueprint.SetVariable" or "SetVariable";

    /// <summary>
    /// Stamps id, position and the <c>VariableId</c> property onto a freshly created
    /// <see cref="GetVariableNode"/>/<see cref="SetVariableNode"/> so
    /// <see cref="NodePinSchema"/> can type the Value pin from the declared variable.
    /// </summary>
    private Node FinishVariableNode(
        Node node,
        Vector2 position,
        IReadOnlyDictionary<string, object?>? props)
    {
        node.Id = Guid.NewGuid();
        node.EditorMetadata = new NodeMetadata { X = position.X, Y = position.Y };
        if (props != null)
            ApplyInitialProperties(node, props);
        // BCP-BATCH-04 Task 1: honor caller-supplied pin GUIDs for the Get/Set wire-drop path too.
        ApplyPinIds(node, props);
        return node;
    }

    private static void ApplyInitialProperties(Node node, IReadOnlyDictionary<string, object?> props)
    {
        // Apply well-known properties that map directly to asset fields.
        if (props.TryGetValue("Comment", out var comment) && comment is string s)
            node.EditorMetadata.Comment = s;

        if (node is FunctionCallNode fc)
        {
            if (props.TryGetValue("TargetTypeId", out var t) && t is string tid) fc.TargetTypeId = tid;
            if (props.TryGetValue("MethodName",   out var m) && m is string mn)  fc.MethodName   = mn;
        }
        else if (node is GetVariableNode gv)
        {
            if (props.TryGetValue("VariableId", out var vid) && vid is string vs) gv.VariableId = vs;
        }
        else if (node is SetVariableNode sv)
        {
            if (props.TryGetValue("VariableId", out var vid) && vid is string vs) sv.VariableId = vs;
        }
        else if (node is LiteralNode lit)
        {
            if (props.TryGetValue("TypeId",     out var typeId)  && typeId  is string ts) lit.TypeId     = ts;
            if (props.TryGetValue("ValueJson",  out var valJson) && valJson is string vs) lit.ValueJson  = vs;
        }
        else if (node is EventEntryNode ee)
        {
            if (props.TryGetValue("EventTypeId", out var eid) && eid is string es) ee.EventTypeId = es;
        }
        else if (node is ChannelCommandNode cc)
        {
            if (props.TryGetValue("ChannelType", out var ct) && ct is string cts) cc.ChannelType = cts;
            if (props.TryGetValue("ActionId",    out var ai) && ai is string ais) cc.ActionId    = ais;
        }
        else if (node is GetSharedNode gsn)
        {
            // Slice 2a-3: bake VariableId/SharedTypeId at create-time (mirrors GetVariableNode).
            if (props.TryGetValue("VariableId",   out var vid) && vid is string vs)  gsn.VariableId   = vs;
            if (props.TryGetValue("SharedTypeId", out var tid) && tid is string ts)  gsn.SharedTypeId = ts;
        }
        else if (node is SetSharedNode ssn)
        {
            if (props.TryGetValue("VariableId",   out var vid) && vid is string vs)  ssn.VariableId   = vs;
            if (props.TryGetValue("SharedTypeId", out var tid) && tid is string ts)  ssn.SharedTypeId = ts;
        }
    }

    // ── RemoveNodes ──────────────────────────────────────────────────────────

    private GraphCommandResult ApplyRemoveNodes(GraphCommand.RemoveNodes remove)
    {
        foreach (var nodeId in remove.Nodes)
        {
            var assetNode = _graph.Nodes.FirstOrDefault(n => n.Id == nodeId.Value);
            if (assetNode == null) continue;

            // Remove the node AND its incident links through CommandHistory as one step
            // (DeleteNodeCommand captures the incident links so Undo restores node + wires together).
            var delCmd = new DeleteNodeCommand(_graph, assetNode);
            _history.Execute(delCmd);
        }

        _markDirty(_asset);
        _model.RebuildAndNotify();
        return new GraphCommandResult(true, null);
    }

    // ── AddLink ──────────────────────────────────────────────────────────────

    private GraphCommandResult ApplyAddLink(GraphCommand.AddLink link)
    {
        // Validate through our validator first.
        var validation = _validator.Validate(link.From, link.To);

        // Any existing link this add must replace is collected here and dropped atomically WITH the add
        // in a single undoable LinkEditCommand (so Undo restores the prior wiring in one step).
        var toRemove = new List<Link>();
        if (validation.Verdict == LinkValidity.Invalid)
        {
            if (validation.Reason?.Contains("Exec output") == true)
            {
                // Exec-output replace: remove the existing exec-out link by source pin.
                toRemove.AddRange(FindLinksByFromPin(link.From));
            }
            else if (validation.Reason?.Contains("replace") == true ||
                     validation.Reason?.Contains("already has") == true)
            {
                // Data-input replace: remove the existing data link by target pin.
                toRemove.AddRange(FindLinksByToPin(link.To));
            }
            else
            {
                return new GraphCommandResult(false, validation.Reason);
            }
        }

        // Resolve pin GUIDs from the model.
        var fromPinGuid = link.From.Value;
        var toPinGuid   = link.To.Value;

        // Resolve source/target node ids for the asset link record.
        var fromPin = _model.FindPin(link.From);
        var toPin   = _model.FindPin(link.To);
        if (fromPin == null || toPin == null)
            return new GraphCommandResult(false, "Pin not found in model.");

        var assetLink = new Link
        {
            FromNodeId = fromPin.OwnerNodeId.Value,
            FromPinId  = fromPinGuid,
            ToNodeId   = toPin.OwnerNodeId.Value,
            ToPinId    = toPinGuid,
        };

        // CA-07c: author-time accessor baking on wire -- see TryBakeCollectionConsumer's doc comment.
        // Mutates the target consumer node's asset properties directly, OUTSIDE CommandHistory (same
        // pattern as ComponentNodeDrawers.ApplyComponentTypeFqn's picker-bake: a stale bake surviving
        // an Undo-of-link is harmless, since Stage2's BP2066 / Stage5's safe-default lowering only act
        // when the Collection pin is ACTUALLY wired). RebuildAndNotify() below re-projects the
        // consumer's now-element-typed pins from the freshly-baked FQNs.
        TryBakeCollectionConsumer(fromPin, toPin);

        // Undoable: drop any replaced link(s) + add the new one as one history step.
        _history.Execute(new LinkEditCommand(_graph, toRemove, new[] { assetLink }, "Add Link"));
        _markDirty(_asset);
        _model.RebuildAndNotify();
        return new GraphCommandResult(true, null);
    }

    /// <summary>
    /// CA-07c/CA-07d-1 -- author-time accessor baking on wire for the five component-collection
    /// CONSUMER nodes (<see cref="ComponentForEachNode"/>/<see cref="ComponentItemGetNode"/>/
    /// <see cref="ComponentItemCountNode"/>/<see cref="ComponentContainsNode"/>/
    /// <see cref="ComponentFindNode"/>). Those nodes have NO type picker of their own (CA-07b):
    /// their <c>ComponentTypeFqn</c>/<c>CountAccessorFqn</c>/<c>ItemAccessorFqn</c>/
    /// <c>ElementTypeFqn</c> props start empty and stay empty until a designer wires a
    /// <see cref="GetComponentNode"/> collection out-pin into the consumer's "Collection" data-IN
    /// pin -- THIS is that wiring moment.
    /// <para>
    /// Detection: <paramref name="toPin"/>'s Label is "Collection" (ordinal-ignore-case) AND its
    /// owner node is one of the five consumer kinds, AND <paramref name="fromPin"/>'s owner node is
    /// a <see cref="GetComponentNode"/>. If either side doesn't match, this is an ordinary wire (or a
    /// designer's mistake heading for a type-mismatch/BP2066 rejection elsewhere) -- do nothing and
    /// leave the link as-is; some other tool/validation handles that case.
    /// </para>
    /// <para>
    /// Resolution: looks up the GetComponent's baked collection decl by NAME
    /// (<c>gcn.Fields.First(f =&gt; f.IsCollection &amp;&amp; f.Name == fromPin.Label)</c> -- the
    /// source pin's Label is the field/collection Name verbatim, no display-label override exists
    /// for GetComponent field pins). If no matching decl is found (stale/mismatched wire, or the
    /// source isn't actually a collection pin), do nothing -- same "leave it to other validation"
    /// rule as above.
    /// </para>
    /// <para>
    /// Bake: switches on the consumer's concrete type and sets ONLY that type's props (mirrors each
    /// node's own field set from <c>Nodes.cs</c>): <c>ComponentForEachNode</c> gets all four
    /// (ComponentTypeFqn/CountAccessorFqn/ItemAccessorFqn/ElementTypeFqn); <c>ComponentItemGetNode</c>
    /// gets ComponentTypeFqn/ItemAccessorFqn/ElementTypeFqn (no CountAccessorFqn -- Get never counts);
    /// <c>ComponentItemCountNode</c> gets only ComponentTypeFqn/CountAccessorFqn (no
    /// ItemAccessorFqn/ElementTypeFqn -- Count's "Collection" pin is always <c>System.Object</c>,
    /// Stage0/NodePinSchema never consult an element type for it). <c>ComponentContainsNode</c>/
    /// <c>ComponentFindNode</c> (CA-07d-1) are pure-data SEARCH nodes -- they loop the collection
    /// (need CountAccessorFqn) AND compare each element against the query "Item" (need
    /// ItemAccessorFqn + ElementTypeFqn to type "Collection"/"Item"), so like ForEach they get all
    /// four props. Accessor FQNs baked as <c>""</c> (never null) when the decl's own accessor is
    /// null -- defensive; a decl with <c>IsCollection == true</c> should always carry both accessor
    /// FQNs (CA-07a bakes them together), but this never throws on a malformed/hand-edited one.
    /// </para>
    /// <para>
    /// CA-07d-2: every consumer ALSO gets <c>CollectionKind</c>/<c>CollectionFieldName</c> copied
    /// verbatim off the decl -- <c>CuratedStatic</c>/<c>""</c> for a curated decl (identical to
    /// before this existed) or <c>ManagedMember</c>/the managed member name for a managed one (whose
    /// accessor FQNs are empty by construction, per above). <c>ComponentItemCountNode</c> ALSO gets
    /// <c>ElementTypeFqn</c> baked, but ONLY for the managed case -- the compiler needs it to type the
    /// native <c>IReadOnlyList&lt;TElement&gt;</c> local; curated Count still never sets it (preserves
    /// curated JSON byte-identity).
    /// </para>
    /// <para>
    /// Mutates the target <see cref="Node"/> object directly, NOT through <see cref="_history"/> --
    /// see the call site's doc comment for why that's acceptable here.
    /// </para>
    /// </summary>
    private void TryBakeCollectionConsumer(IPinModel fromPin, IPinModel toPin)
    {
        if (!string.Equals(toPin.Label, "Collection", StringComparison.OrdinalIgnoreCase))
            return;

        var consumerNode = _graph.Nodes.FirstOrDefault(n => n.Id == toPin.OwnerNodeId.Value);
        if (consumerNode is not (ComponentForEachNode or ComponentItemGetNode or ComponentItemCountNode or ComponentContainsNode or ComponentFindNode or CollectionWriteNode))
            return;

        var sourceNode = _graph.Nodes.FirstOrDefault(n => n.Id == fromPin.OwnerNodeId.Value);

        // FC-2/LV-2 (Q#19-A): the source may be a GetVariable over a FIXED-LIST variable -- bake
        // Kind=BlackboardFixedList + the VARIABLE name into CollectionFieldName (ComponentTypeFqn
        // and accessor FQNs stay empty; Stage5 resolves the decl through the wire). The WRITE node
        // is list-writable only via the LV-3 List* nodes -- a CollectionWriteNode wired to a list
        // variable stays unbaked (component-write ops don't apply to blackboard fields).
        if (sourceNode is GetVariableNode gvSrc)
        {
            var listDecl = NodePinSchema.FindVariableDecl(gvSrc.VariableId, _asset);
            if (listDecl is not { Type.Capacity: > 0 }) return;
            switch (consumerNode)
            {
                case ComponentForEachNode cfeL:
                    cfeL.CollectionKind = CollectionKind.BlackboardFixedList;
                    cfeL.CollectionFieldName = listDecl.Name;
                    cfeL.ElementTypeFqn = listDecl.Type.TypeId;
                    cfeL.ComponentTypeFqn = ""; cfeL.CountAccessorFqn = ""; cfeL.ItemAccessorFqn = "";
                    break;
                case ComponentItemGetNode cigL:
                    cigL.CollectionKind = CollectionKind.BlackboardFixedList;
                    cigL.CollectionFieldName = listDecl.Name;
                    cigL.ElementTypeFqn = listDecl.Type.TypeId;
                    cigL.ComponentTypeFqn = ""; cigL.ItemAccessorFqn = "";
                    break;
                case ComponentItemCountNode cicL:
                    cicL.CollectionKind = CollectionKind.BlackboardFixedList;
                    cicL.CollectionFieldName = listDecl.Name;
                    cicL.ElementTypeFqn = listDecl.Type.TypeId;
                    cicL.ComponentTypeFqn = ""; cicL.CountAccessorFqn = "";
                    break;
                case ComponentContainsNode ccnL:
                    ccnL.CollectionKind = CollectionKind.BlackboardFixedList;
                    ccnL.CollectionFieldName = listDecl.Name;
                    ccnL.ElementTypeFqn = listDecl.Type.TypeId;
                    ccnL.ComponentTypeFqn = ""; ccnL.CountAccessorFqn = ""; ccnL.ItemAccessorFqn = "";
                    break;
                case ComponentFindNode cfnL:
                    cfnL.CollectionKind = CollectionKind.BlackboardFixedList;
                    cfnL.CollectionFieldName = listDecl.Name;
                    cfnL.ElementTypeFqn = listDecl.Type.TypeId;
                    cfnL.ComponentTypeFqn = ""; cfnL.CountAccessorFqn = ""; cfnL.ItemAccessorFqn = "";
                    break;
            }
            return;
        }

        if (sourceNode is not GetComponentNode gcn)
            return;

        var decl = gcn.Fields?.FirstOrDefault(f =>
            f.IsCollection && string.Equals(f.Name, fromPin.Label, StringComparison.OrdinalIgnoreCase));
        if (decl == null)
            return;

        switch (consumerNode)
        {
            case ComponentForEachNode cfe:
                cfe.ComponentTypeFqn    = gcn.ComponentTypeFqn;
                cfe.CountAccessorFqn    = decl.CountAccessorFqn ?? "";
                cfe.ItemAccessorFqn     = decl.ItemAccessorFqn  ?? "";
                cfe.ElementTypeFqn      = decl.ElementTypeId    ?? "";
                cfe.CollectionKind      = decl.CollectionKind;
                cfe.CollectionFieldName = decl.CollectionFieldName;
                break;
            case ComponentItemGetNode cig:
                cig.ComponentTypeFqn    = gcn.ComponentTypeFqn;
                cig.ItemAccessorFqn     = decl.ItemAccessorFqn ?? "";
                cig.ElementTypeFqn      = decl.ElementTypeId   ?? "";
                cig.CollectionKind      = decl.CollectionKind;
                cig.CollectionFieldName = decl.CollectionFieldName;
                break;
            case ComponentItemCountNode cic:
                cic.ComponentTypeFqn    = gcn.ComponentTypeFqn;
                cic.CountAccessorFqn    = decl.CountAccessorFqn ?? "";
                cic.CollectionKind      = decl.CollectionKind;
                cic.CollectionFieldName = decl.CollectionFieldName;
                if (decl.CollectionKind == CollectionKind.ManagedMember)
                    cic.ElementTypeFqn = decl.ElementTypeId;
                break;
            case ComponentContainsNode ccn:
                ccn.ComponentTypeFqn    = gcn.ComponentTypeFqn;
                ccn.CountAccessorFqn    = decl.CountAccessorFqn ?? "";
                ccn.ItemAccessorFqn     = decl.ItemAccessorFqn  ?? "";
                ccn.ElementTypeFqn      = decl.ElementTypeId    ?? "";
                ccn.CollectionKind      = decl.CollectionKind;
                ccn.CollectionFieldName = decl.CollectionFieldName;
                break;
            case ComponentFindNode cfn:
                cfn.ComponentTypeFqn    = gcn.ComponentTypeFqn;
                cfn.CountAccessorFqn    = decl.CountAccessorFqn ?? "";
                cfn.ItemAccessorFqn     = decl.ItemAccessorFqn  ?? "";
                cfn.ElementTypeFqn      = decl.ElementTypeId    ?? "";
                cfn.CollectionKind      = decl.CollectionKind;
                cfn.CollectionFieldName = decl.CollectionFieldName;
                break;

            // FC-1 (Q#20): the WRITE node bakes only when BOTH writability gates pass -- otherwise
            // it deliberately stays unbaked (canvas bake-incomplete error + Stage2 BP2067 flag it;
            // the "do nothing, other validation handles it" rule this method already follows for a
            // mismatched wire). Gates: (0) ManagedMember decls are NEVER element-writable (Q#20-C /
            // BP2068 -- do not bake a shape Stage2 exists to reject); (1) the component carries
            // [BlueprintWritable] (the same opt-in the SetComponent picker enforces, CA-04);
            // (2) the op's [BlueprintCollectionWrite] accessor exists for this (component,
            // collection) -- accessor PRESENCE is the per-field/per-op opt-in (Q#20-A amendment).
            case CollectionWriteNode cwn:
                if (decl.CollectionKind == CollectionKind.ManagedMember) return;
                if (!NodeDrawers.ComponentFieldReflector.IsWritableComponent(gcn.ComponentTypeFqn)) return;
                var writeOps = NodeDrawers.ComponentFieldReflector.TryReflectWriteAccessors(
                    gcn.ComponentTypeFqn, decl.Name);
                if (!writeOps.TryGetValue(cwn.Op, out var writeFqn)) return;
                cwn.ComponentTypeFqn = gcn.ComponentTypeFqn;
                cwn.WriteAccessorFqn = writeFqn;
                cwn.ElementTypeFqn   = decl.ElementTypeId ?? "";
                cwn.CollectionKind   = CollectionKind.CuratedStatic;
                break;
        }
    }

    private List<Link> FindLinksByToPin(PinId toPin)
        => _graph.Links.Where(l => l.ToPinId == toPin.Value).ToList();

    private List<Link> FindLinksByFromPin(PinId fromPin)
        => _graph.Links.Where(l => l.FromPinId == fromPin.Value).ToList();

    // ── RemoveLinks ──────────────────────────────────────────────────────────

    private GraphCommandResult ApplyRemoveLinks(GraphCommand.RemoveLinks removeLinks)
    {
        // Match the exact Link objects whose stable LinkId is in the request, then drop them through
        // CommandHistory so the delete is undoable (Ctrl-Z restores the wire).
        var toRemove = _graph.Links
            .Where(l => removeLinks.Links.Any(id =>
                BlueprintGraphModel.MakeLinkId(l.FromPinId, l.ToPinId) == id))
            .ToList();
        if (toRemove.Count > 0)
            _history.Execute(new LinkEditCommand(_graph, toRemove, null, "Remove Link(s)"));

        _markDirty(_asset);
        _model.RebuildAndNotify();
        return new GraphCommandResult(true, null);
    }

    // ── MoveNodes ────────────────────────────────────────────────────────────

    private GraphCommandResult ApplyMoveNodes(GraphCommand.MoveNodes move)
    {
        var movedIds = new List<NodeId>(move.Moves.Count);

        foreach (var nodeMove in move.Moves)
        {
            // 1. Persist the new position to the asset so saving captures it.
            var assetNode = _graph.Nodes.FirstOrDefault(n => n.Id == nodeMove.Node.Value);
            if (assetNode == null) continue;

            assetNode.EditorMetadata.X = nodeMove.NewPosition.X;
            assetNode.EditorMetadata.Y = nodeMove.NewPosition.Y;

            // 2. Mutate the existing projection node instance in place —
            //    no full Rebuild() needed, preserving model identity across drag frames.
            if (_model.FindNode(nodeMove.Node) is BlueprintNodeModel projNode)
                projNode.SetPosition(nodeMove.NewPosition);

            movedIds.Add(nodeMove.Node);
        }

        // Move is not pushed to CommandHistory — continuous drag would overflow it.
        _markDirty(_asset);
        // Fire a lightweight NodesMoved notification; do NOT call RebuildAndNotify().
        _model.NotifyMoved(movedIds);
        return new GraphCommandResult(true, null);
    }

    // ── ChangeParentMultiple ─────────────────────────────────────────────────

    /// <summary>
    /// Handles <see cref="GraphCommand.ChangeParentMultiple"/> — the command the canvas
    /// issues for every node drop (BPF-029).  Persists <c>NewLocalPosition</c> to the
    /// asset's <see cref="NodeMetadata"/> so position survives save/reload.
    /// Blueprint graphs are flat (no real container hierarchy), so only position is updated;
    /// no reparent bookkeeping is required.
    /// </summary>
    private GraphCommandResult ApplyChangeParentMultiple(GraphCommand.ChangeParentMultiple cpm)
    {
        var movedIds = new List<NodeId>(cpm.Moves.Count);

        foreach (var m in cpm.Moves)
        {
            var assetNode = _graph.Nodes.FirstOrDefault(n => n.Id == m.NodeId.Value);
            if (assetNode == null) continue;

            // Persist to asset so saving captures the new position.
            assetNode.EditorMetadata.X = m.NewLocalPosition.X;
            assetNode.EditorMetadata.Y = m.NewLocalPosition.Y;

            movedIds.Add(m.NodeId);
        }

        _markDirty(_asset);
        // Lightweight notification — no full rebuild needed.
        _model.NotifyMoved(movedIds);
        return new GraphCommandResult(true, null);
    }

    // ── SetNodeProperty ──────────────────────────────────────────────────────

    private GraphCommandResult ApplySetNodeProperty(GraphCommand.SetNodeProperty prop)
    {
        var assetNode = _graph.Nodes.FirstOrDefault(n => n.Id == prop.Node.Value);
        if (assetNode == null)
            return new GraphCommandResult(false, $"Node {prop.Node} not found.");

        // Route through the EditService for undo/redo.
        object? previousValue = GetNodeProperty(assetNode, prop.Key);
        object? newValue      = prop.Value;

        _editService.RecordPropertyEdit(
            _asset,
            $"Set {prop.Key} on {assetNode.Id}",
            apply: () => SetNodeProperty(assetNode, prop.Key, newValue),
            undo:  () => SetNodeProperty(assetNode, prop.Key, previousValue));

        _model.RebuildAndNotify();
        return new GraphCommandResult(true, null);
    }

    private static object? GetNodeProperty(Node node, string key) => key switch
    {
        "Comment"      => node.EditorMetadata.Comment,
        "TargetTypeId" => (node as FunctionCallNode)?.TargetTypeId,
        "MethodName"   => (node as FunctionCallNode)?.MethodName,
        // Slice 2a-3: GetSharedNode/SetSharedNode share the "VariableId" key with
        // GetVariableNode/SetVariableNode (same authoring concept: the slot name).
        "VariableId"   => (node as GetVariableNode)?.VariableId
                       ?? (node as SetVariableNode)?.VariableId
                       ?? (node as GetSharedNode)?.VariableId
                       ?? (node as SetSharedNode)?.VariableId,
        // Slice 2a-3: the Category-1 shared struct FQN, unique to GetShared/SetShared.
        "SharedTypeId" => (node as GetSharedNode)?.SharedTypeId
                       ?? (node as SetSharedNode)?.SharedTypeId,
        "EventTypeId"  => (node as EventEntryNode)?.EventTypeId,
        "isBreakpoint" => null,   // runtime-only; not stored on asset
        _              => null,
    };

    private static void SetNodeProperty(Node node, string key, object? value)
    {
        switch (key)
        {
            case "Comment":
                node.EditorMetadata.Comment = value as string;
                break;
            case "TargetTypeId" when node is FunctionCallNode fc1:
                fc1.TargetTypeId = value as string ?? "";
                break;
            case "MethodName" when node is FunctionCallNode fc2:
                fc2.MethodName = value as string ?? "";
                break;
            case "VariableId" when node is GetVariableNode gv:
                gv.VariableId = value as string ?? "";
                break;
            case "VariableId" when node is SetVariableNode sv:
                sv.VariableId = value as string ?? "";
                break;
            // Slice 2a-3: GetSharedNode/SetSharedNode VariableId + SharedTypeId — the same
            // SetNodeProperty path GetVariableNode/SetVariableNode use, so any UI that issues
            // GraphCommand.SetNodeProperty (e.g. a future picker widget) works unmodified.
            case "VariableId" when node is GetSharedNode gsn:
                gsn.VariableId = value as string ?? "";
                break;
            case "VariableId" when node is SetSharedNode ssn:
                ssn.VariableId = value as string ?? "";
                break;
            case "SharedTypeId" when node is GetSharedNode gsn2:
                gsn2.SharedTypeId = value as string ?? "";
                break;
            case "SharedTypeId" when node is SetSharedNode ssn2:
                ssn2.SharedTypeId = value as string ?? "";
                break;
            case "EventTypeId" when node is EventEntryNode ee:
                ee.EventTypeId = value as string ?? "";
                break;
            // "isBreakpoint" is runtime-only; silently skip.
        }
    }

    // ── SetPinDefault ────────────────────────────────────────────────────────

    /// <summary>
    /// Handles <see cref="GraphCommand.SetPinDefault"/>.
    /// Stores the new value in <see cref="Node.PinDefaults"/> (keyed by pin name),
    /// marks the asset dirty, and rebuilds the canvas model.
    /// The <see cref="EditService"/> is used so the operation participates in undo/redo.
    /// </summary>
    private GraphCommandResult ApplySetPinDefault(GraphCommand.SetPinDefault cmd)
    {
        // Resolve which pin + node this refers to via the current model.
        var pinModel = _model.FindPin(cmd.Pin);
        if (pinModel == null)
            return new GraphCommandResult(false, $"Pin {cmd.Pin} not found.");

        // Find the asset node that owns this pin.
        var assetNode = _graph.Nodes.FirstOrDefault(n => n.Id == pinModel.OwnerNodeId.Value);
        if (assetNode == null)
            return new GraphCommandResult(false, $"Node {pinModel.OwnerNodeId} not found.");

        string pinName  = pinModel.Label;
        string typeId   = pinModel.Type?.Id ?? "";

        // Literal: the inline body editor commits into LiteralNode.ValueJson (with the correct C#
        // literal formatting — float 'f' suffix, string quotes) rather than the generic PinDefaults
        // map, so the designer types a bare value and never sees the C# syntax.
        if (assetNode is LiteralNode literal)
        {
            var newJson = LiteralValueJson.ToValueJson(literal.TypeId, cmd.NewValue);
            var oldJson = literal.ValueJson;
            _editService.RecordPropertyEdit(
                _asset,
                "Set literal value",
                apply: () => literal.ValueJson = newJson,
                undo:  () => literal.ValueJson = oldJson);
            _model.RebuildAndNotify();
            return new GraphCommandResult(true, null);
        }

        // For enum pins the editor sets value = (long)selectedEntry.Value.
        // ENUM-NAME: persist as the member name string, not the integer.
        string? newStr;
        if (!string.IsNullOrEmpty(typeId)
            && typeId.StartsWith("global::", StringComparison.Ordinal)
            && cmd.NewValue is long enumLong)
        {
            newStr = BlueprintPinDefaultValue.FormatEnumValue(enumLong, typeId, _enumProvider);
        }
        else
        {
            newStr = BlueprintPinDefaultValue.FormatValue(cmd.NewValue);
        }

        // Capture old value for undo.
        string? oldStr  = assetNode.PinDefaults?.TryGetValue(pinName, out var o) == true ? o : null;

        _editService.RecordPropertyEdit(
            _asset,
            $"Set pin default '{pinName}'",
            apply: () => SetPinDefaultOnNode(assetNode, pinName, newStr),
            undo:  () => SetPinDefaultOnNode(assetNode, pinName, oldStr));

        _model.RebuildAndNotify();
        return new GraphCommandResult(true, null);
    }

    private static void SetPinDefaultOnNode(Node node, string pinName, string? value)
    {
        if (value == null)
        {
            // Remove entry when value is cleared.
            node.PinDefaults?.Remove(pinName);
            if (node.PinDefaults?.Count == 0)
                node.PinDefaults = null;
        }
        else
        {
            node.PinDefaults ??= new Dictionary<string, string>();
            node.PinDefaults[pinName] = value;
        }
    }

    // ── Reroute ──────────────────────────────────────────────────────────────

    private GraphCommandResult ApplyInsertReroute(GraphCommand.InsertReroute cmd)
    {
        var assetLink = _model.FindAssetLink(cmd.Link);
        if (assetLink == null)
            return new GraphCommandResult(true, null);   // unknown link — safe no-op

        assetLink.Waypoints ??= new List<LinkWaypoint>();
        assetLink.Waypoints.Add(new LinkWaypoint { X = cmd.Position.X, Y = cmd.Position.Y });

        _markDirty(_asset);
        _model.RebuildAndNotify();
        return new GraphCommandResult(true, null);
    }

    private GraphCommandResult ApplyMoveReroute(GraphCommand.MoveReroute cmd)
    {
        var assetLink = _model.FindAssetLink(cmd.Link);
        if (assetLink == null || assetLink.Waypoints == null)
            return new GraphCommandResult(true, null);   // unknown link / no waypoints — safe no-op

        if (cmd.WaypointIndex < 0 || cmd.WaypointIndex >= assetLink.Waypoints.Count)
            return new GraphCommandResult(true, null);   // out-of-range — safe no-op

        assetLink.Waypoints[cmd.WaypointIndex] = new LinkWaypoint
            { X = cmd.NewPosition.X, Y = cmd.NewPosition.Y };

        _markDirty(_asset);
        _model.RebuildAndNotify();
        return new GraphCommandResult(true, null);
    }

    private GraphCommandResult ApplyRemoveReroute(GraphCommand.RemoveReroute cmd)
    {
        var assetLink = _model.FindAssetLink(cmd.Link);
        if (assetLink == null || assetLink.Waypoints == null)
            return new GraphCommandResult(true, null);   // unknown link / no waypoints — safe no-op

        if (cmd.WaypointIndex < 0 || cmd.WaypointIndex >= assetLink.Waypoints.Count)
            return new GraphCommandResult(true, null);   // out-of-range — safe no-op

        assetLink.Waypoints.RemoveAt(cmd.WaypointIndex);

        _markDirty(_asset);
        _model.RebuildAndNotify();
        return new GraphCommandResult(true, null);
    }

    // ── Comments (Unreal-style comment boxes) ─────────────────────────────────

    /// <summary>
    /// Handles <see cref="GraphCommand.AddComment"/>. Creates a <see cref="GraphComment"/> on the
    /// asset graph (mirrors how <see cref="ApplyInsertReroute"/> mutates <see cref="Link.Waypoints"/>).
    /// Routed through <see cref="_editService"/> so Add-Comment participates in undo/redo.
    /// </summary>
    private GraphCommandResult ApplyAddComment(GraphCommand.AddComment cmd)
    {
        var comment = new GraphComment
        {
            Id               = cmd.AssignedId.Value,
            Text             = cmd.Text,
            X                = cmd.Position.X,
            Y                = cmd.Position.Y,
            W                = cmd.Size.X,
            H                = cmd.Size.Y,
            ColorR           = cmd.Color.X,
            ColorG           = cmd.Color.Y,
            ColorB           = cmd.Color.Z,
            ColorA           = cmd.Color.W,
            MoveWithContents = cmd.MoveWithContents,
        };

        _editService.RecordPropertyEdit(
            _asset,
            "Add Comment",
            apply: () => _graph.Comments.Add(comment),
            undo:  () => _graph.Comments.RemoveAll(c => c.Id == comment.Id));

        _model.RebuildAndNotify();
        return new GraphCommandResult(true, null);
    }

    /// <summary>
    /// Handles <see cref="GraphCommand.UpdateComment"/>. Every field is a nullable "only touch
    /// what's set" patch (rename, drag-move, resize, recolor, z-order restack, move-with-contents
    /// toggle all funnel through this one command — see <c>CanvasRenderer.DrawContextMenu</c> and
    /// <c>CommentsRenderer.RenderRenameField</c>). Undo restores exactly the fields that changed.
    /// </summary>
    private GraphCommandResult ApplyUpdateComment(GraphCommand.UpdateComment cmd)
    {
        var comment = _graph.Comments.FirstOrDefault(c => c.Id == cmd.Id.Value);
        if (comment == null)
            return new GraphCommandResult(true, null);   // unknown id — safe no-op

        string?  oldText   = cmd.Text             is not null ? comment.Text             : null;
        Vector2? oldPos    = cmd.Position          is not null ? new Vector2(comment.X, comment.Y) : null;
        Vector2? oldSize   = cmd.Size              is not null ? new Vector2(comment.W, comment.H) : null;
        Vector4? oldColor  = cmd.Color             is not null ? new Vector4(comment.ColorR, comment.ColorG, comment.ColorB, comment.ColorA) : null;
        int?     oldZOrder = cmd.ZOrder            is not null ? comment.ZOrder            : null;
        bool?    oldMwc    = cmd.MoveWithContents  is not null ? comment.MoveWithContents  : null;

        _editService.RecordPropertyEdit(
            _asset,
            "Update Comment",
            apply: () => ApplyCommentFields(comment, cmd.Text, cmd.Position, cmd.Size, cmd.Color, cmd.ZOrder, cmd.MoveWithContents),
            undo:  () => ApplyCommentFields(comment, oldText, oldPos, oldSize, oldColor, oldZOrder, oldMwc));

        _model.RebuildAndNotify();
        return new GraphCommandResult(true, null);
    }

    private static void ApplyCommentFields(
        GraphComment comment, string? text, Vector2? position, Vector2? size, Vector4? color,
        int? zOrder, bool? moveWithContents)
    {
        if (text is not null) comment.Text = text;
        if (position is not null) { comment.X = position.Value.X; comment.Y = position.Value.Y; }
        if (size is not null) { comment.W = size.Value.X; comment.H = size.Value.Y; }
        if (color is not null)
        {
            comment.ColorR = color.Value.X;
            comment.ColorG = color.Value.Y;
            comment.ColorB = color.Value.Z;
            comment.ColorA = color.Value.W;
        }
        if (zOrder is not null) comment.ZOrder = zOrder.Value;
        if (moveWithContents is not null) comment.MoveWithContents = moveWithContents.Value;
    }

    /// <summary>Handles <see cref="GraphCommand.RemoveComment"/>.</summary>
    private GraphCommandResult ApplyRemoveComment(GraphCommand.RemoveComment cmd)
    {
        var comment = _graph.Comments.FirstOrDefault(c => c.Id == cmd.Id.Value);
        if (comment == null)
            return new GraphCommandResult(true, null);   // unknown id — safe no-op

        _editService.RecordPropertyEdit(
            _asset,
            "Remove Comment",
            apply: () => _graph.Comments.RemoveAll(c => c.Id == comment.Id),
            undo:  () => _graph.Comments.Add(comment));

        _model.RebuildAndNotify();
        return new GraphCommandResult(true, null);
    }

    // ── Batch ────────────────────────────────────────────────────────────────

    private GraphCommandResult ApplyBatch(GraphCommand.Batch batch)
    {
        foreach (var inner in batch.Commands)
        {
            var result = Apply(inner);
            if (!result.Success)
                return result;   // stop on first failure
        }
        return new GraphCommandResult(true, null);
    }
}
