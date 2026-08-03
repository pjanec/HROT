using System.Reflection;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Core.Compiler.Stages;

/// <summary>
/// Stage 0 — Pin Rehydration.
/// <para>
/// Blueprints are saved projection-only (<c>"Pins": []</c>).  On reload the compiler receives
/// pin-less nodes and connection resolution in Stage4/Stage5 is 100% pin-ID-driven, so every
/// wire dangles → empty Tick → "Count stays 0".
/// </para>
/// <para>
/// This stage runs as the FIRST pass in <see cref="BlueprintCompiler.Compile"/> (before Stage2_Validate).
/// For each node whose <c>Pins</c> list is <b>empty</b>, it:
/// <list type="number">
///   <item>Builds the canonical ordered pin list from static registry shapes + dynamic asset state.</item>
///   <item>Assigns link GUIDs using the positional-within-direction-bucket algorithm mirrored from
///     <c>BlueprintGraphModel.Rebuild</c> (slow-path, Pins:[] case at ~:181-228):
///     Out pins ← distinct <c>FromPinId</c> from outgoing links in order;
///     In pins  ← distinct <c>ToPinId</c>  from incoming links in order;
///     leftover pins ← deterministic synthetic GUID.</item>
///   <item>Assigns the rehydrated pins to <c>node.Pins</c>.</item>
/// </list>
/// </para>
/// <para>
/// NO-SWALLOW: if pins cannot be resolved (e.g. CLR FunctionCall target not loaded), a diagnostic
/// is emitted naming the node + reason, and an exec-only fallback is used.
/// </para>
/// </summary>
internal static class Stage0_Rehydrate
{
    /// <summary>
    /// Rehydrate all pin-less nodes in <paramref name="asset"/> using the registry in
    /// <paramref name="options"/>.  Mutates <c>node.Pins</c> in place.
    /// </summary>
    public static void Run(BlueprintAsset asset, CompileOptions options)
    {
        var registry = options.NodeRegistry;

        // Build sibling-signature lookup for CallPeerBlueprint.
        var siblingLookup = options.SiblingSignatures.Count > 0
            ? (Func<Guid, BlueprintSignature?>)(id =>
                options.SiblingSignatures.FirstOrDefault(s => s.AssetId == id))
            : null;

        foreach (var graph in asset.Graphs)
        {
            // Pre-build link adjacency lists for this graph.
            var outLinks = new Dictionary<Guid, List<Link>>();  // nodeId -> links originating from node
            var inLinks  = new Dictionary<Guid, List<Link>>();  // nodeId -> links arriving at node

            foreach (var link in graph.Links)
            {
                if (!outLinks.TryGetValue(link.FromNodeId, out var ol))
                    outLinks[link.FromNodeId] = ol = new List<Link>();
                ol.Add(link);

                if (!inLinks.TryGetValue(link.ToNodeId, out var il))
                    inLinks[link.ToNodeId] = il = new List<Link>();
                il.Add(link);
            }

            foreach (var node in graph.Nodes)
            {
                // Skip nodes that already have pins (authored test fixtures, Stage3 cast nodes).
                if (node.Pins.Count > 0) continue;

                // Pre-fetch link adjacency for this node (needed by pure-CLR-call fallback).
                outLinks.TryGetValue(node.Id, out var outL);
                inLinks .TryGetValue(node.Id, out var inL);

                // Build canonical pin list for this node.
                var canonicalPins = BuildCanonicalPins(node, graph, asset, registry, siblingLookup, options, outL, inL);

                // Assign link GUIDs via the positional-within-direction-bucket algorithm.
                AssignLinkGuids(canonicalPins, node.Id, outL, inL);

                // Write back to node.
                node.Pins = canonicalPins;
            }
        }
    }

    // ── Canonical pin construction ────────────────────────────────────────────

    private static List<Pin> BuildCanonicalPins(
        Node node, Graph graph, BlueprintAsset asset,
        INodeRegistry registry,
        Func<Guid, BlueprintSignature?>? siblingLookup,
        CompileOptions options,
        List<Link>? outL,
        List<Link>? inL)
    {
        // Get the static skeleton from the registry.
        IReadOnlyList<PinSchema> staticShapes;
        try
        {
            staticShapes = registry.GetStaticPins(node);
        }
        catch (Exception ex)
        {
            // Registry threw — fall back to empty (dynamic will enrich below or exec fallback).
            staticShapes = Array.Empty<PinSchema>();
            _ = ex; // suppress unused warning
        }

        // Convert static schemas to Pin objects (no GUIDs yet — assigned later).
        var pins = new List<Pin>(staticShapes.Count + 8);
        foreach (var s in staticShapes)
            pins.Add(MakePin(s.Name, s.Direction, s.IsExec, s.TypeId));

        // Enrich with dynamic pins for dynamic-pin node kinds.
        switch (node)
        {
            case EventEntryNode:
                EnrichEventEntryPins(pins, graph, staticShapes);
                break;

            case GetAllParametersNode:
                EnrichGetAllParametersPins(pins, asset, staticShapes);
                break;

            case ReturnNode:
                EnrichReturnPins(pins, graph, staticShapes);
                break;

            case GetVariableNode gv:
                EnrichGetVariablePins(pins, gv, asset, staticShapes);
                break;

            case SetVariableNode sv:
                EnrichSetVariablePins(pins, sv, asset, staticShapes);
                break;

            case GetSharedNode gsn:
                EnrichGetSharedPins(pins, gsn, staticShapes);
                break;

            case SetSharedNode ssn:
                EnrichSetSharedPins(pins, ssn, staticShapes);
                break;

            case GetComponentNode gcn:
                EnrichGetComponentPins(pins, gcn, staticShapes);
                break;

            case SetComponentNode scn:
                EnrichSetComponentPins(pins, scn, staticShapes);
                break;

            case ComponentForEachNode cfe:
                EnrichComponentForEachPins(pins, cfe);
                break;

            case ComponentItemGetNode cig:
                EnrichComponentItemGetPins(pins, cig);
                break;

            case ComponentItemCountNode cic:
                EnrichComponentItemCountPins(pins, cic);
                break;

            case ComponentContainsNode ccn:
                EnrichComponentContainsPins(pins, ccn);
                break;

            case ComponentFindNode cfn:
                EnrichComponentFindPins(pins, cfn);
                break;

            case CollectionWriteNode cwn:
                EnrichCollectionWritePins(pins, cwn);
                break;

            case MakeStructNode msn:
                EnrichMakeStructPins(pins, msn);
                break;

            case BreakStructNode bsn:
                EnrichBreakStructPins(pins, bsn);
                break;

            case SetMembersNode smn:
                EnrichSetMembersPins(pins, smn);
                break;

            case FunctionCallNode fc:
                EnrichFunctionCallPins(pins, fc, asset, graph, options, staticShapes, outL, inL);
                break;

            case PublishEventNode pen:
                EnrichPublishEventPins(pins, pen, options);
                break;

            case ChannelCommandNode cc:
                EnrichChannelCommandPins(pins, cc, options);
                break;

            case CallCustomEventNode cce:
                EnrichCallCustomEventPins(pins, cce, asset, staticShapes);
                break;

            case CallPeerBlueprintNode cpb:
                EnrichCallPeerBlueprintPins(pins, cpb, siblingLookup, staticShapes);
                break;
        }

        // NO-SWALLOW: if node has no pins at all and it is a known kind that must have at least
        // exec pins, emit a warning and fall back to exec In+Out.
        if (pins.Count == 0 && NodeRequiresExecFallback(node))
        {
            // Log to console — compiler has no sink here (pre-Stage2), but must not swallow.
            // In practice this fires for CLR FunctionCall whose assembly is not loaded (MSBuild host).
            System.Diagnostics.Debug.WriteLine(
                $"[BP-Stage0] WARN: Node {node.Id} ({node.GetType().Name}) has no resolvable pins; " +
                $"using exec-only fallback.");
            pins.Add(MakePin("In",  "In",  isExec: true, typeId: ""));
            pins.Add(MakePin("Out", "Out", isExec: true, typeId: ""));
        }

        return pins;
    }

