using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Core.Compiler.Transform;

/// <summary>What a selection is being collapsed INTO.</summary>
public enum CollapseTarget
{
    /// <summary>A <see cref="GraphKind.Macro"/> graph, spliced back at every call site. Latent allowed.</summary>
    Macro,

    /// <summary>A <see cref="GraphKind.Function"/> graph, compiled to a synchronous method.</summary>
    Function,
}

/// <summary>Why a collapse was refused. Each reason names the nodes the designer must look at.</summary>
public sealed class CollapseRefusalReason
{
    public CollapseRefusalReason(string code, string message, IReadOnlyList<Guid> nodeIds)
    {
        Code = code; Message = message; NodeIds = nodeIds;
    }

    /// <summary>Stable identifier for the rule, so a test asserts the RULE and not prose.</summary>
    public string Code { get; }
    public string Message { get; }

    /// <summary>⭐ The offending nodes. Q26-B2 requires the refusal to name them.</summary>
    public IReadOnlyList<Guid> NodeIds { get; }
}

/// <summary>One exec crossing of the selection boundary — a door in, or a door out.</summary>
public sealed class ExecCrossing
{
    public ExecCrossing(string name, Guid interiorNodeId, Guid interiorPinId)
    {
        Name = name; InteriorNodeId = interiorNodeId; InteriorPinId = interiorPinId;
    }

    public string Name { get; }

    /// <summary>The pin INSIDE the selection: the first node an entry reaches, or the pin an exit leaves from.</summary>
    public Guid InteriorNodeId { get; }
    public Guid InteriorPinId { get; }
}

/// <summary>One data crossing — becomes a declared input or output of the extracted graph.</summary>
public sealed class DataCrossing
{
    public DataCrossing(string name, BlueprintTypeRef type, Guid sourceNodeId, Guid sourcePinId)
    {
        Name = name; Type = type; SourceNodeId = sourceNodeId; SourcePinId = sourcePinId;
    }

    public string Name { get; }
    public BlueprintTypeRef Type { get; }

    /// <summary>
    /// The PRODUCING pin — outside the selection for an input, inside it for an output. Crossings are
    /// deduplicated by this pin, which is what makes one producer feeding three consumers ONE
    /// parameter rather than three.
    /// </summary>
    public Guid SourceNodeId { get; }
    public Guid SourcePinId { get; }
}

/// <summary>The four boundary sets. Everything else about a collapse is derived from these.</summary>
public sealed class CollapsePlan
{
    public CollapsePlan(
        IReadOnlyList<Guid> selection,
        IReadOnlyList<ExecCrossing> entries,
        IReadOnlyList<ExecCrossing> exits,
        IReadOnlyList<DataCrossing> inputs,
        IReadOnlyList<DataCrossing> outputs)
    {
        Selection = selection; Entries = entries; Exits = exits; Inputs = inputs; Outputs = outputs;
    }

    public IReadOnlyList<Guid> Selection { get; }

    /// <summary>Exec into the selection ⇒ one <see cref="ExecInDecl"/> each (Q26-A3: N of them).</summary>
    public IReadOnlyList<ExecCrossing> Entries { get; }

    /// <summary>Exec out of the selection ⇒ one <see cref="ExecOutDecl"/> each.</summary>
    public IReadOnlyList<ExecCrossing> Exits { get; }

    /// <summary>Data into the selection ⇒ one <c>Graph.Inputs</c> entry each.</summary>
    public IReadOnlyList<DataCrossing> Inputs { get; }

    /// <summary>Data out of the selection ⇒ one <c>Graph.Outputs</c> entry each.</summary>
    public IReadOnlyList<DataCrossing> Outputs { get; }
}

/// <summary>The outcome of <see cref="CollapseAnalysis.Analyse"/>: a plan, or reasons it was refused.</summary>
public sealed class CollapseResult
{
    private CollapseResult(CollapsePlan? plan, IReadOnlyList<CollapseRefusalReason> refusals)
    {
        Plan = plan; Refusals = refusals;
    }

    public static CollapseResult Ok(CollapsePlan plan) =>
        new(plan, Array.Empty<CollapseRefusalReason>());

    public static CollapseResult Refused(IReadOnlyList<CollapseRefusalReason> reasons) =>
        new(null, reasons);

    public CollapsePlan? Plan { get; }
    public IReadOnlyList<CollapseRefusalReason> Refusals { get; }
    public bool IsRefused => Plan is null;
}

