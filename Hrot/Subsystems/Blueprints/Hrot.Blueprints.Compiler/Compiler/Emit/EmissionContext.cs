using Hrot.Blueprints.Core.Compiler.Ir;
using AssetDispatch = Hrot.Blueprints.Core.Assets.BlueprintDispatchKind;

namespace Hrot.Blueprints.Core.Compiler.Emit;

/// <summary>
/// Per-asset mutable state threaded through all emitters.
/// </summary>
internal sealed class EmissionContext
{
    private readonly Dictionary<string, int> _counters = new();
    private readonly Dictionary<int, string> _blockLabels;

    public IrAsset Asset { get; }
    public CompilerMode Mode { get; }
    public IrGraph? CurrentGraph { get; set; }

    /// <summary>
    /// CallPeerBlueprint/AiPrimitiveCall alias fix -- the cross-asset function signatures the
    /// caller was compiled WITH (<c>CompileOptions.SiblingSignatures</c>), threaded through so
    /// <c>CSharpEmitter.EmitUsings</c> can resolve a peer's REAL generated class name
    /// (<c>{SanitizedName}_{BlueprintId:X8}_Bp</c>) for the <c>__Peer_{id:X8}_Bp</c> /
    /// <c>__AiPrim_{id:X8}_Bp</c> bare names <c>StatementEmitter</c> emits as call targets. Stage2's
    /// <c>V_PeerReferences</c> already validated (BP1301) that every <c>CallPeerBlueprintNode</c>
    /// reaching this stage has a matching entry here -- this is the SAME list, not recomputed.
    /// </summary>
    public IReadOnlyList<BlueprintSignature> SiblingSignatures { get; }

    public EmissionContext(
        IrAsset asset, CompilerMode mode,
        IReadOnlyList<BlueprintSignature>? siblingSignatures = null)
    {
        Asset = asset;
        Mode = mode;
        SiblingSignatures = siblingSignatures ?? Array.Empty<BlueprintSignature>();
        _blockLabels = new Dictionary<int, string>();
        foreach (var g in asset.Graphs)
            foreach (var b in g.Blocks)
                _blockLabels[b.Id.Value] = b.Label;
    }

    /// <summary>Returns next integer suffix per prefix (e.g. "ch" -> 0, 1, 2, ...).</summary>
    public string NextLocalCounter(string prefix)
    {
        _counters.TryGetValue(prefix, out int n);
        _counters[prefix] = n + 1;
        return n.ToString();
    }

    /// <summary>Block label by IrBlockId, for goto emission.</summary>
    public string LabelForBlock(IrBlockId id)
        => _blockLabels.TryGetValue(id.Value, out var lbl) ? lbl : $"block_{id.Value}";

    /// <summary>C# field name for a WorkingState or Variable by index.</summary>
    public string VarFieldName(int index)
    {
        var fields = Asset.Variables;
        if (index >= 0 && index < fields.Count)
            return fields[index].Name;
        var ws = Asset.WorkingState;
        if (index >= 0 && index < ws.Count)
            return ws[index].Name;
        return $"__var_{index}";
    }

    /// <summary>
    /// BP-57 — the emitted C# identifier for a function-local.
    ///
    /// <para>
    /// ⚠ Prefixed so a designer's local cannot collide with a method parameter (a graph's inputs
    /// become parameters), a C# keyword, or any other emitted symbol.
    /// </para>
    /// </summary>
    public static string LocalName(string declaredName) => "__loc_" + declaredName;

