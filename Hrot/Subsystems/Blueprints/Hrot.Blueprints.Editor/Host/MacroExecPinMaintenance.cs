using System;
using System.Collections.Generic;
using System.Linq;
using Hrot.Blueprints.Core.Assets;
using Hrot.Blueprints.Core.Compiler;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// BP-225 — keeps a macro's wires attached when its exec <b>declarations</b> are edited.
///
/// <para>
/// ⭐⭐ <b>Read this before changing anything here: what the hazard actually is.</b> The obvious fear
/// is that reordering declarations silently re-targets wires, because
/// <c>Stage2_5_ExpandMacros</c> pairs <c>execIn[k]</c> with <c>entryExecOuts[k]</c> positionally.
/// <b>It does not</b>, and the reason is worth stating so nobody "fixes" it back:
/// </para>
///
/// <list type="bullet">
///   <item>A pin's identity is <c>DeterministicIds.PinId(nodeId, name, direction)</c> — <b>a function
///     of the NAME</b>, not of the position. A wire therefore follows the named pin.</item>
///   <item>Both sides of the splice — the boundary node's pins and every call site's pins — are
///     projected from the <b>same</b> declaration list, in the same order. So index <c>k</c> names
///     the same declaration on both sides, and permuting the list permutes both consistently.</item>
/// </list>
///
/// <para>
/// ⇒ <b>Reorder is safe.</b> <c>ExecDeclarationEditTests.Reordering…</c> proves it rather than
/// asserting it, because the property is not obvious and would be easy to break.
/// </para>
///
/// <para>
/// ⚠ <b>The real hazards are the two that change a NAME</b>, and they are exactly
/// <see cref="DerivedPinMaintenance"/>'s <c>BP-202</c> shape one level up:
/// </para>
///
/// <list type="table">
///   <item><term>Rename</term><description>destroys the pin <c>(node, old, dir)</c> and creates
///     <c>(node, new, dir)</c>. Every incident link keeps the old GUID ⇒ a <b>dangling</b> link, which
///     is worse than a dropped one: it breaks the solution build with <c>BP1602</c> naming two GUIDs
///     from a graph that looks fine on screen. ⇒ <see cref="Repoint"/> rewrites the wires, because a
///     rename — unlike BP-202's <c>Format</c> edit — hands us the old→new mapping outright.</description></item>
///   <item><term>Delete</term><description>the pin vanishes with no successor, so the wires are
///     removed and handed back for the undo. Silent survival as a dangling id is the one outcome
///     that must not happen.</description></item>
/// </list>
///
/// <para>
/// ⭐ <b>Every projection site must be visited, not just the macro's own boundary.</b> A declaration
/// projects onto the macro's <see cref="EventEntryNode"/>/<see cref="ReturnNode"/> <i>and</i> onto
/// every <see cref="MacroCallNode"/> targeting it, in every graph of the asset — that is Q26-A3's
/// whole point. Missing the call sites would repair the macro and dangle its callers.
/// </para>
/// </summary>
public static class MacroExecPinMaintenance
{
    /// <summary>
    /// One place a declaration projects to: a node, and the pin direction it projects with there.
    /// </summary>
    private readonly record struct Site(Graph Graph, Node Node, string Direction);

    /// <summary>
    /// What a <see cref="Prune"/> removed — <b>both</b> the wires and the materialised pins they
    /// landed on. ⚠ Keeping only the links makes the undo restore a wire onto a pin that no longer
    /// exists, i.e. the undo recreates the dangling state this type exists to prevent.
    /// </summary>
    public sealed record PruneResult(
        IReadOnlyList<(Graph Graph, Link Link)> Links,
        IReadOnlyList<(Node Node, Pin Pin, int Index)> Pins);

    /// <summary>
    /// Renames a declaration's projected pin everywhere it appears and moves the incident wires with
    /// it. ⭐ <b>Its own inverse</b>: calling it with <paramref name="oldName"/> and
    /// <paramref name="newName"/> swapped undoes it exactly, which is what lets the edit model pair
    /// a rename with a trivially correct undo instead of a second snapshot.
    /// </summary>
    /// <param name="isEntry">
    /// <c>true</c> for a <see cref="Graph.ExecInputs"/> declaration (entry), <c>false</c> for
    /// <see cref="Graph.ExecOutputs"/> (exit). Decides which boundary node and which pin directions
    /// are involved.
    /// </param>
    public static void Repoint(
        BlueprintAsset asset, Graph macro, bool isEntry, string oldName, string newName)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(macro);
        if (oldName == newName) return;

