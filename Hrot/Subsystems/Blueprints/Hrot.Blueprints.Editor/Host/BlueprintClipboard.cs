using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Hrot.Blueprints.Core.Assets;

namespace Hrot.Blueprints.Editor.Host;

/// <summary>
/// BP-23a — the clipboard payload for canvas copy/cut/paste/duplicate, and the id-remapping that
/// makes a paste a genuinely independent copy.
///
/// <para>
/// <b>Why this lives host-side.</b> A clipboard entry is a list of asset <see cref="Node"/>s, which
/// the vendored NodeEdit tree knows nothing about. <see cref="Node"/> is already
/// <c>[JsonPolymorphic]</c>, so the round-trip is free and every node kind survives — including the
/// 42 of 50 that <c>BlueprintCommandSink.ApplyInitialProperties</c> does not know how to configure.
/// Building paste on <c>GraphCommand.AddNode</c> would have silently dropped their settings; paste
/// instead ships fully-built nodes through <see cref="BlueprintEditCommand"/>.
/// </para>
///
/// <para>
/// Text-based so it survives the OS clipboard and can be pasted into another editor instance.
/// <see cref="Payload.Format"/> is the guard: arbitrary clipboard text must not be mistaken for a
/// node graph.
/// </para>
/// </summary>
public static class BlueprintClipboard
{
    /// <summary>Marker stored in every payload so foreign clipboard text is rejected.</summary>
    public const string FormatId = "hrot.blueprint.nodes/1";

    /// <summary>Offset applied to a paste that has no explicit target position.</summary>
    public static readonly Vector2 DefaultPasteOffset = new(40f, 40f);

    private static readonly JsonSerializerOptions Options = BuildOptions();

    private static JsonSerializerOptions BuildOptions()
    {
        var opts = new JsonSerializerOptions
        {
            IncludeFields               = true,
            PropertyNameCaseInsensitive = true,
            WriteIndented               = false,
        };
        opts.Converters.Add(new JsonStringEnumConverter());
        return opts;
    }

    /// <summary>A copied fragment: the nodes, plus only the links whose <b>both</b> ends are in it.</summary>
    public sealed class Payload
    {
        public string      Format { get; set; } = FormatId;
        public List<Node>  Nodes  { get; set; } = new();
        public List<Link>  Links  { get; set; } = new();
    }

    // ── Copy ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds the clipboard text for <paramref name="nodeIds"/> taken from <paramref name="graph"/>.
    /// Returns <see langword="null"/> when the selection contains no nodes of this graph.
    ///
    /// <para>
    /// Links are included only when both endpoints are inside the selection. A half-copied wire
    /// would either dangle or silently re-attach to whatever node happened to hold that id in the
    /// destination.
    /// </para>
    /// </summary>
    public static string? Copy(Graph graph, IReadOnlyCollection<Guid> nodeIds)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(nodeIds);

        var selected = new HashSet<Guid>(nodeIds);
        var nodes    = graph.Nodes.Where(n => selected.Contains(n.Id)).ToList();
        if (nodes.Count == 0) return null;

        var links = graph.Links
            .Where(l => selected.Contains(l.FromNodeId) && selected.Contains(l.ToNodeId))
            .ToList();