    // ── Dynamic pin enrichers ─────────────────────────────────────────────────

    private static void EnrichEventEntryPins(
        List<Pin> pins, Graph graph, IReadOnlyList<PinSchema> staticShapes)
    {
        // Static skeleton: exec-Out "Out" already added from registry.
        // Enrich: add one data-Out per Graph.Inputs entry. Function graphs expose their declared
        // inputs; Event graphs (Q#14 custom-event subscribers) expose the event PAYLOAD fields the
        // same way, so downstream nodes can wire the payload (EventEntry data-out → e.g. SetVariable).
        // Stage5's EventEntryNode resolution (IrOp_ReadInputArg, name-matched against Graph.Inputs)
        // and the InstanceEmitter thunk (which passes each __ev.{field}) already handle both kinds;
        // this early-return was the sole gate keeping subscribers from reading their payload.
        if ((graph.Kind != GraphKind.Function && graph.Kind != GraphKind.Event) || graph.Inputs.Count == 0)
            return;

        // Ensure we have the exec-Out from static; then add data-Out pins.
        // (Clear and rebuild to avoid duplicates when static already added exec-Out.)
        pins.Clear();
        pins.Add(MakePin("Out", "Out", isExec: true, typeId: ""));
        foreach (var inp in graph.Inputs)
        {
            var inpTypeId = GetTypeId(inp.Type);
            pins.Add(MakePin(inp.Name, "Out", isExec: false, typeId: inpTypeId));
        }
    }

    /// <summary>
    /// GetAllParametersNode: pure-data node, static skeleton is empty (registry mirrors
    /// EventEntryNode's dynamic-kind treatment). Enrich: add ONE data-Out pin per
    /// <c>asset.Parameters</c> entry (name = <see cref="ParameterDecl.Name"/>, type from
    /// <see cref="ParameterDecl.Type"/>; fallback <c>System.Object</c>) -- mirrors
    /// <see cref="EnrichEventEntryPins"/> exactly, retargeted at <c>asset.Parameters</c> instead of
    /// <c>graph.Inputs</c>. No exec pins (pure node, unlike EventEntryNode's exec-Out).
    /// </summary>
    private static void EnrichGetAllParametersPins(
        List<Pin> pins, BlueprintAsset asset, IReadOnlyList<PinSchema> staticShapes)
    {
        pins.Clear();
        foreach (var p in asset.Parameters)
        {
            var typeId = GetTypeId(p.Type);
            pins.Add(MakePin(p.Name, "Out", isExec: false, typeId: typeId));
        }
    }

    private static void EnrichReturnPins(
        List<Pin> pins, Graph graph, IReadOnlyList<PinSchema> staticShapes)
    {
        // Static skeleton: exec-In "In" already added from registry.
        // Enrich: add data-Out from Graph.Outputs[0] for Function graphs.
        if (graph.Kind != GraphKind.Function || graph.Outputs.Count == 0)
            return;

        var output = graph.Outputs[0];
        var typeId = GetTypeId(output.Type);
        // Data pin already not in static (static is exec-In only) — just add it.
        pins.Add(MakePin(output.Name, "Out", isExec: false, typeId: typeId));
    }

    private static void EnrichGetVariablePins(
        List<Pin> pins, GetVariableNode gv, BlueprintAsset asset,
        IReadOnlyList<PinSchema> staticShapes)
    {
        // Static: empty (registry returns empty for GetVariable).
        // Build: data-Out "Value" typed from the variable.
        var typeId = ResolveVariableTypeId(gv.VariableId, asset);
        pins.Clear();
        pins.Add(MakePin("Value", "Out", isExec: false, typeId: typeId));
    }

    private static void EnrichSetVariablePins(
        List<Pin> pins, SetVariableNode sv, BlueprintAsset asset,
        IReadOnlyList<PinSchema> staticShapes)
    {
        // Static: exec In/Out from registry.
        // Enrich: add data-In "Value" + data-Out "Value" typed from the variable.
        var typeId = ResolveVariableTypeId(sv.VariableId, asset);
        pins.Clear();
        pins.Add(MakePin("In",    "In",  isExec: true,  typeId: ""));
        pins.Add(MakePin("Out",   "Out", isExec: true,  typeId: ""));
        pins.Add(MakePin("Value", "In",  isExec: false, typeId: typeId));
        pins.Add(MakePin("Value", "Out", isExec: false, typeId: typeId));
    }

    /// <summary>
    /// GetSharedNode (Slice 2a-2 + Slice 2b): pure-data node. Static skeleton is empty (registry
    /// mirrors GetVariableNode). Build data-In "Target" (OPTIONAL, <c>Fdp.Core.Entity</c> -- Slice
    /// 2b cross-entity read; unwired = self, mirrors how <c>IrOp_GetComponent</c>'s Entity argument
    /// is carried/typed) + data-Out "Value" typed DIRECTLY from
    /// <see cref="GetSharedNode.SharedTypeId"/> (NOT <c>ResolveVariableTypeId</c> over
    /// <c>asset.Variables</c> -- the shared struct is foreign to this asset's variable list) +
    /// data-Out "Found" (<c>System.Boolean</c>).
    /// </summary>
    private static void EnrichGetSharedPins(
        List<Pin> pins, GetSharedNode gsn, IReadOnlyList<PinSchema> staticShapes)
    {
        pins.Clear();
        pins.Add(MakePin("Target", "In", isExec: false, typeId: "Fdp.Core.Entity"));

        // Q#14 multi-pin: baked per-field decls → one data-OUT pin per field (read the struct once,
        // project each field) + "Found". Mirrors EnrichSetSharedPins / the PublishEvent baked path.
        if (gsn.Fields is { Count: > 0 })
        {
            foreach (var f in gsn.Fields)
                pins.Add(MakePin(f.Name, "Out", isExec: false, typeId: f.TypeId));
            pins.Add(MakePin("Found", "Out", isExec: false, typeId: "System.Boolean"));
            return;
        }

        // Legacy whole-struct path: single "Value" data-out (typed by SharedTypeId) + "Found".
        var typeId = SharedTypePinTypeId(gsn.SharedTypeId);
        pins.Add(MakePin("Value", "Out", isExec: false, typeId: typeId));
        pins.Add(MakePin("Found", "Out", isExec: false, typeId: "System.Boolean"));
    }

    /// <summary>
    /// SetSharedNode (Slice 2a-2): exec node. Static skeleton is exec In/Out (registry mirrors
    /// SetVariableNode). Enrich: add data-In "Value" typed DIRECTLY from
    /// <see cref="SetSharedNode.SharedTypeId"/> + data-Out "Written" (<c>System.Boolean</c>).
    /// </summary>
    private static void EnrichSetSharedPins(
        List<Pin> pins, SetSharedNode ssn, IReadOnlyList<PinSchema> staticShapes)
    {
        pins.Clear();
        pins.Add(MakePin("In",  "In",  isExec: true, typeId: ""));
        pins.Add(MakePin("Out", "Out", isExec: true, typeId: ""));

        // Q#14 multi-pin: baked per-field decls → one data-in pin per field (name = field name, matched
        // by Stage5 lowering) so the designer sets fields directly. Unwired fields are simply not written.
        if (ssn.Fields is { Count: > 0 })
        {
            foreach (var f in ssn.Fields)
                pins.Add(MakePin(f.Name, "In", isExec: false, typeId: f.TypeId));
            return;
        }

        // Legacy whole-struct path: single "Value" data-in + "Written" data-out.
        var typeId = SharedTypePinTypeId(ssn.SharedTypeId);
        pins.Add(MakePin("Value",   "In",  isExec: false, typeId: typeId));
        pins.Add(MakePin("Written", "Out", isExec: false, typeId: "System.Boolean"));
    }

