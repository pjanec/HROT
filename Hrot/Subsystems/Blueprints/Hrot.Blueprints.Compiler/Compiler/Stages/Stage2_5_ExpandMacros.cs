using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Diagnostics;
using Hrot.Blueprints.Core.Compiler.Transform;

namespace Hrot.Blueprints.Core.Compiler.Stages;

/// <summary>
/// BP-81 / Q25-B1 — splices every <see cref="MacroCallNode"/>'s target body into its host graph, then
/// deletes the call. After this stage no macro call exists and no later stage needs to know macros
/// were ever there.
///
/// <para>
/// <b>Where it runs, and why that placement is load-bearing.</b> Between <c>Stage2_Validate</c> and
/// <c>Stage3_Normalize</c> — specifically <b>after</b> Stage 2's <c>if (sink.HasErrors) return</c>
/// gate. Expansion therefore never runs on a graph Stage 2 rejected, which is what lets
/// <see cref="Splice"/> ASSUME a resolvable, acyclic target (<c>BP1660</c>/<c>BP1662</c>) instead of
/// defending against null on every rule.
/// </para>
///
/// <para>
/// <b>Fixpoint, not recursion.</b> A macro body containing another macro call is spliced in whole;
/// the clone of the inner call then sits in the host and is picked up next round. So nesting falls
/// out for free and <b>the round counter IS the depth cap</b> — there is no second mechanism.
/// </para>
///
/// <para>
/// ⚠ Only non-<see cref="GraphKind.Macro"/> graphs are expanded. Macro declarations are left exactly
/// as authored: they are never compilation targets (Stage 5 skips them), rewriting them would mutate
/// a declaration shared by every call site, and nesting resolves through the host anyway.
/// </para>
/// </summary>
internal static class Stage2_5_ExpandMacros
{
    /// <summary>
    /// Rounds before <c>BP1665</c>. A genuine macro cycle is caught upstream by <c>BP1662</c>, so this
    /// only ever trips on pathological nesting depth — which is exactly what the message says, and why
    /// pulling BP1662 forward mattered: without it this error would blame depth for a loop.
    /// </summary>
    private const int MaxRounds = 16;

    public static BlueprintAsset Run(BlueprintAsset asset, ValidationContext ctx)
    {
        // Cheap exit for the overwhelmingly common case: no macro call anywhere in the asset.
        if (!asset.Graphs.Any(g => g.Kind != GraphKind.Macro && g.Nodes.OfType<MacroCallNode>().Any()))
            return asset;

        var macroById = asset.Graphs
            .Where(g => g.Kind == GraphKind.Macro)
            .ToDictionary(g => g.Id);

        foreach (var host in asset.Graphs.Where(g => g.Kind != GraphKind.Macro))
        {
            int round = 0;
            while (true)
            {
                var calls = host.Nodes.OfType<MacroCallNode>().ToList();
                if (calls.Count == 0) break;

                if (++round > MaxRounds)
                {
                    ctx.Diagnostics.Add(Diagnostic.Error(DiagnosticCodes.BP1665,
                        $"Macro expansion in graph '{host.Name}' exceeded {MaxRounds} rounds. Macro "
                        + "bodies are spliced one nesting level per round, so this means macros are "
                        + "nested more than "
                        + $"{MaxRounds} deep. (A macro call CYCLE is reported separately as BP1662 "
                        + "and is not the cause here.)",
                        asset.AssetId, host.Id));
                    break;
                }

                foreach (var call in calls)
                {
                    // BP1660 guarantees this resolves; if a caller ran this stage without Stage 2's
                    // gate, skip rather than throw -- an unexpanded call is still caught by BP1668.
                    if (!Guid.TryParse(call.TargetGraphId, out var targetId)
                        || !macroById.TryGetValue(targetId, out var macro))
                        continue;

                    Splice(asset, host, call, macro, ctx);
                }
            }
        }

        // ⭐ The LinkedToIds mirror is rebuilt WHOLESALE, once, rather than patched at each rewire.
        // It is a denormalised copy of the link list, and the class of defect this programme keeps
        // finding is a denormalised copy that no test compares against its source. A full rebuild is
        // O(links), cannot drift, and cannot be got subtly wrong by a rule that forgot one endpoint.
        foreach (var host in asset.Graphs.Where(g => g.Kind != GraphKind.Macro))
            RebuildLinkedToIds(host);

        return asset;
    }

