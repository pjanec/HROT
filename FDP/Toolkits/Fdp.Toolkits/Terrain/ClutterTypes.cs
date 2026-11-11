using System;
using System.Collections.Generic;

namespace Fdp.Toolkit.Terrain
{
    /// <summary>
    /// ⭐ T-2 (CE-4429, docs/DESIGN_Terrain_Authoring.md §18g G-3) — the CLUTTER table (§7): the small solid things a block or a scatter places as terrain (a <c>prism</c> of a material), with their box size. ⭐ The first four ARE the
    /// obstacle TKB types (<c>NedTkbCatalog.RegisterObstacles</c> reads its sizes from here — one table, never two): a clutter item and the live obstacle of the same name are the same box of the same material; the live
    /// one stays the entity path (it can be moved, shot, destroyed — baked clutter cannot, G-15). Sizes are starter values, to tune.
    /// </summary>
    public static class ClutterTypes
    {
        /// <summary>A clutter type: its display name, the box (length along the heading, width, height, metres) and the terrain material.</summary>
        public readonly record struct Type(string Name, float Length, float Width, float Height, string Material)
        {
            /// <summary>The id a recipe or a feature names it by: the name, lower-case, spaces as dashes (<c>"sandbag-wall"</c>).</summary>
            public string Id => Name.ToLowerInvariant().Replace(' ', '-');
        }

        public static readonly Type Car = new("Car", 4.5f, 1.8f, 1.5f, "car-body");
        public static readonly Type SandbagWall = new("Sandbag wall", 3.0f, 0.6f, 1.0f, "sandbags");
        public static readonly Type ConcreteBlock = new("Concrete block", 2.0f, 1.0f, 1.0f, "concrete");
        public static readonly Type Crate = new("Crate", 1.2f, 1.0f, 1.0f, "fence-wood");
        public static readonly Type Barrel = new("Barrel", 0.6f, 0.6f, 0.9f, "steel-plate");
        public static readonly Type JerseyBarrier = new("Jersey barrier", 3.0f, 0.6f, 0.8f, "concrete");
        /// <summary>⭐ T-3 (CE-4430) — a checkpoint's guard booth (a small wooden box).</summary>
        public static readonly Type GuardBooth = new("Guard booth", 2.4f, 2.4f, 2.6f, "fence-wood");

        public static IReadOnlyList<Type> All { get; } = new[] { Car, SandbagWall, ConcreteBlock, Crate, Barrel, JerseyBarrier, GuardBooth };

        /// <summary>The type called <paramref name="idOrName"/> (a display name or its dashed id, any case).</summary>
        public static bool TryGet(string? idOrName, out Type type)
        {
            if (!string.IsNullOrWhiteSpace(idOrName))
            {
                string id = idOrName.Trim().ToLowerInvariant().Replace(' ', '-');
                foreach (var t in All) if (t.Id == id) { type = t; return true; }
            }
            type = default;
            return false;
        }

        public static Type Get(string? idOrName, string where)
            => TryGet(idOrName, out var t) ? t : throw new ArgumentException($"[Terrain] {where}: unknown clutter type '{idOrName}' (known: {string.Join(", ", System.Linq.Enumerable.Select(All, x => x.Id))}).");
    }
}