    /// <summary>
    /// GetComponentNode (CA-01, Slice 1a): pure-data node, mirrors <see cref="EnrichGetSharedPins"/>
    /// exactly. Static skeleton is empty (registry mirrors GetSharedNode). Build data-In "Target"
    /// (OPTIONAL, <c>Fdp.Core.Entity</c> -- cross-entity read, unwired = self) + either one data-Out
    /// pin PER baked <see cref="GetComponentNode.Fields"/> entry (multi-pin) or the legacy single
    /// "Value" data-Out typed from <see cref="GetComponentNode.FieldTypeFqn"/> -- plus data-Out
    /// "Found" (<c>System.Boolean</c>) in BOTH cases.
    /// </summary>
    private static void EnrichGetComponentPins(
        List<Pin> pins, GetComponentNode gcn, IReadOnlyList<PinSchema> staticShapes)
    {
        pins.Clear();

        // CA-01 multi-pin (Fields baked): optional cross-entity "Target" (unwired = self) + one
        // data-OUT per field (read the component once, project each field) + "Found". Mirrors
        // EnrichGetSharedPins's Fields branch. Target/Found are MULTI-PIN-MODE ONLY -- the legacy
        // single-field path below is frozen at its pre-CA-01 shape so Stage5's untouched legacy
        // branch never leaves a projected pin uncomputed, and authored-pin hill-attack assets
        // (which skip this enricher via the Pins.Count>0 guard) round-trip byte-identically.
        if (gcn.Fields is { Count: > 0 })
        {
            pins.Add(MakePin("Target", "In", isExec: false, typeId: "Fdp.Core.Entity"));
            foreach (var f in gcn.Fields)
            {
                // CA-07a: a collection decl projects ONE out-pin typed by its ELEMENT type with
                // IsArray true (the "whole collection" pin) instead of the scalar TypeId pin --
                // in the SAME position it appears in Fields (append order), between the scalar
                // field pins and the trailing "Found" pin below.
                if (f.IsCollection)
                    pins.Add(MakePin(f.Name, "Out", isExec: false, typeId: string.IsNullOrEmpty(f.ElementTypeId) ? "System.Object" : f.ElementTypeId!, isArray: true));
                else
                    pins.Add(MakePin(f.Name, "Out", isExec: false, typeId: f.TypeId));
            }
            pins.Add(MakePin("Found", "Out", isExec: false, typeId: "System.Boolean"));
            return;
        }

        // Legacy single-field path (FROZEN, self-only): single "Value" data-out typed by FieldTypeFqn
        // VERBATIM -- unlike GetShared's whole-struct SharedTypeId, FieldTypeFqn is routinely a
        // well-known primitive (e.g. "System.Single") that StaticTypeRegistry's TypeTable matches
        // directly; stamping a "global::" prefix would misroute it into the AN2 enum/project-type
        // acceptance path (wrong -- that path assumes a 4-byte enum-int32 underlying type). Mirrors
        // Stage5_Schedule's existing legacy fallback, which also uses FieldTypeFqn verbatim. No
        // Target/Found pins -- matches the untouched legacy lowering (self-only, single value).
        var typeId = string.IsNullOrEmpty(gcn.FieldTypeFqn) ? "System.Object" : gcn.FieldTypeFqn;
        pins.Add(MakePin("Value", "Out", isExec: false, typeId: typeId));
    }

    /// <summary>
    /// SetComponentNode (CA-03/CA-06): exec node, mirrors <see cref="EnrichSetSharedPins"/>.
    /// Static skeleton is exec In/Out. UNMANAGED (<see cref="SetComponentNode.IsManaged"/> == false):
    /// one data-IN pin PER baked <see cref="SetComponentNode.Fields"/> entry (no fields baked yet ⇒
    /// none). MANAGED (CA-06, Slice W2, Q#16-C): a SINGLE data-IN "Value" pin typed by
    /// <see cref="SetComponentNode.ComponentTypeFqn"/> instead -- whole-replace only, never per-field
    /// (checked first, below, before the unmanaged Fields branch). Both shapes get a data-OUT
    /// "Written" (<c>System.Boolean</c>) -- UNCONDITIONALLY, unlike SetShared's per-field branch
    /// (which has no "Written" at all): SetComponent is write-if-present (no implicit add), so
    /// "Written" is the write's Has(Managed)Component guard result and always exists. Self-only
    /// (Q#16) -- NO "Target" pin, ever, in either shape.
    /// </summary>
    private static void EnrichSetComponentPins(
        List<Pin> pins, SetComponentNode scn, IReadOnlyList<PinSchema> staticShapes)
    {
        pins.Clear();
        pins.Add(MakePin("In",  "In",  isExec: true, typeId: ""));
        pins.Add(MakePin("Out", "Out", isExec: true, typeId: ""));

        // CA-06 (Slice W2, Q#16-C): managed write is WHOLE-REPLACE ONLY -- a single data-IN "Value"
        // pin typed by ComponentTypeFqn (global::-stamped, mirrors SetShared's legacy whole-struct
        // "Value" pin), NEVER per-field pins (per-field managed write is FORBIDDEN -- snapshot
        // aliasing). Checked BEFORE scn.Fields below so a managed node's Fields (which the editor
        // must never bake -- see SetComponentNodeSession.ApplyComponentTypeFqn) can't leak a
        // per-field shape through even if a hand-authored/legacy asset carries both.
        if (scn.IsManaged)
        {
            pins.Add(MakePin("Value", "In", isExec: false, typeId: SharedTypePinTypeId(scn.ComponentTypeFqn)));
            pins.Add(MakePin("Written", "Out", isExec: false, typeId: "System.Boolean"));
            return;
        }

        if (scn.Fields is { Count: > 0 })
        {
            foreach (var f in scn.Fields)
                pins.Add(MakePin(f.Name, "In", isExec: false, typeId: f.TypeId));
        }

        pins.Add(MakePin("Written", "Out", isExec: false, typeId: "System.Boolean"));
    }

    /// <summary>
    /// ComponentForEachNode (CA-07b): exec node. Static skeleton (registry) types "Collection"/
    /// "CurrentItem" as System.Object -- rebuild here with the REAL element type from the node's
    /// OWN baked <see cref="ComponentForEachNode.ElementTypeFqn"/> (falls back to System.Object
    /// when not yet baked, e.g. a freshly dropped node before CA-07c wires it). "Collection" is
    /// IsArray (mirrors the GetComponent collection out-pin it consumes -- see
    /// <see cref="EnrichGetComponentPins"/>'s collection-decl branch); "CurrentItem" is the same
    /// element type; "CurrentIndex"/"Count" are System.Int32. "Body"/"Completed" exec-out names are
    /// load-bearing for Stage5 (mirrors FlowForEachNode, which needs no enricher since its item
    /// type is always the fixed Fdp.Core.Entity).
    /// </summary>
    private static void EnrichComponentForEachPins(List<Pin> pins, ComponentForEachNode cfe)
    {
        var elemType = string.IsNullOrEmpty(cfe.ElementTypeFqn) ? "System.Object" : cfe.ElementTypeFqn;
        pins.Clear();
        pins.Add(MakePin("In", "In", isExec: true, typeId: ""));
        pins.Add(MakePin("Collection", "In", isExec: false, typeId: elemType, isArray: true));
        pins.Add(MakePin("Body",      "Out", isExec: true, typeId: ""));
        pins.Add(MakePin("Completed", "Out", isExec: true, typeId: ""));
        pins.Add(MakePin("CurrentItem",  "Out", isExec: false, typeId: elemType));
        pins.Add(MakePin("CurrentIndex", "Out", isExec: false, typeId: "System.Int32"));
        pins.Add(MakePin("Count",        "Out", isExec: false, typeId: "System.Int32"));
    }

