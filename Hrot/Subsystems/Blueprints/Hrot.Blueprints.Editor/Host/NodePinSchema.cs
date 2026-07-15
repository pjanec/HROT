using System.Reflection;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;
using Hrot.Blueprints.Editor.ActionCatalog;
using Hrot.Blueprints.Editor.NodeDrawers;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// Canonical per-kind pin schema for Blueprint nodes.
/// <para>
/// Loaded <c>.bp.json</c> assets store <c>"Pins": []</c> (the compiler does not
/// require persisted pins).  This class resolves the authoritative pin list for a
/// given <see cref="Node"/> instance so the editor projection can hydrate pins for
/// the canvas without mutating the asset or the serialization format.
/// </para>
/// <para>
/// Resolution order:
/// <list type="number">
///   <item>If <paramref name="node"/>.Pins is non-empty (test-builder assets), return it as-is.</item>
///   <item>Try the <see cref="NodeKindRegistry"/> via <paramref name="registry"/> —
///     call <c>CreateInstance().Pins</c> on the matching descriptor.</item>
///   <item>Fall back to the built-in table below for kinds that are not in the registry
///     (core compiler kinds that appear in the JSON test fixtures).</item>
/// </list>
/// </para>
/// <para>
/// All projected pins (exec + data) are the pins the <b>compiler actually consumes</b>
/// for that kind (verified against <c>Stage2_Validate</c>/<c>Stage4_TypeResolve</c>/
/// <c>Stage5_Schedule</c>); a wired data pin is therefore meaningful.  Pins remain
/// projection-only — nothing is persisted to the asset or to disk.
/// </para>
/// </summary>
internal static class NodePinSchema
{
    /// <summary>
    /// Returns the canonical <see cref="Pin"/> list for <paramref name="node"/>.
    /// The returned pins have freshly generated (non-stable) GUIDs.  The caller's
    /// two-pass GUID-binding step must replace them with the real GUIDs from
    /// incident links before projecting <see cref="BlueprintPinModel"/> instances.
    /// </summary>
    /// <param name="node">The asset node to build pins for.</param>
    /// <param name="registry">Optional node-kind registry for registry-backed pin schemas.</param>
    /// <param name="asset">
    /// Optional owning asset; when non-null, Get/Set variable node Value pins are typed
    /// from the declared variable type rather than defaulting to <c>System.Object</c>.
    /// </param>
    /// <param name="channelCommands">
    /// Optional channel-command catalog.  When non-null it is used to resolve the parameter
    /// data-IN pins of a <see cref="ChannelCommandNode"/> from the matching
    /// <see cref="ChannelCommandCatalogEntry.ParamsTypeFqn"/> (the compiler's source of truth,
    /// Stage2_Validate §V_ChannelCommandReferences / Stage5_Schedule §ChannelCommand).
    /// When null (or the action/params type cannot be resolved) the node falls back to
    /// exec-only, matching the prior behavior.
    /// </param>
    /// <param name="behaviorActions">
    /// AN7 — optional unified behavior-action catalog (<see cref="IBehaviorActionCatalog"/>).
    /// When non-null and the <see cref="ChannelCommandNode"/> has a non-null/non-empty
    /// <see cref="ChannelCommandNode.ActionFqn"/>, the catalog is used to look up the matching
    /// <see cref="BehaviorActionEntry.ParamsTypeFqn"/> and project data-IN pins from it
    /// (same <c>ReflectDataMembers</c> path as channel commands; enum fields stamped
    /// <c>"global::"</c> per AN6).  When null, non-channel nodes fall back to exec-only.
    /// </param>
    /// <param name="containingGraph">
    /// Optional graph that owns <paramref name="node"/>.  Required to project value pins for
    /// <see cref="EventEntryNode"/> (outputs from <c>Graph.Inputs</c>) and
    /// <see cref="ReturnNode"/> (output from <c>Graph.Outputs[0]</c>) when the graph has
    /// <see cref="GraphKind.Function"/> kind.  Also used by <see cref="FunctionCallNode"/> to
    /// resolve a <c>TargetGraphId</c> within the containing asset's graph list.
    /// Pass <c>null</c> (default) for catalog contexts where the containing graph is not known;
    /// behavior degrades gracefully to the original exec-only pins.
    /// </param>
    /// <param name="peerSignatureLookup">
    /// Optional delegate that resolves a peer asset's <see cref="BlueprintSignature"/> by its
    /// asset GUID.  When non-null and a <see cref="CallPeerBlueprintNode"/>'s peer + function
    /// are found, the projection emits typed argument data-IN pins + a typed Return data-OUT pin.
    /// When null (or the peer/function cannot be resolved) the node falls back to the static
    /// exec In/Out + <c>Return:System.Object</c> shape.
    /// </param>
    public static IReadOnlyList<Pin> GetCanonicalPins(
        Node node,
        NodeKindRegistry?               registry             = null,
        BlueprintAsset?                 asset                = null,
        IChannelCommandCatalog?         channelCommands      = null,
        Graph?                          containingGraph      = null,
        Func<Guid, BlueprintSignature?>? peerSignatureLookup = null,
        IBehaviorActionCatalog?         behaviorActions      = null)
    {
        // Pass 0: asset already has pins (builder-created test assets).
        if (node.Pins.Count > 0)
            return node.Pins;

        // Pass 1: registry descriptor.
        if (registry != null)
        {
            var kindName = node.GetType().Name; // e.g. "WhenNode", "ReadEqsResultNode"
            var descriptor = registry.TryGet(kindName);
            if (descriptor == null)
            {
                // Also try without the "Node" suffix (registry keys like "When", "ReadEqsResult").
                var shortName = kindName.EndsWith("Node")
                    ? kindName[..^4]
                    : kindName;
                descriptor = registry.TryGet(shortName);
            }
            if (descriptor != null)
            {
                try
                {
                    var instance = descriptor.CreateInstance();
                    if (instance.Pins.Count > 0)
                        return instance.Pins;
                }
                catch { /* fallthrough to built-in table */ }
            }
        }

        // Pass 2: built-in fallback table for core compiler node kinds.
        // Static kinds delegate to BuiltInNodeRegistry (single source of truth).
        // Dynamic kinds keep their editor-side computation (need asset/graph/catalog/peer context).
        return node switch
        {
            // ── Dynamic kinds: editor-side computation required ───────────────────
            EventEntryNode      => EventEntryNodePins(containingGraph),
            ReturnNode          => ReturnNodePins(containingGraph),
            FunctionCallNode fc => FunctionCallPinsDispatch(fc, asset, containingGraph),
            GetVariableNode gv  => GetVariablePins(gv, ResolveVariableTypeId(gv.VariableId, asset)),
            SetVariableNode sv  => SetVariablePins(sv, ResolveVariableTypeId(sv.VariableId, asset)),
            GetSharedNode gsn   => GetSharedPins(gsn),
            SetSharedNode ssn   => SetSharedPins(ssn),
            ChannelCommandNode cc => ChannelCommandPins(cc, channelCommands, behaviorActions),
            CallCustomEventNode cce => CallCustomEventPins(cce, asset),
            CallPeerBlueprintNode cpb => CallPeerBlueprintPins(cpb, peerSignatureLookup),

            // ── Static kinds: delegate to BuiltInNodeRegistry ────────────────────
            // BranchNode, SequenceNode, LiteralNode, CastNode, LatentDelayNode,
            // ArrayMakeNode, ArrayGetNode, ScoreDecisionNode, ReadRankedResultNode,
            // WhenNode, ReadEqsResultNode, and exec-only kinds.
            _ => FromRegistry(node),
        };
    }