    /// <summary>
    /// BP-57 — the emitted identifier for the local at <paramref name="index"/> in the CURRENT graph.
    ///
    /// <para>
    /// ⛔ Reads <c>CurrentGraph.Locals</c>, never the asset's variable lists. That separation is the
    /// point of the op: see <c>IrGraph.Locals</c> and <c>FINDING_Variable_Index_Space.md</c> for what
    /// sharing an index space with them costs.
    /// </para>
    /// </summary>
    public string LocalFieldName(int index)
    {
        var locals = CurrentGraph?.Locals;
        if (locals is null || index < 0 || index >= locals.Count) return $"__loc_unknown_{index}";

        // BP-57 / ⭐⭐ Q27-A3 — a suspending graph's locals are blackboard slots, not C# locals: the
        // frame they would otherwise live in dies at every `return NodeStatus.Running`.
        var prefix = CurrentGraph!.LocalSlotPrefix;
        return prefix is null
            ? LocalName(locals[index].Name)
            : $"{StateVar}.{Lowering.LocalStorage.SlotName(prefix, locals[index].Name)}";
    }

    /// <summary>C# field name for a Parameters entry by index.</summary>
    public string ParamFieldName(int index)
    {
        var ps = Asset.Parameters;
        return index >= 0 && index < ps.Count ? ps[index].Name : $"__p_{index}";
    }

    /// <summary>Custom event name by index.</summary>
    public string CustomEventName(int index)
    {
        var evts = Asset.CustomEvents;
        return index >= 0 && index < evts.Count ? evts[index].Name : $"__customEvent_{index}";
    }

    /// <summary>Library class name for a LibraryBlueprintId.</summary>
    public string ResolveLibraryClass(int libraryBlueprintId)
        => $"__LibBp_{libraryBlueprintId:X8}_Bp";

    /// <summary>World access expression based on dispatch kind.</summary>
    public string WorldVar =>
        Asset.Dispatch == AssetDispatch.AiPrimitive
            ? "world"
            : "((global::Fdp.Core.EntityRepository)view)";

    /// <summary>State struct local variable name based on dispatch kind.</summary>
    public string StateVar =>
        Asset.Dispatch == AssetDispatch.AiPrimitive ? "ws" : "s";

    /// <summary>
    /// P7 -- read-only <c>ISimulationView</c> expression for the in-scope view, used when
    /// appending the trailing engine-context argument to a FunctionCall (see
    /// <c>IrOp_PureCall.AppendViewArg</c> / <c>IrOp_LibraryCall.AppendViewArg</c>).
    /// <para>
    /// AiPrimitive: <c>world</c> -- typed <c>Fdp.Core.EntityRepository</c>, which implements
    /// <c>ISimulationView</c>, so passing it to a parameter typed <c>ISimulationView</c> is an
    /// ordinary (read-only-surfaced) implicit reference conversion; no cast is emitted.
    /// Instance: <c>view</c> -- already typed <c>ISimulationView</c> directly.
    /// </para>
    /// Deliberately DIFFERENT from <see cref="WorldVar"/>, which casts to the mutable
    /// <c>EntityRepository</c> for write access (GetShared/SetShared) -- P7's FunctionCall
    /// context argument must stay read-only per the architect-blessed design.
    /// Never read for a Library-dispatch asset: <c>HasSelfInScope</c> is false there, and
    /// Stage 5 never sets AppendSelfArg/AppendViewArg true for Library dispatch (see
    /// Stage5_Schedule.ResolveFunctionCallTrailingContext), so this value is unused in that case.
    /// </summary>
    public string ViewVar =>
        Asset.Dispatch == AssetDispatch.AiPrimitive ? "world" : "view";

