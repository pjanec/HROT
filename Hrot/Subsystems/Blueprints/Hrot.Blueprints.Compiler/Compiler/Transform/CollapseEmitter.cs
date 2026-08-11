using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Core.Compiler.Transform;

/// <summary>The graph edit a collapse performs, as data — so the caller can apply and undo it.</summary>
public sealed class CollapseEdit
{
    public CollapseEdit(Graph extracted, Node callNode, Graph rewrittenHost)
    {
        Extracted = extracted; CallNode = callNode; RewrittenHost = rewrittenHost;
    }

    /// <summary>The new <see cref="GraphKind.Macro"/> or <see cref="GraphKind.Function"/> graph.</summary>
    public Graph Extracted { get; }

    /// <summary>The <see cref="MacroCallNode"/> or <see cref="FunctionCallNode"/> standing in its place.</summary>
    public Node CallNode { get; }

    /// <summary>The host with the selection removed and the call spliced in.</summary>
    public Graph RewrittenHost { get; }
}

/// <summary>
/// BP-74 / Q26 — turns a <see cref="CollapsePlan"/> into the two graphs it implies.
///
/// <para>
/// ⭐ <b>The exact inverse of <c>Stage2_5_ExpandMacros</c>, and deliberately built to be checkable as
/// one.</b> Q26-E1 makes <i>collapse → expand → structurally equivalent</i> a test-locked invariant,
/// which is the strongest evidence available here because expansion is already proven by execution.
/// </para>
///
/// <para>
/// ⚠ Both targets share the analysis and the lift; only the declaration shape and the call node
/// differ. Macro gets <c>ExecInputs</c>/<c>ExecOutputs</c> (Q26-A3); Function gets neither, because
/// the analysis has already refused anything a Function cannot express.
/// </para>
/// </summary>
public static class CollapseEmitter
{
    public static CollapseEdit Emit(
        Graph host, CollapsePlan plan, CollapseTarget target, string extractedName)
    {
        if (host is null) throw new ArgumentNullException(nameof(host));
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        var selected = new HashSet<Guid>(plan.Selection);
        var byId     = host.Nodes.ToDictionary(n => n.Id);

        var lifted = host.Nodes.Where(n => selected.Contains(n.Id)).ToList();
        var internalLinks = host.Links
            .Where(l => selected.Contains(l.FromNodeId) && selected.Contains(l.ToNodeId))
            .ToList();

        // ⭐ Reuse GraphFragmentCloner: it returns NodeMap/PinMap, which is exactly what re-tying the
        // boundary needs -- the same reason macro expansion needed them. Cloning (rather than moving)
        // keeps the host's own nodes untouched until the caller swaps graphs, which is what makes undo
        // a pure substitution.
        var fragment = GraphFragmentCloner.Clone(lifted, internalLinks);

        var extracted = new Graph
        {
            Id    = Guid.NewGuid(),
            Name  = extractedName,
            Kind  = target == CollapseTarget.Macro ? GraphKind.Macro : GraphKind.Function,
            Nodes = fragment.Nodes.ToList(),
            Links = fragment.Links.ToList(),
        };

        // ── the extracted graph's boundary nodes ─────────────────────────────────────────
        var entryNode = new EventEntryNode { Id = Guid.NewGuid() };
        var retNode   = new ReturnNode     { Id = Guid.NewGuid() };

        // Exec entries: one exec-OUT pin on the entry node per entry (Q26-A3). Macro declares them;
        // a Function has exactly one by construction (the analysis refused more).
        var entryExecPins = new List<Pin>();
        foreach (var e in plan.Entries)
        {
            var pin = MakePin(e.Name, "Out", isExec: true);
            entryExecPins.Add(pin);
            if (target == CollapseTarget.Macro)
                extracted.ExecInputs.Add(new ExecInDecl { Id = Guid.NewGuid(), Name = e.Name });
        }
        if (entryExecPins.Count == 0) entryExecPins.Add(MakePin("Out", "Out", isExec: true));
        entryNode.Pins.AddRange(entryExecPins);

        // Data inputs: one data-OUT pin on the entry node per declared input.
        var inputPins = new List<Pin>();
        foreach (var i in plan.Inputs)
        {
            var pin = MakePin(i.Name, "Out", isExec: false, i.Type);
            inputPins.Add(pin);
            entryNode.Pins.Add(pin);
            extracted.Inputs.Add(new ParameterDecl
            {
                Id = Guid.NewGuid(), Name = i.Name, Type = i.Type,
            });
        }

        // Exec exits: one exec-IN pin on the return node per exit.
        var exitExecPins = new List<Pin>();
        foreach (var x in plan.Exits)
        {
            var pin = MakePin(x.Name, "In", isExec: true);
            exitExecPins.Add(pin);
            if (target == CollapseTarget.Macro)
                extracted.ExecOutputs.Add(new ExecOutDecl { Id = Guid.NewGuid(), Name = x.Name });
        }
        if (exitExecPins.Count == 0) exitExecPins.Add(MakePin("In", "In", isExec: true));
        retNode.Pins.AddRange(exitExecPins);

        // Data outputs: one data-IN pin on the return node per declared output.
        var outputPins = new List<Pin>();
        foreach (var o in plan.Outputs)
        {
            var pin = MakePin(o.Name, "In", isExec: false, o.Type);
            outputPins.Add(pin);
            retNode.Pins.Add(pin);
            extracted.Outputs.Add(new ParameterDecl
            {
                Id = Guid.NewGuid(), Name = o.Name, Type = o.Type,
            });
        }

        extracted.Nodes.Add(entryNode);
        extracted.Nodes.Add(retNode);

        // ── wire the extracted graph's boundary to the lifted body ───────────────────────
        for (int k = 0; k < plan.Entries.Count; k++)
        {
            var e = plan.Entries[k];
            extracted.Links.Add(new Link
            {
                FromNodeId = entryNode.Id, FromPinId = entryExecPins[k].Id,
                ToNodeId   = fragment.NodeMap[e.InteriorNodeId],
                ToPinId    = fragment.PinMap[e.InteriorPinId],
            });
        }
        for (int k = 0; k < plan.Exits.Count; k++)
        {
            var x = plan.Exits[k];
            extracted.Links.Add(new Link
            {
                FromNodeId = fragment.NodeMap[x.InteriorNodeId],
                FromPinId  = fragment.PinMap[x.InteriorPinId],
                ToNodeId   = retNode.Id, ToPinId = exitExecPins[k].Id,
            });
        }
        // Inputs: every interior consumer of the outside producer now reads the entry node's pin.
        for (int k = 0; k < plan.Inputs.Count; k++)
        {
            var i = plan.Inputs[k];
            foreach (var consumer in host.Links.Where(
                         l => l.FromPinId == i.SourcePinId && selected.Contains(l.ToNodeId)))
            {
                extracted.Links.Add(new Link
                {
                    FromNodeId = entryNode.Id, FromPinId = inputPins[k].Id,
                    ToNodeId   = fragment.NodeMap[consumer.ToNodeId],
                    ToPinId    = fragment.PinMap[consumer.ToPinId],
                });
            }
        }
        // Outputs: the interior producer now also feeds the return node's pin.
        for (int k = 0; k < plan.Outputs.Count; k++)
        {
            var o = plan.Outputs[k];
            extracted.Links.Add(new Link
            {
                FromNodeId = fragment.NodeMap[o.SourceNodeId],
                FromPinId  = fragment.PinMap[o.SourcePinId],
                ToNodeId   = retNode.Id, ToPinId = outputPins[k].Id,
            });
        }

        // ── the call node standing in the host's place ───────────────────────────────────
        Node callNode;
        var callEntryPins = plan.Entries.Select(e => MakePin(e.Name, "In",  isExec: true)).ToList();
        var callExitPins  = plan.Exits  .Select(x => MakePin(x.Name, "Out", isExec: true)).ToList();
        if (callEntryPins.Count == 0) callEntryPins.Add(MakePin("In",  "In",  isExec: true));
        if (callExitPins.Count  == 0) callExitPins .Add(MakePin("Out", "Out", isExec: true));

        var callInPins  = plan.Inputs .Select(i => MakePin(i.Name, "In",  isExec: false, i.Type)).ToList();
        var callOutPins = plan.Outputs.Select(o => MakePin(o.Name, "Out", isExec: false, o.Type)).ToList();

        if (target == CollapseTarget.Macro)
            callNode = new MacroCallNode { Id = Guid.NewGuid(), TargetGraphId = extracted.Id.ToString() };
        else
            callNode = new FunctionCallNode
            {
                Id = Guid.NewGuid(), IsPure = false, TargetGraphId = extracted.Id.ToString(),
            };

        callNode.Pins.AddRange(callEntryPins);
        callNode.Pins.AddRange(callExitPins);
        callNode.Pins.AddRange(callInPins);
        callNode.Pins.AddRange(callOutPins);

        // ── the rewritten host ───────────────────────────────────────────────────────────
        var hostNodes = host.Nodes.Where(n => !selected.Contains(n.Id)).ToList();
        hostNodes.Add(callNode);

        var hostLinks = new List<Link>();
        foreach (var link in host.Links)
        {
            bool fi = selected.Contains(link.FromNodeId);
            bool ti = selected.Contains(link.ToNodeId);
            if (fi && ti) continue;                    // moved into the extracted graph
            if (!fi && !ti) { hostLinks.Add(link); continue; }

            var fromPin = FindPin(byId, link.FromNodeId, link.FromPinId);
            var toPin   = FindPin(byId, link.ToNodeId,   link.ToPinId);
            if (fromPin is null || toPin is null) continue;
            bool isExec = fromPin.IsExec || toPin.IsExec;

            if (isExec && ti)
            {
                // entry: X.out → call.entry[k]
                int k = IndexOfEntry(plan, toPin.Id);
                if (k >= 0)
                    hostLinks.Add(new Link
                    {
                        FromNodeId = link.FromNodeId, FromPinId = link.FromPinId,
                        ToNodeId   = callNode.Id,     ToPinId   = callEntryPins[k].Id,
                    });
            }
            else if (isExec)
            {
                // exit: call.exit[k] → Y.in
                int k = IndexOfExit(plan, fromPin.Id);
                if (k >= 0)
                    hostLinks.Add(new Link
                    {
                        FromNodeId = callNode.Id, FromPinId = callExitPins[k].Id,
                        ToNodeId   = link.ToNodeId, ToPinId  = link.ToPinId,
                    });
            }
            else if (ti)
            {
                // data in: producer.out → call.in[k]. Several interior consumers collapse to ONE
                // link here -- that is case (a), and the dedup already happened in the analysis.
                int k = IndexOfInput(plan, fromPin.Id);
                if (k >= 0)
                {
                    var candidate = new Link
                    {
                        FromNodeId = link.FromNodeId, FromPinId = link.FromPinId,
                        ToNodeId   = callNode.Id,     ToPinId   = callInPins[k].Id,
                    };
                    if (!hostLinks.Any(l => l.FromPinId == candidate.FromPinId
                                         && l.ToPinId   == candidate.ToPinId))
                        hostLinks.Add(candidate);
                }
            }
            else
            {
                // data out: call.out[k] → consumer.in. Each outside consumer keeps its own link --
                // that is case (b): one output, many readers.
                int k = IndexOfOutput(plan, fromPin.Id);
                if (k >= 0)
                    hostLinks.Add(new Link
                    {
                        FromNodeId = callNode.Id,  FromPinId = callOutPins[k].Id,
                        ToNodeId   = link.ToNodeId, ToPinId  = link.ToPinId,
                    });
            }
        }

        var rewritten = host.WithNodesAndLinks(hostNodes, hostLinks);

        // ⚠ Wholesale, never per rewire -- Batch 32's established pattern for this denormalised copy.
        RebuildLinkedToIds(rewritten);
        RebuildLinkedToIds(extracted);

        return new CollapseEdit(extracted, callNode, rewritten);
    }