    /// <summary>
    /// ComponentItemGetNode (CA-07b): pure-data node. "Collection" (IsArray) + "Element" are typed
    /// by the node's OWN baked <see cref="ComponentItemGetNode.ElementTypeFqn"/> (falls back to
    /// System.Object when not yet baked). Mirrors <see cref="EnrichComponentForEachPins"/>.
    /// </summary>
    private static void EnrichComponentItemGetPins(List<Pin> pins, ComponentItemGetNode cig)
    {
        var elemType = string.IsNullOrEmpty(cig.ElementTypeFqn) ? "System.Object" : cig.ElementTypeFqn;
        pins.Clear();
        pins.Add(MakePin("Collection", "In",  isExec: false, typeId: elemType, isArray: true));
        pins.Add(MakePin("Index",      "In",  isExec: false, typeId: "System.Int32"));
        pins.Add(MakePin("Element",    "Out", isExec: false, typeId: elemType));
    }

    /// <summary>
    /// ComponentItemCountNode (CA-07b): pure-data node. No <c>ElementTypeFqn</c> on this node
    /// (Count never needs the element type), so "Collection" is always typed System.Object
    /// (IsArray) here -- <c>Stage4_TypeResolve.VerifyLinkTypes</c> already suppresses a link-type
    /// mismatch when either side is System.Object (the same escape hatch reflection-less CLR-call
    /// pins rely on), so this never mismatches the typed collection out-pin it is wired from.
    /// </summary>
    private static void EnrichComponentItemCountPins(List<Pin> pins, ComponentItemCountNode cic)
    {
        pins.Clear();
        pins.Add(MakePin("Collection", "In",  isExec: false, typeId: "System.Object", isArray: true));
        pins.Add(MakePin("Count",      "Out", isExec: false, typeId: "System.Int32"));
    }

    /// <summary>
    /// ComponentContainsNode (CA-07d-1): pure-data node. "Collection" (IsArray) + "Item" (the query
    /// value) are typed by the node's OWN baked <see cref="ComponentContainsNode.ElementTypeFqn"/>
    /// (falls back to System.Object); "Result" is Boolean. Mirrors <see cref="EnrichComponentItemGetPins"/>.
    /// </summary>
    private static void EnrichComponentContainsPins(List<Pin> pins, ComponentContainsNode ccn)
    {
        var elemType = string.IsNullOrEmpty(ccn.ElementTypeFqn) ? "System.Object" : ccn.ElementTypeFqn;
        pins.Clear();
        pins.Add(MakePin("Collection", "In",  isExec: false, typeId: elemType, isArray: true));
        pins.Add(MakePin("Item",       "In",  isExec: false, typeId: elemType));
        pins.Add(MakePin("Result",     "Out", isExec: false, typeId: "System.Boolean"));
    }

    /// <summary>
    /// CollectionWriteNode (FC-1, Q#20): exec node. "Collection" (IsArray, element-typed by the
    /// node's baked <see cref="CollectionWriteNode.ElementTypeFqn"/>, System.Object fallback --
    /// mirrors <see cref="EnrichComponentItemGetPins"/>) is the author-time binding pin ONLY (the
    /// write entity is always <c>self</c>); operand data-ins vary per <see
    /// cref="CollectionWriteNode.Op"/> (Add: Value · SetAt/InsertAt: Index+Value · RemoveAt: Index ·
    /// Clear: none · Resize: Length); "Ok" (Boolean) is the write-if-present AND-op-applied result
    /// (mirrors SetComponent's unconditional "Written").
    /// </summary>
    private static void EnrichCollectionWritePins(List<Pin> pins, CollectionWriteNode cwn)
    {
        var elemType = string.IsNullOrEmpty(cwn.ElementTypeFqn) ? "System.Object" : cwn.ElementTypeFqn;
        pins.Clear();
        pins.Add(MakePin("In",  "In",  isExec: true, typeId: ""));
        pins.Add(MakePin("Out", "Out", isExec: true, typeId: ""));
        pins.Add(MakePin("Collection", "In", isExec: false, typeId: elemType, isArray: true));
        switch (cwn.Op)
        {
            case CollectionWriteOp.Add:
                pins.Add(MakePin("Value", "In", isExec: false, typeId: elemType));
                break;
            case CollectionWriteOp.SetAt:
            case CollectionWriteOp.InsertAt:
                pins.Add(MakePin("Index", "In", isExec: false, typeId: "System.Int32"));
                pins.Add(MakePin("Value", "In", isExec: false, typeId: elemType));
                break;
            case CollectionWriteOp.RemoveAt:
                pins.Add(MakePin("Index", "In", isExec: false, typeId: "System.Int32"));
                break;
            case CollectionWriteOp.Clear:
                break;
            case CollectionWriteOp.Resize:
                pins.Add(MakePin("Length", "In", isExec: false, typeId: "System.Int32"));
                break;
        }
        pins.Add(MakePin("Ok", "Out", isExec: false, typeId: "System.Boolean"));
    }

    /// <summary>
    /// ComponentFindNode (CA-07d-1): pure-data node. "Collection" (IsArray) + "Item" (query) typed by
    /// the node's baked <see cref="ComponentFindNode.ElementTypeFqn"/>; "Index" is Int32, "Found" is
    /// Boolean (Q#18-B). Mirrors <see cref="EnrichComponentContainsPins"/>.
    /// </summary>
    private static void EnrichComponentFindPins(List<Pin> pins, ComponentFindNode cfn)
    {
        var elemType = string.IsNullOrEmpty(cfn.ElementTypeFqn) ? "System.Object" : cfn.ElementTypeFqn;
        pins.Clear();
        pins.Add(MakePin("Collection", "In",  isExec: false, typeId: elemType, isArray: true));
        pins.Add(MakePin("Item",       "In",  isExec: false, typeId: elemType));
        pins.Add(MakePin("Index",      "Out", isExec: false, typeId: "System.Int32"));
        pins.Add(MakePin("Found",      "Out", isExec: false, typeId: "System.Boolean"));
    }

    /// <summary>
    /// MakeStructNode (Q#14 Option B): pure data node. One data-IN pin per baked field (name + TypeId) +
    /// a struct-typed data-OUT "Value". Mirrors the editor's NodePinSchema.MakeStructPins.
    /// </summary>
    private static void EnrichMakeStructPins(List<Pin> pins, MakeStructNode msn)
    {
        pins.Clear();
        foreach (var f in msn.Fields)
            pins.Add(MakePin(f.Name, "In", isExec: false, typeId: f.TypeId));
        pins.Add(MakePin("Value", "Out", isExec: false, typeId: SharedTypePinTypeId(msn.StructTypeId)));
    }

    /// <summary>
    /// BreakStructNode (Q#14 Option B): pure data node. A struct-typed data-IN "Value" + one data-OUT pin
    /// per baked field. Mirrors the editor's NodePinSchema.BreakStructPins.
    /// </summary>
    private static void EnrichBreakStructPins(List<Pin> pins, BreakStructNode bsn)
    {
        pins.Clear();
        pins.Add(MakePin("Value", "In", isExec: false, typeId: SharedTypePinTypeId(bsn.StructTypeId)));
        foreach (var f in bsn.Fields)
            pins.Add(MakePin(f.Name, "Out", isExec: false, typeId: f.TypeId));
    }

