using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler.Diagnostics;

namespace Hrot.Blueprints.Core.Compiler.Transform;

/// <summary>
/// BP-81 / BP-76 — splices ONE <see cref="MacroCallNode"/>'s target body into its host graph and
/// deletes the call. The five splice rules of <c>Macro_Implementation_Design §3</c>, and the exact
/// inverse of <see cref="CollapseEmitter"/>.
///
/// <para>
/// ⭐⭐ <b>One algorithm, two callers — and the alternative had a receipt.</b>
/// <c>Stage2_5_ExpandMacros</c> is the compile-time pass (fixpoint, depth cap, wholesale mirror
/// rebuild) and calls straight into this; the editor's <c>Expand Node</c> calls it for a single
/// designer-selected node. Writing the rules twice is what <c>BP-69</c> recorded when
/// <c>ResolveCustomEventDecl</c> was duplicated across exactly this boundary and the two copies
/// drifted, and it is why Batch 30 moved the clipboard cloner <i>down</i> rather than copying it.
/// </para>
///
/// <para>
/// ⚠ <b>The two callers hand it assets at different lifecycle points</b> — the pass runs after
/// <c>Stage2_Validate</c>'s error gate on a rehydrated asset; the editor runs on the live authored
/// one. The rules turn out to be identical because <b>every one of them addresses pins through
/// <see cref="MacroCallPinView"/> and the boundary clones</b>, never through anything Stage 0/2 add.
/// The one real difference is what may be MISSING: the editor can be handed an unresolvable target
/// or a call whose pins were never materialised, where the pass is guaranteed neither by
/// <c>BP1660</c>. That is why the entry point <b>returns a result</b> rather than assuming, and why
/// diagnostics arrive through an optional delegate — <c>ValidationContext</c> is <c>internal</c>, so a
/// public method cannot take one, and an editor has none to give.
/// </para>
///
/// <para>
/// ⛔ <b>Expansion mints fresh ids.</b> Every caller wanting undo must snapshot the host and restore
/// it, never predict what this will produce — that prediction is precisely the corruption BP-76's
/// menu item shipped (it hardcoded the demo backend's <c>_exp1</c>/<c>_exp2</c> scheme in shared
/// production UI).
/// </para>
/// </summary>
public static class MacroExpander
{
    /// <summary>Why a single-call expansion was refused. Empty <see cref="Code"/> means it ran.</summary>
    public sealed record ExpandRefusal(string Code, string Message);

    /// <summary>Refusal codes, mirroring <c>CollapseAnalysis.RefusalCodes</c>' shape.</summary>
    public static class RefusalCodes
    {
        /// <summary>The call's <c>TargetGraphId</c> names no Macro graph in the asset.</summary>
        public const string UnresolvableTarget = "expand.unresolvable-target";

        /// <summary>The node is not a macro call. ⚠ A FunctionCall is a real call, not an inlinable body.</summary>
        public const string NotAMacroCall = "expand.not-a-macro-call";
    }

