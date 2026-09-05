using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;
using Hrot.Blueprints.Core.Compiler.Catalogs;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// BP-202 — keeps a node whose pin set is <b>derived from its own properties</b> consistent with the
/// graph's links when one of those properties changes.
///
/// <para>
/// ⭐ <b>The defect this exists for.</b> A <c>Print String</c>'s data-in pins come from parsing its
/// <c>Format</c>, and a pin's identity is <c>DeterministicIds.PinId(nodeId, name, direction)</c> —
/// <b>a function of the NAME</b>. So renaming a placeholder (<c>{Threat}</c> → <c>{threat}</c>) does
/// not rename a pin: it <b>destroys one pin and creates another</b>, and the link still points at the
/// pin that no longer exists. The result is <c>BP1602: Link references unknown ToPinId …</c> at
/// solution-build time, naming two GUIDs and nothing else — the user's *"I don't know what blueprint
/// it was"*.
/// </para>
///
/// <para>
/// ⚠ <b>Not a "dropped link" — a dangling one.</b> The earlier design note called this acceptable
/// breakage. It is not: a dropped link is a visible edit the designer can redo, while a dangling link
/// breaks the whole solution build from a graph that looks fine on screen. Worse, the editor's own
/// projection binds a link whose GUID matches no pin <b>positionally</b>
/// (<c>BlueprintGraphModel.Rebuild</c>'s slow path), so a stale data link can capture the exec-In pin
/// and the node appears to lose its wiring — *"the Print String LOST the pins and no editing of format
/// restored them"*.
/// </para>
///
/// <para>
/// ⚠ <b>Why pruning is scoped to pins that VANISHED, not to every unmatched id.</b> A JSON-loaded
/// asset's links carry legacy, non-deterministic GUIDs that match no pin id and are bound positionally
/// by design. Pruning "every link whose endpoint is not a current pin id" would delete those — real
/// wires, in shipped assets. <see cref="PruneVanished"/> therefore removes a link only when its
/// endpoint <b>was</b> a valid pin id before the edit and <b>is not</b> after it. A legacy GUID is in
/// neither set and is never touched.
/// </para>
/// </summary>
public static class DerivedPinMaintenance
{
    /// <summary>
    /// The pin ids currently addressable on <paramref name="node"/>: the deterministic id of every
    /// registry-derived pin, plus whatever ids the node's own in-memory pin list carries.
    ///
    /// <para>
    /// ⚠ Both halves are needed. Registry schemas are the projection a JSON-loaded node uses;
    /// <c>node.Pins</c> is non-empty for a node created through the canvas's drag-to-create path,
    /// whose pins carry <b>random</b> GUIDs stamped by <c>BlueprintCommandSink.ApplyPinIds</c>.
    /// </para>
    /// </summary>
    public static HashSet<Guid> PinIds(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var ids = new HashSet<Guid>();
        foreach (var schema in BuiltInNodeRegistry.Instance.GetStaticPins(node))
            ids.Add(DeterministicIds.PinId(node.Id, schema.Name, schema.Direction));
        foreach (var pin in node.Pins)
            ids.Add(pin.Id);
        return ids;
    }

    /// <summary>
    /// BP-208 — re-derives <paramref name="node"/>'s in-memory pin list from the registry after a
    /// pin-affecting property changed, preserving the GUID of every pin whose
    /// <c>(Name, Direction)</c> survives so incident links keep resolving.
    ///
    /// <para>
    /// ⭐ <b>Why this is needed at all.</b> <c>NodePinSchema.GetCanonicalPins</c> opens with
    /// <c>if (node.Pins.Count > 0) return node.Pins;</c> — an in-memory pin list <b>shadows</b> the
    /// derived one permanently. A node placed by dragging a wire onto empty canvas gets exactly such a
    /// list (<c>BlueprintCommandSink.ApplyPinIds</c>), so for that node editing <c>Format</c> changed
    /// the property and <b>never changed a pin</b> until the asset was saved and reloaded. Nodes
    /// placed from the palette carry no pins and were unaffected, which is why the same gesture
    /// appeared to work sometimes and not others.
    /// </para>
    ///
    /// <para>
    /// No-op when the node carries no pins — that node projects from the registry every rebuild and
    /// must keep doing so.
    /// </para>
    /// </summary>
    public static void ResyncPins(Node node)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (node.Pins.Count == 0) return;

        // Keyed by (Name, Direction) — the same tuple the pin id is derived from, so a surviving pin
        // is exactly one whose id would be unchanged.
        var existing = new Dictionary<(string, string), Pin>();
        foreach (var pin in node.Pins)
            existing[(pin.Name, pin.Direction)] = pin;

        var rebuilt = new List<Pin>();
        foreach (var schema in BuiltInNodeRegistry.Instance.GetStaticPins(node))
        {
            if (existing.TryGetValue((schema.Name, schema.Direction), out var kept))
            {
                // Retype in place: ArgTypes edits change a pin's TYPE, never its identity.
                kept.IsExec  = schema.IsExec;
                kept.TypeRef = new BlueprintTypeRef { TypeId = schema.TypeId };
                rebuilt.Add(kept);
            }
            else
            {
                rebuilt.Add(new Pin
                {
                    Id        = DeterministicIds.PinId(node.Id, schema.Name, schema.Direction),
                    Name      = schema.Name,
                    Direction = schema.Direction,
                    IsExec    = schema.IsExec,
                    TypeRef   = new BlueprintTypeRef { TypeId = schema.TypeId },
                });
            }
        }

        node.Pins = rebuilt;
    }

    /// <summary>
    /// Removes every link incident on <paramref name="node"/> whose endpoint pin existed in
    /// <paramref name="validBefore"/> and does not exist now, and returns them so the caller can
    /// restore them on undo. Call <b>after</b> the property change and after
    /// <see cref="ResyncPins"/>.
    /// </summary>
    public static List<Link> PruneVanished(Graph graph, Node node, HashSet<Guid> validBefore)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(validBefore);

        var validAfter = PinIds(node);
        var removed    = new List<Link>();

        foreach (var link in graph.Links)
        {
            bool danglingFrom = link.FromNodeId == node.Id
                && validBefore.Contains(link.FromPinId) && !validAfter.Contains(link.FromPinId);
            bool danglingTo   = link.ToNodeId == node.Id
                && validBefore.Contains(link.ToPinId)   && !validAfter.Contains(link.ToPinId);

            if (danglingFrom || danglingTo)
                removed.Add(link);
        }

        foreach (var link in removed)
            graph.Links.Remove(link);

        return removed;
    }

    /// <summary>
    /// Re-adds links removed by <see cref="PruneVanished"/>, skipping any that are already present
    /// (an undo/redo cycle must not duplicate a wire). Order is not preserved — nothing in the graph
    /// model depends on link order.
    /// </summary>
    public static void Restore(Graph graph, IReadOnlyList<Link>? links)
    {
        ArgumentNullException.ThrowIfNull(graph);
        if (links == null || links.Count == 0) return;

        foreach (var link in links)
            if (!graph.Links.Contains(link))
                graph.Links.Add(link);
    }

    /// <summary>The graph within <paramref name="asset"/> that contains <paramref name="node"/>.</summary>
    public static Graph? FindOwningGraph(BlueprintAsset asset, Node node)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(node);

        foreach (var graph in asset.Graphs)
            foreach (var candidate in graph.Nodes)
                if (ReferenceEquals(candidate, node))
                    return graph;
        return null;
    }
}
