using System;
using System.Linq;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Core.Compiler.Diagnostics;

/// <summary>
/// BP-206 — turns the GUIDs a <see cref="Diagnostic"/> carries into the names a designer can act on.
///
/// <para>
/// ⭐ <b>The complaint.</b> <c>CSC : error BP1602: Link references unknown ToPinId 2f2db7d9… on node
/// 8a6eb895…</c> names two GUIDs and nothing else. With forty blueprints in the repo the user's
/// response was *"I don't know what blueprint it was"* — and they were right: finding it means grepping
/// every asset file for a GUID. A diagnostic that cannot be traced to a node is a search, not a fix.
/// </para>
///
/// <para>
/// ⭐ <b>Why this is a resolver and not a change to a hundred call sites.</b> The handoff proposed
/// threading blueprint/graph/node names through every <c>Diagnostic.Error(...)</c> call. That is
/// unnecessary: <see cref="Diagnostic"/> <b>already</b> carries <c>AssetId</c>, <c>GraphId</c>,
/// <c>NodeId</c> and <c>PinId</c> — every validator populates them. The names were never missing from
/// the data, only from the rendered message. Resolving once, where the asset is in hand, is a fraction
/// of the change and <b>cannot drift</b>: a new diagnostic gets its identity for free, whereas a
/// threaded parameter is one more thing each new call site can forget.
/// </para>
///
/// <para>
/// ⚠ <b>Written to <see cref="Diagnostic.Origin"/>, never spliced into <see cref="Diagnostic.Message"/>.</b>
/// A large number of tests assert on exact message text; rewriting messages would redden them for no
/// behavioural reason and bury any real regression among the noise. Consumers compose the two.
/// </para>
/// </summary>
public static class DiagnosticIdentity
{
    /// <summary>
    /// Returns <paramref name="diagnostics"/> with <see cref="Diagnostic.Origin"/> filled in from
    /// <paramref name="asset"/>. Diagnostics that already carry an origin, or that name nothing
    /// resolvable, are returned unchanged.
    /// </summary>
    public static Diagnostic[] Attribute(
        System.Collections.Generic.IEnumerable<Diagnostic> diagnostics, BlueprintAsset? asset)
    {
        if (diagnostics == null) return Array.Empty<Diagnostic>();
        return diagnostics.Select(d => Attribute(d, asset)).ToArray();
    }

    /// <summary>
    /// Fills in <see cref="Diagnostic.Origin"/> for one diagnostic — <c>"asset ▸ graph ▸ node"</c>,
    /// omitting any part that cannot be resolved.
    /// </summary>
    public static Diagnostic Attribute(Diagnostic diagnostic, BlueprintAsset? asset)
    {
        if (diagnostic == null) throw new ArgumentNullException(nameof(diagnostic));
        if (!string.IsNullOrEmpty(diagnostic.Origin)) return diagnostic;

        var origin = Describe(diagnostic, asset);
        return string.IsNullOrEmpty(origin) ? diagnostic : diagnostic with { Origin = origin };
    }

    /// <summary>
    /// The human-readable location: <c>"SmokePatrol ▸ Tick ▸ Print String"</c>. Empty when nothing
    /// resolves — an asset-less diagnostic (a JSON parse failure, say) has no location to name, and an
    /// empty origin is honest where a half-filled one would be noise.
    /// </summary>
    public static string Describe(Diagnostic diagnostic, BlueprintAsset? asset)
    {
        if (diagnostic == null) throw new ArgumentNullException(nameof(diagnostic));
        if (asset == null) return "";

        // ⚠ A diagnostic from a sibling asset must not be labelled with THIS asset's name. When the
        // ids disagree, say nothing rather than something false.
        if (diagnostic.AssetId.HasValue && diagnostic.AssetId.Value != asset.AssetId) return "";

        var parts = new System.Collections.Generic.List<string>(3);
        if (!string.IsNullOrEmpty(asset.Name)) parts.Add(asset.Name);

        Graph? graph = null;
        if (diagnostic.GraphId.HasValue)
        {
            graph = asset.Graphs.FirstOrDefault(g => g.Id == diagnostic.GraphId.Value);
            if (graph != null && !string.IsNullOrEmpty(graph.Name)) parts.Add(graph.Name);
        }

        if (diagnostic.NodeId.HasValue)
        {
            // The graph id is not always populated, so fall back to a whole-asset search: a node name
            // is the most useful part of the three and dropping it because one id was absent would
            // defeat the point.
            var node = graph?.Nodes.FirstOrDefault(n => n.Id == diagnostic.NodeId.Value)
                       ?? asset.Graphs.SelectMany(g => g.Nodes)
                              .FirstOrDefault(n => n.Id == diagnostic.NodeId.Value);
            if (node != null) parts.Add(NodeDisplayName(node));
        }

        return parts.Count == 0 ? "" : string.Join(" ▸ ", parts);
    }

    /// <summary>
    /// What to call a node: the author's own header text when they set one (BP-17's
    /// <see cref="NodeMetadata.CustomTitle"/>), otherwise its kind without the <c>Node</c> suffix
    /// (<c>PrintStringNode</c> → <c>Print String</c>).
    ///
    /// <para>
    /// ⚠ The kind is spaced out rather than printed raw because the designer never sees the class name
    /// — the palette calls it <i>Print String</i>, and a diagnostic naming <c>PrintStringNode</c> asks
    /// them to translate.
    /// </para>
    /// </summary>
    public static string NodeDisplayName(Node node)
    {
        if (node == null) throw new ArgumentNullException(nameof(node));

        var custom = node.EditorMetadata?.CustomTitle;
        if (!string.IsNullOrWhiteSpace(custom)) return custom!.Trim();

        var kind = node.GetType().Name;
        if (kind.EndsWith("Node", StringComparison.Ordinal) && kind.Length > 4)
            kind = kind.Substring(0, kind.Length - 4);

        return SpaceOutPascalCase(kind);
    }

    /// <summary>"PrintString" → "Print String"; runs of capitals are kept together ("EQS", "AI").</summary>
    private static string SpaceOutPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            bool startsWord = i > 0
                && char.IsUpper(c)
                && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])));
            if (startsWord) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
