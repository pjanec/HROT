namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>
    /// ⭐ <c>CE-3117</c> — the map layer bits of the debug-trace gizmos, toggled by <c>LayerControlDto</c> (bits 0–3 are Entities,
    /// Perception, AiHelpers, FireTraces). 📄 docs/DESIGN_Terrain_Combat_Tuning.md §5a.
    /// </summary>
    public static class DebugTraceLayers
    {
        public const byte FireTraces = 3;
        public const byte Doors      = 4;
        public const byte Paths      = 5;
        public const byte Blast      = 6;
        public const byte Hearing    = 7;
        /// <summary>⭐ <c>CE-3124</c> — the loaded terrain's road network (<c>RoadNetworkGizmo</c>).</summary>
        public const byte Roads      = 8;
        /// <summary>⭐ Stage 7a (<c>CE-3134</c>) — the terrain's cover points and window firing positions (<c>CoverPointsGizmo</c>).</summary>
        public const byte Cover      = 9;
        /// <summary>⭐ <c>CE-3133</c> — the baked navmesh's polygons (<c>NavmeshGizmo</c>).</summary>
        public const byte Navmesh    = 10;
        /// <summary>⭐ CE-1033 S5b — the entity CARDS: frame in the side colour, name, health bar (docs/DESIGN_Map_3D_Mode.md §3.6).</summary>
        public const byte Labels     = 11;
        /// <summary>⭐ CE-1065 — per-unit annotations nobody needs on every unit all the time: the heading arrow and its degree label
        /// (<c>EntityRotationGizmo</c>) and the stance label (<c>StanceGizmo</c>). Off by default.</summary>
        public const byte UnitDetails = 12;
        /// <summary>⭐ CE-1065 — the spatial hash's tiles (<c>SpatialGridGizmo</c>): hundreds of long lines over the whole map. Off by
        /// default; before, only the Entities layer could hide it.</summary>
        public const byte SpatialGrid = 13;
        /// <summary>⭐ <c>CE-1225</c> (docs/DESIGN_Command_Display.md §8 S-1) — every SECTOR: the units' sector areas, the NoFire areas and
        /// the arcs (<c>SectorAreasGizmo</c>, <c>SectorArcGizmo</c>). Off by default; before, only the Entities layer could hide them.</summary>
        public const byte Sectors = 14;
        /// <summary>⭐ <c>CE-1227</c> (docs/DESIGN_Terrain_World.md §10; the "Terrain (2-D)" row of DESIGN_Terrain_Combat_Tuning.md §5e FL-1) —
        /// the 2-D map's terrain plan, drawn by <c>TerrainLayer2D</c>. On by default; before, it rode on the Entities layer (0), so
        /// unticking Entities hid the city.</summary>
        public const byte Terrain2D = 15;
        /// <summary>⭐ CE-1160 — the SELECTION ring (<c>SelectionHighlightGizmo</c>): above every bit a layer toggle owns
        /// (<c>LayerControlDto.FirstUntoggledLayer</c>), so it is always drawn. ⭐ <c>CE-1225</c> (§8 S-4): moved from 14 to the TOP bit, so the
        /// toggles stay one range from 0 and a new toggle never lands on it. 🔒 User, 2026-10-16: <i>"I need the entity selection and right
        /// click work always independently of what i pick in the debug layers menu."</i> It used to sit on layer 0 with the entity symbols,
        /// and turning <c>Entities</c> off hid it.</summary>
        public const byte Selection = 255;
    }
}

namespace Fdp.Toolkit.Diagnostics.Gizmos
{
    /// <summary>⭐ <c>CE-3117</c> — the sim time a debug trace is aged against (R-143: sim time only). Restored with the world on a
    /// replay seek, so a trace's age is the same live and replayed.</summary>
    public static class DebugTraceClock
    {
        public static double Now(Fdp.Core.EntityRepository repo) =>
            repo.HasSingleton<Fdp.Core.GlobalTime>() ? repo.GetSingleton<Fdp.Core.GlobalTime>().TotalTime : 0d;

        /// <summary>1 at age 0, falling to 0 at <paramref name="shown"/>; negative when the trace is not shown.</summary>
        public static float Fade(double age, double shown) => age < 0d || age > shown ? -1f : (float)(1d - age / shown);
    }
}
