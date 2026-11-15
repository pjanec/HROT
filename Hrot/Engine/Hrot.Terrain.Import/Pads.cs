using System.Numerics;
using Fdp.Toolkit.Terrain;

namespace Hrot.Terrain.Import;

/// <summary>
/// ⭐⭐ <c>CE-4403</c> (P-1; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §4 G-2…G-4, §9.2 V-16, R-4228) — the importer's house pads: one
/// <see cref="GroundPatch"/> per building (its ring, its base — the input set's, the lowest ground under it), applied by the ONE rule
/// (<see cref="GroundPatches.ApplyTo"/>) to the 1 m posts before the store is written. Counts go to the report.
/// </summary>
internal static class Pads
{
    public static void Apply(float[] z, int cols, int rows, IReadOnlyList<BuiltBuilding> built, ImportReport rep, bool[]? roadLock = null)
    {
        float Ground(float x, float y)
        {
            int c = Math.Clamp((int)MathF.Round(x), 0, cols - 1), r = Math.Clamp((int)MathF.Round(y), 0, rows - 1);
            return z[(long)r * cols + c];
        }
        var patches = new List<GroundPatch>(built.Count);
        foreach (var b in built)
        {
            if (b.Ring.Count < 3) continue;
            var ring = new Vector2[b.Ring.Count];
            for (int i = 0; i < ring.Length; i++) ring[i] = new Vector2((float)b.Ring[i].X, (float)b.Ring[i].Y);
            var pad = GroundPatches.PadOf(ring, (float)b.BaseZ, Ground);
            patches.Add(pad);
            if (pad.Skirt > GroundPatches.MinSkirt + 1e-3f) rep.PadsWidened++;
        }
        rep.Pads = patches.Count;
        rep.PadPostsCut = GroundPatches.ApplyTo(z, cols, rows, Vector2.Zero, 1f, patches, roadLock);
    }
}