        foreach (var site in SitesFor(asset, macro, isEntry))
        {
            var oldId = DeterministicIds.PinId(site.Node.Id, oldName, site.Direction);
            var newId = DeterministicIds.PinId(site.Node.Id, newName, site.Direction);

            // ⚠ The materialised pin list must move too. NodePinSchema.GetCanonicalPins opens with
            // `if (node.Pins.Count > 0) return node.Pins;`, so for a node that carries pins the
            // in-memory list SHADOWS the projection permanently — BP-208's finding. Repointing links
            // without this leaves the wire pointing at an id no pin claims.
            foreach (var pin in site.Node.Pins)
            {
                if (pin.Direction != site.Direction || pin.Name != oldName) continue;
                pin.Name = newName;
                if (pin.Id == oldId) pin.Id = newId;
            }

            foreach (var link in site.Graph.Links)
            {
                if (link.FromNodeId == site.Node.Id && link.FromPinId == oldId) link.FromPinId = newId;
                if (link.ToNodeId   == site.Node.Id && link.ToPinId   == oldId) link.ToPinId   = newId;
            }
        }
    }

    /// <summary>
    /// Removes every wire incident on a declaration's projected pins and returns them, paired with
    /// the graph each came from so <see cref="Restore"/> can put them back on undo.
    /// </summary>
    public static PruneResult Prune(
        BlueprintAsset asset, Graph macro, bool isEntry, string name)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(macro);

        var removed     = new List<(Graph, Link)>();
        var removedPins = new List<(Node, Pin, int)>();

        foreach (var site in SitesFor(asset, macro, isEntry))
        {
            var pinId = DeterministicIds.PinId(site.Node.Id, name, site.Direction);

            // A node whose pins are materialised carries random GUIDs (BlueprintCommandSink
            // .ApplyPinIds), so the deterministic id alone would miss its wires.
            var ids = new HashSet<Guid> { pinId };
            foreach (var pin in site.Node.Pins)
                if (pin.Direction == site.Direction && pin.Name == name)
                    ids.Add(pin.Id);

            foreach (var link in site.Graph.Links.ToList())
            {
                bool incident = (link.FromNodeId == site.Node.Id && ids.Contains(link.FromPinId))
                             || (link.ToNodeId   == site.Node.Id && ids.Contains(link.ToPinId));
                if (!incident) continue;

                site.Graph.Links.Remove(link);
                removed.Add((site.Graph, link));
            }

            // ⚠ The materialised pin must be captured, not just deleted. Restoring the links without
            // it puts a wire back onto an id no pin claims — the dangling state this whole type
            // exists to prevent, reintroduced by its own undo. Caught by
            // UndoingADelete_RestoresTheDeclarationAtItsIndex_AndItsWires.
            for (int i = site.Node.Pins.Count - 1; i >= 0; i--)
            {
                var pin = site.Node.Pins[i];
                if (pin.Direction != site.Direction || pin.Name != name) continue;
                removedPins.Add((site.Node, pin, i));
                site.Node.Pins.RemoveAt(i);
            }
        }

        return new PruneResult(removed, removedPins);
    }

    /// <summary>
    /// Re-adds wires removed by <see cref="Prune"/>, skipping any already present — an undo entry can
    /// be replayed (undo → redo → undo) and must not duplicate a wire.
    /// </summary>
    public static void Restore(PruneResult? removed)
    {
        if (removed == null) return;

        // Pins first: a restored link must land on a pin that already exists again.
        foreach (var (node, pin, index) in removed.Pins.OrderBy(p => p.Index))
        {
            if (node.Pins.Any(p => p.Id == pin.Id)) continue;
            node.Pins.Insert(Math.Min(index, node.Pins.Count), pin);
        }

        foreach (var (graph, link) in removed.Links)
            if (!graph.Links.Contains(link))
                graph.Links.Add(link);
    }

    /// <summary>
    /// Counts the wires a delete would remove, without removing them — so the refusal/notification
    /// can say how many before the designer commits to it.
    /// </summary>
    public static int CountWires(BlueprintAsset asset, Graph macro, bool isEntry, string name)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(macro);

        int count = 0;
        foreach (var site in SitesFor(asset, macro, isEntry))
        {
            var ids = new HashSet<Guid> { DeterministicIds.PinId(site.Node.Id, name, site.Direction) };
            foreach (var pin in site.Node.Pins)
                if (pin.Direction == site.Direction && pin.Name == name)
                    ids.Add(pin.Id);

            count += site.Graph.Links.Count(l =>
                (l.FromNodeId == site.Node.Id && ids.Contains(l.FromPinId))
             || (l.ToNodeId   == site.Node.Id && ids.Contains(l.ToPinId)));
        }
        return count;
    }

    /// <summary>
    /// Every node a declaration of <paramref name="macro"/> projects a pin onto, with the direction
    /// it uses there.
    ///
    /// <para>
    /// ⚠ The directions are <b>opposite</b> between the boundary and the call site, and that is not a
    /// bug to tidy: an entry is an exec-<i>Out</i> on the macro's entry node (execution leaves the
    /// boundary into the body) and an exec-<i>In</i> on the call node (execution enters the call).
    /// Mirrors <c>NodePinSchema.MacroEntryExecPins</c> / <c>MacroCallPins</c> exactly.
    /// </para>
    /// </summary>
    private static IEnumerable<Site> SitesFor(BlueprintAsset asset, Graph macro, bool isEntry)
    {
        var boundary = isEntry
            ? macro.Nodes.FirstOrDefault(n => n is EventEntryNode)
            : macro.Nodes.FirstOrDefault(n => n is ReturnNode);

        if (boundary is not null)
            yield return new Site(macro, boundary, isEntry ? "Out" : "In");

        var targetId = macro.Id.ToString();
        foreach (var graph in asset.Graphs)
            foreach (var node in graph.Nodes)
                if (node is MacroCallNode call
                    && string.Equals(call.TargetGraphId, targetId, StringComparison.OrdinalIgnoreCase))
                    yield return new Site(graph, node, isEntry ? "In" : "Out");
    }
}