    /// <summary>
    /// SetMembersNode (Q#14 Option B): pure data node. Struct-typed data-IN "Source" + one member data-IN
    /// per baked field + struct-typed data-OUT "Result". Mirrors the editor's NodePinSchema.SetMembersPins.
    /// </summary>
    private static void EnrichSetMembersPins(List<Pin> pins, SetMembersNode smn)
    {
        var structType = SharedTypePinTypeId(smn.StructTypeId);
        pins.Clear();
        pins.Add(MakePin("Source", "In", isExec: false, typeId: structType));
        foreach (var f in smn.Fields)
            pins.Add(MakePin(f.Name, "In", isExec: false, typeId: f.TypeId));
        pins.Add(MakePin("Result", "Out", isExec: false, typeId: structType));
    }

    /// <summary>
    /// Resolves the pin <c>TypeId</c> for a GetShared/SetShared "Value" pin directly from the
    /// node's <c>SharedTypeId</c> (a foreign Category-1 struct FQN, not a declared asset variable).
    /// Stamped with the <c>"global::"</c> AN2 sentinel (mirrors <c>EnumStampedTypeFqn</c> in the
    /// editor's <c>NodePinSchema</c>) so <see cref="Catalogs.StaticTypeRegistry"/> accepts it as a
    /// project/unmanaged type without requiring reflection over an assembly that, in the analyzer
    /// host, is the very assembly currently being compiled.
    /// </summary>
    private static string SharedTypePinTypeId(string sharedTypeId)
    {
        if (string.IsNullOrEmpty(sharedTypeId)) return "System.Object";
        return sharedTypeId.StartsWith("global::", StringComparison.Ordinal)
            ? sharedTypeId
            : "global::" + sharedTypeId;
    }

    /// <summary>
    /// PublishEvent (P4/GAP-3): exec node whose data-IN pins are catalog-driven (baked, reflection-free).
    /// The static registry provides exec In/Out; this adds the optional <c>Target</c> pin (when the event
    /// has a target field) plus one pin per baked <see cref="EngineEventCatalogEntry.PayloadFields"/>
    /// entry — exactly the pins <c>Stage5_Schedule</c> reads by name when lowering the publish. Lets a
    /// pin-less PublishEvent node round-trip without hand-authored pins. Unknown event id → exec-only
    /// (Stage5 emits nothing; surfaced by validation), same graceful shape as the FunctionCall fallback.
    /// </summary>
    private static void EnrichPublishEventPins(List<Pin> pins, PublishEventNode pen, CompileOptions options)
    {
        // Q#14: baked custom-event path (EventTypeFqn set by the editor from discovery) takes precedence;
        // otherwise resolve the shape from the EngineEventCatalog by EventId (legacy/system events).
        string? targetFieldName;
        IEnumerable<(string Name, string TypeId)> payload;

        if (!string.IsNullOrEmpty(pen.EventTypeFqn))
        {
            targetFieldName = pen.TargetFieldName;
            payload = (pen.PayloadFields ?? new List<PublishEventFieldDecl>())
                .Select(f => (f.Name, f.TypeId));
        }
        else
        {
            var entry = options.EngineEvents.GetEntries()
                .FirstOrDefault(e => string.Equals(e.Name, pen.EventId, StringComparison.OrdinalIgnoreCase));
            if (entry is null) return;
            targetFieldName = entry.TargetFieldName;
            payload = entry.PayloadFields is null
                ? Enumerable.Empty<(string, string)>()
                : entry.PayloadFields.Select(f => (f.Name, f.TypeId));
        }

        // Optional target: pin is always named "Target" (Stage5 matches that name), mapped to the event's
        // TargetFieldName at lowering. Present only when the event declares a target field.
        if (!string.IsNullOrEmpty(targetFieldName))
            pins.Add(MakePin("Target", "In", isExec: false, typeId: "Fdp.Core.Entity"));

        foreach (var (name, typeId) in payload)
            pins.Add(MakePin(name, "In", isExec: false, typeId: typeId));
    }

    /// <summary>
    /// ChannelCommand (Blocker-1, GAP-4): exec node whose data-IN pins are catalog-driven (baked,
    /// reflection-free) — mirrors <see cref="EnrichPublishEventPins"/>. The static registry provides
    /// exec In/Out; this adds one data-IN pin per baked
    /// <see cref="ChannelCommandCatalogEntry.ParamFields"/> entry, matched exactly the way
    /// <c>Stage2_Validate.V_ChannelCommandReferences</c> / <c>NodePinSchema.ChannelCommandPinsFromCatalog</c>
    /// match it: <c>LastSegment(ChannelTypeFqn) == node.ChannelType &amp;&amp; entry.Name == node.ActionId</c>.
    /// These are exactly the pins <c>Stage5_Schedule</c> reads by name (via <c>node.Pins</c>, not-exec
    /// In pins) when lowering the command. Lets a pin-less ChannelCommand node round-trip without
    /// hand-authored pins. Unknown action / unbaked entry → exec-only (mirrors the graceful PublishEvent
    /// and FunctionCall fallback shapes).
    /// <para>
    /// AN7 non-channel path (<see cref="ChannelCommandNode.ActionFqn"/> set) is NOT enriched here — that
    /// path resolves params via <c>IBehaviorActionCatalog</c> reflection (net8 editor host only) and has
    /// no Stage0 equivalent yet; such nodes fall through to the exec-only fallback below, same as before
    /// this enricher existed (no regression, no new gap).
    /// </para>
    /// </summary>
    private static void EnrichChannelCommandPins(List<Pin> pins, ChannelCommandNode cc, CompileOptions options)
    {
        if (!string.IsNullOrEmpty(cc.ActionFqn))
            return; // AN7 non-channel path — no baked catalog to enrich from (see doc above).

        var entry = options.ChannelCommands.GetEntries().FirstOrDefault(e =>
            Stage2Helpers.LastSegment(e.ChannelTypeFqn) == cc.ChannelType
            && e.Name == cc.ActionId);
        if (entry?.ParamFields == null) return;

        foreach (var f in entry.ParamFields)
            pins.Add(MakePin(f.Name, "In", isExec: false, typeId: f.TypeId));
    }

    private static void EnrichFunctionCallPins(
        List<Pin> pins, FunctionCallNode fc,
        BlueprintAsset asset, Graph containingGraph,
        CompileOptions options,
        IReadOnlyList<PinSchema> staticShapes,
        List<Link>? outL,
        List<Link>? inL)
    {
        // Graph-call path: non-empty TargetGraphId + resolvable Function graph in asset.
        if (!string.IsNullOrEmpty(fc.TargetGraphId) && asset != null)
        {
            if (Guid.TryParse(fc.TargetGraphId, out var targetGuid))
            {
                var target = asset.Graphs.FirstOrDefault(
                    g => g.Id == targetGuid && g.Kind == GraphKind.Function);
                if (target != null)
                {
                    EnrichFunctionGraphCallPins(pins, fc, target);
                    return;
                }
            }
        }

        // CLR-reflection path. `asset` is non-nullable at this method's own signature (the
        // preceding `asset != null` check above is defensive/pre-existing and only guards the
        // graph-call branch); the null-forgiving operator documents that for the nullable-flow
        // analysis, which otherwise cannot see past the compound condition above.
        EnrichClrFunctionCallPins(pins, fc, asset!, options, outL, inL);
    }

    private static void EnrichFunctionGraphCallPins(
        List<Pin> pins, FunctionCallNode fc, Graph target)
    {
        pins.Clear();
        if (!fc.IsPure)
        {
            pins.Add(MakePin("In",  "In",  isExec: true, typeId: ""));
            pins.Add(MakePin("Out", "Out", isExec: true, typeId: ""));
        }
        foreach (var inp in target.Inputs)
        {
            var typeId = GetTypeId(inp.Type);
            pins.Add(MakePin(inp.Name, "In", isExec: false, typeId: typeId));
        }
        if (target.Outputs.Count > 0)
        {
            var output = target.Outputs[0];
            var typeId = GetTypeId(output.Type);
            pins.Add(MakePin(output.Name, "Out", isExec: false, typeId: typeId));
        }
    }