    /// <summary>
    /// CA-05 (Slice 1b) -- an expression whose STATIC type is <c>Fdp.ModuleHost.Abstractions.
    /// ISimulationView</c>, safe to use as the RECEIVER of an explicitly-implemented interface member
    /// (e.g. <c>GetManagedComponentRO&lt;T&gt;</c> -- see <see cref="IrOp_GetManagedComponentRO"/>).
    /// <para>
    /// Distinct from <see cref="ViewVar"/>: that property returns the bare <c>world</c> identifier for
    /// AiPrimitive dispatch, whose STATIC type is the concrete <c>Fdp.Core.EntityRepository</c> --
    /// fine for passing as an ARGUMENT to an <c>ISimulationView</c> parameter (implicit reference
    /// conversion at the call site) but NOT sufficient to invoke an explicitly-implemented interface
    /// method directly on it (C# resolves explicit interface implementations only through an
    /// expression statically typed as the interface; on the concrete class such a call would instead
    /// try to bind the type's own OWN internal member of the same name, which is not
    /// <c>InternalsVisibleTo</c>-accessible from generated blueprint code). Instance dispatch's
    /// <c>view</c> parameter is already declared <c>ISimulationView</c>, so no cast is needed there
    /// (mirrors every real call site in the engine, e.g. <c>SmartEgressUtil</c>, which always calls
    /// <c>GetManagedComponentRO</c> through an <c>ISimulationView</c>-typed variable or an explicit
    /// <c>((ISimulationView)repo)</c> cast).
    /// </para>
    /// </summary>
    public string SimulationViewVar =>
        Asset.Dispatch == AssetDispatch.AiPrimitive
            ? "((global::Fdp.ModuleHost.Abstractions.ISimulationView)world)"
            : "view";

    /// <summary>
    /// CA-06 (Slice W2) -- the <c>IEntityCommandBuffer</c>-typed expression for the in-scope ECB,
    /// used by <see cref="IrOp_SetManagedComponent"/>'s emit (<c>Emit.StatementEmitter</c>'s case).
    /// <para>
    /// Instance dispatch: bare <c>ecb</c> -- every Instance-emitted method (<c>Tick</c>,
    /// <c>Event_*</c>, the in-blueprint <c>Func_*</c> helpers, and their thunks -- see
    /// <c>InstanceEmitter</c>) declares an <c>IEntityCommandBuffer ecb</c> parameter, exactly the
    /// identifier the pre-existing ECB-write ops (<see cref="IrOp_AddComponent"/>,
    /// <see cref="IrOp_RemoveComponent"/>, <see cref="IrOp_DestroyEntity"/>,
    /// <see cref="IrOp_PublishEvent"/>) already emit as a literal <c>"ecb"</c> string -- this property
    /// exists so CA-06's NEW op resolves the identifier through the context (matching
    /// <see cref="SimulationViewVar"/>'s style) rather than adding yet another ad-hoc literal.
    /// </para>
    /// <para>
    /// AiPrimitive dispatch: THROWS. <c>TickCore(ref Params p, ref WorkingState ws, Entity self,
    /// EntityRepository world, float time)</c> (see <c>AiPrimitiveEmitter.EmitTickCore</c>) carries NO
    /// <c>IEntityCommandBuffer</c> parameter at all -- there is no ECB in scope to name. Reaching this
    /// property for an AiPrimitive-dispatch asset would mean Stage2's <c>V_ComponentAccessRules</c>
    /// (BP2065) failed to reject a managed <see cref="Assets.SetComponentNode"/> there BEFORE Stage5/
    /// emit ever ran (the compiler pipeline stops at the first Stage2 error -- see
    /// <c>BlueprintCompiler.Compile</c> -- so this is defense-in-depth, not a reachable runtime path).
    /// </para>
    /// </summary>
    public string EcbVar =>
        Asset.Dispatch == AssetDispatch.AiPrimitive
            ? throw new System.InvalidOperationException(
                "EmissionContext.EcbVar has no scope in AiPrimitive dispatch -- TickCore carries no " +
                "IEntityCommandBuffer parameter. A managed SetComponentNode write must be rejected by " +
                "Stage2_Validate's V_ComponentAccessRules (BP2065) before Stage5/emit ever reaches here.")
            : "ecb";

    /// <summary>
    /// True when the emitted method has an <c>Entity self</c> parameter in scope.
    /// AiPrimitive (TickCore / thunks) and Instance (Tick/Event) methods carry <c>self</c>;
    /// Library-dispatch function graphs are stateless static methods with no entity context,
    /// so entity-scoped debug probes (NodeEnter / PinValueChanged) must not reference <c>self</c>
    /// there — doing so emits uncompilable C# (CS0103). See StatementEmitter debug-probe cases.
    /// </summary>
    public bool HasSelfInScope => Asset.Dispatch != AssetDispatch.Library;
}
