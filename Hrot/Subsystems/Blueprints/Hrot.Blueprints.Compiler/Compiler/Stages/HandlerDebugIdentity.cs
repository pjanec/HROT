using Hrot.Blueprints.Core.Compiler.Ir;

namespace Hrot.Blueprints.Core.Compiler.Stages;

/// <summary>
/// ⭐ CE-2017 (<c>DESIGN_Typed_Event_Nodes</c> E6, T-3) — rewrites a split handler's DEBUG identities to the authored
/// ones: every annotation's graph / node / pin / exec-entry id, every block's source node, the breakpoint targets, and
/// the node ids two collection ops pass to their probes. ⇒ a probe in a shared tail fires the AUTHORED node's id (one
/// breakpoint there pauses whichever handler runs it), and the debug map names the authored graph.
/// <para>
/// ⛔ Only debug identities move. Everything that keys STATE — a When's memory field, a Run Behaviour site, a per-node
/// helper — was named from the clone's own id in Stage 5 and keeps it: that is what keeps the handlers apart.
/// </para>
/// </summary>
internal static class HandlerDebugIdentity
{
    public static IrGraph Apply(IrGraph graph, IReadOnlyDictionary<Guid, Guid> ids)
    {
        Guid Map(Guid id) => ids.TryGetValue(id, out var a) ? a : id;
        Guid? MapN(Guid? id) => id is { } v ? Map(v) : null;

        IrDebugAnnotation? Debug(IrDebugAnnotation? d) => d is null ? null : d with
        {
            GraphId         = Map(d.GraphId),
            NodeId          = MapN(d.NodeId),
            PinId           = MapN(d.PinId),
            ExecEntryNodeId = MapN(d.ExecEntryNodeId),
        };

        IReadOnlyList<IrStatement> Statements(IReadOnlyList<IrStatement> list) => list.Select(s => s with
        {
            Debug = Debug(s.Debug)!,
            Operation = s.Operation switch
            {
                IrOp_ForEach fe       => fe with { Body = Statements(fe.Body) },
                IrOp_If br            => br with { Then = Statements(br.Then), Else = Statements(br.Else) },
                IrOp_CollectionWrite c => c with { NodeId = Map(c.NodeId) },
                IrOp_ListWrite w       => w with { NodeId = Map(w.NodeId) },
                var op                 => op,
            },
        }).ToList();

        return graph with
        {
            Blocks = graph.Blocks.Select(b => b with
            {
                SourceNodeId = MapN(b.SourceNodeId),
                Statements   = Statements(b.Statements),
                Terminator   = b.Terminator is null ? null! : b.Terminator with { Debug = Debug(b.Terminator.Debug)! },
            }).ToList(),
            BreakpointTargets = Targets(),
        };

        // ⚠ Two ids can map to one authored node (a Behaviour Task and its compile-time abort node, S7a): the node's own
        //   entry wins, so a breakpoint on the task pauses where the TASK runs.
        IReadOnlyDictionary<Guid, Guid> Targets()
        {
            var result = new Dictionary<Guid, Guid>();
            foreach (var kv in graph.BreakpointTargets.OrderBy(kv => ids.ContainsKey(kv.Key) ? 1 : 0))
            {
                var key = Map(kv.Key);
                if (!result.ContainsKey(key)) result[key] = Map(kv.Value);   // netstandard2.0: no TryAdd
            }
            return result;
        }
    }
}