    // ────────────────────────────────────────────────────────────────────────
    // The five splice rules (Macro_Implementation_Design §3)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces <paramref name="call"/> with a fresh clone of <paramref name="macro"/>'s body, rewiring
    /// the four boundaries and deleting the call plus both boundary nodes.
    /// </summary>
    private static void Splice(
        BlueprintAsset asset, Graph host, MacroCallNode call, Graph macro, ValidationContext ctx)
    {
        var fragment = GraphFragmentCloner.Clone(macro.Nodes, macro.Links);

        // Provenance: every synthesized node remembers the authored node it came from, so BP-83 can
        // arm a breakpoint at EVERY expansion site. Set here, on the clone, before anything else can
        // observe the fragment.
        var authoredById = macro.Nodes.ToDictionary(n => n.Id);
        foreach (var kv in fragment.NodeMap)
        {
            var clone = fragment.Nodes.First(n => n.Id == kv.Value);
            // A clone of a clone (nested expansion) keeps the ORIGINAL authored id, not the
            // intermediate one -- provenance should name something a designer can open.
            clone.OriginNodeId  = authoredById[kv.Key].OriginNodeId  ?? kv.Key;
            clone.OriginGraphId = authoredById[kv.Key].OriginGraphId ?? macro.Id;
        }

        var entryClone  = FindClone<EventEntryNode>(macro, fragment);
        var returnClone = FindClone<ReturnNode>(macro, fragment);

        // Add the body first; the rules below rewrite links in `host` and in `fragment` uniformly
        // once both live in the same list.
        host.Nodes.AddRange(fragment.Nodes);
        host.Links.AddRange(fragment.Links);

        var callPins = new MacroCallPinView(call, macro);

        SpliceExecIn(host, call, callPins, entryClone, ctx, asset, macro);
        SpliceExecOuts(host, call, callPins, returnClone, macro, fragment);
        SpliceDataIns(host, call, callPins, entryClone, macro, fragment);
        SpliceDataOuts(host, call, callPins, returnClone, macro, fragment);

        // Rule 5 — teardown. Drop the call and both boundary nodes, and every link still touching
        // them (the rules above rewired the ones that carry meaning; whatever is left is by
        // definition dangling).
        var dead = new HashSet<Guid> { call.Id };
        if (entryClone  is not null) dead.Add(entryClone.Id);
        if (returnClone is not null) dead.Add(returnClone.Id);

        host.Nodes.RemoveAll(n => dead.Contains(n.Id));
        host.Links.RemoveAll(l => dead.Contains(l.FromNodeId) || dead.Contains(l.ToNodeId));
    }

    /// <summary>
    /// Rule 1 — <c>X.out → C.execIn</c> becomes <c>X.out → succ(In′.execOut)</c>.
    /// An unwired <c>In′.execOut</c> means the macro body is empty: the call is a no-op, every
    /// exec-out continuation is unreachable, and that is worth saying out loud (<c>BP1667</c>).
    /// </summary>
    private static void SpliceExecIn(
        Graph host, MacroCallNode call, MacroCallPinView callPins, EventEntryNode? entryClone,
        ValidationContext ctx, BlueprintAsset asset, Graph macro)
    {
        var incoming = callPins.ExecInPin is null
            ? new List<Link>()
            : host.Links.Where(l => l.ToNodeId == call.Id && l.ToPinId == callPins.ExecInPin.Id).ToList();

        var entryExecOut = entryClone?.Pins.FirstOrDefault(p => p.IsExec && p.Direction == "Out");
        var firstBodyLink = entryExecOut is null
            ? null
            : host.Links.FirstOrDefault(
                l => l.FromNodeId == entryClone!.Id && l.FromPinId == entryExecOut.Id);

        if (firstBodyLink is null)
        {
            if (incoming.Count > 0)
            {
                ctx.Diagnostics.Add(Diagnostic.Warning(DiagnosticCodes.BP1667,
                    $"Macro '{macro.Name}' has an empty body (its entry node's exec output is not "
                    + $"wired), so the call in graph '{host.Name}' (node id={call.Id}) does nothing "
                    + "and every one of its exec outputs is unreachable.",
                    asset.AssetId, host.Id, call.Id));
            }
            return;   // teardown drops `incoming`; the host's exec chain simply ends here
        }

        foreach (var link in incoming)
        {
            link.ToNodeId = firstBodyLink.ToNodeId;
            link.ToPinId  = firstBodyLink.ToPinId;
        }
    }

