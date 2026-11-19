using System.Collections.Generic;
using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>The look of a terrain feature on the map: the colour of its kind and whether it is a single point, a line or an area — the ONE table the gizmo and the drawing ghost read.</summary>
    public static class TerrainFeatureLook
    {
        public static readonly Rgba32 Tree = new(91, 208, 106, 255), Wall = new(176, 176, 190, 255), Fence = new(196, 150, 90, 255), Hedge = new(96, 190, 96, 255), Wood = new(60, 170, 70, 255),
                                      Scrub = new(130, 190, 90, 255), Water = new(70, 140, 230, 255), Bridge = new(86, 150, 210, 255), Scatter = new(200, 170, 90, 255), Clutter = new(170, 170, 170, 255),
                                      House = new(230, 150, 70, 255), Ground = new(150, 120, 80, 255), Other = new(200, 180, 120, 255);

        /// <summary>The colour of a feature of <paramref name="kind"/> (a family colour; a wood and a tree row are both greens).</summary>
        public static Rgba32 ColourOf(byte kind) => kind switch
        {
            TerrainFeature.Tree or TerrainFeature.Row or TerrainFeature.Group => Tree,
            TerrainFeature.Wall => Wall, TerrainFeature.Fence => Fence, TerrainFeature.Hedge => Hedge,
            TerrainFeature.Wood => Wood, TerrainFeature.Scrub => Scrub, TerrainFeature.Water => Water, TerrainFeature.Bridge => Bridge,
            TerrainFeature.Scatter => Scatter, TerrainFeature.Clutter => Clutter, TerrainFeature.House => House, TerrainFeature.Ground => Ground,
            _ => Other,
        };

        /// <summary>A single placed item (a handle dot): a tree, a clutter item, a house, a foxhole / crater.</summary>
        public static bool IsPoint(byte kind, string template)
            => kind is TerrainFeature.Tree or TerrainFeature.Clutter or TerrainFeature.House || (kind == TerrainFeature.Ground && template is "foxhole" or "crater");

        /// <summary>A drawn LINE (open): a wall / fence / hedge, a bridge, a tree row, a ditch.</summary>
        public static bool IsLine(byte kind, string template)
            => kind is TerrainFeature.Wall or TerrainFeature.Fence or TerrainFeature.Hedge or TerrainFeature.Bridge or TerrainFeature.Row || (kind == TerrainFeature.Ground && template is "ditch" or "at-ditch" or "berm");
    }

    /// <summary>
    /// ⭐⭐ CE-5011 A (docs/DESIGN_Terrain_Editor_UX.md §5 TE-9 / TE-18; mockup panel F) — a placed terrain FEATURE on the map, drawn only while the Terrain toolbar is open (<see cref="TerrainLock"/>): a single item (a tree, a clutter item, a house, a foxhole) is a small
    /// filled handle dot in its family colour with a white centre, picked by a click; a line / area feature (<see cref="EditablePolyline"/> relative to its <see cref="SimTransform"/>) a thin outline in its kind's colour — dashed when idle, solid blue when selected —
    /// the WHOLE feature pickable as ONE (its outline's segments carry its id). The block's template (<see cref="TerrainBlockGizmo"/>); it replaces the generic entity symbol, which draws nothing for a feature now (one mark, not two).
    /// ⭐ HOT PATH (R-2084): per entity the world points are cached and rebuilt only when the position, the outline's version or the kind change; 0 B steady.
    /// </summary>
    [GizmoProjector(typeof(TerrainFeature), DrawsOffScreen = true)]
    public sealed class TerrainFeatureGizmo : IStatelessGizmo
    {
        public static readonly Rgba32 SelectedColour = new(86, 166, 250, 255), DotCentre = new(255, 255, 255, 255), DotRing = new(16, 24, 24, 255);
        /// <summary>The handle dot's radius and its pick half-extent, screen pixels.</summary>
        public const float DotRadiusPx = 7f, PickHalfPx = 10f;

        private sealed class Cache
        {
            public ulong Key; public readonly List<Vector2> Points = new();
        }

        private readonly Dictionary<int, Cache> _cache = new();

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!TerrainLock.IsOpen(view)) return;   // an authoring mark: drawn only while the Terrain toolbar is open (TE-18)
            if (!view.HasComponent<TerrainFeature>(entity) || !view.HasComponent<SimTransform>(entity) || !view.HasComponent<NetworkIdentity>(entity)) return;
            int net = view.GetComponentRO<NetworkIdentity>(entity).Value;
            if (net == 0) return;
            ref readonly var f = ref view.GetComponentRO<TerrainFeature>(entity);
            var at = view.GetComponentRO<SimTransform>(entity).Position;
            bool selected = view.HasComponent<SelectionState>(entity) && view.GetComponentRO<SelectionState>(entity).IsSelected;
            string template = f.Template.ToString() ?? "";
            var kindColour = TerrainFeatureLook.ColourOf(f.Kind);
            byte layer = DebugTraceLayers.TacticalGraphics;

            // ⭐⭐ CE-5030 BE-4 part 2 (📄 docs/DESIGN_Terrain_Base_Edits.md §5; mockup docs/UX/terrain-base-edits.svg) — a REMOVE edit is drawn as the building it took away: a dashed ghost of its
            //   footprint (the merged world's record of it, TerrainWorld.Removed) with a "removed" chip, the whole outline its pick (a click selects the edit; Delete restores the building)
            System.Span<char> target = stackalloc char[64];   // the label compared in place: 0 B per frame (R-2084)
            if (f.Kind == TerrainFeature.Edit && template is "remove" or "" && RemovedOf(view, target[..f.Target.CopyTo(target)]) is { } gone)
            {
                var fp = gone.Footprint;
                var ghost = selected ? SelectedColour : RemovedGhost;
                for (int i = 0; i < fp.Length; i++)
                {
                    var a = fp[i]; var b = fp[(i + 1) % fp.Length];
                    draw.DrawLine(new Vector3(a.X, a.Y, 0f), new Vector3(b.X, b.Y, 0f), ghost, selected ? 2.4f : 1.6f, layer: layer, style: LineStyle.Dashed);
                }
                var c = Centre(fp);
                draw.DrawTextLong(c.X, c.Y, RemovedChip, RemovedChipText, style: GizmoTextStyle.Backdrop);
                EntityPresentationGizmoShared.EmitPickSegments(draw, view, entity, fp, Vector2.Zero, isClosed: true, layer);
                return;
            }

            bool hasLine = view.HasManagedComponent<EditablePolyline>(entity);
            if (TerrainFeatureLook.IsPoint(f.Kind, template) || !hasLine)
            {
                // a handle dot: filled in the family colour, a dark ring (blue when selected), a white centre; the pick box is the dot's own
                var c = new Vector3(at.X, at.Y, 0f);
                draw.DrawSphere(c, selected ? DotRadiusPx + 1.5f : DotRadiusPx, selected ? SelectedColour : DotRing, 1.6f, SizeMode.ScreenPixels, layer: layer, fillColor: kindColour);
                draw.DrawSphere(c, 2.2f, DotCentre, 0f, SizeMode.ScreenPixels, layer: layer, fillColor: DotCentre);
                EntityPresentationGizmoShared.EmitPickBox(draw, net, at, PickHalfPx, layer, SizeMode.ScreenPixels);
                return;
            }

            var poly = view.GetManagedComponentRO<EditablePolyline>(entity);
            if (poly is null || poly.Points.Count < 2) return;
            ulong key = Key(at, poly.Version, poly.Points.Count);
            if (!_cache.TryGetValue(entity.Index, out var cache)) _cache[entity.Index] = cache = new Cache { Key = ~key };
            if (cache.Key != key)   // only on a change: the world points
            {
                cache.Key = key; cache.Points.Clear();
                for (int i = 0; i < poly.Points.Count; i++) cache.Points.Add(new Vector2(at.X, at.Y) + poly.Points[i]);
            }
            bool closed = !TerrainFeatureLook.IsLine(f.Kind, template);
            var colour = selected ? SelectedColour : kindColour;
            int n = cache.Points.Count, segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                var a = cache.Points[i]; var b = cache.Points[(i + 1) % n];
                draw.DrawLine(new Vector3(a.X, a.Y, 0f), new Vector3(b.X, b.Y, 0f), colour, selected ? 2.4f : 1.4f, layer: layer, style: selected ? LineStyle.Solid : LineStyle.Dashed);
            }
            EntityPresentationGizmoShared.EmitPickSegments(draw, view, entity, cache.Points, Vector2.Zero, closed, layer);   // the whole feature, as ONE
        }

        /// <summary>⭐ CE-5030 — the ghost of a removed building (grey-white, dashed), and its chip.</summary>
        public static readonly Rgba32 RemovedGhost = new(225, 225, 230, 220), RemovedChipText = new(255, 150, 140, 255);
        public const string RemovedChip = "removed";

        private static TerrainRemovedBuilding? RemovedOf(ISimulationView view, System.ReadOnlySpan<char> label)
        {
            if (label.Length == 0 || view is not EntityRepository repo || !repo.HasSingletonManaged<TerrainWorld>()) return null;
            if (repo.GetSingletonManaged<TerrainWorld>() is not { } terrain) return null;
            var removed = terrain.Removed;
            for (int i = 0; i < removed.Count; i++) if (label.SequenceEqual(removed[i].Label.AsSpan())) return removed[i];
            return null;
        }

        private static Vector2 Centre(Vector2[] p)
        {
            var c = Vector2.Zero;
            for (int i = 0; i < p.Length; i++) c += p[i];
            return p.Length == 0 ? c : c / p.Length;
        }

        private static ulong Key(Vector3 at, int version, int count)
        {
            ulong h = 1469598103934665603UL;
            void Mix(long v) { h ^= (ulong)v; h *= 1099511628211UL; }
            Mix((long)System.MathF.Round(at.X * 100f)); Mix((long)System.MathF.Round(at.Y * 100f)); Mix(version); Mix(count);
            return h;
        }
    }
}
