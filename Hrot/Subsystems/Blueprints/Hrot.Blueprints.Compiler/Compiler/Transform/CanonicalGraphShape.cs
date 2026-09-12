using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Core.Compiler.Transform;

/// <summary>
/// BP-74 / Q26-E1 — a canonical, id-free description of a graph's <b>structure</b>, so two graphs can
/// be compared for structural equivalence.
///
/// <para>
/// ⭐ <b>What "equivalent" means is the whole test</b>, so it is written down here rather than inside
/// one assertion:
/// </para>
///
/// <list type="table">
///   <item><term>Compared</term><description>node <b>kinds</b> (as a multiset) · link <b>topology</b>,
///     described as <c>(fromKind, fromPinName) → (toKind, toPinName)</c> · declared inputs/outputs and
///     exec entries/exits by <b>name + type</b>.</description></item>
///   <item><term>Ignored</term><description>node ids and pin ids (fresh by construction on every
///     clone) · editor positions · declaration ORDER within each list · the graph's own id and
///     name.</description></item>
/// </list>
///
/// <para>
/// ⚠ <b>Order is ignored deliberately, and it is arguable.</b> Positional pairing IS load-bearing
/// between a call node's pins and the declaration list it mirrors — but that pairing is internal to a
/// single projection, and both sides of a round-trip derive their order from the same walk. Comparing
/// order here would make the property sensitive to link enumeration order, which is an artefact of
/// <c>List&lt;Link&gt;</c>, not of meaning. Sorting is what makes this a statement about STRUCTURE.
/// </para>
///
/// <para>
/// ⚠ Node kind is the CLR type name, not a title or an id: <c>BP-76</c> is a live example of what
/// happens when shared code keys off a display title (<c>node.Title == "ScaleBy"</c>).
/// </para>
///
/// <para>
/// ⭐ Built as a reusable comparator rather than a one-off assert — <c>BP-76</c>'s <c>ExpandNode</c>
/// will want exactly this, and so will any future refactoring gesture.
/// </para>
/// </summary>
public static class CanonicalGraphShape
{
    /// <summary>
    /// A stable string describing <paramref name="graph"/>'s structure. Two graphs are structurally
    /// equivalent exactly when their descriptions are equal — and when they are not, the diff between
    /// the two strings shows why, which a hash alone would not.
    /// </summary>
    public static string Describe(Graph graph)
    {
        if (graph is null) throw new ArgumentNullException(nameof(graph));

        var sb = new StringBuilder();
        sb.Append("kind=").Append(graph.Kind).Append('\n');

        // Declarations, by name+type, sorted — see the note on order above.
        AppendDecls(sb, "inputs",  graph.Inputs .Select(p => p.Name + ":" + TypeOf(p.Type)));
        AppendDecls(sb, "outputs", graph.Outputs.Select(p => p.Name + ":" + TypeOf(p.Type)));
        AppendDecls(sb, "execIn",  graph.ExecInputs .Select(d => d.Name));
        AppendDecls(sb, "execOut", graph.ExecOutputs.Select(d => d.Name));

        // Node kinds as a multiset.
        AppendDecls(sb, "nodes", graph.Nodes.Select(n => n.GetType().Name));

        // Link topology, described only through kinds and pin names.
        var byId = graph.Nodes.ToDictionary(n => n.Id);
        var edges = new List<string>();
        foreach (var link in graph.Links)
        {
            if (!byId.TryGetValue(link.FromNodeId, out var from)) continue;
            if (!byId.TryGetValue(link.ToNodeId,   out var to))   continue;

            var fromPin = from.Pins.FirstOrDefault(p => p.Id == link.FromPinId);
            var toPin   = to.Pins  .FirstOrDefault(p => p.Id == link.ToPinId);

            edges.Add($"{from.GetType().Name}.{fromPin?.Name ?? "?"}"
                      + $"->{to.GetType().Name}.{toPin?.Name ?? "?"}");
        }
        AppendDecls(sb, "links", edges);

        return sb.ToString();
    }

    /// <summary>True when the two graphs describe the same structure. See <see cref="Describe"/>.</summary>
    public static bool AreEquivalent(Graph a, Graph b) => Describe(a) == Describe(b);

    private static void AppendDecls(StringBuilder sb, string label, IEnumerable<string> items)
    {
        sb.Append(label).Append('=');
        sb.Append(string.Join(",", items.OrderBy(x => x, StringComparer.Ordinal)));
        sb.Append('\n');
    }

    private static string TypeOf(BlueprintTypeRef? t)
        => string.IsNullOrEmpty(t?.TypeId) ? "?" : t!.TypeId;
}
