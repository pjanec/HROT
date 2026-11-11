using System.Runtime.InteropServices;
using Fdp.Core;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ O-4 (CE-4415, docs/DESIGN_Terrain_Authoring.md §18c) — a placed BLOCK of terrain features: a recipe, the seed the expander draws from, the heading, and how it fits the site.
    /// One entity per block (its <c>SimTransform</c> is the block's origin; its guid prefixes every part's stable key). Unmanaged, authored, recorded and saved with the scenario —
    /// ⛔ never expanded at load: the <c>OverlayCompiler</c> expands it at save into the scenario's terrain overlay.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.TerrainBlock)]
    [DataPolicy(DataPolicy.Default)]   // authored world state: recorded, saved, snapshotted
    public struct TerrainBlock
    {
        public const byte AlignRoad = 1, AvoidWater = 2, AvoidBuildings = 4, LevelPads = 8, FitAll = 15;

        /// <summary>The recipe id (<c>assets/terrain-recipes/&lt;id&gt;.recipe.json</c>).</summary>
        public FixedString32 Recipe;
        public uint Seed;
        public float HeadingDeg;
        /// <summary>Four bits (<see cref="AlignRoad"/>…<see cref="LevelPads"/>); the default is all on (<see cref="FitAll"/>).</summary>
        public byte Fit;
    }

    /// <summary>
    /// ⭐ O-4 (CE-4415, §18c) — ONE placed terrain feature; <see cref="Kind"/> 1 = a house (a building template). Its position is the entity's <c>SimTransform</c>. Authored, saved;
    /// expanded by the <c>OverlayCompiler</c> at save (the part is labelled <c>&lt;entity guid&gt;/0</c>).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.TerrainFeature)]
    [DataPolicy(DataPolicy.Default)]
    public struct TerrainFeature
    {
        public const byte House = 1;

        public byte Kind;
        /// <summary>The building template (<c>assets/buildings/&lt;name&gt;.building.json</c>).</summary>
        public FixedString32 Template;
        public float HeadingDeg;
        /// <summary>Written as the feature's <c>pad</c> property when non-zero (a level pad's height; flattening is later work).</summary>
        public float Pad;
    }
}
