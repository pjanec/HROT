using System;
using System.Collections.Generic;
using System.Numerics;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ O-2 (CE-4416, docs/DESIGN_Terrain_Authoring.md §18a) — the general world MERGE: a terrain's world plus an overlay's world (parsed by the
    /// same <see cref="TerrainWorldParser"/>, indices from 0) become ONE <see cref="TerrainWorld"/>.
    /// <para>The overlay APPENDS: base parts keep their indices (every saved / replicated state keyed by them stays valid); overlay part <c>i</c> of a list
    /// lands at <c>base.Count + i</c>, and every cross-index is offset (−1 stays −1): <see cref="TerrainPrism.Panel"/>/<see cref="TerrainPrism.Building"/>,
    /// <see cref="TerrainWalkable.Building"/>, <see cref="TerrainWallPanel.Building"/>, <see cref="TerrainDoorDef.Panel"/> (its <c>Opening</c> is panel-local —
    /// not offset). The merged world is the base the runtime obstacle bake (<see cref="TerrainObstacles.With(TerrainWorld, IReadOnlyList{TerrainPrism})"/>) lays over.</para>
    /// <para>Name, SourceFolder, Height, GroundZ, Materials, Breaches and Beyond are the base's; bounds are the union; <see cref="TerrainWorld.SeedFolders"/> =
    /// the base's, then the overlay's folder. Pure; allocates only here, at merge time.</para>
    /// </summary>
    public static class TerrainWorldMerge
    {
        /// <summary>
        /// ⭐⭐ <c>CE-4597</c> (P-3; 📄 docs/DESIGN_Ground_Patches_And_Road_Blockage.md §4 G-5, G-6) — the base's heights with every overlay house that stands on a pad
        /// (<see cref="TerrainBuilding.Pad"/>) cut in: <see cref="GroundPatches.PadOf"/> at its base, applied copy-on-write (the base grid is unchanged and shares every
        /// tile no pad touches). Every node merges the same overlay over the same base ⇒ the same ground everywhere; the editor's live bake merges the same way.
        /// </summary>
        public static TerrainHeightGrid? PadHeights(TerrainHeightGrid? height, TerrainWorld overlay)
        {
            if (height == null) return null;
            List<GroundPatch>? pads = null;
            foreach (var b in overlay.Buildings)
                if (b.Pad && b.Footprint.Length >= 3)
                    (pads ??= new List<GroundPatch>()).Add(GroundPatches.PadOf(b.Footprint, b.BaseZ, height.Sample));
            return pads == null ? height : GroundPatches.Apply(height, pads);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-5025</c> BE-1 (📄 docs/DESIGN_Terrain_Base_Edits.md D-2) — <paramref name="world"/> without the buildings named in <paramref name="labels"/>: a building RECORD of
        /// that label goes with every part that belongs to it (its panels, the prisms of its panels and of it — roof caps, floors, a structure's blocks —, its walkables, the doors
        /// in its panels, its baked collapse), and a SOLID BLOCK prism of that label (an imported solid house is only a prism, no record) goes too. Every remaining index is
        /// re-pointed (−1 stays −1); a label the world does not have is a warning, never an error (the base may have been re-imported). Pure; allocates only here.
        /// </summary>
        /// <summary>⭐ CE-5030 — how many doors the building <paramref name="label"/> has (a solid block: none).</summary>
        private static int DoorsOf(TerrainWorld world, string label)
        {
            int b = -1;
            for (int i = 0; i < world.Buildings.Count; i++) if (world.Buildings[i].Label == label) { b = i; break; }
            if (b < 0) return 0;
            int n = 0;
            foreach (var d in world.Doors) if (d.Panel >= 0 && d.Panel < world.Panels.Count && world.Panels[d.Panel].Building == b) n++;
            return n;
        }

        public static TerrainWorld Without(TerrainWorld world, IReadOnlyCollection<string> labels)
        {
            var set = labels as ISet<string> ?? new HashSet<string>(labels, StringComparer.Ordinal);
            var bMap = new int[world.Buildings.Count];
            var buildings = new List<TerrainBuilding>(world.Buildings.Count);
            var found = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < world.Buildings.Count; i++)
            {
                var b = world.Buildings[i];
                if (set.Contains(b.Label)) { bMap[i] = -1; found.Add(b.Label); continue; }
                bMap[i] = buildings.Count; buildings.Add(b);
            }
            int B(int i) => i < 0 ? i : (i < bMap.Length ? bMap[i] : i);

            var pMap = new int[world.Panels.Count];
            var panels = new List<TerrainWallPanel>(world.Panels.Count);
            for (int i = 0; i < world.Panels.Count; i++)
            {
                var p = world.Panels[i];
                if (p.Building >= 0 && B(p.Building) < 0) { pMap[i] = -1; continue; }
                pMap[i] = panels.Count;
                panels.Add(p.Building < 0 || B(p.Building) == p.Building ? p : new TerrainWallPanel
                {
                    A = p.A, B = p.B, Thickness = p.Thickness, BaseZ = p.BaseZ, TopZ = p.TopZ, Material = p.Material, Openings = p.Openings,
                    Building = B(p.Building), Storey = p.Storey, Label = p.Label,
                });
            }
            int P(int i) => i < 0 ? i : (i < pMap.Length ? pMap[i] : i);

            var prisms = new List<TerrainPrism>(world.Prisms.Count);
            foreach (var p in world.Prisms)
            {
                if (p.Building >= 0 && B(p.Building) < 0) continue;
                if (p.Panel >= 0 && P(p.Panel) < 0) continue;
                if (p.Building < 0 && p.Panel < 0 && p.Kind == TerrainPrismKind.Building && p.Label is { } solid && set.Contains(solid)) { found.Add(solid); continue; }   // an imported SOLID house: a block, no record
                prisms.Add(B(p.Building) == p.Building && P(p.Panel) == p.Panel ? p : new TerrainPrism
                {
                    Kind = p.Kind, Footprint = p.Footprint, BaseZ = p.BaseZ, TopZ = p.TopZ, Floors = p.Floors, Label = p.Label, Triangles = p.Triangles,
                    Min = p.Min, Max = p.Max, Material = p.Material, Panel = P(p.Panel), Building = B(p.Building),
                });
            }

            var walkables = new List<TerrainWalkable>(world.Walkables.Count);
            foreach (var w in world.Walkables)
            {
                if (w.Building >= 0 && B(w.Building) < 0) continue;
                walkables.Add(B(w.Building) == w.Building ? w : new TerrainWalkable
                {
                    Kind = w.Kind, Vertices = w.Vertices, Triangles = w.Triangles, Min = w.Min, Max = w.Max, MinZ = w.MinZ, MaxZ = w.MaxZ, Building = B(w.Building),
                });
            }

            var doors = new List<TerrainDoorDef>(world.Doors.Count);
            foreach (var d in world.Doors)
            {
                if (P(d.Panel) < 0) continue;
                doors.Add(P(d.Panel) == d.Panel ? d : new TerrainDoorDef { Key = d.Key, Panel = P(d.Panel), Opening = d.Opening, Initial = d.Initial, Center = d.Center, SillZ = d.SillZ });
            }

            var baked = new List<int>(world.BakedCollapsed.Count);
            foreach (int c in world.BakedCollapsed) if (B(c) >= 0) baked.Add(B(c));

            var warnings = new List<string>(world.Warnings);
            foreach (var l in set)
                if (!found.Contains(l)) warnings.Add($"[Terrain] an overlay removes building '{l}', which the terrain '{world.Name}' does not have — ignored.");

            var result = new TerrainWorld
            {
                Name = world.Name, SourceFolder = world.SourceFolder, SeedFolders = world.SeedFolders, BoundsMin = world.BoundsMin, BoundsMax = world.BoundsMax,
                GroundZ = world.GroundZ, Height = world.Height, Warnings = warnings, Prisms = prisms, Walkables = walkables, Surfaces = world.Surfaces,
                GroundShapes = world.GroundShapes, Panels = panels, Buildings = buildings, Doors = doors, Materials = world.Materials, Breaches = world.Breaches,
                BakedCollapsed = baked, RoadCorridors = world.RoadCorridors, BareHeight = world.BareHeight, HeightCraters = world.HeightCraters,
                BakedPassages = world.BakedPassages, Minefields = world.Minefields,
            };
            if (world.Beyond is { } beyond) result.AttachBeyond(beyond);
            return result;
        }

        /// <summary>⭐ CE-4592 (V-28) — every building footprint of both worlds: a ground shape leaves them (and a margin) as islands, never pushing the ground into a house.</summary>
        private static List<Vector2[]>? IslandsOf(TerrainWorld @base, TerrainWorld overlay)
        {
            if (overlay.GroundShapes.Count == 0 && overlay.RoadCorridors.Count == 0) return null;
            var list = new List<Vector2[]>(@base.Buildings.Count + overlay.Buildings.Count);
            foreach (var b in @base.Buildings) if (b.Footprint.Length >= 3) list.Add(b.Footprint);
            foreach (var b in overlay.Buildings) if (b.Footprint.Length >= 3) list.Add(b.Footprint);
            return list;
        }

        /// <summary>⭐ CE-1963 — the grid a flat terrain's ground shapes are carved into: the world's bounds at <see cref="TerrainWorld.GroundZ"/>, 1 m cells (coarser on a world
        /// wider than 2 km, ≤ 2049 posts a side); with no bounds, the shapes' box + 50 m. Outside it the ground is <c>GroundZ</c> as before (the grid's untouched edge).</summary>
        public static TerrainHeightGrid FlatGrid(TerrainWorld @base, IReadOnlyList<GroundShape> shapes)
        {
            Vector2 min = @base.BoundsMin, max = @base.BoundsMax;
            if (!(max.X > min.X && max.Y > min.Y))
            {
                min = new Vector2(float.MaxValue); max = new Vector2(float.MinValue);
                foreach (var s in shapes) foreach (var v in s.Polygon) { min = Vector2.Min(min, v); max = Vector2.Max(max, v); }
                min -= new Vector2(50f); max += new Vector2(50f);
            }
            float extent = MathF.Max(max.X - min.X, max.Y - min.Y);
            float cell = MathF.Max(1f, MathF.Ceiling(extent / 2048f));
            int cols = (int)MathF.Ceiling((max.X - min.X) / cell) + 1, rows = (int)MathF.Ceiling((max.Y - min.Y) / cell) + 1;
            var z = new float[cols * rows];
            if (@base.GroundZ != 0f) Array.Fill(z, @base.GroundZ);
            return new TerrainHeightGrid(min, cell, cols, rows, z);
        }

        private static IReadOnlyList<GroundShape> Concat(IReadOnlyList<GroundShape> a, IReadOnlyList<GroundShape> b)
        {
            if (b.Count == 0) return a;
            if (a.Count == 0) return b;
            var l = new List<GroundShape>(a.Count + b.Count); l.AddRange(a); l.AddRange(b); return l;
        }

        public static TerrainWorld Merge(TerrainWorld @base, TerrainWorld overlay)
        {
            if (@base == null) throw new ArgumentNullException(nameof(@base));
            if (overlay == null) throw new ArgumentNullException(nameof(overlay));

            // keys must stay globally unique: a door key / building label is what saved state and the damage path name a part by
            var labels = new HashSet<string>(StringComparer.Ordinal);
            foreach (var b in @base.Buildings) labels.Add(b.Label);
            foreach (var b in overlay.Buildings)
                if (labels.Contains(b.Label))
                    throw new ArgumentException($"[Terrain] Cannot merge overlay into '{@base.Name}': building label '{b.Label}' exists in both the base terrain and the overlay '{overlay.SourceFolder}'.");

            var doorKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in @base.Doors) doorKeys.Add(d.Key);
            foreach (var d in overlay.Doors)
                if (doorKeys.Contains(d.Key))
                    throw new ArgumentException($"[Terrain] Cannot merge overlay into '{@base.Name}': door key '{d.Key}' exists in both the base terrain and the overlay '{overlay.SourceFolder}'.");
            // ⭐ CE-5025 BE-1 (📄 docs/DESIGN_Terrain_Base_Edits.md D-2) — the overlay REMOVES base buildings by label first: the base is compacted (the building
            //   record with its panels, prisms, walkables and doors, or a solid block prism of that label) and every base index re-pointed; then the overlay appends
            var removed = new List<TerrainRemovedBuilding>(overlay.RemovedBuildings.Count);   // ⭐ CE-5030 — what was taken, as the base had it (the ghost, the Details)
            foreach (var label in overlay.RemovedBuildings)
                if (FeatureExpander.BaseFootprint(@base, label, out var footprint, out float floorZ)) removed.Add(new TerrainRemovedBuilding(label, footprint, floorZ, DoorsOf(@base, label)));
            if (overlay.RemovedBuildings.Count > 0) @base = Without(@base, overlay.RemovedBuildings);
            int panb = @base.Panels.Count, bb = @base.Buildings.Count;
            static int Off(int index, int by) => index < 0 ? index : index + by;

            var prisms = new List<TerrainPrism>(@base.Prisms.Count + overlay.Prisms.Count);
            prisms.AddRange(@base.Prisms);
            foreach (var p in overlay.Prisms)
                prisms.Add(new TerrainPrism
                {
                    Kind = p.Kind, Footprint = p.Footprint, BaseZ = p.BaseZ, TopZ = p.TopZ, Floors = p.Floors, Label = p.Label, Triangles = p.Triangles,
                    Min = p.Min, Max = p.Max, Material = p.Material, Panel = Off(p.Panel, panb), Building = Off(p.Building, bb),
                });

            var walkables = new List<TerrainWalkable>(@base.Walkables.Count + overlay.Walkables.Count);
            walkables.AddRange(@base.Walkables);
            foreach (var w in overlay.Walkables)
                walkables.Add(new TerrainWalkable
                {
                    Kind = w.Kind, Vertices = w.Vertices, Triangles = w.Triangles, Min = w.Min, Max = w.Max, MinZ = w.MinZ, MaxZ = w.MaxZ, Building = Off(w.Building, bb),
                });

            var panels = new List<TerrainWallPanel>(panb + overlay.Panels.Count);
            panels.AddRange(@base.Panels);
            foreach (var p in overlay.Panels)
                panels.Add(new TerrainWallPanel
                {
                    A = p.A, B = p.B, Thickness = p.Thickness, BaseZ = p.BaseZ, TopZ = p.TopZ, Material = p.Material, Openings = p.Openings,
                    Building = Off(p.Building, bb), Storey = p.Storey, Label = p.Label,
                });

            var buildings = new List<TerrainBuilding>(bb + overlay.Buildings.Count);
            buildings.AddRange(@base.Buildings);
            buildings.AddRange(overlay.Buildings);

            var doors = new List<TerrainDoorDef>(@base.Doors.Count + overlay.Doors.Count);
            doors.AddRange(@base.Doors);
            foreach (var d in overlay.Doors)
                doors.Add(new TerrainDoorDef { Key = d.Key, Panel = d.Panel + panb, Opening = d.Opening, Initial = d.Initial, Center = d.Center, SillZ = d.SillZ });

            var surfaces = new List<TerrainSurface>(@base.Surfaces.Count + overlay.Surfaces.Count);
            surfaces.AddRange(@base.Surfaces);
            surfaces.AddRange(overlay.Surfaces);

            var baked = new List<int>(@base.BakedCollapsed.Count + overlay.BakedCollapsed.Count);
            baked.AddRange(@base.BakedCollapsed);
            foreach (int c in overlay.BakedCollapsed) baked.Add(c + bb);

            var warnings = new List<string>(@base.Warnings.Count + overlay.Warnings.Count);
            warnings.AddRange(@base.Warnings);
            warnings.AddRange(overlay.Warnings);

            var seeds = new List<string>();
            foreach (var f in @base.SeedFolders) seeds.Add(f);
            foreach (var f in overlay.SeedFolders) if (!seeds.Contains(f)) seeds.Add(f);

            bool overlayHasParts = prisms.Count > @base.Prisms.Count || walkables.Count > @base.Walkables.Count || surfaces.Count > @base.Surfaces.Count;
            var shapes = GroundShapes.OffCorridors(overlay.GroundShapes, overlay.RoadCorridors);   // ⭐ CE-4593 (G-29) — a ditch stops at a road
            var islands = IslandsOf(@base, overlay);
            var merged = new TerrainWorld
            {
                Name = @base.Name,
                SourceFolder = @base.SourceFolder,
                SeedFolders = seeds,
                BoundsMin = overlayHasParts ? Vector2.Min(@base.BoundsMin, overlay.BoundsMin) : @base.BoundsMin,
                BoundsMax = overlayHasParts ? Vector2.Max(@base.BoundsMax, overlay.BoundsMax) : @base.BoundsMax,
                GroundZ = @base.GroundZ,
                // ⭐ CE-1963 / CE-1964 — a FLAT terrain (no height grid, only GroundZ: bt-range, the test ranges) gets a flat grid the shapes can carve: ⛔ was
                //   GroundShapes.Apply(null, …) ⇒ null, so every ditch, raise, lower and level vanished there silently (R-248: never count on the ground being flat)
                Height = RoadCorridors.Relevel(PadHeights(GroundShapes.Apply(@base.Height ?? (shapes.Count > 0 ? FlatGrid(@base, shapes) : null), shapes, islands), overlay), overlay.RoadCorridors, islands),   // ⭐ CE-4592 (G-18, G-24) — shapes, pads on the shaped ground, then the touched road corridors re-levelled
                Warnings = warnings,
                Prisms = prisms,
                Walkables = walkables,
                Surfaces = surfaces,
                GroundShapes = Concat(@base.GroundShapes, shapes),   // ⭐ CE-4593 — the ditches' navmesh areas read them
                Panels = panels,
                Buildings = buildings,
                Doors = doors,
                Materials = @base.Materials,
                Breaches = @base.Breaches,
                BakedCollapsed = baked,
                Removed = removed,   // ⭐ CE-5030
            };
            if (@base.Beyond is { } beyond) merged.AttachBeyond(beyond);
            return merged;
        }
    }
}
