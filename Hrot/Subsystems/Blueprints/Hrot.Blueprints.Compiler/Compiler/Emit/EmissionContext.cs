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

    public EmissionContext(IrAsset asset, CompilerMode mode)
    {
        Asset = asset;
        Mode = mode;
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
    /// True when the emitted method has an <c>Entity self</c> parameter in scope.
    /// AiPrimitive (TickCore / thunks) and Instance (Tick/Event) methods carry <c>self</c>;
    /// Library-dispatch function graphs are stateless static methods with no entity context,
    /// so entity-scoped debug probes (NodeEnter / PinValueChanged) must not reference <c>self</c>
    /// there — doing so emits uncompilable C# (CS0103). See StatementEmitter debug-probe cases.
    /// </summary>
    public bool HasSelfInScope => Asset.Dispatch != AssetDispatch.Library;
}
