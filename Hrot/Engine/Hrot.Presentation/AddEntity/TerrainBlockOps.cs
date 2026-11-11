using Fdp.Core;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Terrain;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐⭐ Terrain authoring U-3 (docs/DESIGN_Terrain_Authoring.md §15a; §18h H-1/H-2) — the block menu's WRITES: <i>Re-roll</i> (a new seed, derived from the current one so a redo is the same layout), <i>Rotate</i> (a quarter turn),
/// <i>Change recipe</i>. Each is ONE edit step (<see cref="EditSteps"/>: the editor's history snapshots the world around the bracket, so Undo restores the block — B-6), and the live bake re-expands only that block (O-7 B), so the outline follows.
/// ⚠ Backend's <c>BlockOperations</c> (T-4, CE-4431) is not built yet; when it is, these three are replaced by it (it owns Unpack too — greyed until then). Until then the SAME seed rule (H-1) lives here, once.
/// </summary>
public static class TerrainBlockOps
{
    /// <summary>H-1 — the seed after <paramref name="seed"/>: deterministic (splitmix64 of it), four digits like a fresh one.</summary>
    public static uint NextSeed(uint seed)
    {
        ulong z = seed + 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL; z ^= z >> 31;
        return 1000u + (uint)(z % 9000UL);
    }

    private static bool IsBlock(EntityRepository world, Entity e) => world.IsAlive(e) && world.IsComponentTypeRegistered<TerrainBlock>() && world.HasComponent<TerrainBlock>(e);

    public static bool Reroll(EntityRepository world, Entity e)
    {
        if (!IsBlock(world, e)) return false;
        EditSteps.Begin(world, "Re-roll block");
        try { ref var b = ref world.GetComponentRW<TerrainBlock>(e); b.Seed = NextSeed(b.Seed); }
        finally { EditSteps.End(world); }
        return true;
    }

    public static bool Rotate(EntityRepository world, Entity e)
    {
        if (!IsBlock(world, e)) return false;
        EditSteps.Begin(world, "Rotate block");
        try { ref var b = ref world.GetComponentRW<TerrainBlock>(e); b.HeadingDeg = (b.HeadingDeg + 90f) % 360f; }
        finally { EditSteps.End(world); }
        return true;
    }

    public static bool ChangeRecipe(EntityRepository world, Entity e, string recipeId)
    {
        if (!IsBlock(world, e) || string.IsNullOrWhiteSpace(recipeId)) return false;
        EditSteps.Begin(world, "Change block recipe");
        try { ref var b = ref world.GetComponentRW<TerrainBlock>(e); b.Recipe = new FixedString32(recipeId); }
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
