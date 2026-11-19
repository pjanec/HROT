using System.Globalization;
using System.Numerics;
using Fdp.Toolkit.Terrain;

namespace Hrot.UI.Common.AddEntity;

/// <summary>
/// ⭐⭐ <c>CE-5030</c> BE-4 part 2 (📄 docs/DESIGN_Terrain_Base_Edits.md §5, mockup <c>docs/UX/terrain-base-edits.svg</c>) — what an ARMED building edit shows under the cursor: the BAKED
/// building the click would act on (<see cref="FeatureExpander.BaseBuildingAt"/>, the SAME pick the click uses — <c>TerrainBirth.ResolveEdit</c>), outlined, and three lines:
/// <c>Baked building · &lt;label&gt;[ · NO DOOR]</c> · the facts (SOLID / enterable, storeys, area) · what the click does, or why it cannot. Computed only when the cursor moves
/// half a metre (<see cref="TerrainPlacement.Preview"/> caches) — it allocates then, never per frame.
/// </summary>
public static class BuildingEditHover
{
    public const string NoBuilding = "no baked building here";

    public static TerrainGhostShape Compute(TerrainWorld world, string action, Vector2 at)
    {
        string? label = FeatureExpander.BaseBuildingAt(world, at);
        if (label is null) return new TerrainGhostShape { Line1 = NoBuilding, Line2 = "", Line3 = "click a building that came with the terrain", Fits = false, Highlight = true };
        if (!FeatureExpander.BaseFootprint(world, label, out var footprint, out float floorZ))
            return new TerrainGhostShape { Line1 = "Baked building · " + label, Line3 = "its footprint is unknown", Fits = false, Highlight = true };

        int record = -1;
        for (int i = 0; i < world.Buildings.Count; i++) if (world.Buildings[i].Label == label) { record = i; break; }
        bool solid = record < 0 || world.Buildings[record].Solid;
        bool hasDoor = !solid && HasDoor(world, record);
        int storeys = solid ? Math.Max(1, (int)MathF.Round(FeatureExpander.BaseHeight(world, label, floorZ) / 3f)) : Math.Max(1, world.Buildings[record].Storeys);
        string area = MathF.Round(Area(footprint)).ToString("0", CultureInfo.InvariantCulture);
        string facts = (solid ? "SOLID — no interior" : "enterable") + " · " + storeys.ToString(CultureInfo.InvariantCulture) + (storeys == 1 ? " storey" : " storeys") + " · " + area + " m²";

        string verdict; bool fits;
        switch (action)
        {
            case "enterable":
                fits = solid;
                verdict = solid ? "click: make it enterable" : hasDoor ? "already enterable" : "already enterable — Add door gives it a door";
                break;
            case "door":
                fits = true;
                verdict = solid ? "click: make it enterable with a door here" : "click a free wall to put a door there";
                break;
            default:
                fits = true;
                verdict = "click: remove it — flat ground, its doors and damage go";
                break;
        }
        return new TerrainGhostShape
        {
            Outline = footprint, Highlight = true, Fits = fits,
            Line1 = "Baked building · " + label + (!solid && !hasDoor ? " · NO DOOR" : ""),
            Line2 = facts, Line3 = verdict,
        };
    }

    private static bool HasDoor(TerrainWorld world, int building)
    {
        for (int i = 0; i < world.Doors.Count; i++)
        {
            int panel = world.Doors[i].Panel;
            if (panel >= 0 && panel < world.Panels.Count && world.Panels[panel].Building == building) return true;
        }
        return false;
    }

    private static float Area(Vector2[] p)
    {
        float a = 0f;
        for (int i = 0; i < p.Length; i++) { var u = p[i]; var v = p[(i + 1) % p.Length]; a += u.X * v.Y - v.X * u.Y; }
        return MathF.Abs(a) * 0.5f;
    }
}
