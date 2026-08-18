using System.Numerics;
using NodeEditor.Core;
using NodeEditor.Core.Action;
using NodeEditor.Core.Commands;
using NodeEditor.Core.Interfaces;
using NodeEditor.Core.View;
using NodeEditor.Primitives;

namespace NodeEditor.UI.Action;

/// <summary>
/// BP-13 — node alignment, distribution and connection-straightening.
///
/// <para>
/// <see cref="CommandCatalog"/> has declared these nine command ids since it was written, with
/// <b>zero</b> implementations anywhere in the editor. Every one is a batch move, so they all reduce
/// to <see cref="CommandBuilder.MoveNodes"/> — the same primitive node dragging already uses, which
/// means each is a single undoable step for free.
/// </para>
///
/// <para>
/// <b>Alignment uses node bounds, not positions.</b> Aligning right or centring by the top-left
/// corner would leave nodes of different widths visibly ragged, which is the opposite of what the
/// command is for.
/// </para>
/// </summary>
public static class AlignCommands
{
    /// <summary>Assumed node size when a node has not yet reported one (mirrors ViewCommands).</summary>
    private static readonly Vector2 DefaultNodeSize = new(160, 64);

    /// <summary>Register all nine alignment commands.</summary>
    public static void Register(EditorCommandsImpl cmds, GraphView view)
    {
        var reg = new CommandRegistration(cmds);

        // Two nodes cannot be "distributed" — the ends are already where they will stay — so those
        // two need three. Everything else is meaningful from two.
        bool AtLeast(int n) => view.Selection.Nodes.Take(n).Count() >= n;

        void AddAlign(string id, string name, Func<IReadOnlyList<INodeModel>, RectF, INodeModel, Vector2> place)
            => reg.Add(id, name, "Align",
                _ => Apply(view, name, place),
                isEnabled: () => AtLeast(2),
                description: $"{name} the selected nodes.");

        AddAlign(CommandCatalog.AlignLeft,  "Align Left",
            (_, b, n) => new Vector2(b.Min.X, n.Position.Y));
        AddAlign(CommandCatalog.AlignRight, "Align Right",
            (_, b, n) => new Vector2(b.Min.X + b.Size.X - SizeOf(n).X, n.Position.Y));
        AddAlign(CommandCatalog.AlignTop,   "Align Top",
            (_, b, n) => new Vector2(n.Position.X, b.Min.Y));
        AddAlign(CommandCatalog.AlignBottom,"Align Bottom",
            (_, b, n) => new Vector2(n.Position.X, b.Min.Y + b.Size.Y - SizeOf(n).Y));

        // "Center H" centres the nodes on a shared VERTICAL axis (they line up horizontally),
        // matching the axis naming used by graph editors generally.
        AddAlign(CommandCatalog.AlignCenterH, "Align Center Horizontally",
            (_, b, n) => new Vector2(b.Min.X + (b.Size.X - SizeOf(n).X) * 0.5f, n.Position.Y));
        AddAlign(CommandCatalog.AlignCenterV, "Align Center Vertically",
            (_, b, n) => new Vector2(n.Position.X, b.Min.Y + (b.Size.Y - SizeOf(n).Y) * 0.5f));

        reg.Add(CommandCatalog.DistributeH, "Distribute Horizontally", "Align",
            _ => Distribute(view, horizontal: true),
            isEnabled: () => AtLeast(3),
            description: "Spaces the selected nodes evenly between the leftmost and rightmost.");

        reg.Add(CommandCatalog.DistributeV, "Distribute Vertically", "Align",
            _ => Distribute(view, horizontal: false),
            isEnabled: () => AtLeast(3),
            description: "Spaces the selected nodes evenly between the topmost and bottommost.");

        reg.Add(CommandCatalog.StraightenConn, "Straighten Connection", "Align",
            _ => Straighten(view),
            isEnabled: () => AtLeast(2),
            description: "Lines up connected nodes vertically so their wires run straight.");
    }

    // ── align ─────────────────────────────────────────────────────────────────

    private static void Apply(
        GraphView view, string label,
        Func<IReadOnlyList<INodeModel>, RectF, INodeModel, Vector2> place)
    {
        var nodes = Selected(view);
        if (nodes.Count < 2) return;

        var bounds = Bounds(nodes);
        var moves  = new List<(NodeId, Vector2)>(nodes.Count);
        foreach (var n in nodes)
        {
            var target = place(nodes, bounds, n);
            if (target != n.Position) moves.Add((n.Id, target));
        }

        Commit(view, label, moves);
    }

    // ── distribute ────────────────────────────────────────────────────────────

