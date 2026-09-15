using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Core.Compiler.Transform;

/// <summary>
/// BP-81 — an independent deep copy of a set of nodes and the links between them, with fresh node
/// and pin GUIDs, <b>and the id maps that produced them</b>.
///
/// <para>
/// ⭐ <b>Why this lives in <c>.Compiler</c> and not beside the clipboard.</b> This is
/// <c>BlueprintClipboard.Rehydrate</c>'s core, moved DOWN rather than duplicated. The clipboard is in
/// <c>.Editor</c>, <c>Stage2_5_ExpandMacros</c> is in <c>.Compiler</c>, and the assembly dependency
/// runs Editor → Compiler, so a copy-paste would have been the only alternative. BP-69 duplicated
/// <c>ResolveCustomEventDecl</c> across this exact boundary and the two copies drifted; the clipboard
/// now layers its editor-only concern (a <c>Vector2</c> paste offset) on top of this instead.
/// </para>
///
/// <para>
/// ⭐ <b>Returning the maps is the reason this is a separate type at all.</b> <c>Rehydrate</c> built
/// <c>nodeMap</c>/<c>pinMap</c> and threw them away. Macro expansion cannot: every splice rule is
/// phrased as <i>"the clone of <c>Out′.dataIn[q]</c>"</i>, and without the maps there is no way to
/// name a clone. Boundary links — the ones with an endpoint outside the fragment — are deliberately
/// NOT emitted here; they are precisely what the splice rules rebuild, and they need the maps to do it.
/// </para>
/// </summary>
public sealed class ClonedFragment
{
    public ClonedFragment(
        IReadOnlyList<Node> nodes,
        IReadOnlyList<Link> links,
        IReadOnlyDictionary<Guid, Guid> nodeMap,
        IReadOnlyDictionary<Guid, Guid> pinMap)
    {
        Nodes   = nodes;
        Links   = links;
        NodeMap = nodeMap;
        PinMap  = pinMap;
    }

    /// <summary>The cloned nodes, carrying fresh <see cref="Node.Id"/> and fresh <see cref="Pin.Id"/>s.</summary>
    public IReadOnlyList<Node> Nodes { get; }

    /// <summary>Only links whose <b>both</b> endpoints are inside the fragment, remapped to the clones.</summary>
    public IReadOnlyList<Link> Links { get; }

    /// <summary>original node id → clone node id.</summary>
    public IReadOnlyDictionary<Guid, Guid> NodeMap { get; }

    /// <summary>original pin id → clone pin id.</summary>
    public IReadOnlyDictionary<Guid, Guid> PinMap { get; }
}

/// <summary>
/// The deep-copy-and-remap primitive shared by macro expansion and the editor clipboard.
/// See <see cref="ClonedFragment"/> for why it lives here.
/// </summary>
public static class GraphFragmentCloner
{
    private static readonly JsonSerializerOptions Options = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        // Mirrors BlueprintClipboard's options: IncludeFields because several asset value types
        // (NodeMetadata and friends) carry fields, and JsonStringEnumConverter because every enum on
        // the asset model is persisted by name.
        var opts = new JsonSerializerOptions
        {
            IncludeFields               = true,
            PropertyNameCaseInsensitive = true,
            WriteIndented               = false,
        };
        opts.Converters.Add(new JsonStringEnumConverter());
        return opts;
    }

    /// <summary>
    /// Deep-copies <paramref name="nodes"/> and the subset of <paramref name="links"/> internal to
    /// them, assigning a fresh GUID to every node and every pin.
    ///
    /// <para>
    /// The copy goes through JSON so that <see cref="Node"/>'s <c>[JsonPolymorphic]</c> discriminator
    /// does the work: every node kind survives, including the many the editor's command sink cannot
    /// configure by hand. Callers may clone the same source repeatedly and get fully independent
    /// fragments rather than several views of one object graph.
    /// </para>
    ///
    /// <para>
    /// ⚠ <see cref="Pin.LinkedToIds"/> is a <b>denormalised mirror</b> of the link list. It is remapped
    /// here and narrowed to ids inside the fragment — leaving stale ids would make a cloned node claim
    /// wires it does not have (the BP-23a lesson). Callers that go on to rewire boundary links must
    /// refresh the mirror again afterwards; <c>Stage2_5_ExpandMacros</c> rebuilds it wholesale.
    /// </para>
    /// </summary>
    public static ClonedFragment Clone(IReadOnlyList<Node> nodes, IReadOnlyList<Link> links)
    {
        if (nodes is null) throw new ArgumentNullException(nameof(nodes));
        if (links is null) throw new ArgumentNullException(nameof(links));

        var clonedNodes = DeepCopyNodes(nodes);

        var nodeMap = new Dictionary<Guid, Guid>();
        var pinMap  = new Dictionary<Guid, Guid>();

        foreach (var node in clonedNodes)
        {
            var freshNodeId = Guid.NewGuid();
            nodeMap[node.Id] = freshNodeId;
            node.Id = freshNodeId;

            foreach (var pin in node.Pins)
            {
                var freshPinId = Guid.NewGuid();
                pinMap[pin.Id] = freshPinId;
                pin.Id = freshPinId;
            }
        }

        // Set, not Dictionary.ContainsValue: the mirror is filtered once per pin, and a linear scan
        // per entry would make a large fragment quadratic for no reason.
        var freshPinIds = new HashSet<Guid>(pinMap.Values);

        foreach (var node in clonedNodes)
            foreach (var pin in node.Pins)
                pin.LinkedToIds = pin.LinkedToIds
                    .Select(id => pinMap.TryGetValue(id, out var mapped) ? mapped : id)
                    .Where(freshPinIds.Contains)
                    .ToList();

        var clonedLinks = new List<Link>(links.Count);
        foreach (var link in links)
        {
            // Boundary links (one endpoint outside the fragment) are dropped: the caller rebuilds
            // them from NodeMap/PinMap, which is the only way to know what the clone of the far pin is.
            if (!nodeMap.TryGetValue(link.FromNodeId, out var fromNode)) continue;
            if (!nodeMap.TryGetValue(link.ToNodeId,   out var toNode))   continue;
            if (!pinMap.TryGetValue(link.FromPinId,   out var fromPin))  continue;
            if (!pinMap.TryGetValue(link.ToPinId,     out var toPin))    continue;

            clonedLinks.Add(new Link
            {
                FromNodeId = fromNode,
                FromPinId  = fromPin,
                ToNodeId   = toNode,
                ToPinId    = toPin,
                Waypoints  = link.Waypoints is null ? null : new List<LinkWaypoint>(link.Waypoints),
            });
        }

        return new ClonedFragment(clonedNodes, clonedLinks, nodeMap, pinMap);
    }

    /// <summary>
    /// JSON round-trip of the node list. Serialised as <c>List&lt;Node&gt;</c> so the polymorphic
    /// discriminator on the base type is written and read back.
    /// </summary>
    private static List<Node> DeepCopyNodes(IReadOnlyList<Node> nodes)
    {
        var asList = nodes as List<Node> ?? nodes.ToList();
        var json   = JsonSerializer.Serialize(asList, Options);
        return JsonSerializer.Deserialize<List<Node>>(json, Options)
               ?? new List<Node>();
    }
}
