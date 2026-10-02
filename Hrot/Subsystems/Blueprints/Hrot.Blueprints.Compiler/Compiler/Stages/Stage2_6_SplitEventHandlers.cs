using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Transform;

namespace Hrot.Blueprints.Core.Compiler.Stages;

/// <summary>
/// ⭐⭐ CE-2013 (<c>DESIGN_Typed_Event_Nodes</c> T-2) — an Event graph holding N typed event nodes becomes N HANDLER
/// graphs, one per node, before anything is scheduled. Every stage after this one sees exactly what it saw before:
/// one Event graph = one handler, with one entry, its own inputs (the node's payload), its own fiber storage and
/// policy (U-6) and its own locals (T-5). ⇒ nothing from Stage 3 on learns that an authored graph can hold several.
///
/// <para>
/// A handler is the node's exec chain plus every node it pulls data from. ⭐ A node reached by an EARLIER handler too
/// (a shared tail, T-3) is CLONED for this one, with ids derived from the original and the handler's event node:
/// everything keyed by a node id — a When's memory, a Run Behaviour site, a per-node helper — is then that handler's
/// own, and the clone carries <see cref="Node.OriginNodeId"/> / <see cref="Node.OriginGraphId"/> back to the authored
/// node, the seam macro expansion already uses for the debugger.
/// </para>
///
/// <para>
/// ⚠ Runs after macro expansion (a macro body may be part of a shared tail) and before Stage 3 (whose orphan pass
/// and literal synthesis then work per handler, and whose Stage 4 types every clone's pins).
/// </para>
/// </summary>
internal static class Stage2_6_SplitEventHandlers
{
    public static BlueprintAsset Run(BlueprintAsset asset, ValidationContext ctx)
    {
        var graphs = new List<Graph>(asset.Graphs.Count);
        var taken  = new HashSet<string>(asset.Graphs.Select(g => g.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var graph in asset.Graphs)
        {
            var entries = graph.Kind == GraphKind.Event
                ? graph.Nodes.OfType<EventEntryNode>().ToList()
                : new List<EventEntryNode>();
            if (entries.Count == 0)
                graphs.Add(graph);
            else if (entries.Count == 1)
                graphs.Add(WithNodePayload(graph, entries[0]));
            else
                graphs.AddRange(Split(asset, graph, entries, taken, ctx));
        }
        asset.Graphs = graphs;
        return asset;
    }

    /// <summary>One event node: the graph is already one handler; only a node-carried payload becomes its inputs.
    /// ⭐ A graph whose payload is still on the graph is passed through as the same object ⇒ no golden can move.</summary>
    private static Graph WithNodePayload(Graph graph, EventEntryNode entry)
    {
        if (entry.Fields is null) return graph;
        var copy = graph.WithNodesAndLinks(new List<Node>(graph.Nodes), new List<Link>(graph.Links));
        copy.Inputs = EventPayload.FieldsOf(graph, entry).ToList();
        return copy;
    }

    private static IEnumerable<Graph> Split(
        BlueprintAsset asset, Graph graph, List<EventEntryNode> entries, HashSet<string> taken, ValidationContext ctx)
    {
        var pinById = new Dictionary<Guid, Pin>();
        foreach (var n in graph.Nodes)
            foreach (var p in n.Pins)
                pinById[p.Id] = p;
        bool IsExec(Link l)
            => pinById.TryGetValue(l.FromPinId, out var from) ? from.IsExec
             : pinById.TryGetValue(l.ToPinId, out var to) && to.IsExec;

        var reached = new HashSet<Guid>();   // by any earlier handler
        for (int k = 0; k < entries.Count; k++)
        {
            var entry = entries[k];
            var mine  = Reach(graph, entry, IsExec, asset, ctx);

            // A node an earlier handler also runs is cloned for this one: its node-keyed state becomes its own (T-3).
            var shared = graph.Nodes.Where(n => mine.Contains(n.Id) && reached.Contains(n.Id)).ToList();
            var fragment = GraphFragmentCloner.Clone(shared, Array.Empty<Link>(),
                id => DeterministicIds.FromString($"event-handler:{entry.Id:N}:{id:N}"));
            var cloneOf = new Dictionary<Guid, Node>();
            for (int i = 0; i < shared.Count; i++)
            {
                var clone = fragment.Nodes[i];
                clone.OriginNodeId  = shared[i].OriginNodeId  ?? shared[i].Id;
                clone.OriginGraphId = shared[i].OriginGraphId ?? graph.Id;
                cloneOf[shared[i].Id] = clone;
            }
            Guid NodeIdOf(Guid id) => fragment.NodeMap.TryGetValue(id, out var c) ? c : id;
            Guid PinIdOf(Guid id)  => fragment.PinMap.TryGetValue(id, out var c) ? c : id;

            var nodes = graph.Nodes.Where(n => mine.Contains(n.Id))
                                   .Select(n => cloneOf.TryGetValue(n.Id, out var c) ? c : n)
                                   .ToList();
            var links = graph.Links.Where(l => mine.Contains(l.FromNodeId) && mine.Contains(l.ToNodeId))
                                   .Select(l => new Link
                                   {
                                       FromNodeId = NodeIdOf(l.FromNodeId), FromPinId = PinIdOf(l.FromPinId),
                                       ToNodeId   = NodeIdOf(l.ToNodeId),   ToPinId   = PinIdOf(l.ToPinId),
                                       Waypoints  = l.Waypoints,
                                   })
                                   .ToList();
            reached.UnionWith(mine);

            var handler = graph.WithNodesAndLinks(nodes, links);
            // ⭐ The first handler keeps the authored graph's id and name, so a graph that gains a second event keeps
            //   its first handler's generated names (and its recordings) as they were.
            if (k > 0)
            {
                handler.Id   = DeterministicIds.FromString($"event-handler-graph:{graph.Id:N}:{entry.Id:N}");
                handler.Name = FreshName(graph.Name, k, taken);
            }
            handler.Inputs = EventPayload.FieldsOf(graph, entry).ToList();
            yield return handler;
        }

        foreach (var orphan in graph.Nodes.Where(n => !reached.Contains(n.Id)))
            ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP3010,
                $"Orphan node '{orphan.Id}' in graph '{graph.Name}' is reached by none of its event nodes and was eliminated.",
                asset.AssetId, graph.Id, orphan.Id));
    }

    /// <summary>
    /// The handler of <paramref name="entry"/>: its exec chain, then every node that chain pulls data from. ⛔ Data read
    /// from ANOTHER event node is refused (<c>BP1683</c>): that payload does not exist when this event fires.
    /// </summary>
    private static HashSet<Guid> Reach(
        Graph graph, EventEntryNode entry, Func<Link, bool> isExec, BlueprintAsset asset, ValidationContext ctx)
    {
        var events = new HashSet<Guid>(graph.Nodes.OfType<EventEntryNode>().Select(e => e.Id));
        var mine = new HashSet<Guid> { entry.Id };
        var work = new Stack<Guid>();
        work.Push(entry.Id);
        while (work.Count > 0)
        {
            var at = work.Pop();
            foreach (var l in graph.Links.Where(l => l.FromNodeId == at && isExec(l)))
                if (mine.Add(l.ToNodeId)) work.Push(l.ToNodeId);
        }

        foreach (var at in mine.ToList()) work.Push(at);
        while (work.Count > 0)
        {
            var at = work.Pop();
            foreach (var l in graph.Links.Where(l => l.ToNodeId == at && !isExec(l)))
            {
                if (events.Contains(l.FromNodeId) && l.FromNodeId != entry.Id)
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1683,
                        $"Event graph '{graph.Name}': a node run by event '{entry.EventTypeId}' reads data from another " +
                        "event node, whose payload does not exist when this event fires. Read this event's own pins.",
                        asset.AssetId, graph.Id, at));
                    continue;
                }
                if (mine.Add(l.FromNodeId)) work.Push(l.FromNodeId);
            }
        }
        return mine;
    }

    private static string FreshName(string baseName, int k, HashSet<string> taken)
    {
        var name = $"{baseName}_{k}";
        while (!taken.Add(name)) name += "_";
        return name;
    }
}