        return JsonSerializer.Serialize(
            new Payload { Nodes = nodes, Links = links }, Options);
    }

    // ── Paste ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses clipboard text. Returns <see langword="false"/> for anything that is not a Blueprint
    /// node fragment — ordinary text, malformed JSON, or a payload from a different format version.
    /// </summary>
    public static bool TryParse(string? text, out Payload payload)
    {
        payload = new Payload();
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Cheap pre-check: the format marker must be present before we attempt a polymorphic parse,
        // which throws on unknown "kind" discriminators.
        if (!text!.Contains(FormatId, StringComparison.Ordinal)) return false;

        Payload? parsed;
        try { parsed = JsonSerializer.Deserialize<Payload>(text, Options); }
        catch (JsonException) { return false; }

        if (parsed is null || parsed.Format != FormatId || parsed.Nodes.Count == 0) return false;

        payload = parsed;
        return true;
    }

    /// <summary>The result of rehydrating a payload: independent nodes and the links between them.</summary>
    public sealed record Fragment(IReadOnlyList<Node> Nodes, IReadOnlyList<Link> Links);

    /// <summary>
    /// Turns a payload into nodes that can be added to a graph: every node id, pin id and link
    /// endpoint is re-minted, and the whole fragment is translated by <paramref name="offset"/>.
    ///
    /// <para>
    /// Re-minting <b>pin</b> ids as well as node ids is what makes the copy independent. Pins carry
    /// their own GUIDs, links reference them directly, and a paste that reused them would produce
    /// two nodes whose pins collide — so a later wire-drop or link lookup could resolve to either.
    /// </para>
    ///
    /// <para>
    /// The payload is re-serialised per call, so pasting the same clipboard entry twice yields two
    /// fully independent fragments rather than two views of one object graph.
    /// </para>
    /// </summary>
    public static Fragment Rehydrate(Payload payload, Vector2 offset)
    {
        ArgumentNullException.ThrowIfNull(payload);

        // Deep-copy through JSON: the caller may paste the same payload repeatedly, and the nodes
        // must not be shared between pastes (or with the clipboard's own copy).
        var clone = JsonSerializer.Deserialize<Payload>(
            JsonSerializer.Serialize(payload, Options), Options)!;

        var nodeMap = new Dictionary<Guid, Guid>();
        var pinMap  = new Dictionary<Guid, Guid>();

        foreach (var node in clone.Nodes)
        {
            nodeMap[node.Id] = Guid.NewGuid();
            node.Id = nodeMap[node.Id];

            foreach (var pin in node.Pins)
            {
                pinMap[pin.Id] = Guid.NewGuid();
                pin.Id = pinMap[pin.Id];
            }

            node.EditorMetadata ??= new NodeMetadata();
            node.EditorMetadata.X += offset.X;
            node.EditorMetadata.Y += offset.Y;
        }

        // Pin.LinkedToIds is a denormalised mirror of the link list; leaving stale ids in it would
        // make a pasted node claim wires it does not have.
        foreach (var node in clone.Nodes)
            foreach (var pin in node.Pins)
                pin.LinkedToIds = pin.LinkedToIds
                    .Select(id => pinMap.TryGetValue(id, out var mapped) ? mapped : id)
                    .Where(id => pinMap.ContainsValue(id))
                    .ToList();

        var links = new List<Link>(clone.Links.Count);
        foreach (var link in clone.Links)
        {
            // Both endpoints were required to be inside the selection at copy time; anything that
            // does not remap now came from a hand-edited payload and is dropped rather than trusted.
            if (!nodeMap.TryGetValue(link.FromNodeId, out var fromNode)) continue;
            if (!nodeMap.TryGetValue(link.ToNodeId,   out var toNode))   continue;
            if (!pinMap.TryGetValue(link.FromPinId,   out var fromPin))  continue;
            if (!pinMap.TryGetValue(link.ToPinId,     out var toPin))    continue;

            link.FromNodeId = fromNode;
            link.ToNodeId   = toNode;
            link.FromPinId  = fromPin;
            link.ToPinId    = toPin;
            links.Add(link);
        }

        return new Fragment(clone.Nodes, links);
    }

    /// <summary>
    /// Top-left corner of a payload's nodes in graph space — the anchor a paste-at-cursor uses to
    /// place the fragment under the mouse rather than at its original coordinates.
    /// </summary>
    public static Vector2 TopLeftOf(Payload payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Nodes.Count == 0) return Vector2.Zero;

        float minX = float.MaxValue, minY = float.MaxValue;
        foreach (var n in payload.Nodes)
        {
            var meta = n.EditorMetadata;
            if (meta is null) continue;
            if (meta.X < minX) minX = meta.X;
            if (meta.Y < minY) minY = meta.Y;
        }

        return minX == float.MaxValue ? Vector2.Zero : new Vector2(minX, minY);
    }
}
