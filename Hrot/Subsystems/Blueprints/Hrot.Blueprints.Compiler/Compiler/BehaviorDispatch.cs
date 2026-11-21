using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler;

/// <summary>
/// ⭐⭐ <c>CE-446</c> (<c>Q77</c> §5.6) — the one rule for "is this graph the TICK of a blueprint BEHAVIOUR?".
///
/// <para>
/// ⭐ A <see cref="BlueprintDispatchKind.Behavior"/> asset IS an Instance body (same lowering, same emitter, same node set)
/// whose Tick returns a <c>NodeStatus</c>: a <c>Return</c> node yields its <c>Success</c>/<c>Failure</c> (the behaviour
/// finishes), and every other exit — falling off the end, suspending on a latent node, the cursor-staleness bail — is
/// <c>Running</c>. ⭐ Only the Tick is status-shaped; Event and helper Function graphs keep the Instance shapes.
/// </para>
/// <para>
/// ⚠ The tick-graph selection MIRRORS <c>InstanceEmitter</c> (the Function graph named <c>Tick</c>, else the first
/// Function graph) — both sides call this, so Stage 5 and the emitter cannot disagree about which graph returns a status.
/// </para>
/// <para>
/// ⭐ Public (CE-496) so the editor's Return-node panel asks the SAME rule which control the compiler reads —
/// on the <c>MacroExpander</c> precedent: this assembly's <c>InternalsVisibleTo</c> does not list <c>.Editor</c>, and a
/// second copy of the rule is the drift <c>BP-69</c> recorded.
/// </para>
/// </summary>
public static class BehaviorDispatch
{
    public const string TickGraphName = "Tick";

    public static bool IsTickGraph(BlueprintAsset asset, Graph graph)
        => asset.Dispatch == BlueprintDispatchKind.Behavior
           && graph.Kind == GraphKind.Function
           && ReferenceEquals(graph,
                  asset.Graphs.FirstOrDefault(g => g.Kind == GraphKind.Function && g.Name == TickGraphName)
                  ?? asset.Graphs.FirstOrDefault(g => g.Kind == GraphKind.Function));

    internal static bool IsTickGraph(IrAsset asset, IrGraph? graph)
        => graph is not null
           && asset.Dispatch == BlueprintDispatchKind.Behavior
           && graph.Kind == IrGraphKind.Function
           && ReferenceEquals(graph,
                  asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Function && g.Name == TickGraphName)
                  ?? asset.Graphs.FirstOrDefault(g => g.Kind == IrGraphKind.Function));
}