    /// <summary>
    /// Expands <paramref name="call"/> in place. Mutates <paramref name="host"/>'s node and link
    /// lists directly — the editor holds live references to that <see cref="Graph"/> object (BP-24's
    /// <c>Retarget</c> contract), so substituting a new instance would leave the canvas rendering a
    /// graph nothing writes to.
    /// </summary>
    /// <param name="report">
    /// Optional diagnostic sink. ⚠ <b>A delegate rather than the <c>ValidationContext</c> the pass
    /// holds</b>, because that type is <c>internal</c> and this method is <c>public</c> — and because
    /// the editor has no validation context to give. The pass forwards into its own context; the
    /// editor passes null and simply gets the splice.
    /// </param>
    public static void Expand(
        BlueprintAsset asset, Graph host, MacroCallNode call, Graph macro, Action<Diagnostic>? report)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));
        if (host is null) throw new ArgumentNullException(nameof(host));
        if (call is null) throw new ArgumentNullException(nameof(call));
        if (macro is null) throw new ArgumentNullException(nameof(macro));
        Splice(asset, host, call, macro, report);
    }

    /// <summary>
    /// Resolves <paramref name="call"/>'s target and expands it, or explains why not. The editor's
    /// entry point — <c>Stage2_5</c> has already been guaranteed a resolvable target by <c>BP1660</c>
    /// and calls <see cref="Expand"/> directly.
    /// </summary>
    public static ExpandRefusal? TryExpand(BlueprintAsset asset, Graph host, Node node)
    {
        if (asset is null) throw new ArgumentNullException(nameof(asset));
        if (host is null) throw new ArgumentNullException(nameof(host));
        if (node is null) throw new ArgumentNullException(nameof(node));

        if (node is not MacroCallNode call)
            return new ExpandRefusal(RefusalCodes.NotAMacroCall,
                "Only a macro call can be expanded. A function call is a real call at run time, not a "
                + "body that can be inlined here.");

        var macro = Guid.TryParse(call.TargetGraphId, out var id)
            ? asset.Graphs.FirstOrDefault(g => g.Kind == GraphKind.Macro && g.Id == id)
            : null;

        if (macro is null)
            return new ExpandRefusal(RefusalCodes.UnresolvableTarget,
                "This macro call points at no macro in this blueprint, so there is no body to splice "
                + "in. (The compiler reports the same thing as BP1660.)");

        Expand(asset, host, call, macro, report: null);
        return null;
    }

    // ────────────────────────────────────────────────────────────────────────
    // The five splice rules (Macro_Implementation_Design §3)
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Replaces <paramref name="call"/> with a fresh clone of <paramref name="macro"/>'s body, rewiring
    /// the four boundaries and deleting the call plus both boundary nodes.
    /// </summary>
    private static void Splice(
        BlueprintAsset asset, Graph host, MacroCallNode call, Graph macro, Action<Diagnostic>? report)
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

        SpliceExecIn(host, call, callPins, entryClone, report, asset, macro);
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
        Action<Diagnostic>? report, BlueprintAsset asset, Graph macro)
    {
        var entryExecOuts = entryClone is null
            ? new List<Pin>()
            : entryClone.Pins.Where(p => p.IsExec && p.Direction == "Out").ToList();

        // ⭐ BP-74 / Q26-A3: INDEXED, the exact mirror of rule 2. Entry k of the call re-ties to the
        // successor of entry k of the macro's boundary node, paired positionally with ExecInputs.
        bool anyIncoming = false;
        bool anyBodyLink = false;

        for (int k = 0; k < callPins.ExecInPins.Count; k++)
        {
            // Stale-asset guard, mirroring rule 2's `if (k >= returnExecIns.Count) break;`. An asset
            // saved against an older declaration list can carry more call pins than the macro now
            // declares; that is a shape mismatch to survive, not to throw on.
            if (k >= entryExecOuts.Count) break;

            var incoming = host.Links
                .Where(l => l.ToNodeId == call.Id && l.ToPinId == callPins.ExecInPins[k].Id)
                .ToList();
            if (incoming.Count > 0) anyIncoming = true;

            var firstBodyLink = host.Links.FirstOrDefault(
                l => l.FromNodeId == entryClone!.Id && l.FromPinId == entryExecOuts[k].Id);

            if (firstBodyLink is null)
            {
                // ⚠ NOT BP1667. This entry simply has no body wired behind it -- one unused door, not
                // an empty macro. Teardown drops the incoming links and that host path ends here.
                continue;
            }
            anyBodyLink = true;

            // Several X may feed one entry; in-degree >= 2 at the body block is fine, exactly as in
            // rule 2 -- ComputeMergePoints allocates one shared block for that shape.
            foreach (var link in incoming)
            {
                link.ToNodeId = firstBodyLink.ToNodeId;
                link.ToPinId  = firstBodyLink.ToPinId;
            }
        }

        // BP1667 is about a genuinely EMPTY body: no entry leads anywhere. With N entries that is
        // "no exec-out of the boundary node is wired", not "entry 0 is unwired".
        if (!anyBodyLink && anyIncoming)
        {
            report?.Invoke(Diagnostic.Warning(DiagnosticCodes.BP1667,
                $"Macro '{macro.Name}' has an empty body (no exec output of its entry node is "
                + $"wired), so the call in graph '{host.Name}' (node id={call.Id}) does nothing "
                + "and every one of its exec outputs is unreachable.",
                asset.AssetId, host.Id, call.Id));
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
    internal static void RebuildLinkedToIds(Graph graph)
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
            ExecInPins  = call.Pins.Where(p => p.IsExec && p.Direction == "In").ToList();
            ExecOutPins = call.Pins.Where(p => p.IsExec && p.Direction == "Out").ToList();
            DataInPins  = call.Pins.Where(p => !p.IsExec && p.Direction == "In").ToList();
            DataOutPins = call.Pins.Where(p => !p.IsExec && p.Direction == "Out").ToList();
        }

        /// <summary>BP-74/Q26-A3: N, one per target ExecInputs entry (or one implicit).</summary>
        public IReadOnlyList<Pin> ExecInPins { get; }
        public IReadOnlyList<Pin> ExecOutPins { get; }
        public IReadOnlyList<Pin> DataInPins { get; }
        public IReadOnlyList<Pin> DataOutPins { get; }
    }
}