    // ── variable type resolution ─────────────────────────────────────────────

    /// <summary>
    /// Look up the <c>TypeId</c> for a variable by its string id (e.g. <c>"var:abc123"</c>
    /// or plain GUID string) from the asset's variable list.
    /// Returns <c>"System.Object"</c> when the variable is not found or the asset is null.
    /// </summary>
    private static string ResolveVariableTypeId(string variableId, BlueprintAsset? asset)
    {
        if (asset == null || string.IsNullOrEmpty(variableId))
            return "System.Object";

        // CanvasRenderer.PlaceVariableNode passes the raw My-Blueprint item-id which may be
        // in the form "var:<Guid>" (as built by BlueprintMyBlueprintModel.BuildVariableItems).
        // Strip the "var:" prefix before parsing.
        var idStr = variableId.StartsWith("var:", StringComparison.OrdinalIgnoreCase)
            ? variableId[4..]
            : variableId;

        if (Guid.TryParse(idStr, out var guid))
        {
            var decl = asset.Variables.FirstOrDefault(v => v.Id == guid);
            if (decl != null && !string.IsNullOrEmpty(decl.Type?.TypeId))
                return decl.Type.TypeId;
        }

        return "System.Object";
    }

    // ── registry delegation (static kinds) ───────────────────────────────────