    /// <summary>
    /// Rule 2 — <c>Z.out → Out′.execIn[k]</c> plus <c>C.execOut[k] → Y.in</c> become <c>Z.out → Y.in</c>.
    /// <para>
    /// Several <c>Z</c> may feed one <c>execIn[k]</c>, and an in-degree ≥ 2 at <c>Y</c> is fine:
    /// <c>ComputeMergePoints</c> (<c>Stage5_Schedule:4624</c>) allocates one shared block for exactly
    /// this shape.
    /// </para>
    /// </summary>
    private static void SpliceExecOuts(
        Graph host, MacroCallNode call, MacroCallPinView callPins, ReturnNode? returnClone,
        Graph macro, ClonedFragment fragment)
    {
        if (returnClone is null) return;

        var returnExecIns = returnClone.Pins.Where(p => p.IsExec && p.Direction == "In").ToList();

        for (int k = 0; k < callPins.ExecOutPins.Count; k++)
        {
            if (k >= returnExecIns.Count) break;   // stale asset: fewer exec-ins than call exec-outs

            var continuation = host.Links.FirstOrDefault(
                l => l.FromNodeId == call.Id && l.FromPinId == callPins.ExecOutPins[k].Id);

            var feeders = host.Links
                .Where(l => l.ToNodeId == returnClone.Id && l.ToPinId == returnExecIns[k].Id)
                .ToList();

            if (continuation is null)
            {
                // Nothing wired after this exec-out: the body path simply ends. Teardown removes the
                // feeder links along with Out'.
                continue;
            }

            foreach (var feeder in feeders)
            {
                feeder.ToNodeId = continuation.ToNodeId;
                feeder.ToPinId  = continuation.ToPinId;
            }
        }
    }

    /// <summary>
    /// Rule 3 — consumers of <c>In′.dataOut[p]</c> re-tie to <c>pred(C.dataIn[p])</c>.
    /// <para>
    /// When <c>C.dataIn[p]</c> is unwired the argument has no producer, so the body's readers are given
    /// a synthesized <see cref="LiteralNode"/> built from the call pin's inline default, else the macro
    /// input's <see cref="ParameterDecl.DefaultValueJson"/>. With neither, the consumer pin is left
    /// unwired on purpose: <c>Stage5</c> already reports that as <c>BP4001</c> with a typed
    /// <c>default(T)</c>, and inventing a second diagnostic for the same condition would just split the
    /// designer's attention.
    /// </para>
    /// </summary>
    private static void SpliceDataIns(
        Graph host, MacroCallNode call, MacroCallPinView callPins, EventEntryNode? entryClone,
        Graph macro, ClonedFragment fragment)
    {
        if (entryClone is null) return;

        var entryDataOuts = entryClone.Pins.Where(p => !p.IsExec && p.Direction == "Out").ToList();

        for (int p = 0; p < entryDataOuts.Count; p++)
        {
            var consumers = host.Links
                .Where(l => l.FromNodeId == entryClone.Id && l.FromPinId == entryDataOuts[p].Id)
                .ToList();
            if (consumers.Count == 0) continue;

            var callPin  = p < callPins.DataInPins.Count ? callPins.DataInPins[p] : null;
            var producer = callPin is null
                ? null
                : host.Links.FirstOrDefault(l => l.ToNodeId == call.Id && l.ToPinId == callPin.Id);

            if (producer is not null)
            {
                foreach (var consumer in consumers)
                {
                    consumer.FromNodeId = producer.FromNodeId;
                    consumer.FromPinId  = producer.FromPinId;
                }
                continue;
            }

            var literal = TrySynthesizeArgumentLiteral(call, callPin, macro, p, entryDataOuts[p]);
            if (literal is null)
            {
                // No producer and no default: drop the wires and let BP4001 report the unwired pin.
                foreach (var consumer in consumers) host.Links.Remove(consumer);
                continue;
            }

            // OriginGraphId is stamped here rather than in the helper: the synthesized literal
            // stands in for an argument at the CALL SITE, so its origin graph is the host.
            literal.Node.OriginGraphId = host.Id;
            host.Nodes.Add(literal.Node);
            foreach (var consumer in consumers)
            {
                consumer.FromNodeId = literal.Node.Id;
                consumer.FromPinId  = literal.OutPin.Id;
            }
        }
    }