    private static void EnrichClrFunctionCallPins(List<Pin> pins, FunctionCallNode fc,
        BlueprintAsset asset, CompileOptions options, List<Link>? outL, List<Link>? inL)
    {
        // Resolve the target signature. Two sources, in precedence order:
        //   1. options.ClrSignatureResolver -- the Roslyn semantic model (supplied by the incremental
        //      generator, which CANNOT reflect over the assembly it is currently compiling). This is
        //      what lets same-assembly curated-helper calls rehydrate typed pins at generate time, so
        //      the blueprints need NO explicit persisted pins and the editor save round-trip is safe.
        //   2. CLR reflection over loaded assemblies -- the in-process path (compiler unit tests, the
        //      editor host), where the game assembly IS loaded. Byte-for-byte the pre-existing behavior.
        // Either yields a reflection-free ClrMethodSig; a single builder below turns it into pins.
        var sig = TryResolveSignature(fc, options);
        if (sig == null)
        {
            // NO-SWALLOW: emit to debug output naming node + reason.
            if (!string.IsNullOrEmpty(fc.TargetTypeId) || !string.IsNullOrEmpty(fc.MethodName))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[BP-Stage0] WARN: FunctionCallNode {fc.Id} cannot resolve " +
                    $"'{fc.TargetTypeId}.{fc.MethodName}' via semantic model or CLR reflection — " +
                    $"using link-adjacency placeholder pins (MSBuild host or missing assembly).");
            }