/// <summary>
/// BP-74 / Q26 — the boundary analysis behind <b>collapse a selection into a Function or Macro</b>.
///
/// <para>
/// ⭐ <b>A pure function: selection → plan, or refusal.</b> No mutation, no editor types, no ImGui.
/// That is what makes it headlessly testable, and it is the reason Q26-D1 puts it in
/// <c>.Compiler</c> rather than <c>.Editor</c> — reachable because the chain is
/// <c>.Editor → .Core → .Compiler</c>, the same path <c>BlueprintClipboard</c> already uses to call
/// <see cref="GraphFragmentCloner"/>.
/// </para>
///
/// <para>
/// ⭐ It sits beside <see cref="GraphFragmentCloner"/> and one folder from
/// <c>Stage2_5_ExpandMacros</c>, which is its <b>exact inverse</b> — an operation and its inverse
/// belong together, and the round-trip property (Q26-E1) is what ties them.
/// </para>
///
/// <para>
/// ⚠ No type registry is needed: <see cref="Pin.TypeRef"/> already carries the type, so a
/// <see cref="ParameterDecl"/> can be built straight from the crossing pin.
/// </para>
/// </summary>
public static class CollapseAnalysis
{
    public static class RefusalCodes
    {
        /// <summary>The selection contains the host graph's own entry or return boundary node.</summary>
        public const string BoundaryNodeSelected = "collapse.boundary-node-selected";

        /// <summary>A selected node feeds an outside node that feeds back into the selection.</summary>
        public const string CyclicBoundary = "collapse.cyclic-boundary";

        /// <summary>A Function target cannot contain a latent node.</summary>
        public const string FunctionLatent = "collapse.function-latent";

        /// <summary>A Function returns once, so it cannot have more than one exec exit.</summary>
        public const string FunctionMultipleExits = "collapse.function-multiple-exits";

        /// <summary>A FunctionCallNode has one exec input, so a Function cannot have several entries.</summary>
        public const string FunctionMultipleEntries = "collapse.function-multiple-entries";

        /// <summary>Nothing selected, or the selection names nodes that are not in the graph.</summary>
        public const string EmptySelection = "collapse.empty-selection";
    }

