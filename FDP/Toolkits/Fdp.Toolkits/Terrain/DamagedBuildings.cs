using System.Collections.Generic;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐⭐ <c>CE-4392</c> (DESIGN_Map_3D_Mode §6aj X-1) — which pieces a building is DRAWN with under the damage of a view: the ONE rule the 3-D chunks and the 2-D plan both ask, so
    /// they cannot disagree. A collapsed building's pieces go; a breached panel is re-cut from its own openings plus ALL its breaches (holes and passages) through the one panel →
    /// prisms function (<see cref="TerrainBuildingExpander.ExpandPanelPieces"/>) and the one gap merge (<see cref="TerrainObstacles.WithGaps"/>), the navmesh overlay's own. ⭐ It is
    /// idempotent over a muscle's REBAKED world (R-4196: the drawing reads the replicated table; the rebake is a separate path): every piece of a breached panel is replaced, and a
    /// resident rubble prism is skipped (the rubble is drawn from <see cref="BuildingDamageTable.RubbleOf"/>). ⛔ Never changes the world.
    /// </summary>
    public static class DamagedBuildings
    {
        /// <summary>True when <paramref name="p"/> is drawn as it stands under <paramref name="damage"/>.</summary>
        public static bool Keeps(TerrainPrism p, BuildingDamageTable damage)
        {
            if (p.Building >= 0 && damage.Collapsed(p.Building)) return false;   // a collapsed building's pieces
            if (p.Panel >= 0 && damage.HasBreaches(p.Panel)) return false;        // re-cut by RecutPanel
            if (IsResidentRubble(p)) return false;                                // a rebake's own mound: drawn from the table instead
            return true;
        }

        /// <summary>True when <paramref name="w"/> (a slab, a ramp) is drawn under <paramref name="damage"/>.</summary>
        public static bool Keeps(TerrainWalkable w, BuildingDamageTable damage) => !(w.Building >= 0 && damage.Collapsed(w.Building));

        /// <summary>The rubble prism a muscle's rebake put into its derived world (<c>TerrainObstacles.With</c>: <c>Building = −1</c>, the table's label).</summary>
        public static bool IsResidentRubble(TerrainPrism p)
            => p.Building < 0 && p.Material?.Name == BuildingDamageTable.RubbleMaterial && p.Label != null && p.Label.EndsWith(" (rubble)", System.StringComparison.Ordinal);

        /// <summary>True when panel <paramref name="panel"/> is drawn re-cut (it has a breach and its building stands).</summary>
        public static bool IsRecut(TerrainWorld world, BuildingDamageTable damage, int panel)
            => damage.HasBreaches(panel) && !damage.Collapsed(world.Panels[panel].Building);

        /// <summary>The pieces of breached panel <paramref name="panel"/>: its openings with every breach a Gap, through the parser's own expander. Allocates (a rebuild, never a frame).</summary>
        public static void RecutPanel(TerrainWorld world, BuildingDamageTable damage, int panel, List<TerrainPrism> into)
        {
            var breaches = new List<WallBreach>();
            foreach (var b in damage.BreachesOf(panel)) breaches.Add(b);
            if (breaches.Count == 0) return;
            var p = world.Panels[panel];
            TerrainBuildingExpander.ExpandPanelPieces(p, panel, TerrainObstacles.WithGaps(p, breaches), into, "damage drawing");
        }

        /// <summary>The distinct panels with a breach, ascending (the table is sorted by panel).</summary>
        public static void BreachedPanels(BuildingDamageTable damage, List<int> into)
        {
            for (int i = 0; i < damage.Count; i++)
            {
                int p = damage[i].Panel;
                if (into.Count == 0 || into[^1] != p) into.Add(p);
            }
        }

        /// <summary>
        /// The buildings whose drawing differs between <paramref name="before"/> and <paramref name="after"/> (either may be null = no damage): every building collapsed or
        /// breached in either. ⭐ A superset is fine — a rebuild of an unchanged building draws the same. Allocates (on a damage change only).
        /// </summary>
        public static void Touched(TerrainWorld world, BuildingDamageTable? before, BuildingDamageTable? after, HashSet<int> into)
        {
            foreach (var d in new[] { before, after })
            {
                if (d == null) continue;
                foreach (int b in d.CollapsedBuildings) into.Add(b);
                for (int i = 0; i < d.Count; i++)
                {
                    int p = d[i].Panel;
                    if ((uint)p < (uint)world.Panels.Count) into.Add(WallGroups.Of(world, p));   // ⭐ CE-4599 — a free-wall line too (its group follows the buildings)
                }
            }
        }
    }
}
