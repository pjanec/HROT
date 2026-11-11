using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Scenario;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common;
using Hrot.Map.Common.Components;
using Hrot.Map.Common.Services;
using Hrot.UI.Common.AddEntity;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// ⭐⭐ Terrain authoring U-3 (docs/DESIGN_Terrain_Authoring.md §15a; mockup 2) — a placed terrain BLOCK on the map: a dashed outline round what the block EXPANDED to, with its chip (<i>Hamlet</i>; selected: <i>Town block · seed 4471</i>), the
    /// outline turning solid blue when the block is selected; clickable on its outline (select, and the block menu — <see cref="Hrot.Common.Actions.ActionTarget.TerrainBlock"/>). The extent comes from the LIVE bake's
    /// <see cref="LiveTerrainBakeInfo"/> (published after each committed bake), so the outline FOLLOWS every change — a re-roll, a move, a recipe change — with no second expansion here; a block the bake has not held yet draws nothing.
    /// ⭐ HOT PATH (R-2084): a cached query side — per entity a rectangle (4 points) and a chip string rebuilt only when the bounds / recipe / seed change; 0 B steady.
    /// </summary>
    [GizmoProjector(typeof(TerrainBlock), DrawsOffScreen = true)]
    public sealed class TerrainBlockGizmo : IStatelessGizmo
    {
        public static readonly Rgba32 OutlineColour = new(205, 214, 224, 215), SelectedColour = new(86, 166, 250, 255);
        /// <summary>Metres the outline stands off the outermost part.</summary>
        public const float Margin = 4f;

        private sealed class Cache
        {
            public BlockBounds Bounds; public ulong Key;
            public readonly List<Vector2> Corners = new(4) { default, default, default, default };
            public string Chip = "", ChipSelected = "";
        }

        private readonly Dictionary<int, Cache> _cache = new();

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (view is not EntityRepository repo || !repo.HasSingletonManaged<LiveTerrainBakeInfo>()) return;
            if (!view.HasComponent<TerrainBlock>(entity) || !view.HasComponent<EntityGuid>(entity)) return;
            var info = repo.GetSingletonManaged<LiveTerrainBakeInfo>();
            if (info is null || !info.TryGet(view.GetComponentRO<EntityGuid>(entity).Value, out var bounds)) return;

            ref readonly var block = ref view.GetComponentRO<TerrainBlock>(entity);
            ulong key = Fnv(bounds, block.Seed, block.Recipe.GetHashCode());
            if (!_cache.TryGetValue(entity.Index, out var c)) _cache[entity.Index] = c = new Cache { Key = ~key };
            if (c.Key != key)   // only on a change: the rectangle and the two chip texts
            {
                c.Key = key; c.Bounds = bounds;
                var min = bounds.Min - new Vector2(Margin); var max = bounds.Max + new Vector2(Margin);
                c.Corners[0] = min; c.Corners[1] = new Vector2(max.X, min.Y); c.Corners[2] = max; c.Corners[3] = new Vector2(min.X, max.Y);
                c.Chip = ChipText(block.Recipe.ToString() ?? "", block.Seed, selected: false);
                c.ChipSelected = ChipText(block.Recipe.ToString() ?? "", block.Seed, selected: true);
            }

            bool selected = view.HasComponent<SelectionState>(entity) && view.GetComponentRO<SelectionState>(entity).IsSelected;
            var colour = selected ? SelectedColour : OutlineColour;
            byte layer = DebugTraceLayers.TacticalGraphics;
            for (int i = 0; i < 4; i++)
            {
                var a = c.Corners[i]; var b = c.Corners[(i + 1) & 3];
                draw.DrawLine(new Vector3(a.X, a.Y, 0f), new Vector3(b.X, b.Y, 0f), colour, selected ? 2.4f : 1.6f, layer: layer,
                              style: selected ? LineStyle.Solid : LineStyle.Dashed);
            }
            // the chip sits on the outline's top-left corner, as the mockup's (north-up: the top edge is +Y)
            draw.DrawTextLong(c.Corners[3].X, c.Corners[3].Y, selected ? c.ChipSelected : c.Chip, colour, layer: layer, fontSizePx: 12f, lineOffsetPx: -2f, style: GizmoTextStyle.Backdrop);
            EntityPresentationGizmoShared.EmitPickSegments(draw, view, entity, c.Corners, Vector2.Zero, isClosed: true);   // select on the outline, the menu on right-click
        }

        /// <summary>The chip: <c>Hamlet</c>, selected <c>Hamlet · seed 4471</c> (the mockup's).</summary>
        public static string ChipText(string recipe, uint seed, bool selected)
            => selected ? TerrainPickCatalog.Title(recipe) + " · seed " + seed : TerrainPickCatalog.Title(recipe);

        private static ulong Fnv(BlockBounds b, uint seed, int recipe)
        {
            ulong h = 1469598103934665603UL;
            void Mix(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
            void MixF(float f) => Mix((long)System.MathF.Round(f * 100f));
            MixF(b.Min.X); MixF(b.Min.Y); MixF(b.Max.X); MixF(b.Max.Y); Mix(b.Houses); Mix(b.Woods); Mix(seed); Mix(recipe);
            return h;
        }
    }
}