            if (fc.IsPure)
            {
                // For pure CLR calls whose assembly is not loaded, build typed-unknown (System.Object)
                // placeholder pins from link adjacency so AssignLinkGuids can wire them correctly and
                // Stage5 can resolve the connected data flow (NO-SWALLOW per task spec).
                // Distinct ToPinId GUIDs in link order = data-In slots.
                // Distinct FromPinId GUIDs in link order = data-Out slots.
                var seenIn = new HashSet<Guid>();
                if (inL != null)
                    foreach (var link in inL)
                        if (seenIn.Add(link.ToPinId))
                            pins.Add(MakePin($"arg{seenIn.Count}", "In", isExec: false, typeId: "System.Object"));

                // Also infer data-In pins from PinDefaults keys, so that parameters using
                // inline defaults (no physical wire) are visible to Stage3_Normalize.
                // Without this, the Roslyn source generator (netstandard2.0 sandbox where
                // CLR reflection fails) would miss these entirely, causing CS7036 at emit.
                if (fc.PinDefaults != null)
                {
                    foreach (var kvp in fc.PinDefaults)
                    {
                        var synPinId = Stage3_Normalize.SynthesizedGuid(
                            $"fallback-pin:{fc.Id:N}:{kvp.Key}");
                        if (seenIn.Add(synPinId))
                            pins.Add(MakePin(kvp.Key, "In", isExec: false,
                                typeId: "System.Object"));
                    }
                }

                var seenOut = new HashSet<Guid>();
                if (outL != null)
                    foreach (var link in outL)
                        if (seenOut.Add(link.FromPinId))
                            pins.Add(MakePin("Return", "Out", isExec: false, typeId: "System.Object"));
            }
            // For non-pure CLR calls: pins already contain exec In/Out from static skeleton — leave as-is.
            return;
        }

        // Have a resolved signature — rebuild with typed data pins.
        pins.Clear();
        if (!fc.IsPure)
        {
            pins.Add(MakePin("In",  "In",  isExec: true, typeId: ""));
            pins.Add(MakePin("Out", "Out", isExec: true, typeId: ""));
        }
        try
        {
            var allParams = sig.Parameters;

            // Trailing engine-context recognition (Entity self / ISimulationView view). The trailing
            // context params are OMITTED from the data pins (Stage5 appends them as call arguments; the
            // omitted-pin count and the appended-arg count must always agree — see the parity note on
            // Stage5_Schedule.ResolveFunctionCallTrailingContext). Precedence mirrors Stage5:
            //   * Library dispatch has neither self nor view in scope → 0 context params.
            //   * an explicit fc.TrailingContext (the editor bake / hand-authored value) wins with NO
            //     type inspection — this is what makes the pin shape reproducible without reflection.
            //   * Unspecified → fall back to type-based recognition over the resolved parameter list
            //     (identical detection to the former reflection path; all pre-existing P7 tests hit this).
            int contextCount = ResolveContextParamCount(allParams, fc.TrailingContext, asset);
            var dataParamCount = allParams.Count - contextCount;

            for (int i = 0; i < dataParamCount; i++)
            {
                var param = allParams[i];
                pins.Add(MakePin(string.IsNullOrEmpty(param.Name) ? "arg" : param.Name, "In",
                    isExec: false, typeId: PinTypeIdForResolvedType(param.TypeFullName, options)));
            }
            if (sig.ReturnTypeFullName != null)
                pins.Add(MakePin("Return", "Out", isExec: false,
                    typeId: PinTypeIdForResolvedType(sig.ReturnTypeFullName, options)));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[BP-Stage0] WARN: FunctionCallNode {fc.Id} signature build on " +
                $"'{fc.TargetTypeId}.{fc.MethodName}' threw: {ex.Message} — " +
                $"keeping exec-only fallback.");
            // Leave whatever exec pins exist.
        }
    }

    // ── FunctionCall signature resolution (semantic model → reflection) ─────────────────────────

    /// <summary>
    /// Resolves a FunctionCall target's signature to a reflection-free <see cref="ClrMethodSig"/>,
    /// trying the generator-supplied semantic-model resolver first (which sees the assembly currently
    /// being compiled) and CLR reflection second (in-process hosts where the assembly is loaded).
    /// Returns <c>null</c> when neither can resolve it — the caller then uses placeholder pins.
    /// </summary>
    private static ClrMethodSig? TryResolveSignature(FunctionCallNode fc, CompileOptions options)
    {
        if (options.ClrSignatureResolver != null)
        {
            try
            {
                if (options.ClrSignatureResolver.TryResolve(fc.TargetTypeId, fc.MethodName, out var resolved)
                    && resolved != null)
                    return resolved;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[BP-Stage0] WARN: ClrSignatureResolver threw for " +
                    $"'{fc.TargetTypeId}.{fc.MethodName}': {ex.Message} — falling back to reflection.");
            }
        }

        var method = ResolveMethod(fc.TargetTypeId, fc.MethodName);
        return method == null ? null : ReflectToSig(method);
    }

    /// <summary>Converts a resolved CLR <see cref="MethodInfo"/> to a <see cref="ClrMethodSig"/>,
    /// unwrapping by-ref parameters and mapping <c>void</c> returns to <c>null</c> — so the reflection
    /// path and the semantic-model path feed the SAME pin builder.</summary>
    private static ClrMethodSig ReflectToSig(MethodInfo method)
    {
        var ps = method.GetParameters();
        var list = new List<ClrParamInfo>(ps.Length);
        foreach (var p in ps)
        {
            var pt = p.ParameterType;
            if (pt.IsByRef) pt = pt.GetElementType() ?? pt;
            list.Add(new ClrParamInfo(p.Name ?? "arg", pt.FullName ?? pt.Name));
        }
        string? ret = method.ReturnType == typeof(void)
            ? null
            : (method.ReturnType.FullName ?? method.ReturnType.Name);
        return new ClrMethodSig(list, ret);
    }

    /// <summary>
    /// Maps a resolved parameter/return type FQN to the pin <c>TypeId</c>. Types the registry already
    /// knows (C# primitives, <c>Fdp.Core.Entity</c> and its <c>IsEntityHandle</c> flag, the curated
    /// structs declared in <see cref="Catalogs.StaticTypeRegistry"/>) pass through UNPREFIXED so they
    /// resolve via the type table. A curated blittable struct the table does NOT know (e.g. a demo's
    /// own shared struct) is stamped with the <c>"global::"</c> AN2 sentinel — exactly like
    /// <see cref="SharedTypePinTypeId"/> does for GetShared/SetShared — so it flows through the
    /// registry's project-type acceptance path instead of failing BP1500.
    /// <para>
    /// This is what makes the semantic-model FunctionCall rehydration (Blocker-1) general: the
    /// reflection path historically NEVER resolved same-assembly curated structs (it fell back to
    /// <c>System.Object</c> placeholder pins), so this case never arose; now that the resolver DOES
    /// surface them, an unregistered curated struct must still resolve without hand-registering each
    /// one. Trust-the-FQN + emit-a-cast is validated downstream by the C# compiler, same contract as
    /// every other <c>global::</c> type.
    /// </para>
    /// </summary>
    private static string PinTypeIdForResolvedType(string typeFullName, CompileOptions options)
    {
        if (string.IsNullOrEmpty(typeFullName))
            return "System.Object";

        // Already a project sentinel, or the registry resolves it directly → leave as-is.
        if (typeFullName.StartsWith("global::", StringComparison.Ordinal))
            return typeFullName;
        if (options.TypeRegistry.TryResolve(new BlueprintTypeRef { TypeId = typeFullName }, out _))
            return typeFullName;

        // Unknown to the registry — treat as a curated project value type and stamp the sentinel so
        // the registry's global:: acceptance path resolves it (an actually-invalid type surfaces as a
        // downstream Roslyn compile error, same as any other global:: type).
        return "global::" + typeFullName;
    }

    /// <summary>
    /// Number of trailing parameters consumed as engine context (and therefore OMITTED from the data
    /// pins). Precedence mirrors <c>Stage5_Schedule.ResolveFunctionCallTrailingContext</c>: Library
    /// dispatch → 0; an explicit <see cref="FunctionCallContextKind"/> wins with no type inspection;
    /// <see cref="FunctionCallContextKind.Unspecified"/> → type-based recognition over the resolved
    /// parameter list.
    /// </summary>
    private static int ResolveContextParamCount(
        IReadOnlyList<ClrParamInfo> parameters, FunctionCallContextKind trailingContext,
        BlueprintAsset asset)
    {
        if (asset.Dispatch == BlueprintDispatchKind.Library)
            return 0;

        switch (trailingContext)
        {
            case FunctionCallContextKind.None:        return 0;
            case FunctionCallContextKind.Self:        return 1;
            case FunctionCallContextKind.View:        return 1;
            case FunctionCallContextKind.SelfAndView: return 2;
            default: // Unspecified → infer from parameter types (pre-P7-bake behavior).
                var (count, _, _) = ResolveTrailingContext(parameters);
                return count;
        }
    }

    /// <summary>
    /// Recognizes the trailing engine-context parameter convention on a resolved parameter list. The
    /// list MAY end with <c>Entity self</c>, or an <c>ISimulationView</c>-typed parameter (any name),
    /// or both in that exact order (<c>..., Entity self, ISimulationView &lt;name&gt;</c> -- mirrors the
    /// parameter order the compiler uses for generated methods, e.g. <c>TickCore(..., self, world, time)</c>).
    /// <para>
    /// Recognition is by TYPE (exact FQN match against <c>Fdp.Core.Entity</c> /
    /// <c>Fdp.ModuleHost.Abstractions.ISimulationView</c>). The <c>Entity</c> case ALSO requires the
    /// parameter be named exactly <c>"self"</c> (ordinal) -- <c>Entity</c> is a legitimate ordinary
    /// data-pin type elsewhere (e.g. <see cref="GetSharedNode"/>'s "Target" pin), so the name
    /// disambiguates a genuine trailing self-context parameter from an author-supplied data argument.
    /// <c>ISimulationView</c> has no legitimate ordinary blueprint-data use, so type alone suffices.
    /// </para>
    /// Kept in parity with <c>NodePinSchema.ResolveTrailingContext</c> (editor projection) and
    /// <c>Stage5_Schedule.ResolveTrailingContext</c> (IR-lowering arg-append) — all three must
    /// agree on which trailing parameters are "context" so the omitted pin count and the appended
    /// call-argument count always match.
    /// </summary>
    private static (int ContextCount, bool AppendSelf, bool AppendView) ResolveTrailingContext(
        IReadOnlyList<ClrParamInfo> parameters)
    {
        const string EntityFqn = "Fdp.Core.Entity";
        const string ViewFqn   = "Fdp.ModuleHost.Abstractions.ISimulationView";

        int n = parameters.Count;
        if (n == 0) return (0, false, false);

        bool IsSelfParam(ClrParamInfo p) =>
            p.TypeFullName == EntityFqn && string.Equals(p.Name, "self", StringComparison.Ordinal);
        bool IsViewParam(ClrParamInfo p) =>
            p.TypeFullName == ViewFqn;

        if (IsViewParam(parameters[n - 1]))
        {
            if (n >= 2 && IsSelfParam(parameters[n - 2]))
                return (2, true, true);
            return (1, false, true);
        }

        if (IsSelfParam(parameters[n - 1]))
            return (1, true, false);

        return (0, false, false);
    }

    private static void EnrichCallCustomEventPins(
        List<Pin> pins, CallCustomEventNode cce, BlueprintAsset asset,
        IReadOnlyList<PinSchema> staticShapes)
    {
        // Static: exec In/Out from registry.
        // Enrich: add data-In per custom-event parameter.
        if (asset == null) return;
        if (!Guid.TryParse(cce.EventId, out var eventGuid)) return;
        var decl = asset.CustomEvents.FirstOrDefault(e => e.Id == eventGuid);
        if (decl == null || decl.Parameters.Count == 0) return;

        foreach (var param in decl.Parameters)
        {
            var typeId = GetTypeId(param.Type);
            pins.Add(MakePin(param.Name, "In", isExec: false, typeId: typeId));
        }
    }

    private static void EnrichCallPeerBlueprintPins(
        List<Pin> pins, CallPeerBlueprintNode cpb,
        Func<Guid, BlueprintSignature?>? siblingLookup,
        IReadOnlyList<PinSchema> staticShapes)
    {
        // Static: exec In/Out from registry.
        // Enrich: typed data-In per function param + data-Out "Return".
        if (siblingLookup == null) goto addReturnFallback;
        if (!Guid.TryParse(cpb.PeerBlueprintId, out var peerGuid)) goto addReturnFallback;

        BlueprintSignature? peerSig;
        try { peerSig = siblingLookup(peerGuid); }
        catch { goto addReturnFallback; }

        if (peerSig == null) goto addReturnFallback;

        var funcSig = peerSig.ExportedFunctions
            .FirstOrDefault(f => string.Equals(f.Name, cpb.FunctionRef, StringComparison.Ordinal));
        if (funcSig == null) goto addReturnFallback;

        foreach (var inp in funcSig.Inputs)
        {
            var typeId = string.IsNullOrEmpty(inp.TypeId) ? "System.Object" : inp.TypeId;
            pins.Add(MakePin(inp.Name, "In", isExec: false, typeId: typeId));
        }
        var returnTypeId = funcSig.Outputs.Count > 0 && !string.IsNullOrEmpty(funcSig.Outputs[0].TypeId)
            ? funcSig.Outputs[0].TypeId : "System.Object";
        pins.Add(MakePin("Return", "Out", isExec: false, typeId: returnTypeId));
        return;

        addReturnFallback:
        // Static fallback: exec In/Out already added; just add Return:System.Object.
        pins.Add(MakePin("Return", "Out", isExec: false, typeId: "System.Object"));
    }

    // ── Link-GUID positional assignment ──────────────────────────────────────

    /// <summary>
    /// Mirrors the BlueprintGraphModel.Rebuild slow-path (~:181-228).
    /// Splits <paramref name="pins"/> into Out and In buckets (in declaration order),
    /// then assigns the i-th distinct link GUID to the i-th pin in that bucket.
    /// Pins with no link GUID get a deterministic synthetic GUID.
    /// </summary>
    /// <summary>
    /// Assigns pin GUIDs with a per-pin blend of the deterministic content-derived scheme (architect
    /// Q#10-A/C) and the legacy positional scheme, so migrated, legacy, AND mixed assets all reconstruct
    /// with every link resolving. Kept in strict parity with <c>BlueprintGraphModel.Rebuild</c>.
    /// <para>
    /// Per direction bucket: a link whose pin-GUID equals a pin's deterministic GUID
    /// (<see cref="DeterministicIds.PinId"/>) binds THAT pin by name (order-independent — the exec/data
    /// swap the old positional scheme suffered is impossible). Remaining links (legacy/arbitrary GUIDs)
    /// bind positionally to the still-unassigned pins, exactly as before; leftover unconnected pins get
    /// the legacy synthetic GUID. A wholly-migrated node resolves entirely by name; a wholly-legacy node
    /// is byte-for-byte the old positional binding; a mixed node (a link drawn to a pin that had been
    /// saved unconnected, so it already carries its deterministic GUID) resolves both parts.
    /// </para>
    /// </summary>
    private static void AssignLinkGuids(
        List<Pin> pins, Guid nodeId,
        List<Link>? outLinks, List<Link>? inLinks)
    {
        AssignDirection(pins.Where(p => p.Direction == "Out").ToList(), nodeId, "Out",
            DistinctLinkGuids(outLinks, static l => l.FromPinId));
        AssignDirection(pins.Where(p => p.Direction == "In").ToList(), nodeId, "In",
            DistinctLinkGuids(inLinks, static l => l.ToPinId));
    }

    private static List<Guid> DistinctLinkGuids(List<Link>? links, Func<Link, Guid> select)
    {
        var result = new List<Guid>();
        if (links == null) return result;
        var seen = new HashSet<Guid>();
        foreach (var link in links)
        {
            var g = select(link);
            if (seen.Add(g)) result.Add(g);
        }
        return result;
    }

    private static void AssignDirection(List<Pin> dirPins, Guid nodeId, string direction, List<Guid> linkGuids)
    {
        // Reverse map: deterministic pin-GUID → pin (unique pin name within a direction is assumed).
        var detToPin = new Dictionary<Guid, Pin>();
        foreach (var pin in dirPins)
            detToPin[DeterministicIds.PinId(nodeId, pin.Name, direction)] = pin;

        // Pass 1: links carrying a deterministic GUID bind their exact pin by name.
        var assigned = new HashSet<Pin>();
        var legacyGuids = new List<Guid>();
        foreach (var g in linkGuids)
        {
            if (detToPin.TryGetValue(g, out var pin))
            {
                if (assigned.Add(pin)) pin.Id = g;
            }
            else
            {
                legacyGuids.Add(g);
            }
        }

        // Pass 2: legacy links bind positionally to the still-unassigned pins (old behavior);
        // any pin left over (unconnected) gets the legacy synthetic GUID.
        int li = 0;
        foreach (var pin in dirPins)
        {
            if (assigned.Contains(pin)) continue;
            pin.Id = (li < legacyGuids.Count)
                ? legacyGuids[li++]
                : Stage3_Normalize.SynthesizedGuid($"pin:{nodeId:N}:{pin.Name}:{direction}");
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Safe helper: returns the TypeId from a BlueprintTypeRef, or "System.Object" when null/empty.
    /// Avoids repeated null-conditional patterns that confuse the nullable flow analyzer.
    /// </summary>
    private static string GetTypeId(BlueprintTypeRef? typeRef)
    {
        if (typeRef == null) return "System.Object";
        return string.IsNullOrEmpty(typeRef.TypeId) ? "System.Object" : typeRef.TypeId;
    }

    /// <summary>
    /// CA-07a: <paramref name="isArray"/> stamps the pin's <see cref="BlueprintTypeRef.IsArray"/> --
    /// used for a baked collection field's single "whole collection" out-pin (element-typed,
    /// IsArray true). Defaults to <c>false</c> so every pre-CA-07a call site is unaffected.
    /// </summary>
    private static Pin MakePin(string name, string direction, bool isExec, string typeId, bool isArray = false) => new Pin
    {
        Name      = name,
        Direction = direction,
        IsExec    = isExec,
        TypeRef   = new BlueprintTypeRef { TypeId = typeId, IsArray = isArray },
        // Id will be assigned by AssignLinkGuids.
    };

    private static string ResolveVariableTypeId(string variableId, BlueprintAsset asset)
    {
        if (asset == null || string.IsNullOrEmpty(variableId))
            return "System.Object";

        // Variable id may be in the form "var:<Guid>" — strip the prefix.
        var idStr = variableId.StartsWith("var:", StringComparison.OrdinalIgnoreCase)
            ? variableId.Substring(4)
            : variableId;

        if (Guid.TryParse(idStr, out var guid))
        {
            // Check instance variables.
            var varDecl = asset.Variables.FirstOrDefault(v => v.Id == guid);
            if (varDecl != null && varDecl.Type != null && !string.IsNullOrEmpty(varDecl.Type.TypeId))
                return varDecl.Type.TypeId;

            // Check working-state variables (AiPrimitive).
            var wsDecl = asset.WorkingState.FirstOrDefault(v => v.Id == guid);
            if (wsDecl != null && wsDecl.Type != null && !string.IsNullOrEmpty(wsDecl.Type.TypeId))
                return wsDecl.Type.TypeId;
        }

        return "System.Object";
    }

    /// <summary>
    /// Returns true for node kinds that must have exec pins even on the fallback path.
    /// Pure FunctionCallNode and pure-data nodes (GetVariable, Literal, ReadRankedResult)
    /// are excluded — they should remain empty rather than getting spurious exec pins.
    /// </summary>
    private static bool NodeRequiresExecFallback(Node node) => node switch
    {
        FunctionCallNode fc   => !fc.IsPure,
        GetVariableNode       => false,
        GetParameterNode      => false,
        GetAllParametersNode  => false,
        GetSharedNode         => false,
        GetComponentNode      => false,
        CompareNode           => false,
        BinaryOpNode          => false,
        BooleanOpNode         => false,
        NotNode               => false,
        LiteralNode           => false,
        ReadRankedResultNode  => false,
        ReadEqsResultNode     => false,
        // Q#14 Option B — Make/Break/SetMembers are PURE data nodes (no exec pins).
        MakeStructNode        => false,
        BreakStructNode       => false,
        SetMembersNode        => false,
        // PublishEvent (P4 -- GAP-3) IS an exec node -- unlike the pure GetX nodes above.
        PublishEventNode      => true,
        // FlowForEach (P1 -- GAP-1) IS an exec node (In + Body/Completed exec-outs).
        FlowForEachNode       => true,
        _                     => true,
    };

    // ── CLR reflection (netstandard2.0-compatible) ────────────────────────────

    private static System.Reflection.MethodInfo? ResolveMethod(string targetTypeId, string methodName)
    {
        if (string.IsNullOrEmpty(targetTypeId) || string.IsNullOrEmpty(methodName))
            return null;

        var type = ResolveType(targetTypeId);
        if (type == null) return null;

        try
        {
            return type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static  | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == methodName);
        }
        catch { return null; }
    }

    private static Type? ResolveType(string fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return null;
        var direct = Type.GetType(fqn, throwOnError: false);
        if (direct != null) return direct;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                var t = asm.GetType(fqn, throwOnError: false);
                if (t != null) return t;
            }
            catch { /* ignore */ }
        }
        return null;
    }
}