    /// <summary>
    /// Converts a compiler <see cref="PinSchema"/> (GUID-less) to an editor <see cref="Pin"/>
    /// with a freshly generated Id and the same Name / Direction / IsExec / TypeId.
    /// The caller's two-pass GUID-binding step replaces these temporary GUIDs with the
    /// real GUIDs from incident links before projecting <see cref="BlueprintPinModel"/> instances.
    /// </summary>
    private static Pin FromPinSchema(PinSchema schema) => new()
    {
        Id        = Guid.NewGuid(),
        Name      = schema.Name,
        Direction = schema.Direction,
        IsExec    = schema.IsExec,
        TypeRef   = new BlueprintTypeRef { TypeId = schema.TypeId },
    };

    /// <summary>
    /// Delegates to <see cref="BuiltInNodeRegistry.Instance"/> to obtain the canonical static
    /// pin shapes for <paramref name="node"/> and converts them to editor <see cref="Pin"/>s
    /// in registry order (order is load-bearing for <see cref="BlueprintGraphModel"/>'s
    /// link-GUID positional assignment).
    /// Returns an empty array when the registry returns no shapes for the node kind.
    /// </summary>
    private static IReadOnlyList<Pin> FromRegistry(Node node)
    {
        var schemas = BuiltInNodeRegistry.Instance.GetStaticPins(node);
        if (schemas.Count == 0)
            return Array.Empty<Pin>();
        var pins = new Pin[schemas.Count];
        for (var i = 0; i < schemas.Count; i++)
            pins[i] = FromPinSchema(schemas[i]);
        return pins;
    }

    // ── per-kind schema helpers ───────────────────────────────────────────────

    /// <summary>A single exec pin in the given direction.</summary>
    private static IReadOnlyList<Pin> ExecOnly(string direction)
        => new[] { MakeExec(direction == "In" ? "In" : "Out", direction) };

    // ExecInOut() removed: exec-only static kinds now delegate to FromRegistry().

    /// <summary>
    /// EventEntryNode: when the containing graph is a <see cref="GraphKind.Function"/> graph
    /// with inputs, emit <c>exec-Out</c> + one data-Out pin per <c>Graph.Inputs</c> entry
    /// (name from <c>inp.Name</c>, type from <c>inp.Type.TypeId</c>; fallback
    /// <c>System.Object</c>).
    /// <para>
    /// Compiler contract (Stage5_Schedule.cs ~1157-1189): the compiler reads
    /// <c>!IsExec &amp;&amp; Direction=="Out"</c> data pins on EventEntryNode and matches each
    /// to <c>Graph.Inputs</c> by name (OrdinalIgnoreCase) to emit
    /// <c>IrOp_ReadInputArg(argIndex)</c>.  Pin names and <c>Direction="Out"</c> are therefore
    /// load-bearing; the projected pins are the compiler's source of truth.
    /// </para>
    /// Fallback to exec-only for Event/AiPrimitive graphs and Function graphs with no inputs.
    /// </summary>
    private static IReadOnlyList<Pin> EventEntryNodePins(Graph? containingGraph)
    {
        if (containingGraph is null)
            return ExecOnly("Out");

        if (containingGraph?.Kind == GraphKind.Function && containingGraph.Inputs.Count > 0)
        {
            var pins = new List<Pin>(1 + containingGraph.Inputs.Count);
            pins.Add(MakeExec("Out", "Out"));
            foreach (var inp in containingGraph.Inputs)
            {
                var typeId = string.IsNullOrEmpty(inp.Type?.TypeId) ? "System.Object" : inp.Type.TypeId;
                pins.Add(MakeData(inp.Name, "Out", typeId));
            }
            return pins;
        }
        return ExecOnly("Out");
    }

    /// <summary>
    /// ReturnNode: when the containing graph is a <see cref="GraphKind.Function"/> graph
    /// with at least one output, emit <c>exec-In</c> + one data-Out pin from
    /// <c>Graph.Outputs[0]</c> (name from <c>out.Name</c>, type from <c>out.Type.TypeId</c>;
    /// fallback <c>System.Object</c>).
    /// <para>
    /// Compiler contract (Stage5_Schedule.cs ~881-897 <c>BuildReturnTerminator</c>): Stage5
    /// reads <c>rn.Pins.FirstOrDefault(p =&gt; !p.IsExec &amp;&amp; p.Direction == "Out")</c>.
    /// The value pin therefore MUST have <c>Direction="Out"</c>, NOT <c>"In"</c> — this
    /// mirrors the GetVariable convention where data flows OUT of the node toward consumers.
    /// The compiler reads the pin as a producer (it resolves the wired source value and caches
    /// it for the return terminator), so "Out" is semantically correct: the node <em>provides</em>
    /// the return value on that pin.
    /// </para>
    /// Only the single first output is projected; multi-output support is deferred to a later batch.
    /// Fallback to exec-only for non-Function graphs and Function graphs with no outputs.
    /// </summary>
    private static IReadOnlyList<Pin> ReturnNodePins(Graph? containingGraph)
    {
        if (containingGraph is null)
            return ExecOnly("In");

        if (containingGraph?.Kind == GraphKind.Function && containingGraph.Outputs.Count > 0)
        {
            var output = containingGraph.Outputs[0];
            var typeId = string.IsNullOrEmpty(output.Type?.TypeId) ? "System.Object" : output.Type.TypeId;
            return new[]
            {
                MakeExec("In", "In"),
                MakeData(output.Name, "Out", typeId),
            };
        }
        return ExecOnly("In");
    }

