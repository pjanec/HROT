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

            case FunctionCallNode fc:
                EnrichFunctionCallPins(pins, fc, asset, graph, options, staticShapes, outL, inL);
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
        // Enrich: add one data-Out per Graph.Inputs entry for Function graphs.
        if (graph.Kind != GraphKind.Function || graph.Inputs.Count == 0)
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
        var typeId = SharedTypePinTypeId(gsn.SharedTypeId);
        pins.Clear();
        pins.Add(MakePin("Target", "In",  isExec: false, typeId: "Fdp.Core.Entity"));
        pins.Add(MakePin("Value",  "Out", isExec: false, typeId: typeId));
        pins.Add(MakePin("Found",  "Out", isExec: false, typeId: "System.Boolean"));
    }

    /// <summary>
    /// SetSharedNode (Slice 2a-2): exec node. Static skeleton is exec In/Out (registry mirrors
    /// SetVariableNode). Enrich: add data-In "Value" typed DIRECTLY from
    /// <see cref="SetSharedNode.SharedTypeId"/> + data-Out "Written" (<c>System.Boolean</c>).
    /// </summary>
    private static void EnrichSetSharedPins(
        List<Pin> pins, SetSharedNode ssn, IReadOnlyList<PinSchema> staticShapes)
    {
        var typeId = SharedTypePinTypeId(ssn.SharedTypeId);
        pins.Clear();
        pins.Add(MakePin("In",      "In",  isExec: true,  typeId: ""));
        pins.Add(MakePin("Out",     "Out", isExec: true,  typeId: ""));
        pins.Add(MakePin("Value",   "In",  isExec: false, typeId: typeId));
        pins.Add(MakePin("Written", "Out", isExec: false, typeId: "System.Boolean"));
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
        EnrichClrFunctionCallPins(pins, fc, asset!, outL, inL);
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
        BlueprintAsset asset, List<Link>? outL, List<Link>? inL)
    {
        // Attempt CLR reflection.  Fails gracefully in the netstandard2.0 MSBuild host
        // where the game assembly is not loaded; tracked for a later registry-driven FunctionCall.
        var method = ResolveMethod(fc.TargetTypeId, fc.MethodName);
        if (method == null)
        {
            // NO-SWALLOW: emit to debug output naming node + reason.
            if (!string.IsNullOrEmpty(fc.TargetTypeId) || !string.IsNullOrEmpty(fc.MethodName))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[BP-Stage0] WARN: FunctionCallNode {fc.Id} cannot resolve " +
                    $"'{fc.TargetTypeId}.{fc.MethodName}' via CLR reflection — " +
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

        // Have reflection — rebuild with typed data pins.
        pins.Clear();
        if (!fc.IsPure)
        {
            pins.Add(MakePin("In",  "In",  isExec: true, typeId: ""));
            pins.Add(MakePin("Out", "Out", isExec: true, typeId: ""));
        }
        try
        {
            var allParams = method.GetParameters();

            // P7 -- trailing engine-context recognition (Entity self / ISimulationView view).
            // Recognized ONLY when the containing asset's dispatch has self/view in scope
            // (Instance/AiPrimitive -- mirrors EmissionContext.HasSelfInScope). A Library-dispatch
            // asset has neither in scope (LibraryEmitter.EmitFunctionGraph emits a stateless static
            // method with only the declared graph inputs as parameters), so trailing Entity/
            // ISimulationView-typed parameters there are left as ordinary data pins -- unchanged,
            // pre-P7 behavior. Authoring such a call in a Library graph will surface as an ordinary
            // "unresolvable data pin" / downstream Roslyn compile error, same as any other invalid
            // reference in a stateless function body (no new diagnostic added for this edge case;
            // see P7 report for the recorded gap).
            int contextCount = 0;
            if (asset.Dispatch != BlueprintDispatchKind.Library)
            {
                (contextCount, _, _) = ResolveTrailingContext(allParams);
            }
            var dataParamCount = allParams.Length - contextCount;

            for (int i = 0; i < dataParamCount; i++)
            {
                var param = allParams[i];
                var pt = param.ParameterType;
                if (pt.IsByRef) pt = pt.GetElementType() ?? pt;
                pins.Add(MakePin(param.Name ?? "arg", "In", isExec: false,
                    typeId: pt.FullName ?? pt.Name));
            }
            if (method.ReturnType != typeof(void))
                pins.Add(MakePin("Return", "Out", isExec: false,
                    typeId: method.ReturnType.FullName ?? method.ReturnType.Name));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[BP-Stage0] WARN: FunctionCallNode {fc.Id} reflection on " +
                $"'{fc.TargetTypeId}.{fc.MethodName}' threw: {ex.Message} — " +
                $"keeping exec-only fallback.");
            // Leave whatever exec pins exist.
        }
    }

    // ── P7: trailing engine-context recognition (FunctionCall context-aware pins) ──────────────

    /// <summary>
    /// P7 -- recognizes the trailing engine-context parameter convention on a FunctionCall's
    /// resolved CLR method. The parameter list MAY end with <c>Entity self</c>, or an
    /// <c>ISimulationView</c>-typed parameter (any name), or both in that exact order
    /// (<c>..., Entity self, ISimulationView &lt;name&gt;</c> -- mirrors the parameter order the
    /// compiler itself uses for generated methods, e.g. <c>TickCore(..., self, world, time)</c>).
    /// <para>
    /// Recognition is by TYPE (exact <c>Type.FullName</c> match against
    /// <c>Fdp.Core.Entity</c> / <c>Fdp.ModuleHost.Abstractions.ISimulationView</c>, after stripping
    /// a by-ref wrapper). The <c>Entity</c> case ALSO requires the parameter be named exactly
    /// <c>"self"</c> (ordinal) -- <c>Entity</c> is a legitimate ordinary data-pin type elsewhere
    /// (e.g. <see cref="GetSharedNode"/>'s "Target" pin), so the name disambiguates a genuine
    /// trailing self-context parameter from an author-supplied <c>Entity</c> data argument.
    /// <c>ISimulationView</c> has no legitimate ordinary blueprint-data use, so type alone suffices.
    /// </para>
    /// Kept in parity with <c>NodePinSchema.ResolveTrailingContext</c> (editor projection) and
    /// <c>Stage5_Schedule.ResolveTrailingContext</c> (IR-lowering arg-append) — all three must
    /// agree on which trailing parameters are "context" so the omitted pin count and the appended
    /// call-argument count always match.
    /// </summary>
    /// <returns>
    /// <c>ContextCount</c> -- 0, 1, or 2 trailing parameters consumed as engine context.
    /// <c>AppendSelf</c>/<c>AppendView</c> -- which kind(s) were recognized (informational here;
    /// Stage0 only needs <c>ContextCount</c> to slice the pin list -- appending the actual call
    /// arguments happens later, in Stage5_Schedule).
    /// </returns>
    private static (int ContextCount, bool AppendSelf, bool AppendView) ResolveTrailingContext(
        ParameterInfo[] parameters)
    {
        const string EntityFqn = "Fdp.Core.Entity";
        const string ViewFqn   = "Fdp.ModuleHost.Abstractions.ISimulationView";

        int n = parameters.Length;
        if (n == 0) return (0, false, false);

        static Type StripByRef(Type t) => t.IsByRef ? (t.GetElementType() ?? t) : t;
        bool IsSelfParam(ParameterInfo p) =>
            StripByRef(p.ParameterType).FullName == EntityFqn
            && string.Equals(p.Name, "self", StringComparison.Ordinal);
        bool IsViewParam(ParameterInfo p) =>
            StripByRef(p.ParameterType).FullName == ViewFqn;

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
    private static void AssignLinkGuids(
        List<Pin> pins, Guid nodeId,
        List<Link>? outLinks, List<Link>? inLinks)
    {
        var outPins = pins.Where(p => p.Direction == "Out").ToList();
        var inPins  = pins.Where(p => p.Direction == "In" ).ToList();

        // Collect distinct FromPinId GUIDs from outgoing links in order of first occurrence.
        var distinctOutGuids = new List<Guid>();
        var seenOut = new HashSet<Guid>();
        if (outLinks != null)
            foreach (var link in outLinks)
                if (seenOut.Add(link.FromPinId))
                    distinctOutGuids.Add(link.FromPinId);

        // Collect distinct ToPinId GUIDs from incoming links.
        var distinctInGuids = new List<Guid>();
        var seenIn = new HashSet<Guid>();
        if (inLinks != null)
            foreach (var link in inLinks)
                if (seenIn.Add(link.ToPinId))
                    distinctInGuids.Add(link.ToPinId);

        // Assign Out pins.
        for (int i = 0; i < outPins.Count; i++)
        {
            outPins[i].Id = (i < distinctOutGuids.Count)
                ? distinctOutGuids[i]
                : Stage3_Normalize.SynthesizedGuid(
                    $"pin:{nodeId:N}:{outPins[i].Name}:Out");
        }

        // Assign In pins.
        for (int i = 0; i < inPins.Count; i++)
        {
            inPins[i].Id = (i < distinctInGuids.Count)
                ? distinctInGuids[i]
                : Stage3_Normalize.SynthesizedGuid(
                    $"pin:{nodeId:N}:{inPins[i].Name}:In");
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

    private static Pin MakePin(string name, string direction, bool isExec, string typeId) => new Pin
    {
        Name      = name,
        Direction = direction,
        IsExec    = isExec,
        TypeRef   = new BlueprintTypeRef { TypeId = typeId },
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
        GetSharedNode         => false,
        GetComponentNode      => false,
        LiteralNode           => false,
        ReadRankedResultNode  => false,
        ReadEqsResultNode     => false,
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
