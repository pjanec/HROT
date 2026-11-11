using Fdp.Core;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Terrain;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐⭐ Terrain authoring U-3 (docs/DESIGN_Terrain_Authoring.md §15a; §18h H-1/H-2) — the block menu's WRITES: <i>Rotate</i> (a quarter turn), the typed Seed and the Fit bits. Each is ONE edit step (<see cref="EditSteps"/>: the editor's history snapshots the world around the bracket, so Undo restores the block — B-6), and the live bake re-expands only that block (O-7 B), so the outline follows.
/// ⭐ Re-roll, Change recipe and Unpack are backend's <c>BlockOperations</c> (T-4, CE-4431) — ONE implementation, ONE seed rule (H-1); what stays here has no backend equivalent: Rotate, the typed seed, the Fit bits.
/// </summary>
public static class TerrainBlockOps
{
    private static bool IsBlock(EntityRepository world, Entity e) => world.IsAlive(e) && world.IsComponentTypeRegistered<TerrainBlock>() && world.HasComponent<TerrainBlock>(e);

    public static bool Rotate(EntityRepository world, Entity e)
    {
        if (!IsBlock(world, e)) return false;
        EditSteps.Begin(world, "Rotate block");
        try { ref var b = ref world.GetComponentRW<TerrainBlock>(e); b.HeadingDeg = (b.HeadingDeg + 90f) % 360f; }
        finally { EditSteps.End(world); }
        return true;
    }

    /// <summary>⭐ The Details form's Seed field — a typed seed (one edit step). A seed equal to the block's own changes nothing.</summary>
    public static bool SetSeed(EntityRepository world, Entity e, uint seed)
    {
        if (!IsBlock(world, e) || world.GetComponentRO<TerrainBlock>(e).Seed == seed) return false;
        EditSteps.Begin(world, "Set block seed");
        try { ref var b = ref world.GetComponentRW<TerrainBlock>(e); b.Seed = seed; }
        finally { EditSteps.End(world); }
        return true;
    }

    /// <summary>⭐ The Details form's Fit to the site checkboxes — turns the <paramref name="bit"/> (<see cref="TerrainBlock.AlignRoad"/>…) on or off (one edit step); no change when it already is so.</summary>
    public static bool SetFit(EntityRepository world, Entity e, byte bit, bool on)
    {
        if (!IsBlock(world, e)) return false;
        byte fit = world.GetComponentRO<TerrainBlock>(e).Fit;
        byte next = on ? (byte)(fit | bit) : (byte)(fit & ~bit);
        if (next == fit) return false;
        EditSteps.Begin(world, on ? "Fit block to the site" : "Do not fit block to the site");
        try { ref var b = ref world.GetComponentRW<TerrainBlock>(e); b.Fit = next; }
        finally { EditSteps.End(world); }
        return true;
    }
}
