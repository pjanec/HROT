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

    /// <summary>The range's first id (= the <c>-n</c> offset) of <paramref name="subsystemName"/>, in any casing; everything not listed shares 600.</summary>
    public static int OffsetOf(string? subsystemName) => (subsystemName ?? string.Empty).ToUpperInvariant() switch
    {
        "SIMHOST"      => 1,
        "PLAYER"       => 100,   // ⭐ CE-4332 — was spelled "Player" against the upper-cased name, so a Player fell to 600
        "ORCHESTRATOR" => 300,
        "CGF"          => 400,
        "CI"           => 500,
        "TRAFFIC"      => 800,   // ⭐ AQ93 N1 — the SUMO traffic node (several may run: give each its own -n base)
        // ST-015: the "STRIDEMOCK" => 700 arm went with the subsystem.
        _              => 600,
    };
}