    /// <summary>
    /// Spaces the selection evenly along one axis, holding the two extremes still.
    ///
    /// <para>
    /// Gaps are equalised between node <b>edges</b>, not between their origins: distributing by
    /// origin leaves wide nodes visually crowding their neighbours. The two end nodes never move,
    /// so distributing twice is idempotent.
    /// </para>
    /// </summary>
    private static void Distribute(GraphView view, bool horizontal)
    {
        var nodes = Selected(view);
        if (nodes.Count < 3) return;

        // A stable sort by current position, then by id: two nodes at the same coordinate must not
        // swap places depending on enumeration order.
        var ordered = nodes
            .OrderBy(n => horizontal ? n.Position.X : n.Position.Y)
            .ThenBy(n => n.Id.Value)
            .ToList();

        float Extent(INodeModel n) => horizontal ? SizeOf(n).X : SizeOf(n).Y;
        float Origin(INodeModel n) => horizontal ? n.Position.X : n.Position.Y;

        var first = ordered[0];
        var last  = ordered[^1];

        float span      = (Origin(last) + Extent(last)) - Origin(first);
        float occupied  = ordered.Sum(Extent);
        float gap       = (span - occupied) / (ordered.Count - 1);

        var moves  = new List<(NodeId, Vector2)>(ordered.Count);
        float cursor = Origin(first) + Extent(first) + gap;

        for (int i = 1; i < ordered.Count - 1; i++)
        {
            var n = ordered[i];
            var target = horizontal
                ? new Vector2(cursor, n.Position.Y)
                : new Vector2(n.Position.X, cursor);
            if (target != n.Position) moves.Add((n.Id, target));
            cursor += Extent(n) + gap;
        }

        Commit(view, horizontal ? "Distribute Horizontally" : "Distribute Vertically", moves);
    }

    // ── straighten ────────────────────────────────────────────────────────────

    /// <summary>
    /// Lines connected nodes up so their wires run flat.
    ///
    /// <para>
    /// Anchored on the <b>first</b> selected node and walking the links inside the selection: each
    /// node downstream of an already-placed one takes that node's Y. Anchoring rather than averaging
    /// means the designer keeps control of where the row ends up, and a second invocation changes
    /// nothing.
    /// </para>
    /// </summary>
    private static void Straighten(GraphView view)
    {
        var nodes = Selected(view);
        if (nodes.Count < 2) return;

        var byId      = nodes.ToDictionary(n => n.Id);
        var anchor    = nodes[0];
        var resolved  = new Dictionary<NodeId, float> { [anchor.Id] = anchor.Position.Y };
        var queue     = new Queue<NodeId>();
        queue.Enqueue(anchor.Id);

        // Adjacency over links whose both ends are in the selection — a wire to an unselected node
        // says nothing about where the selection should sit.
        var neighbours = new Dictionary<NodeId, List<NodeId>>();
        foreach (var link in view.Model.Links)
        {
            var from = view.Model.FindPin(link.FromPin)?.OwnerNodeId;
            var to   = view.Model.FindPin(link.ToPin)?.OwnerNodeId;
            if (from is null || to is null) continue;
            if (!byId.ContainsKey(from.Value) || !byId.ContainsKey(to.Value)) continue;

            Add(neighbours, from.Value, to.Value);
            Add(neighbours, to.Value, from.Value);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!neighbours.TryGetValue(current, out var adjacent)) continue;

            foreach (var next in adjacent)
            {
                if (resolved.ContainsKey(next)) continue;
                resolved[next] = resolved[current];
                queue.Enqueue(next);
            }
        }

        var moves = new List<(NodeId, Vector2)>();
        foreach (var (id, y) in resolved)
        {
            var node = byId[id];
            if (node.Position.Y != y) moves.Add((id, new Vector2(node.Position.X, y)));
        }

        Commit(view, "Straighten Connection", moves);
    }

    private static void Add(Dictionary<NodeId, List<NodeId>> map, NodeId key, NodeId value)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<NodeId>();
        list.Add(value);
    }

    // ── shared ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Selected nodes in model order, so the anchor a command picks is stable rather than
    /// dependent on the order the user happened to click.
    /// </summary>
    private static List<INodeModel> Selected(GraphView view)
    {
        var ids = view.Selection.Nodes.ToHashSet();
        return view.Model.Nodes.Where(n => ids.Contains(n.Id)).ToList();
    }

    private static Vector2 SizeOf(INodeModel node) => node.SizeOverride ?? DefaultNodeSize;

    private static RectF Bounds(IReadOnlyList<INodeModel> nodes)
    {
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (var n in nodes)
        {
            var size = SizeOf(n);
            if (n.Position.X < minX) minX = n.Position.X;
            if (n.Position.Y < minY) minY = n.Position.Y;
            if (n.Position.X + size.X > maxX) maxX = n.Position.X + size.X;
            if (n.Position.Y + size.Y > maxY) maxY = n.Position.Y + size.Y;
        }

        return new RectF(new Vector2(minX, minY), new Vector2(maxX - minX, maxY - minY));
    }

    /// <summary>
    /// Records the batch as one undo step. An empty move list records nothing — an alignment that
    /// changes nothing must not cost the designer a Ctrl+Z.
    /// </summary>
    private static void Commit(GraphView view, string label, List<(NodeId, Vector2)> moves)
    {
        if (moves.Count == 0) return;

        var cb = new CommandBuilder(view.Model);
        var (forward, inverse) = cb.MoveNodes(moves);
        view.Execute(forward, inverse, label);
    }
}
