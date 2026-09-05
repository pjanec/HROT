using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Transform;

namespace Hrot.Blueprints.Core.Compiler.Stages;

/// <summary>
/// BP-82 (pulled forward into BP-81's batch) — the four Stage 2 rails that make
/// <c>Stage2_5_ExpandMacros</c> safe to write without defensive checks on every splice rule.
///
/// <para>
/// ⭐ <b>These are preconditions, not companions.</b> Stage 2's <c>if (sink.HasErrors) return</c> gate
/// runs BEFORE expansion, so a graph failing any rule here never reaches the splice at all. That is
/// what lets <c>Splice</c> assume a resolvable, acyclic target. Without <c>BP1662</c> in particular, a
/// two-macro cycle would spin the fixpoint to its round cap and be reported as <c>BP1665</c>
/// <i>"exceeded 16 rounds"</i> — fail-loud but misattributed, sending the designer after depth when
/// the cause is a loop.
/// </para>
///
/// <list type="table">
///   <item><term>BP1660</term><description>Error — TargetGraphId does not resolve to a Macro graph (mirrors BP1651).</description></item>
///   <item><term>BP1661</term><description>Error — a macro with a transitively latent body called from a Function graph (design F1).</description></item>
///   <item><term>BP1662</term><description>Error — macro call cycle, direct or mutual (mirrors BP1654's three-colour DFS).</description></item>
///   <item><term>BP1663</term><description>Error — a macro declaring ≥ 2 exec-outs has a data output fed by an impure producer (design F2).</description></item>
/// </list>
/// </summary>
internal sealed class V_MacroCallRules : IValidator
{
    public void Validate(BlueprintAsset asset, ValidationContext ctx)
    {
        var graphById = asset.Graphs.ToDictionary(g => g.Id);
        var macroById = asset.Graphs.Where(g => g.Kind == GraphKind.Macro).ToDictionary(g => g.Id);

        // callEdges[callerGraphId] = resolved macro target ids. Built by pass 1, consumed by 2 and 3.
        var callEdges = new Dictionary<Guid, HashSet<Guid>>();

        // ── Pass 1: BP1660, and build the macro call graph ───────────────────────────────
        foreach (var callerGraph in asset.Graphs)
        {
            foreach (var node in callerGraph.Nodes.OfType<MacroCallNode>())
            {
                if (!Guid.TryParse(node.TargetGraphId, out var targetId)
                    || !macroById.TryGetValue(targetId, out var targetMacro))
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1660,
                        $"MacroCallNode (id={node.Id}) in graph '{callerGraph.Name}' references "
                        + $"TargetGraphId='{node.TargetGraphId}' which does not resolve to a "
                        + "GraphKind.Macro graph in this asset.",
                        asset.AssetId, callerGraph.Id, node.Id));
                    continue;
                }

