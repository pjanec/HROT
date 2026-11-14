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
            var merged = new TerrainWorld
            {
                Name = @base.Name,
                SourceFolder = @base.SourceFolder,
                SeedFolders = seeds,
                BoundsMin = overlayHasParts ? Vector2.Min(@base.BoundsMin, overlay.BoundsMin) : @base.BoundsMin,
                BoundsMax = overlayHasParts ? Vector2.Max(@base.BoundsMax, overlay.BoundsMax) : @base.BoundsMax,
                GroundZ = @base.GroundZ,
                Height = PadHeights(@base.Height, overlay),
                Warnings = warnings,
                Prisms = prisms,
                Walkables = walkables,
                Surfaces = surfaces,
                Panels = panels,
                Buildings = buildings,
                Doors = doors,
                Materials = @base.Materials,
                Breaches = @base.Breaches,
                BakedCollapsed = baked,
            };
            if (@base.Beyond is { } beyond) merged.AttachBeyond(beyond);
            return merged;
        }
    }
}