    /// <summary>
    /// Rule 4 — consumers of <c>C.dataOut[q]</c> re-tie to <c>pred(Out′.dataIn[q])</c>.
    /// An unwired <c>Out′.dataIn[q]</c> is already <c>BP1655</c>'s business (Stage 2, which runs before
    /// this pass), so it is reused verbatim rather than re-reported here.
    /// </summary>
    private static void SpliceDataOuts(
        Graph host, MacroCallNode call, MacroCallPinView callPins, ReturnNode? returnClone,
        Graph macro, ClonedFragment fragment)
    {
        if (returnClone is null) return;

        var returnDataIns = returnClone.Pins.Where(p => !p.IsExec && p.Direction == "In").ToList();

        for (int q = 0; q < callPins.DataOutPins.Count; q++)
        {
            var consumers = host.Links
                .Where(l => l.FromNodeId == call.Id && l.FromPinId == callPins.DataOutPins[q].Id)
                .ToList();
            if (consumers.Count == 0) continue;

            var source = q < returnDataIns.Count
                ? host.Links.FirstOrDefault(
                    l => l.ToNodeId == returnClone.Id && l.ToPinId == returnDataIns[q].Id)
                : null;

            if (source is null)
            {
                foreach (var consumer in consumers) host.Links.Remove(consumer);
                continue;
            }

            foreach (var consumer in consumers)
            {
                consumer.FromNodeId = source.FromNodeId;
                consumer.FromPinId  = source.FromPinId;
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────────────────

    private sealed class SynthesizedLiteral
    {
        public SynthesizedLiteral(LiteralNode node, Pin outPin) { Node = node; OutPin = outPin; }
        public LiteralNode Node { get; }
        public Pin OutPin { get; }
    }

    private static SynthesizedLiteral? TrySynthesizeArgumentLiteral(
        MacroCallNode call, Pin? callPin, Graph macro, int index, Pin entryDataOutPin)
    {
        string? raw = null;
        if (callPin is not null)
        {
            if (!string.IsNullOrEmpty(callPin.DefaultValue))
                raw = callPin.DefaultValue;
            else if (call.PinDefaults != null
                     && call.PinDefaults.TryGetValue(callPin.Name, out var bagged)
                     && !string.IsNullOrEmpty(bagged))
                raw = bagged;
        }
        if (raw is null && index < macro.Inputs.Count)
            raw = macro.Inputs[index].DefaultValueJson;

        if (string.IsNullOrEmpty(raw)) return null;

        var typeRef = callPin?.TypeRef ?? entryDataOutPin.TypeRef ?? new BlueprintTypeRef();
        var outPin  = new Pin
        {
            Id        = Guid.NewGuid(),
            Name      = "Value",
            Direction = "Out",
            IsExec    = false,
            TypeRef   = typeRef,
        };
        var node = new LiteralNode
        {
            Id           = Guid.NewGuid(),
            TypeId       = typeRef.TypeId ?? "",
            ValueJson    = raw!,
            Pins         = new List<Pin> { outPin },
            OriginNodeId  = call.Id,   // attributable to the call site that defaulted the argument
        };
        return new SynthesizedLiteral(node, outPin);
    }

    private static T? FindClone<T>(Graph macro, ClonedFragment fragment) where T : Node
    {
        var authored = macro.Nodes.OfType<T>().FirstOrDefault();
        if (authored is null) return null;
        if (!fragment.NodeMap.TryGetValue(authored.Id, out var cloneId)) return null;
        return fragment.Nodes.OfType<T>().FirstOrDefault(n => n.Id == cloneId);
    }

    /// <summary>
    /// Recomputes <see cref="Pin.LinkedToIds"/> for every pin in the graph from the link list — the
    /// single source of truth. See the note at the call site for why this is a wholesale rebuild.
    /// </summary>
    private static void RebuildLinkedToIds(Graph graph)
    {
        var byPin = new Dictionary<Guid, List<Guid>>();
        foreach (var link in graph.Links)
        {
            if (!byPin.TryGetValue(link.FromPinId, out var fromList))
                byPin[link.FromPinId] = fromList = new List<Guid>();
            fromList.Add(link.ToPinId);

            if (!byPin.TryGetValue(link.ToPinId, out var toList))
                byPin[link.ToPinId] = toList = new List<Guid>();
            toList.Add(link.FromPinId);
        }

        foreach (var node in graph.Nodes)
            foreach (var pin in node.Pins)
                pin.LinkedToIds = byPin.TryGetValue(pin.Id, out var ids)
                    ? ids.Distinct().ToList()
                    : new List<Guid>();
    }

    /// <summary>
    /// The call node's pins, split into the four groups the splice rules address and ordered to match
    /// the target's declarations — the same positional pairing
    /// <c>NodePinSchema.MacroCallPins</c>/<c>Stage0_Rehydrate.EnrichMacroCallPins</c> project.
    /// </summary>
    private sealed class MacroCallPinView
    {
        public MacroCallPinView(MacroCallNode call, Graph macro)
        {
            ExecInPin   = call.Pins.FirstOrDefault(p => p.IsExec && p.Direction == "In");
            ExecOutPins = call.Pins.Where(p => p.IsExec && p.Direction == "Out").ToList();
            DataInPins  = call.Pins.Where(p => !p.IsExec && p.Direction == "In").ToList();
            DataOutPins = call.Pins.Where(p => !p.IsExec && p.Direction == "Out").ToList();
        }

        public Pin? ExecInPin { get; }
        public IReadOnlyList<Pin> ExecOutPins { get; }
        public IReadOnlyList<Pin> DataInPins { get; }
        public IReadOnlyList<Pin> DataOutPins { get; }
    }
}
