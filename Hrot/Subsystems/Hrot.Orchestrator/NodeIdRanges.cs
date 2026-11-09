using System;

namespace Hrot.Orchestrator;

/// <summary>
/// ⭐⭐ CE-4331 ⑥ (📄 docs/DESIGN_Recording_Storage.md §9b, R-4175) — the ONE table of node-id ranges per KIND: the runner's <c>-n</c> launch adds the
/// offset to its base (<c>Program.ResolveAppNodeId</c>), and the master GRANTS inside <c>[offset, offset + Width)</c> — the previous id when free, else
/// the lowest free one (<see cref="NodeIdAllocator"/>). ⇒ the same cluster relaunched gets the same ids, and a replacement computer of a kind takes the
/// vacant slot. ⛔ No second copy of the offsets.
/// </summary>
public static class NodeIdRanges
{
    /// <summary>How many ids a kind's range holds.</summary>
    public const int Width = 100;

    /// <summary>The kinds and their first ids — ⭐ the ONE table (<see cref="OffsetOf"/> and <see cref="NameOf"/> both read it). Everything not listed shares 600.</summary>
    private static readonly (string Kind, int Offset)[] Kinds =
    {
        ("SimHost", 1),
        ("Player", 100),        // ⭐ CE-4332 — was spelled "Player" against the upper-cased name, so a Player fell to 600
        ("Orchestrator", 300),
        ("CGF", 400),
        ("CI", 500),
        ("Traffic", 800),       // ⭐ AQ93 N1 — the SUMO traffic node (several may run: give each its own -n base)
        // ST-015: the "STRIDEMOCK" => 700 arm went with the subsystem.
    };

    /// <summary>The offset every unlisted kind shares.</summary>
    public const int OtherOffset = 600;

    /// <summary>The range's first id (= the <c>-n</c> offset) of <paramref name="subsystemName"/>, in any casing; everything not listed shares 600.</summary>
    public static int OffsetOf(string? subsystemName)
    {
        foreach (var (kind, offset) in Kinds)
            if (string.Equals(kind, subsystemName, StringComparison.OrdinalIgnoreCase)) return offset;
        return OtherOffset;
    }

    /// <summary>
    /// ⭐ G-7 (Review Source popup, mockup ③) — a node's name from its id alone: the kind whose range holds it and its place in that range,
    /// <c>"CGF-1"</c> for 400, <c>"SimHost-1"</c> for 1 — what a recording's participant is called when its sidecar names no role. An id outside every
    /// listed range ⇒ <c>"Node N"</c>.
    /// </summary>
    public static string NameOf(int nodeId)
    {
        // ⚠ the ranges touch (SimHost [1, 101) and Player [100, 200) both hold 100): the kind whose range STARTS closest below the id wins
        string? best = null; int bestOffset = int.MinValue;
        foreach (var (kind, offset) in Kinds)
            if (nodeId >= offset && nodeId < offset + Width && offset > bestOffset) { best = kind; bestOffset = offset; }
        return best != null ? best + "-" + (nodeId - bestOffset + 1) : "Node " + nodeId;
    }
}
