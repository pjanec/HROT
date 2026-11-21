using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.Orchestration
{
    /// <summary>A node of the CURRENT cluster as placement sees it: its id, name and kind (subsystem + the <c>NodeRole</c> flags as an int).</summary>
    public readonly record struct PlacementNode(int NodeId, string Name, string Subsystem, int Roles);

    /// <summary>One recorded file of an archived exercise and the node that made it (its metadata, CE-4331 ①). An empty <see cref="Subsystem"/> = recorded before CE-4331.</summary>
    public readonly record struct RecordedFile(string FileName, int RecordedNodeId, string Name, string Subsystem, int Roles);

    /// <summary>One placed file: it plays on <see cref="NodeId"/> (a record, not a tuple: the placement crosses the wire as JSON).</summary>
    public readonly record struct PlacedFile(RecordedFile File, int NodeId);

    /// <summary>Where each recorded file plays: the placed ones (file → current node), the files no node can play, and the nodes that only listen.</summary>
    public sealed record Placement(IReadOnlyList<PlacedFile> Placed, IReadOnlyList<RecordedFile> Unplaced, IReadOnlyList<int> Listeners)
    {
        /// <summary>⛔ Nothing could be placed: the AAR is refused in the cluster (the all-in-one editor / Replay Browser opens any files merged).</summary>
        public bool Refused => Placed.Count == 0;

        /// <summary>The refusal's text, or null when it is not refused.</summary>
        public string? Refusal => !Refused ? null
            : "no recording of this exercise fits a node of this cluster (no node of the same kind) — open it in the all-in-one editor or the Replay Browser, which merge any recordings";
    }

    /// <summary>
    /// ⭐⭐ CE-4331 ② (📄 docs/DESIGN_Recording_Storage.md §9a, R-4175) — <b>the ONE placement of an exercise's recordings on the CURRENT cluster</b>: a file goes to
    /// the node of the same NAME and kind; else to the next free node of the same KIND (lowest id first — nodes of one kind are interchangeable, each file
    /// replays as its recorded node); ⛔ never across kinds (a fused <c>Brain|MuscleGround</c> file never plays on a pure Brain node, and the reverse); nodes
    /// left without a file LISTEN. Only "nothing placed at all" is refused. A file recorded before CE-4331 (no kind) keeps the old rule: the node of its id.
    /// </summary>
    public static class RecordingPlacement
    {
        public static Placement Place(IReadOnlyList<RecordedFile> files, IReadOnlyList<PlacementNode> nodes)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (nodes == null) throw new ArgumentNullException(nameof(nodes));
            var free = new List<PlacementNode>(nodes);
            free.Sort((a, b) => a.NodeId.CompareTo(b.NodeId));
            var placed = new List<PlacedFile>();
            var waiting = new List<RecordedFile>();

            // ① a legacy file (no kind recorded): the node of its recorded id, as before CE-4331
            // ② by NAME (and kind)
            foreach (var f in files)
            {
                int at = -1;
                if (string.IsNullOrEmpty(f.Subsystem))
                {
                    for (int i = 0; i < free.Count; i++) if (free[i].NodeId == f.RecordedNodeId) { at = i; break; }
                }
                else if (!string.IsNullOrEmpty(f.Name))
                {
                    for (int i = 0; i < free.Count; i++)
                        if (string.Equals(free[i].Name, f.Name, StringComparison.OrdinalIgnoreCase) && SameKind(f, free[i])) { at = i; break; }
                }
                if (at >= 0) { placed.Add(new PlacedFile(f, free[at].NodeId)); free.RemoveAt(at); }
                else waiting.Add(f);
            }

            // ③ by KIND: the lowest free node of the same kind; ⛔ never across kinds
            var unplaced = new List<RecordedFile>();
            foreach (var f in waiting)
            {
                int at = -1;
                if (!string.IsNullOrEmpty(f.Subsystem))
                    for (int i = 0; i < free.Count; i++) if (SameKind(f, free[i])) { at = i; break; }
                if (at >= 0) { placed.Add(new PlacedFile(f, free[at].NodeId)); free.RemoveAt(at); }
                else unplaced.Add(f);
            }

            var listeners = new List<int>(free.Count);
            foreach (var n in free) listeners.Add(n.NodeId);
            return new Placement(placed, unplaced, listeners);
        }

        /// <summary>The same kind: the same subsystem, and the same roles when both are known (0 = not recorded / not known yet).</summary>
        public static bool SameKind(RecordedFile file, PlacementNode node)
            => string.Equals(file.Subsystem, node.Subsystem, StringComparison.OrdinalIgnoreCase)
               && (file.Roles == 0 || node.Roles == 0 || file.Roles == node.Roles);
    }
}