    public static CollapseResult Analyse(
        Graph host,
        IReadOnlyCollection<Guid> selection,
        CollapseTarget target,
        IReadOnlyDictionary<Guid, Graph>? macrosById = null)
    {
        if (host is null) throw new ArgumentNullException(nameof(host));
        if (selection is null) throw new ArgumentNullException(nameof(selection));

        var refusals = new List<CollapseRefusalReason>();

        var byId     = host.Nodes.ToDictionary(n => n.Id);
        var selected = new HashSet<Guid>(selection.Where(byId.ContainsKey));

        if (selected.Count == 0)
        {
            refusals.Add(new CollapseRefusalReason(
                RefusalCodes.EmptySelection,
                "Nothing to collapse: the selection is empty, or none of the selected nodes are in "
                + "this graph.",
                Array.Empty<Guid>()));
            return CollapseResult.Refused(refusals);
        }

        // ── Case (d): the host's own boundary nodes are not movable content ──────────────
        //
        // They ARE the host graph's boundary; extracting one would leave the host with no entry (or
        // no return) and put a second boundary inside the extracted graph, where a fresh one is
        // synthesized anyway. Refusing is the only coherent answer.
        var boundarySelected = selected
            .Where(id => byId[id] is EventEntryNode or ReturnNode)
            .OrderBy(id => id)
            .ToList();

        if (boundarySelected.Count > 0)
        {
            refusals.Add(new CollapseRefusalReason(
                RefusalCodes.BoundaryNodeSelected,
                "The selection contains this graph's own entry or return node. Those are the graph's "
                + "boundary, not content that can be moved into another graph — deselect them and "
                + "collapse the nodes between them.",
                boundarySelected));
        }

        // ── The four boundary sets ───────────────────────────────────────────────────────
        var entries = new List<ExecCrossing>();
        var exits   = new List<ExecCrossing>();
        var inputs  = new List<DataCrossing>();
        var outputs = new List<DataCrossing>();

        // ⚠ Dedup keys differ per set, and getting them wrong is cases (a) and (b):
        //   entries  — by the INTERIOR pin: several outside nodes may converge on one door.
        //   exits    — by the INTERIOR pin: one door out, whatever is behind it.
        //   inputs   — by the OUTSIDE PRODUCER pin: one producer feeding two selected nodes is ONE
        //              parameter, and both interior consumers re-tie to it. (case a)
        //   outputs  — by the INTERIOR PRODUCER pin: one selected node feeding three outside
        //              consumers is ONE result. (case b)
        var seenEntry  = new HashSet<Guid>();
        var seenExit   = new HashSet<Guid>();
        var seenInput  = new HashSet<Guid>();
        var seenOutput = new HashSet<Guid>();

        foreach (var link in host.Links)
        {
            bool fromInside = selected.Contains(link.FromNodeId);
            bool toInside   = selected.Contains(link.ToNodeId);
            if (fromInside == toInside) continue;           // wholly inside, or wholly outside

            var fromPin = FindPin(byId, link.FromNodeId, link.FromPinId);
            var toPin   = FindPin(byId, link.ToNodeId,   link.ToPinId);
            if (fromPin is null || toPin is null) continue; // dangling; V_LinkStructure's business

            bool isExec = fromPin.IsExec || toPin.IsExec;

            if (isExec)
            {
                if (toInside)
                {
                    if (seenEntry.Add(toPin.Id))
                        entries.Add(new ExecCrossing(
                            UniqueName(entries.Select(e => e.Name), toPin.Name, "Enter"),
                            link.ToNodeId, toPin.Id));
                }
                else
                {
                    if (seenExit.Add(fromPin.Id))
                        exits.Add(new ExecCrossing(
                            UniqueName(exits.Select(e => e.Name), fromPin.Name, "Exit"),
                            link.FromNodeId, fromPin.Id));
                }
            }
            else
            {
                if (toInside)
                {
                    if (seenInput.Add(fromPin.Id))
                        inputs.Add(new DataCrossing(
                            UniqueName(inputs.Select(i => i.Name), fromPin.Name, "In"),
                            fromPin.TypeRef ?? new BlueprintTypeRef(),
                            link.FromNodeId, fromPin.Id));
                }
                else
                {
                    if (seenOutput.Add(fromPin.Id))
                        outputs.Add(new DataCrossing(
                            UniqueName(outputs.Select(o => o.Name), fromPin.Name, "Out"),
                            fromPin.TypeRef ?? new BlueprintTypeRef(),
                            link.FromNodeId, fromPin.Id));
                }
            }
        }

        // ── Case (c): a cyclic boundary ──────────────────────────────────────────────────
        var cycleNodes = FindBoundaryCycle(host, selected);
        if (cycleNodes.Count > 0)
        {
            refusals.Add(new CollapseRefusalReason(
                RefusalCodes.CyclicBoundary,
                "The selection both feeds and is fed by the nodes listed. Extracting it would produce "
                + "a graph that must return one of its results before it is given one of its "
                + "arguments, which cannot be expressed as a call. Include the intermediate nodes in "
                + "the selection, or exclude the ones that loop back.",
                cycleNodes));
        }

        // ── Legality, per target ─────────────────────────────────────────────────────────
        if (target == CollapseTarget.Function)
        {
            // ⚠ Latent detection is REUSED, not reimplemented -- MacroLatency is the same predicate
            // BP1661 uses, including its walk through nested macro calls.
            var latent = MacroLatency.FindLatentInNodes(
                selected.Select(id => byId[id]).ToList(), macrosById);

            if (latent is not null)
            {
                refusals.Add(new CollapseRefusalReason(
                    RefusalCodes.FunctionLatent,
                    $"A Function graph compiles to a synchronous method and cannot suspend, but the "
                    + $"selection contains latent node '{latent.GetType().Name}'. Collapse to a Macro "
                    + "instead — factoring out a reusable latent sequence is the one thing a macro can "
                    + "do that a function cannot.",
                    new[] { latent.Id }));
            }

            if (exits.Count > 1)
            {
                refusals.Add(new CollapseRefusalReason(
                    RefusalCodes.FunctionMultipleExits,
                    $"A Function returns once, but the selection has {exits.Count} exec paths leaving "
                    + "it. Collapse to a Macro, which declares one exec output per exit.",
                    exits.Select(e => e.InteriorNodeId).ToList()));
            }

            // ⚠ Not in the handoff, and it is the same class of hole as the exits rule: a
            // FunctionCallNode has exactly ONE exec-in, so a Function built from a two-entry
            // selection would silently lose every path but the first. Refusing is the only
            // non-lossy answer; a Macro expresses it exactly (Q26-A3).
            if (entries.Count > 1)
            {
                refusals.Add(new CollapseRefusalReason(
                    RefusalCodes.FunctionMultipleEntries,
                    $"A Function is entered once, but the selection is entered from {entries.Count} "
                    + "places. Collapse to a Macro, which declares one exec input per entry.",
                    entries.Select(e => e.InteriorNodeId).ToList()));
            }
        }

        return refusals.Count > 0
            ? CollapseResult.Refused(refusals)
            : CollapseResult.Ok(new CollapsePlan(
                selected.OrderBy(id => id).ToList(), entries, exits, inputs, outputs));
    }