    // ────────────────────────────────────────────────────────────────────────

    private static int IndexOfEntry(CollapsePlan p, Guid interiorPinId)
        => IndexOf(p.Entries.Select(e => e.InteriorPinId), interiorPinId);
    private static int IndexOfExit(CollapsePlan p, Guid interiorPinId)
        => IndexOf(p.Exits.Select(e => e.InteriorPinId), interiorPinId);
    private static int IndexOfInput(CollapsePlan p, Guid sourcePinId)
        => IndexOf(p.Inputs.Select(i => i.SourcePinId), sourcePinId);
    private static int IndexOfOutput(CollapsePlan p, Guid sourcePinId)
        => IndexOf(p.Outputs.Select(o => o.SourcePinId), sourcePinId);

    private static int IndexOf(IEnumerable<Guid> ids, Guid id)
    {
        int i = 0;
        foreach (var x in ids) { if (x == id) return i; i++; }
        return -1;
    }

    private static Pin? FindPin(Dictionary<Guid, Node> byId, Guid nodeId, Guid pinId)
        => byId.TryGetValue(nodeId, out var n) ? n.Pins.FirstOrDefault(p => p.Id == pinId) : null;

    private static Pin MakePin(string name, string dir, bool isExec, BlueprintTypeRef? type = null) => new()
    {
        Id = Guid.NewGuid(), Name = name, Direction = dir, IsExec = isExec,
        TypeRef = type ?? new BlueprintTypeRef(),
    };

    /// <summary>Same wholesale rebuild <c>Stage2_5_ExpandMacros</c> uses, and for the same reason.</summary>
    internal static void RebuildLinkedToIds(Graph graph)
    {
        var byPin = new Dictionary<Guid, List<Guid>>();
        foreach (var link in graph.Links)
        {
            if (!byPin.TryGetValue(link.FromPinId, out var f)) byPin[link.FromPinId] = f = new List<Guid>();
            f.Add(link.ToPinId);
            if (!byPin.TryGetValue(link.ToPinId, out var t)) byPin[link.ToPinId] = t = new List<Guid>();
            t.Add(link.FromPinId);
        }
        foreach (var pin in graph.Nodes.SelectMany(n => n.Pins))
            pin.LinkedToIds = byPin.TryGetValue(pin.Id, out var ids)
                ? ids.Distinct().ToList() : new List<Guid>();
    }
}
