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
/// own, and the handler's <see cref="Graph.HandlerDebugIds"/> maps it back to the authored node, which Stage 5 applies
/// to debug identities only (E6) — so the debugger sees the node the designer drew.
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
        // ⭐ S7b — a Started task runs ALONGSIDE: lifted into a task fiber of its own (after the split, so each handler
        //   lifts its own copy; before the abort retarget, so the fiber's own While Running aborts are S7a's).
        graphs = LiftStartedTasks(asset, graphs, taken, ctx);
        // ⭐ S7a — after the split, so each handler's links point at its own task (a shared tail's clone included).
        asset.Graphs = graphs.Select(RetargetAborts).ToList();
        return asset;
    }

    /// <summary>
    /// ⭐⭐ S7b (<c>DESIGN_Unified_Behaviour_Run</c> "S7b design" B1) — a Behaviour Task whose <c>Started</c> pin is wired
    /// runs ALONGSIDE the graph that starts it: the task and its While Running / Succeeded / Failed chains (plus the pure
    /// data they read) are cloned into a compile-time TASK FIBER — an Event graph with no event, policy Restart (B2) —
    /// and in the starting graph the task becomes a <see cref="BehaviorTaskStartNode"/>. ⇒ every later stage sees one
    /// more ordinary fiber (S6), not a new kind of thing. A task fiber may itself start tasks (the list is a work-list).
    /// </summary>
    private static List<Graph> LiftStartedTasks(BlueprintAsset asset, List<Graph> graphs, HashSet<string> taken, ValidationContext ctx)
    {
        if (asset.Dispatch != BlueprintDispatchKind.Behavior) return graphs;
        var result = new List<Graph>(graphs.Count);
        var work = new Queue<Graph>(graphs);
        while (work.Count > 0)
        {
            var graph = work.Dequeue();
            var task = graph.Nodes.OfType<RunBehaviorNode>().FirstOrDefault(t => StartedWired(graph, t));
            if (task is null) { result.Add(graph); continue; }
            var (rest, fiber) = Lift(asset, graph, task, taken, ctx);
            work.Enqueue(rest);                 // another Started task in the same graph
            if (fiber is not null) work.Enqueue(fiber);
        }
        return result;
    }

    private static Pin? ExecPin(Node n, string name, string dir)
        => n.Pins.FirstOrDefault(p => p.IsExec && p.Direction == dir && p.Name == name);

    private static bool StartedWired(Graph graph, RunBehaviorNode task)
        => ExecPin(task, RunBehaviorNode.StartedPin, "Out") is { } p
        && graph.Links.Any(l => l.FromNodeId == task.Id && l.FromPinId == p.Id);

    private static (Graph Starting, Graph? Fiber) Lift(
        BlueprintAsset asset, Graph graph, RunBehaviorNode task, HashSet<string> taken, ValidationContext ctx)
    {
        var pinById = new Dictionary<Guid, Pin>();
        foreach (var n in graph.Nodes) foreach (var p in n.Pins) pinById[p.Id] = p;
        bool IsExec(Link l) => pinById.TryGetValue(l.FromPinId, out var fp) ? fp.IsExec
                             : pinById.TryGetValue(l.ToPinId, out var tp) && tp.IsExec;
        var nodeById = graph.Nodes.ToDictionary(n => n.Id);
        var started = ExecPin(task, RunBehaviorNode.StartedPin, "Out")!;
        var abortIn = ExecPin(task, RunBehaviorNode.AbortPin, "In");
        var startIn = task.Pins.FirstOrDefault(p => p.IsExec && p.Direction == "In" && p.Name != RunBehaviorNode.AbortPin);

        // ① the lifted set: the task + every exec node its While Running / Succeeded / Failed reach …
        var lifted = new HashSet<Guid> { task.Id };
        var stack = new Stack<Guid>();
        foreach (var l in graph.Links.Where(l => l.FromNodeId == task.Id && l.FromPinId != started.Id && IsExec(l)))
            stack.Push(l.ToNodeId);
        while (stack.Count > 0)
        {
            var at = stack.Pop();
            if (!lifted.Add(at)) continue;
            foreach (var l in graph.Links.Where(l => l.FromNodeId == at && IsExec(l))) stack.Push(l.ToNodeId);
        }
        // … ② and the PURE data they read (B4). An exec node's output or an event's payload does not exist in the
        //   task fiber: BP1687.
        bool failed = false;
        foreach (var at in lifted.ToList()) stack.Push(at);
        while (stack.Count > 0)
        {
            var at = stack.Pop();
            foreach (var l in graph.Links.Where(l => l.ToNodeId == at && !IsExec(l)))
            {
                if (lifted.Contains(l.FromNodeId) || !nodeById.TryGetValue(l.FromNodeId, out var src)) continue;
                if (src is EventEntryNode || src.Pins.Any(p => p.IsExec))
                {
                    failed = true;
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1687,
                        $"Behaviour Task '{task.BehaviorName}' runs alongside (Started is wired), so its While Running / " +
                        $"Succeeded / Failed chains run in a fiber of their own and cannot read the value '{src.GetType().Name}' " +
                        "computes in the graph that starts it. Store it in a Variable before Start and read the Variable.",
                        asset.AssetId, graph.Id, at));
                    continue;
                }
                lifted.Add(src.Id);
                stack.Push(src.Id);
            }
        }
        // ③ a graph-local is the starting invocation's own: a lifted chain sharing one with the starting graph is BP1687.
        var localIds = new HashSet<string>(graph.LocalVariables.Select(v => v.Id.ToString()), StringComparer.OrdinalIgnoreCase);
        string? LocalOf(Node n) => n switch
        {
            GetVariableNode g when localIds.Contains(g.VariableId) => g.VariableId,
            SetVariableNode s when localIds.Contains(s.VariableId) => s.VariableId,
            _ => null,
        };

        // the starting graph keeps what is still reachable without the lifted chains (B7)
        var name = FreshName(graph.Name + "_Task", 0, taken);
        var startNode = new BehaviorTaskStartNode
        {
            Id = task.Id, TaskNodeId = task.Id, BehaviorName = task.BehaviorName, FiberGraph = name,
        };
        if (startIn is not null) startNode.Pins.Add(startIn);
        startNode.Pins.Add(started);
        var keptLinks = graph.Links.Where(l => !(l.FromNodeId == task.Id && l.FromPinId != started.Id)).ToList();
        var restNodes = graph.Nodes.Select(n => n.Id == task.Id ? startNode : n).ToList();
        // ⚠ the probe ignores links INTO the task from lifted nodes (a While Running abort, a loop back to Start): they
        //   would pull the lifted chains back in through the start node.
        var probe = graph.WithNodesAndLinks(restNodes,
            keptLinks.Where(l => !(l.ToNodeId == task.Id && lifted.Contains(l.FromNodeId) && l.FromNodeId != task.Id)).ToList());
        var reachable = new HashSet<Guid>();
        if (V_GraphStructure.FindEntryNode(probe) is { } entryNode) Connected(probe, entryNode.Id, reachable);
        restNodes = restNodes.Where(n => n.Id == task.Id || !lifted.Contains(n.Id) || reachable.Contains(n.Id)).ToList();
        var restIds = new HashSet<Guid>(restNodes.Select(n => n.Id));

        foreach (var n in graph.Nodes.Where(n => lifted.Contains(n.Id) && n.Id != task.Id))
            if (LocalOf(n) is { } local && restNodes.Any(r => !lifted.Contains(r.Id) && LocalOf(r) == local))
            {
                failed = true;
                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1687,
                    $"Behaviour Task '{task.BehaviorName}' runs alongside (Started is wired): its chains run in a fiber of " +
                    "their own and do not share the starting graph's local variables. Use a Variable.",
                    asset.AssetId, graph.Id, n.Id));
            }

        // the task fiber — a clone of the lifted set, ids derived from the task (deterministic)
        var liftedNodes = graph.Nodes.Where(n => lifted.Contains(n.Id)).ToList();
        var internalLinks = graph.Links.Where(l => lifted.Contains(l.FromNodeId) && lifted.Contains(l.ToNodeId)
                                                 && !(l.FromNodeId == task.Id && l.FromPinId == started.Id)).ToList();
        var fragment = GraphFragmentCloner.Clone(liftedNodes, internalLinks,
            id => DeterministicIds.FromString($"task-fiber:{task.Id:N}:{id:N}"));
        var taskClone = fragment.NodeMap[task.Id];
        var entry = new EventEntryNode
        {
            Id = DeterministicIds.FromString($"task-fiber-entry:{task.Id:N}"),
            EventTypeId = "", Policy = EventFiberPolicy.Restart,
        };
        var entryOut = new Pin
        {
            Id = DeterministicIds.PinId(entry.Id, "Out", "Out"), Name = "Out", Direction = "Out", IsExec = true,
            TypeRef = new BlueprintTypeRef(),
        };
        entry.Pins.Add(entryOut);
        var fiberLinks = fragment.Links.ToList();
        if (startIn is not null && fragment.PinMap.TryGetValue(startIn.Id, out var startClone))
            fiberLinks.Add(new Link { FromNodeId = entry.Id, FromPinId = entryOut.Id, ToNodeId = taskClone, ToPinId = startClone });

        var fiber = graph.WithNodesAndLinks(new List<Node> { entry }.Concat(fragment.Nodes).ToList(), fiberLinks);
        fiber.Id   = DeterministicIds.FromString($"task-fiber-graph:{task.Id:N}");
        fiber.Name = name;
        fiber.Kind = GraphKind.Event;
        fiber.Inputs = new List<ParameterDecl>();
        fiber.Outputs = new List<ParameterDecl>();
        fiber.LiftedTaskSite = taskClone;
        Guid Authored(Guid id) => graph.HandlerDebugIds is { } h && h.TryGetValue(id, out var a) ? a : id;
        var debugIds = new Dictionary<Guid, Guid>();
        foreach (var kv in fragment.NodeMap) debugIds[kv.Value] = Authored(kv.Key);
        foreach (var kv in fragment.PinMap)  debugIds[kv.Value] = Authored(kv.Key);
        debugIds[entry.Id] = Authored(task.Id);
        debugIds[fiber.Id] = Authored(graph.Id);
        fiber.HandlerDebugIds = debugIds;

        // an Abort from the starting chain (B3) stops the task FIBER
        Node? stop = null;
        if (abortIn is not null)
        {
            var stopNode = new BehaviorTaskAbortNode
            {
                Id = DeterministicIds.FromString($"task-stop:{task.Id:N}"), TaskNodeId = taskClone, FiberGraph = name,
            };
            stopNode.Pins.Add(new Pin
            {
                Id = DeterministicIds.PinId(stopNode.Id, "In", "In"), Name = "In", Direction = "In", IsExec = true,
                TypeRef = new BlueprintTypeRef(),
            });
            stop = stopNode;
        }
        var restLinks = keptLinks.Where(l => restIds.Contains(l.FromNodeId) && restIds.Contains(l.ToNodeId))
            .Select(l => stop is not null && l.ToNodeId == task.Id && l.ToPinId == abortIn!.Id
                ? new Link { FromNodeId = l.FromNodeId, FromPinId = l.FromPinId, ToNodeId = stop.Id, ToPinId = stop.Pins[0].Id, Waypoints = l.Waypoints }
                : l)
            .ToList();
        if (stop is not null && restLinks.Any(l => l.ToNodeId == stop.Id)) restNodes.Add(stop);
        var rest = graph.WithNodesAndLinks(restNodes, restLinks);
        if (rest.HandlerDebugIds is not null || stop is not null)
        {
            var ids = new Dictionary<Guid, Guid>();
            if (graph.HandlerDebugIds is { } h) foreach (var kv in h) ids[kv.Key] = kv.Value;
            if (stop is not null) ids[stop.Id] = Authored(task.Id);
            rest.HandlerDebugIds = ids;
        }
        return (rest, failed ? null : fiber);
    }

    /// <summary>Stage 3's reachability (exec or data, either direction) — what its orphan pass would keep.</summary>
    private static void Connected(Graph graph, Guid start, HashSet<Guid> seen)
    {
        var stack = new Stack<Guid>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            var at = stack.Pop();
            if (!seen.Add(at)) continue;
            foreach (var l in graph.Links)
            {
                if (l.FromNodeId == at) stack.Push(l.ToNodeId);
                if (l.ToNodeId == at) stack.Push(l.FromNodeId);
            }
        }
    }

    /// <summary>
    /// ⭐ S7a (<c>DESIGN_Unified_Behaviour_Run</c> "S7 design" D4) — every exec link into a Behaviour Task's <c>Abort</c> pin
    /// is retargeted to a compile-time <see cref="BehaviorTaskAbortNode"/> (one per task), because the scheduler walks
    /// node to node: entering the task node through Abort would read as entering it through Start. ⭐ A graph with no
    /// Abort link is returned as the same object.
    /// </summary>
    private static Graph RetargetAborts(Graph graph)
    {
        var abortPins = new Dictionary<Guid, RunBehaviorNode>();
        foreach (var task in graph.Nodes.OfType<RunBehaviorNode>())
            foreach (var pin in task.Pins.Where(p => p.IsExec && p.Direction == "In" && p.Name == RunBehaviorNode.AbortPin))
                abortPins[pin.Id] = task;
        if (abortPins.Count == 0 || !graph.Links.Any(l => abortPins.ContainsKey(l.ToPinId))) return graph;

        var nodes = new List<Node>(graph.Nodes);
        var abortNodeOf = new Dictionary<Guid, BehaviorTaskAbortNode>();
        var links = graph.Links.Select(l =>
        {
            if (!abortPins.TryGetValue(l.ToPinId, out var task) || l.ToNodeId != task.Id) return l;
            if (!abortNodeOf.TryGetValue(task.Id, out var abortNode))
            {
                abortNode = new BehaviorTaskAbortNode
                {
                    Id = DeterministicIds.FromString($"task-abort:{task.Id:N}"), TaskNodeId = task.Id,
                    OriginNodeId = task.OriginNodeId, OriginGraphId = task.OriginGraphId,
                };
                abortNode.Pins.Add(new Pin
                {
                    Id = DeterministicIds.PinId(abortNode.Id, "In", "In"), Name = "In", Direction = "In", IsExec = true,
                    TypeRef = new BlueprintTypeRef(),
                });
                abortNodeOf[task.Id] = abortNode;
                nodes.Add(abortNode);
            }
            return new Link
            {
                FromNodeId = l.FromNodeId, FromPinId = l.FromPinId,
                ToNodeId = abortNode.Id, ToPinId = abortNode.Pins[0].Id, Waypoints = l.Waypoints,
            };
        }).ToList();

        var copy = graph.WithNodesAndLinks(nodes, links);
        // ⭐ E6 — the debugger names the TASK when an Abort fires (the abort node is not on the canvas).
        var ids = new Dictionary<Guid, Guid>();
        if (graph.HandlerDebugIds is { } h) foreach (var kv in h) ids[kv.Key] = kv.Value;
        foreach (var kv in abortNodeOf)   // netstandard2.0: no KeyValuePair deconstruction
            ids[kv.Value.Id] = graph.HandlerDebugIds is { } hm && hm.TryGetValue(kv.Key, out var authored) ? authored : kv.Key;
        copy.HandlerDebugIds = ids;
        return copy;
    }

    /// <summary>One event node: the graph is already one handler; only a node-carried payload becomes its inputs.
    /// ⭐ A graph whose payload is still on the graph is passed through as the same object ⇒ no golden can move.</summary>
    private static Graph WithNodePayload(Graph graph, EventEntryNode entry)
    {
        if (entry.Fields is null) return graph;
        var copy = graph.WithNodesAndLinks(new List<Node>(graph.Nodes), new List<Link>(graph.Links));
        copy.Inputs = HandlerInputs(graph, entry, copy.Links);
        return copy;
    }

    /// <summary>
    /// A handler's inputs: the node's payload fields, then — ⭐ CE-2014 (T-6) — the whole event when its pin is wired.
    /// ⇒ an unwired whole-event pin changes nothing in the generated code.
    /// </summary>
    private static List<ParameterDecl> HandlerInputs(Graph graph, EventEntryNode entry, IEnumerable<Link> links)
    {
        var inputs = EventPayload.FieldsOf(graph, entry).ToList();
        var whole = EventPayload.WholeEventPinName(entry);
        var pin = whole is null ? null
            : entry.Pins.FirstOrDefault(p => !p.IsExec && p.Direction == "Out" && p.Name == whole);
        if (pin is not null && links.Any(l => l.FromNodeId == entry.Id && l.FromPinId == pin.Id))
            inputs.Add(new ParameterDecl
            {
                Id   = DeterministicIds.FromString($"event-whole:{entry.Id:N}"),
                Name = EventPayload.WholeEventInput,
                Type = new BlueprintTypeRef { TypeId = EventPayload.WholeEventTypeId(entry) },
            });
        return inputs;
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
            // ⚠ A clone keeps the ORIGINAL's Origin* (a macro back-reference, if any) — its back-reference to the authored
            //   node is the handler's debug-id map instead (E6), which Stage 5 applies to debug identities only.
            var cloneOf = new Dictionary<Guid, Node>();
            for (int i = 0; i < shared.Count; i++)
                cloneOf[shared[i].Id] = fragment.Nodes[i];
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
            // ⭐ CE-2017 (E6) — what the debugger must see instead of the clones' and the handler's own ids.
            var debugIds = new Dictionary<Guid, Guid>();
            foreach (var kv in fragment.NodeMap) debugIds[kv.Value] = kv.Key;
            foreach (var kv in fragment.PinMap)  debugIds[kv.Value] = kv.Key;
            if (handler.Id != graph.Id) debugIds[handler.Id] = graph.Id;
            handler.HandlerDebugIds = debugIds.Count > 0 ? debugIds : null;
            handler.Inputs = HandlerInputs(graph, entry, links);
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
