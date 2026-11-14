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
    /// ⭐ O-4 (CE-4415, §18c) → T-2 (CE-4429, §18g) — ONE placed terrain feature. <see cref="Kind"/> says which; its position is the entity's <c>SimTransform</c>. LINE kinds (wall, fence, hedge, bridge) and AREA kinds
    /// (wood, scrub, water, scatter) carry their shape in an <c>EditablePolyline</c> beside this component (points RELATIVE to the entity's position — a managed, rarely changed, immutable record: the unmanaged
    /// component's 1 024-byte cap rules out a fixed vertex list, G-1). Authored and saved; expanded by <see cref="OverlayExpansion"/> (the compiler at save, the editor's live overlay while it is open); a part's label is
    /// <c>&lt;entity guid&gt;/&lt;i&gt;</c>. Unmanaged (recorded as bytes). ⚠ <see cref="Seed"/> was added in T-2: a recording made with the O-4 layout has no such field.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [ComponentId(GlobalComponentIds.TerrainFeature)]
    [DataPolicy(DataPolicy.Default)]
    public struct TerrainFeature
    {
        public const byte House = 1, Wall = 2, Fence = 3, Hedge = 4, Wood = 5, Tree = 6, Scrub = 7, Clutter = 8, Water = 9, Bridge = 10, Scatter = 11, Lane = 12;

        /// <summary>One of the kind constants above; 0 and anything unknown are ignored.</summary>
        public byte Kind;
        /// <summary>By kind — house: the building template · wall / fence / hedge: the material (empty = the kind's default) · clutter: the type (<see cref="ClutterTypes"/>) · scatter: the recipe id.</summary>
        public FixedString32 Template;
        /// <summary>The heading of a house, a clutter item or a tree.</summary>
        public float HeadingDeg;
        /// <summary>
        /// By kind — house: a level pad's height (written as the feature's <c>pad</c> when non-zero) · wall / fence / hedge: the height in metres (0 = the kind's default) · bridge: the deck's width (0 = 4 m) ·
        /// wood: trees per hectare (0 = the default) · lane (T-3): unused · scatter: items per 100 m² (0 = the recipe's density).
        /// </summary>
        public float Pad;
        /// <summary>The seed of a wood, a scrub patch or a scatter (it draws from it, deterministically).</summary>
        public uint Seed;
    }
}