    /// <summary>
    /// Dispatch helper: routes a <see cref="FunctionCallNode"/> to either
    /// <see cref="FunctionGraphCallPins"/> (when <c>TargetGraphId</c> resolves to a
    /// <see cref="GraphKind.Function"/> graph in the asset) or the existing CLR-reflection
    /// <see cref="FunctionCallPins"/> path (graceful fallback; no throw).
    /// </summary>
    private static IReadOnlyList<Pin> FunctionCallPinsDispatch(
        FunctionCallNode fc, BlueprintAsset? asset, Graph? containingGraph)
    {
        // Graph-call path: non-empty TargetGraphId + resolvable target Function graph.
        if (!string.IsNullOrEmpty(fc.TargetGraphId) && asset != null)
        {
            if (Guid.TryParse(fc.TargetGraphId, out var targetGuid))
            {
                var target = asset.Graphs.FirstOrDefault(
                    g => g.Id == targetGuid && g.Kind == GraphKind.Function);
                if (target != null)
                    return FunctionGraphCallPins(fc, target);
            }
        }

        // CLR-reflection path (unchanged existing behavior).
        return FunctionCallPins(fc);
    }

    /// <summary>
    /// FunctionCall targeting an in-blueprint <see cref="GraphKind.Function"/> graph.
    /// <para>
    /// Compiler contract (Stage5_Schedule.cs ~642-679):
    /// <list type="bullet">
    ///   <item>Data-IN pins are consumed <em>positionally</em> by
    ///     <c>ResolveAllDataInputs(node, stmts)</c> as the call arguments (order matches
    ///     <c>target.Inputs</c>; pin names are used only for readability on the canvas).</item>
    ///   <item>The first data-OUT pin (<c>gcOutPin</c>) is the return-value slot
    ///     (<c>!p.IsExec &amp;&amp; p.Direction == "Out"</c>).</item>
    /// </list>
    /// </para>
    /// Exec In/Out are omitted for pure calls (<see cref="FunctionCallNode.IsPure"/>).
    /// Only the first output is projected as data-OUT (BATCH-03A is single-output).
    /// </summary>
    private static IReadOnlyList<Pin> FunctionGraphCallPins(FunctionCallNode fc, Graph target)
    {
        var pins = new List<Pin>();

        if (!fc.IsPure)
        {
            pins.Add(MakeExec("In",  "In"));
            pins.Add(MakeExec("Out", "Out"));
        }

        foreach (var inp in target.Inputs)
        {
            var typeId = string.IsNullOrEmpty(inp.Type?.TypeId) ? "System.Object" : inp.Type.TypeId;
            pins.Add(MakeData(inp.Name, "In", typeId));
        }

        if (target.Outputs.Count > 0)
        {
            var output = target.Outputs[0];
            var typeId = string.IsNullOrEmpty(output.Type?.TypeId) ? "System.Object" : output.Type.TypeId;
            pins.Add(MakeData(output.Name, "Out", typeId));
        }

        return pins;
    }