    // ────────────────────────────────────────────────────────────────────────
    // Case (c) — the one refusal the four-set table does not reveal
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠⚠ <b>The subtle one, and silent corruption if missed.</b> Contract the whole selection to a
    /// single node and ask whether that node lies on a cycle. If a selected node feeds an outside node
    /// that feeds back into the selection, the extracted graph would have to produce one of its
    /// OUTPUTS before it receives one of its INPUTS — which no call site can express, in either
    /// direction, because a call is a single point in the exec order.
    ///
    /// <para>
    /// The four boundary sets happily describe this shape: it just looks like one output and one
    /// input. Nothing about the table says they are ordered against each other, which is exactly why
    /// this needs its own check.
    /// </para>
    ///
    /// <para>
    /// Returns the OUTSIDE nodes on the offending path, so the refusal names things the designer can
    /// see and either include or exclude — empty when there is no cycle.
    /// </para>
    /// </summary>
    private static IReadOnlyList<Guid> FindBoundaryCycle(Graph host, HashSet<Guid> selected)
    {
        // Adjacency over OUTSIDE nodes only; the selection is the implicit source and sink.
        var outgoing = new Dictionary<Guid, List<Guid>>();
        var fromSelection = new HashSet<Guid>();   // outside nodes the selection feeds
        var intoSelection = new HashSet<Guid>();   // outside nodes that feed the selection

        foreach (var link in host.Links)
        {
            bool fi = selected.Contains(link.FromNodeId);
            bool ti = selected.Contains(link.ToNodeId);

            if (fi && !ti) { fromSelection.Add(link.ToNodeId); continue; }
            if (!fi && ti) { intoSelection.Add(link.FromNodeId); continue; }
            if (fi || ti) continue;                                     // internal

            if (!outgoing.TryGetValue(link.FromNodeId, out var list))
                outgoing[link.FromNodeId] = list = new List<Guid>();
            list.Add(link.ToNodeId);
        }

        if (fromSelection.Count == 0 || intoSelection.Count == 0)
            return Array.Empty<Guid>();

        // Any outside node reachable FROM the selection that also feeds BACK into it closes the loop.
        var path    = new List<Guid>();
        var visited = new HashSet<Guid>();
        var stack   = new Stack<Guid>(fromSelection.OrderBy(id => id));

        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (!visited.Add(id)) continue;
            path.Add(id);

            if (intoSelection.Contains(id))
                return path.Where(intoSelection.Contains)
                           .Concat(new[] { id })
                           .Distinct()
                           .OrderBy(x => x)
                           .ToList();

            if (outgoing.TryGetValue(id, out var next))
                foreach (var n in next) stack.Push(n);
        }
        return Array.Empty<Guid>();
    }

    // ────────────────────────────────────────────────────────────────────────

    private static Pin? FindPin(Dictionary<Guid, Node> byId, Guid nodeId, Guid pinId)
        => byId.TryGetValue(nodeId, out var n) ? n.Pins.FirstOrDefault(p => p.Id == pinId) : null;

    /// <summary>
    /// A readable, collision-free declaration name. Pin names repeat freely across nodes ("In", "Out",
    /// "Value"), and two declarations with the same name would be paired positionally but READ by
    /// name in several places — so uniqueness here is not cosmetic.
    /// </summary>
    private static string UniqueName(IEnumerable<string> taken, string preferred, string fallback)
    {
        var baseName = string.IsNullOrWhiteSpace(preferred) ? fallback : preferred;
        var used     = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
        if (used.Add(baseName)) return baseName;

        for (int i = 2; ; i++)
        {
            var candidate = baseName + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (!used.Contains(candidate)) return candidate;
        }
    }
}