                if (!callEdges.TryGetValue(callerGraph.Id, out var targets))
                    callEdges[callerGraph.Id] = targets = new HashSet<Guid>();
                targets.Add(targetMacro.Id);
            }
        }

        // ── Pass 2: BP1661 — design F1, the rule macros exist for, and the one expansion bypasses ──
        //
        // ⚠ BP1650 forbids a latent node inside a called FUNCTION graph, and it is a STAGE 2 rule.
        // Expansion runs at Stage 2.5, AFTER it. So a macro containing Delay/WaitFor*, called from a
        // function body, drops a latent node in after the only check that forbids it has already run
        // -- producing the Func_X-that-suspends breakage with no diagnostic at all. The check belongs
        // at the CALL SITE, where it names a node the designer actually placed.
        //
        // ⚠⚠ THE GATE IS "IS THIS GRAPH A FunctionCall TARGET", **NOT** "IS ITS KIND Function".
        //
        // Batch 30 shipped it as `Kind != Function → skip`, and that was WRONG in the most damaging
        // possible direction: a **tick graph is also GraphKind.Function** (InstanceEmitter picks the
        // tick graph from among the Function graphs), so the rule rejected latent macros in exactly
        // the place they are legal -- and BP-78 records that factoring out a reusable LATENT sequence
        // is the single capability macros exist to provide. The rail forbade the payoff.
        //
        // It survived Batch 30's whole suite because every negative test built a Function caller that
        // was never a call target, so "Function" and "synchronous method" looked like the same thing.
        // Only executing the payoff case (LatentMacroPayoffTests) separated them. What actually makes
        // a body synchronous is being INVOKED by a FunctionCallNode -- which is precisely how BP1650
        // words its own rule ("a function graph invoked by FunctionCall"), and mirroring that wording
        // is what fixes this.
        var functionCallTargets = new HashSet<Guid>(
            asset.Graphs
                .SelectMany(g => g.Nodes.OfType<FunctionCallNode>())
                .Select(fc => Guid.TryParse(fc.TargetGraphId, out var t) ? t : Guid.Empty)
                .Where(t => t != Guid.Empty));

        foreach (var kv in callEdges)
        {
            if (!graphById.TryGetValue(kv.Key, out var callerGraph)) continue;
            if (callerGraph.Kind != GraphKind.Function) continue;
            if (!functionCallTargets.Contains(callerGraph.Id)) continue;   // top-level graph ⇒ resumable

            foreach (var targetId in kv.Value)
            {
                var latent = MacroLatency.FindTransitivelyLatentNode(targetId, macroById, new HashSet<Guid>());
                if (latent is null) continue;

                var (latentMacro, latentNode) = latent.Value;
                foreach (var call in callerGraph.Nodes.OfType<MacroCallNode>()
                             .Where(n => Guid.TryParse(n.TargetGraphId, out var t) && t == targetId))
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1661,
                        $"MacroCallNode (id={call.Id}) in Function graph '{callerGraph.Name}' -- which "
                        + "is itself called via a FunctionCall -- calls macro "
                        + $"'{macroById[targetId].Name}', whose body transitively contains latent node "
                        + $"'{latentNode.GetType().Name}' (id={latentNode.Id}, in macro "
                        + $"'{latentMacro.Name}'). Expanding it would place a latent node inside a "
                        + "function body, which compiles to a synchronous method and cannot suspend. "
                        + "Call this macro from a Tick or event graph instead (where latent nodes are "
                        + "supported), or stop calling this graph via FunctionCall.",
                        asset.AssetId, callerGraph.Id, call.Id));
                }
            }
        }

        // ── Pass 3: BP1663 — design F2, the definite-assignment hazard ───────────────────
        foreach (var macro in macroById.Values)
            ValidateMultiExecOutPurity(asset, macro, ctx);

        // ── Pass 4: BP1666 — the exec-IN mirror of BP1663 (Q26-A3's recorded cost) ──────
        foreach (var callerGraph in asset.Graphs)
            ValidateMultiExecInPurity(asset, callerGraph, macroById, ctx);

        // ── Pass 5: BP1662 — cycle detection, three-colour DFS over macro-call edges ────
        if (callEdges.Count == 0) return;

        var colour        = new Dictionary<Guid, int>();   // 0 white, 1 grey, 2 black
        var parent        = new Dictionary<Guid, Guid>();
        var emittedCycles = new HashSet<string>();

        foreach (var g in asset.Graphs)
            colour[g.Id] = 0;

        foreach (var startId in colour.Keys.ToList())
        {
            if (colour[startId] != 0) continue;
            DfsVisit(startId, graphById, callEdges, colour, parent, asset.AssetId, ctx, emittedCycles);
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // BP1661 — transitive latency
    // ────────────────────────────────────────────────────────────────────────

    // ────────────────────────────────────────────────────────────────────────
    // BP1663 — the F2 purity rule
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Design F2. With <b>one</b> exec-out, everything downstream of a macro call is reachable only
    /// through the macro's body, so an impure producer's emitted local is definitely assigned wherever
    /// it is read. With <b>two</b>, that stops being true:
    ///
    /// <code>
    ///   goto L_Then1;
    ///   L_Then0: var __t5 = SomeImpureCall();   // assigned only on this path
    ///            goto L_Use;
    ///   L_Then1: goto L_Use;                    // __t5 never assigned
    ///   L_Use:   Log($"{__t5}");                // CS0165
    /// </code>
    ///
    /// The emitted TickCore body is flat (goto-based, no nested scopes), so <c>__t5</c> is in SCOPE at
    /// <c>L_Use</c> but not definitely ASSIGNED — a hard Roslyn error in generated code that names
    /// <c>__t5</c> and points at the CONSUMER, not at the impure producer on the other path. The
    /// designer has no route back to the cause.
    ///
    /// <para>
    /// ⚠ <b>Conservative on purpose.</b> An impure producer placed before the macro's internal branch
    /// dominates both exits and IS definitely assigned, so this rejects some safe graphs. A precise
    /// check would be dominance-based, but dominance exists only at Stage 5 — after expansion — where
    /// the diagnostic would name synthesized nodes. A false rejection is explainable; a CS0165 about
    /// <c>__t5</c> is not. Same attributability trade as BP1661.
    /// </para>
    ///
    /// <para>
    /// ⭐ The canonical case still passes: Unreal's <c>ForEachLoop</c> is exactly this shape — one
    /// exec-in, two exec-outs, plus data outputs — and its outputs are fed by pure array reads.
    /// </para>
    /// </summary>
    private static void ValidateMultiExecOutPurity(
        BlueprintAsset asset, Graph macro, ValidationContext ctx)
    {
        if (macro.ExecOutputs.Count < 2) return;
        if (macro.Outputs.Count == 0) return;

        var returnNode = macro.Nodes.OfType<ReturnNode>().FirstOrDefault();
        if (returnNode is null) return;

        var nodeById = macro.Nodes.ToDictionary(n => n.Id);

        foreach (var dataIn in returnNode.Pins.Where(p => !p.IsExec && p.Direction == "In"))
        {
            var impure = FindImpureProducer(macro, nodeById, returnNode.Id, dataIn.Id, new HashSet<Guid>());
            if (impure is null) continue;

            ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1663,
                $"Macro '{macro.Name}' declares {macro.ExecOutputs.Count} exec outputs, so its data "
                + $"output '{dataIn.Name}' must be fed by pure nodes only — but it is produced by "
                + $"'{impure.GetType().Name}' (id={impure.Id}), which sits on the exec chain and "
                + "therefore runs on only one of the paths. After expansion the generated C# would "
                + "read a local that is not assigned on every path (CS0165), reported against the "
                + "reader rather than this node. Feed the output from pure nodes, or declare a single "
                + "exec output.",
                asset.AssetId, macro.Id, impure.Id));
        }
    }

    /// <summary>
    /// Walks backwards over data links from one consumer pin, returning the first IMPURE producer.
    ///
    /// <para>
    /// ⭐ <b>Purity is read structurally — "does this node carry exec pins" — not from a hand-written
    /// kind list.</b> That is exactly the property the emitter relies on: a node on the exec chain is
    /// materialised once at its scheduling point and cached, while a node with no exec pins is
    /// re-emitted at each point of use and so is safe from any path. A kind list would be a second
    /// copy of that fact, free to drift from it.
    /// </para>
    ///
    /// <para>
    /// ⚠ The macro's <see cref="EventEntryNode"/> is a TERMINAL, not an impure producer, despite
    /// carrying an exec-out. Its data-outs are the macro's arguments: after splicing they resolve to
    /// whatever the HOST wired at the call site, and that producer sits in the host graph dominating
    /// the call — hence dominating both exec-outs, hence definitely assigned.
    /// </para>
    /// </summary>
    private static Node? FindImpureProducer(
        Graph graph, Dictionary<Guid, Node> nodeById,
        Guid consumerNodeId, Guid consumerPinId, HashSet<Guid> visited)
    {
        var link = graph.Links.FirstOrDefault(
            l => l.ToNodeId == consumerNodeId && l.ToPinId == consumerPinId);
        if (link is null) return null;

        if (!nodeById.TryGetValue(link.FromNodeId, out var producer)) return null;
        if (!visited.Add(producer.Id)) return null;

        if (producer is EventEntryNode) return null;

        if (producer.Pins.Any(p => p.IsExec)) return producer;

        foreach (var pin in producer.Pins.Where(p => !p.IsExec && p.Direction == "In"))
        {
            var deeper = FindImpureProducer(graph, nodeById, producer.Id, pin.Id, visited);
            if (deeper is not null) return deeper;
        }
        return null;
    }

    // ────────────────────────────────────────────────────────────────────────
    // BP1666 — the exec-IN purity mirror
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Q26-A3's recorded cost, and the mirror of <see cref="ValidateMultiExecOutPurity"/> — but
    /// ⚠⚠ <b>NOT a copy of it, because it walks a different graph.</b>
    ///
    /// <para>
    /// <c>BP1663</c> walks backwards from the <c>ReturnNode</c>'s data-in pins <b>inside the macro
    /// body</b>, because a macro's OUTPUTS are produced there. A macro's INPUTS are supplied
    /// <b>at the call site</b>, so this walks the <b>HOST</b> graph backwards from the
    /// <see cref="MacroCallNode"/>'s data-in pins. <see cref="FindImpureProducer"/> is reusable; the
    /// graph handed to it is the whole difference.
    /// </para>
    ///
    /// <para>
    /// ⇒ Two consequences that are not cosmetic. It is <b>per call site, not per declaration</b> — the
    /// same macro can be safe at one call site and unsafe at another — and the diagnostic
    /// <b>names the call node</b>, a node the designer placed, which is the same reasoning that fixed
    /// <c>BP1661</c>.
    /// </para>
    ///
    /// <para>
    /// 📐 <b>Gated on WIRED entries, not DECLARED ones.</b> A call site that declares two entries but
    /// wires only one has exactly one entering path, so the host-side producer dominates it and the
    /// generated local is definitely assigned — rejecting that would refuse code that cannot fail.
    /// Both gates cost the same at Stage 2, so precision is free.
    /// ⚠ This does not claim a single wired entry is dominance-safe in general: a producer sitting on
    /// a sibling branch that does not reach the call is the ordinary, pre-existing hazard every graph
    /// has, not a macro-specific one, and it is the same question only Stage 5 could answer.
    /// </para>
    ///
    /// <para>
    /// ⚠ Inherits F2's caveat verbatim: purity is <b>conservative</b> and rejects impure producers that
    /// genuinely dominate every entry. The precise check is dominance-based, but dominance exists only
    /// at Stage 5 — after expansion — where the diagnostic would name synthesized nodes nobody placed.
    /// A false rejection is explainable; a <c>CS0165</c> about <c>__t5</c> is not.
    /// </para>
    /// </summary>
    private static void ValidateMultiExecInPurity(
        BlueprintAsset asset, Graph host, Dictionary<Guid, Graph> macroById, ValidationContext ctx)
    {
        var calls = host.Nodes.OfType<MacroCallNode>().ToList();
        if (calls.Count == 0) return;

        var nodeById = host.Nodes.ToDictionary(n => n.Id);

        foreach (var call in calls)
        {
            if (!Guid.TryParse(call.TargetGraphId, out var targetId)
                || !macroById.TryGetValue(targetId, out var macro))
                continue;                       // BP1660's business

            // WIRED, not declared -- see the gate note above.
            int wiredEntries = call.Pins
                .Where(p => p.IsExec && p.Direction == "In")
                .Count(p => host.Links.Any(l => l.ToNodeId == call.Id && l.ToPinId == p.Id));

            if (wiredEntries < 2) continue;

            foreach (var dataIn in call.Pins.Where(p => !p.IsExec && p.Direction == "In"))
            {
                var impure = FindImpureProducer(host, nodeById, call.Id, dataIn.Id, new HashSet<Guid>());
                if (impure is null) continue;

                ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1666,
                    $"MacroCallNode (id={call.Id}) in graph '{host.Name}' calls macro "
                    + $"'{macro.Name}' through {wiredEntries} wired exec entries, so its data input "
                    + $"'{dataIn.Name}' must be fed by pure nodes only -- but it is produced by "
                    + $"'{impure.GetType().Name}' (id={impure.Id}), which sits on the exec chain and "
                    + "therefore runs on only one of the entering paths. After expansion the generated "
                    + "C# would read a local that is not assigned on every path (CS0165), reported "
                    + "against the reader rather than this call. Feed this input from pure nodes, or "
                    + "enter the macro through a single exec input.",
                    asset.AssetId, host.Id, call.Id));
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // BP1662 — cycle detection (mirrors V_FunctionGraphCallRules' DFS)
    // ────────────────────────────────────────────────────────────────────────

    private static void DfsVisit(
        Guid nodeId,
        Dictionary<Guid, Graph> graphById,
        Dictionary<Guid, HashSet<Guid>> callEdges,
        Dictionary<Guid, int> colour,
        Dictionary<Guid, Guid> parent,
        Guid assetId,
        ValidationContext ctx,
        HashSet<string> emittedCycles)
    {
        colour[nodeId] = 1;

        if (callEdges.TryGetValue(nodeId, out var neighbours))
        {
            foreach (var neighbourId in neighbours)
            {
                if (!colour.TryGetValue(neighbourId, out var nc)) continue;

                if (nc == 1)
                {
                    var cyclePath = BuildCyclePath(nodeId, neighbourId, parent, graphById);
                    var key = string.Join("→", cyclePath.OrderBy(s => s, StringComparer.Ordinal));
                    if (emittedCycles.Add(key))
                    {
                        ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1662,
                            $"Macro call cycle detected: {string.Join(" → ", cyclePath)}. A macro is "
                            + "spliced into its call site, so a cycle would expand forever.",
                            assetId));
                    }
                }
                else if (nc == 0)
                {
                    parent[neighbourId] = nodeId;
                    DfsVisit(neighbourId, graphById, callEdges, colour, parent,
                             assetId, ctx, emittedCycles);
                }
            }
        }

        colour[nodeId] = 2;
    }

    private static List<string> BuildCyclePath(
        Guid currentId, Guid cycleStartId,
        Dictionary<Guid, Guid> parent, Dictionary<Guid, Graph> graphById)
    {
        var path    = new List<string>();
        var visited = new HashSet<Guid>();
        var id      = currentId;

        while (id != cycleStartId && visited.Add(id))
        {
            path.Add(graphById.TryGetValue(id, out var g) ? g.Name : id.ToString());
            if (!parent.TryGetValue(id, out var p)) break;
            id = p;
        }

        path.Add(graphById.TryGetValue(cycleStartId, out var start) ? start.Name : cycleStartId.ToString());
        path.Reverse();
        path.Add(graphById.TryGetValue(cycleStartId, out var back) ? back.Name : cycleStartId.ToString());
        return path;
    }
}