    /// <summary>
    /// CallCustomEvent: exec In + exec Out + one data-IN pin per custom-event parameter in
    /// declaration order, typed from <c>param.Type.TypeId</c> (fallback: <c>System.Object</c>).
    /// Grounded in Stage5_Schedule.cs:695-703: <c>ResolveAllDataInputs(node, stmts)</c> maps
    /// all non-exec data-IN pins positionally to the raised event's parameters
    /// (<c>IrOp_RaiseCustomEvent(idx, inputVals)</c>).
    /// The event is matched by <c>Guid.TryParse(EventId) &amp;&amp; events[i].Id == guid</c>
    /// (Stage5_Schedule.cs:1157-1159 — primary key is <see cref="CustomEventDecl.Id"/>, with
    /// a Name fallback at line 1160).
    /// Graceful fallback to exec-only when: asset is null, EventId does not parse to a Guid,
    /// no matching <see cref="CustomEventDecl"/> found, or the event has zero parameters.
    /// </summary>
    private static IReadOnlyList<Pin> CallCustomEventPins(CallCustomEventNode cce, BlueprintAsset? asset)
    {
        var execPins = new[]
        {
            MakeExec("In",  "In"),
            MakeExec("Out", "Out"),
        };

        if (asset == null)
            return execPins;

        if (!Guid.TryParse(cce.EventId, out var eventGuid))
            return execPins;

        var decl = asset.CustomEvents.FirstOrDefault(e => e.Id == eventGuid);
        if (decl == null || decl.Parameters.Count == 0)
            return execPins;

        var pins = new List<Pin>(2 + decl.Parameters.Count);
        pins.AddRange(execPins);
        foreach (var param in decl.Parameters)
        {
            var typeId = string.IsNullOrEmpty(param.Type?.TypeId) ? "System.Object" : param.Type.TypeId;
            pins.Add(MakeData(param.Name, "In", typeId));
        }
        return pins;
    }

    /// <summary>
    /// CallPeerBlueprint: exec In + exec Out + one data-IN per peer function parameter
    /// (positional, Stage5_Schedule.cs:660 <c>ResolveAllDataInputs</c>) + a <c>Return</c>
    /// data-OUT pin typed from the function's first output (or <c>System.Object</c>).
    /// <para>
    /// When <paramref name="peerSignatureLookup"/> is non-null and the peer signature and its
    /// matching <see cref="BlueprintFunctionSig"/> are found, the projection emits typed pins.
    /// Otherwise falls back to the static shape: exec In/Out + <c>Return:System.Object</c>.
    /// This graceful fallback preserves backward-compatible behavior when no lookup is wired.
    /// </para>
    /// Grounded in Stage5_Schedule.cs:656-673: <c>ResolveAllDataInputs</c> consumes all
    /// data-IN pins positionally as call arguments; the first data-OUT pin is the return slot.
    /// </summary>
    private static IReadOnlyList<Pin> CallPeerBlueprintPins(
        CallPeerBlueprintNode          cpb,
        Func<Guid, BlueprintSignature?>? peerSignatureLookup)
    {
        // Static fallback shape (used when no lookup or peer/function not resolved).
        var fallback = new Pin[]
        {
            MakeExec("In",  "In"),
            MakeExec("Out", "Out"),
            MakeData("Return", "Out", "System.Object"),
        };

        if (peerSignatureLookup == null)
            return fallback;

        if (!Guid.TryParse(cpb.PeerBlueprintId, out var peerGuid))
            return fallback;

        BlueprintSignature? peerSig;
        try { peerSig = peerSignatureLookup(peerGuid); }
        catch { return fallback; }

        if (peerSig == null)
            return fallback;

        var funcSig = peerSig.ExportedFunctions
            .FirstOrDefault(f => string.Equals(f.Name, cpb.FunctionRef, StringComparison.Ordinal));
        if (funcSig == null)
            return fallback;

        // Signature-aware projection.
        var pins = new List<Pin>
        {
            MakeExec("In",  "In"),
            MakeExec("Out", "Out"),
        };

        // Data-IN: one per peer function input (positional, declaration order).
        foreach (var inp in funcSig.Inputs)
        {
            var typeId = string.IsNullOrEmpty(inp.TypeId) ? "System.Object" : inp.TypeId;
            pins.Add(MakeData(inp.Name, "In", typeId));
        }

        // Data-OUT: Return pin typed from Outputs[0] (or System.Object when no outputs).
        var returnTypeId = funcSig.Outputs.Count > 0 && !string.IsNullOrEmpty(funcSig.Outputs[0].TypeId)
            ? funcSig.Outputs[0].TypeId
            : "System.Object";
        pins.Add(MakeData("Return", "Out", returnTypeId));

        return pins;
    }

    /// <summary>
    /// FunctionCall: exec In/Out (only when <see cref="FunctionCallNode.IsPure"/> is false)
    /// plus one data-IN pin per method parameter (in declaration order, matching
    /// Stage5_Schedule.ResolveAllDataInputs) and a single <c>Return</c> data-OUT pin when the
    /// method has a non-void return type.  The target <see cref="Type"/> is resolved by FQN
    /// across loaded assemblies; when the type/method cannot be found the node degrades
    /// gracefully to exec-only (pure → empty), matching the prior behavior.
    /// </summary>
    private static IReadOnlyList<Pin> FunctionCallPins(FunctionCallNode fc)
    {
        var pins = new List<Pin>();
        if (!fc.IsPure)
        {
            pins.Add(MakeExec("In",  "In"));
            pins.Add(MakeExec("Out", "Out"));
        }

        var method = ResolveMethod(fc.TargetTypeId, fc.MethodName);
        if (method == null)
            return pins; // graceful fallback: exec-only (or empty for pure).

        foreach (var param in method.GetParameters())
        {
            var pt = param.ParameterType;
            if (pt.IsByRef) pt = pt.GetElementType() ?? pt;
            pins.Add(MakeData(param.Name ?? "arg", "In", pt.FullName ?? pt.Name));
        }

        if (method.ReturnType != typeof(void))
            pins.Add(MakeData("Return", "Out",
                method.ReturnType.FullName ?? method.ReturnType.Name));

        return pins;
    }

