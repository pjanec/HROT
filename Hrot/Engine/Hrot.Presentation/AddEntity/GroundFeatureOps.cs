using Fdp.Core;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Terrain;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐⭐ <c>CE-4592</c> / <c>CE-4593</c> (P-5 2b, P-6; mockup <c>ground-shaping-and-ditches.svg</c> C, D) — the Ground / Ditch Details tab's WRITES: the mode (a ground area's Level · Raise · Lower, a ditch's
/// preset) and the value (dZ, Z, the depth). Each is ONE edit step (<see cref="EditSteps"/>: Undo restores the shape) and the live bake re-expands the feature, as the block's ops (<see cref="TerrainBlockOps"/>).
/// ⭐ The draw ORDER (<see cref="TerrainFeature.Seed"/>) is never touched — an edit does not make a shape newer (V-28: the author sees which wins by what was drawn last).
/// </summary>
public static class GroundFeatureOps
{
    public static bool IsGround(EntityRepository world, Entity e)
        => world.IsAlive(e) && world.IsComponentTypeRegistered<TerrainFeature>() && world.HasComponent<TerrainFeature>(e) && world.GetComponentRO<TerrainFeature>(e).Kind == TerrainFeature.Ground;

    /// <summary>Switches the mode (<c>level</c> / <c>raise</c> / <c>lower</c>, or a ditch preset) and sets <paramref name="value"/> with it — one edit step; no change when both already are so.</summary>
    public static bool SetMode(EntityRepository world, Entity e, string mode, float value)
    {
        if (!IsGround(world, e)) return false;
        var f = world.GetComponentRO<TerrainFeature>(e);
        if (f.Template.ToString() == mode && f.Pad == value) return false;
        GroundShapes.ModeOf(mode, "the Details mode");   // ⛔ an unknown mode is refused here, not at the save
        EditSteps.Begin(world, "Change ground mode");
        try { ref var w = ref world.GetComponentRW<TerrainFeature>(e); w.Template = new FixedString32(mode); w.Pad = value; }
        finally { EditSteps.End(world); }
        return true;
    }

    /// <summary>Sets the value (a raise / lower dZ ≥ 0.1 m, a level Z, a ditch depth 0 = its preset's or ≥ 0.1 m) — one edit step; refused when out of range or unchanged.</summary>
    public static bool SetValue(EntityRepository world, Entity e, float value)
    {
        if (!IsGround(world, e) || float.IsNaN(value) || float.IsInfinity(value)) return false;
        var f = world.GetComponentRO<TerrainFeature>(e);
        string mode = f.Template.ToString() ?? "";
        bool ok = mode == "level" ? MathF.Abs(value) < 10000f : DitchPresets.Of(mode) is not null ? value == 0f || (value >= 0.1f && value <= 20f) : value >= 0.1f && value <= 50f;
        if (!ok || f.Pad == value) return false;
        EditSteps.Begin(world, "Set ground height");
        try { ref var w = ref world.GetComponentRW<TerrainFeature>(e); w.Pad = value; }
        finally { EditSteps.End(world); }
        return true;
    }
}
