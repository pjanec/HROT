using System;
using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ <c>CE-3488</c> (flame FL-4 B-1, B-4; 📄 docs/designs/weapons/DESIGN_Flame_Weapons.md §7c) — what a building's FIRE is: ONE spot over its footprint (its centre,
    /// a radius to its farthest corner, a metre above its floor) burning as long as its most burnable wall (<see cref="TerrainMaterial.BurnSeconds"/>; 0 = it does not
    /// burn — brick, concrete, stone). ONE rule, read by every igniter (the flame jet, napalm), so they cannot light a building differently.
    /// </summary>
    public static class BuildingFires
    {
        /// <summary>How long <paramref name="building"/> burns (s): its most burnable wall's time; 0 when no wall of it burns.</summary>
        public static float BurnSecondsOf(TerrainWorld world, int building)
        {
            float best = 0f;
            var panels = world.Panels;
            for (int i = 0; i < panels.Count; i++)
                if (panels[i].Building == building && panels[i].Material is { } m && m.BurnSeconds > best) best = m.BurnSeconds;
            return best;
        }

        /// <summary>The footprint's centre and the radius to its farthest corner (world metres).</summary>
        public static (Vector2 Centre, float Radius) Extent(TerrainBuilding b)
        {
            var f = b.Footprint;
            if (f.Length == 0) return (b.Position, 1f);
            var c = Vector2.Zero;
            for (int i = 0; i < f.Length; i++) c += f[i];
            c /= f.Length;
            float r = 0f;
            for (int i = 0; i < f.Length; i++) r = MathF.Max(r, Vector2.Distance(c, f[i]));
            return (c, r);
        }

        /// <summary>The fire of <paramref name="building"/> lit at <paramref name="now"/>; false when it does not burn.</summary>
        public static bool TryOf(TerrainWorld world, int building, double now, float intensity, out BuildingFireEvent fire)
        {
            fire = default;
            if ((uint)building >= (uint)world.Buildings.Count) return false;
            float seconds = BurnSecondsOf(world, building);
            if (seconds <= 0f) return false;
            var b = world.Buildings[building];
            var (c, r) = Extent(b);
            fire = new BuildingFireEvent
            {
                Building = building, X = c.X, Y = c.Y, Z = b.BaseZ + 1f, Radius = r, Since = (SimInstant)now, Seconds = seconds,
                Intensity = intensity > 0f ? MathF.Min(intensity, 1f) : 1f,
            };
            return true;
        }

        /// <summary>The buildings of <paramref name="world"/> any of whose walls burns (built once per world — a few in any terrain).</summary>
        public static IReadOnlyList<int> Burnable(TerrainWorld world)
        {
            if (ReferenceEquals(world, t_world) && t_burnable != null) return t_burnable;
            var set = new SortedSet<int>();
            var panels = world.Panels;
            for (int i = 0; i < panels.Count; i++)
                if (panels[i].Building >= 0 && panels[i].Material is { BurnSeconds: > 0f }) set.Add(panels[i].Building);
            t_burnable = new List<int>(set);
            t_world = world;
            return t_burnable;
        }

        /// <summary>The burnable PANELS of <paramref name="world"/> (indices), built once per world.</summary>
        public static IReadOnlyList<int> BurnablePanels(TerrainWorld world)
        {
            if (ReferenceEquals(world, t_panelWorld) && t_panels != null) return t_panels;
            var list = new List<int>();
            var panels = world.Panels;
            for (int i = 0; i < panels.Count; i++)
                if (panels[i].Building >= 0 && panels[i].Material is { BurnSeconds: > 0f }) list.Add(i);
            t_panels = list;
            t_panelWorld = world;
            return t_panels;
        }

        // ⭐ per thread (BEHAVIOUR CODE RUNS IN PARALLEL: no shared static scratch) — a world's burnable set, built once
        [ThreadStatic] private static TerrainWorld? t_world, t_panelWorld;
        [ThreadStatic] private static List<int>? t_burnable, t_panels;
    }
}