    private static IReadOnlyList<Pin> GetVariablePins(GetVariableNode gv, string typeId)
        => new[]
        {
            MakeData("Value", "Out", typeId),
        };

    private static IReadOnlyList<Pin> SetVariablePins(SetVariableNode sv, string typeId)
        => new[]
        {
            MakeExec("In",    "In"),
            MakeExec("Out",   "Out"),
            MakeData("Value", "In",  typeId),
            MakeData("Value", "Out", typeId),
        };

    /// <summary>
    /// GetSharedNode (Slice 2a-2): pure-data node. Data-out "Value" typed DIRECTLY from
    /// <see cref="GetSharedNode.SharedTypeId"/> (NOT <see cref="ResolveVariableTypeId"/> --
    /// the shared struct is foreign to this asset's variable list) + data-out "Found"
    /// (<c>System.Boolean</c>). Kept in parity with the compiler's
    /// <c>Stage0_Rehydrate.EnrichGetSharedPins</c>.
    /// </summary>
    private static IReadOnlyList<Pin> GetSharedPins(GetSharedNode gsn)
        => new[]
        {
            MakeData("Value", "Out", SharedTypePinTypeId(gsn.SharedTypeId)),
            MakeData("Found", "Out", "System.Boolean"),
        };

    /// <summary>
    /// SetSharedNode (Slice 2a-2): exec node. Data-in "Value" typed DIRECTLY from
    /// <see cref="SetSharedNode.SharedTypeId"/> + data-out "Written" (<c>System.Boolean</c>).
    /// Kept in parity with the compiler's <c>Stage0_Rehydrate.EnrichSetSharedPins</c>.
    /// </summary>
    private static IReadOnlyList<Pin> SetSharedPins(SetSharedNode ssn)
        => new[]
        {
            MakeExec("In",      "In"),
            MakeExec("Out",     "Out"),
            MakeData("Value",   "In",  SharedTypePinTypeId(ssn.SharedTypeId)),
            MakeData("Written", "Out", "System.Boolean"),
        };

    /// <summary>
    /// Resolves the pin <c>TypeId</c> for a GetShared/SetShared "Value" pin directly from the
    /// node's <c>SharedTypeId</c> (a foreign Category-1 struct FQN). Stamped with the
    /// <c>"global::"</c> AN2 sentinel (mirrors <see cref="EnumStampedTypeFqn"/>) so the compiler's
    /// <c>StaticTypeRegistry</c> accepts it as a project/unmanaged type.
    /// </summary>
    private static string SharedTypePinTypeId(string sharedTypeId)
    {
        if (string.IsNullOrEmpty(sharedTypeId)) return "System.Object";
        return sharedTypeId.StartsWith("global::", StringComparison.Ordinal)
            ? sharedTypeId
            : "global::" + sharedTypeId;
    }

    // ── ChannelCommand / non-channel action parameter resolution (DYNAMIC) ────

    /// <summary>
    /// ChannelCommandNode pin projection — dispatches to one of two paths based on whether
    /// <see cref="ChannelCommandNode.ActionFqn"/> is set (AN7 non-channel path) or not
    /// (existing channel-command path).
    /// <para>
    /// <b>Channel-command path</b> (ActionFqn null/empty): exec In/Out + parameter data-IN pins
    /// resolved from <paramref name="channelCommands"/>.  The matching
    /// <see cref="ChannelCommandCatalogEntry"/> is found exactly the way
    /// Stage2_Validate.V_ChannelCommandReferences matches it:
    /// <c>LastSegment(ChannelTypeFqn) == node.ChannelType &amp;&amp; entry.Name == node.ActionId</c>.
    /// </para>
    /// <para>
    /// <b>Non-channel action path</b> (ActionFqn non-null, AN7): exec In/Out + parameter
    /// data-IN pins resolved from <paramref name="behaviorActions"/> by looking up the entry
    /// whose <c>Id == ActionFqn</c> and reflecting its <c>ParamsTypeFqn</c>.  Enum fields are
    /// stamped with <c>"global::"</c> per AN6.  Compile lowering is deferred to AN8.
    /// </para>
    /// <para>
    /// Both paths: unknown action / unresolvable params type / null catalog → exec-only (no throw).
    /// </para>
    /// </summary>
    private static IReadOnlyList<Pin> ChannelCommandPins(
        ChannelCommandNode      cc,
        IChannelCommandCatalog? channelCommands,
        IBehaviorActionCatalog? behaviorActions)
    {
        // AN7: non-channel path — ActionFqn is set.
        if (!string.IsNullOrEmpty(cc.ActionFqn))
            return NonChannelActionPins(cc.ActionFqn, behaviorActions);

        // Existing channel-command path — ActionFqn is null/empty.
        return ChannelCommandPinsFromCatalog(cc, channelCommands);
    }

    /// <summary>
    /// AN7 — non-channel action pin projection.
    /// Looks up the action by <paramref name="actionFqn"/> in <paramref name="behaviorActions"/>,
    /// resolves its <c>ParamsTypeFqn</c>, and reflects the DTO fields as data-IN pins
    /// (same logic as <see cref="ChannelCommandPinsFromCatalog"/>).
    /// Returns exec-only when the catalog is null, the FQN is not found, or the params
    /// type cannot be resolved.
    /// </summary>
    private static IReadOnlyList<Pin> NonChannelActionPins(
        string                  actionFqn,
        IBehaviorActionCatalog? behaviorActions)
    {
        var pins = new List<Pin>
        {
            MakeExec("In",  "In"),
            MakeExec("Out", "Out"),
        };

        if (behaviorActions == null)
            return pins;

        BehaviorActionEntry? entry;
        try
        {
            entry = behaviorActions.GetActions(BehaviorActionHosts.Blueprint)
                .FirstOrDefault(e =>
                    e.Source != BehaviorActionSource.ChannelCommand
                    && e.Id == actionFqn);
        }
        catch
        {
            return pins; // catalog failure → exec-only.
        }

        if (entry == null || string.IsNullOrEmpty(entry.ParamsTypeFqn))
            return pins; // unknown FQN → exec-only.

        return AppendParamPins(pins, entry.ParamsTypeFqn);
    }

    /// <summary>
    /// Channel-command parameter resolution — the original pre-AN7 path.
    /// <c>ChannelType</c> + <c>ActionId</c> are matched against <paramref name="channelCommands"/>.
    /// Unknown action / unresolvable params type / null catalog → exec-only (no throw).
    /// </summary>
    private static IReadOnlyList<Pin> ChannelCommandPinsFromCatalog(
        ChannelCommandNode      cc,
        IChannelCommandCatalog? channelCommands)
    {
        var pins = new List<Pin>
        {
            MakeExec("In",  "In"),
            MakeExec("Out", "Out"),
        };

        if (channelCommands == null)
            return pins;

        ChannelCommandCatalogEntry? entry;
        try
        {
            entry = channelCommands.GetEntries().FirstOrDefault(e =>
                LastSegment(e.ChannelTypeFqn) == cc.ChannelType
                && e.Name == cc.ActionId);
        }
        catch
        {
            return pins; // catalog failure → exec-only.
        }

        if (entry == null || string.IsNullOrEmpty(entry.ParamsTypeFqn))
            return pins; // unknown action → exec-only.

        return AppendParamPins(pins, entry.ParamsTypeFqn);
    }

    /// <summary>
    /// Shared helper: resolves <paramref name="paramsFqn"/> to a CLR <see cref="Type"/> and
    /// appends data-IN pins to <paramref name="pins"/>.
    /// <list type="bullet">
    ///   <item>Decomposable struct/class → one pin per public instance field/property.</item>
    ///   <item>Primitive/enum/opaque → a single pin named after the type's short name.</item>
    ///   <item>Type not loadable → a single pin with the raw FQN as the TypeId (wire is still
    ///     meaningful for the canvas).</item>
    /// </list>
    /// Returns <paramref name="pins"/> for fluent chaining (same list, mutated).
    /// </summary>
    private static IReadOnlyList<Pin> AppendParamPins(List<Pin> pins, string paramsFqn)
    {
        var paramsType = ResolveType(paramsFqn);
        if (paramsType == null)
        {
            // Type not loadable: still surface a single typed param pin so the wire is meaningful.
            pins.Add(MakeData(LastSegment(paramsFqn), "In", paramsFqn));
            return pins;
        }

        var members = ReflectDataMembers(paramsType);
        if (members.Count == 0)
        {
            // Primitive/enum/opaque params: a single value pin typed as the params type.
            pins.Add(MakeData(LastSegment(paramsFqn), "In", paramsFqn));
            return pins;
        }

        foreach (var (name, typeFqn) in members)
            pins.Add(MakeData(name, "In", typeFqn));

        return pins;
    }

    /// <summary>
    /// Returns the public instance fields and read/write properties of <paramref name="type"/>
    /// as <c>(Name, TypeFqn)</c> pairs, in declaration order.  Returns an empty list for
    /// primitives, enums and types with no decomposable members.
    /// <para>
    /// <b>Enum fields:</b> when a field's CLR type <c>IsEnum</c>, the TypeFqn is stamped with
    /// the <c>"global::"</c> prefix (<c>"global::" + field.FieldType.FullName</c>) so the
    /// emitted <see cref="BlueprintTypeRef.TypeId"/> matches the AN2 compiler sentinel.
    /// The compiler then resolves the enum as an unmanaged type (size 4) and emits
    /// <c>(global::FQN)N</c> for the default literal.  Non-enum fields are unchanged.
    /// </para>
    /// </summary>
    private static List<(string Name, string TypeFqn)> ReflectDataMembers(Type type)
    {
        var result = new List<(string, string)>();
        if (type.IsPrimitive || type.IsEnum || type == typeof(string))
            return result;

        try
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                var typeFqn = EnumStampedTypeFqn(field.FieldType);
                result.Add((field.Name, typeFqn));
            }

            foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (prop.GetIndexParameters().Length > 0) continue; // skip indexers
                if (!prop.CanRead) continue;
                var typeFqn = EnumStampedTypeFqn(prop.PropertyType);
                result.Add((prop.Name, typeFqn));
            }
        }
        catch
        {
            return new List<(string, string)>();
        }

        return result;
    }

    /// <summary>
    /// Returns the TypeFqn string for a CLR member type.
    /// <para>
    /// When <paramref name="memberType"/> is an enum, returns
    /// <c>"global::" + memberType.FullName</c> so the resulting
    /// <see cref="BlueprintTypeRef.TypeId"/> matches the AN2 compiler sentinel.
    /// For all other types, returns <c>memberType.FullName ?? memberType.Name</c>.
    /// </para>
    /// </summary>
    private static string EnumStampedTypeFqn(Type memberType)
    {
        if (memberType.IsEnum)
        {
            var fqn = memberType.FullName ?? memberType.Name;
            return "global::" + fqn;
        }
        return memberType.FullName ?? memberType.Name;
    }

    // ── reflection helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Resolves a <see cref="Type"/> by its fully-qualified name across all loaded assemblies.
    /// Tries <see cref="Type.GetType(string)"/> first, then scans
    /// <see cref="AppDomain.CurrentDomain"/>.  Returns null when not found.
    /// </summary>
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
            catch
            {
                // Ignore assemblies that fail type resolution.
            }
        }
        return null;
    }

    /// <summary>
    /// Resolves a method by declaring-type FQN and method name across loaded assemblies.
    /// Returns the first public/non-public, static/instance method matching
    /// <paramref name="methodName"/>; null when the type or method cannot be found.
    /// </summary>
    private static MethodInfo? ResolveMethod(string targetTypeId, string methodName)
    {
        if (string.IsNullOrEmpty(targetTypeId) || string.IsNullOrEmpty(methodName))
            return null;

        var type = ResolveType(targetTypeId);
        if (type == null) return null;

        try
        {
            return type.GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Static | BindingFlags.Instance)
                .FirstOrDefault(m => m.Name == methodName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns the last dotted segment of <paramref name="fqn"/> (e.g. "LocomotionChannel").</summary>
    private static string LastSegment(string fqn)
    {
        if (string.IsNullOrEmpty(fqn)) return fqn;
        var idx = fqn.LastIndexOf('.');
        return idx >= 0 && idx < fqn.Length - 1 ? fqn[(idx + 1)..] : fqn;
    }

    // ── primitive factory helpers ─────────────────────────────────────────────

    private static Pin MakeExec(string name, string direction) => new()
    {
        Id        = Guid.NewGuid(),
        Name      = name,
        Direction = direction,
        IsExec    = true,
        TypeRef   = new BlueprintTypeRef(),
    };

    private static Pin MakeData(string name, string direction, string typeId) => new()
    {
        Id        = Guid.NewGuid(),
        Name      = name,
        Direction = direction,
        IsExec    = false,
        TypeRef   = new BlueprintTypeRef { TypeId = typeId },
    };
}
