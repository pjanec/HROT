using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Terrain;
using Hrot.IG.Components;
using Hrot.Map.Common;

namespace Hrot.ScenarioEditor.Gizmos
{
    /// <summary>
    /// ⭐⭐⭐ <c>E1</c> — draws a TERRAIN ZONE (<see cref="TkbEntityTypes.TerrainZone"/> = 8804) and
    /// <b>renders its LOAD STATE in the stroke</b>.
    ///
    /// <para>⭐ <b>The state it shows is LOCAL, and that is the design's point, not a limitation.</b>
    /// 🔒 §9.1: the marker is node-local and the component is NEVER replicated — so what an operator sees
    /// here is *"is this zone's terrain resident on THIS node"*, which is the only question a map can
    /// honestly answer. ⛔ A replicated badge would claim to show the cluster and show one node.</para>
    ///
    /// <para>⭐⭐ <b>Staleness is computed, not stored.</b> The marker records the footprint hash the data
    /// was built FOR; this recomputes the zone's CURRENT footprint and compares. ⇒ reshaping a loaded
    /// zone changes its stroke immediately, with no reload and no writer's cooperation — which is exactly
    /// why the key is a hash and not a counter (§5.3, §9.7 ③c).</para>
    ///
    /// <para>⚠ <b>Why a separate projector from <c>TacticalAreaGizmo</c>.</b> Both draw a closed polyline
    /// from relative points, and that geometry is shared through
    /// <c>EntityPresentationGizmoShared.DrawClosedPolylineOutline</c> — ⛔ NOT copied, because
    /// <c>BP-517</c> was a copy of that loop with the wrong origin. What differs is only the STROKE
    /// POLICY, and a tactical area has no load state to show.</para>
    ///
    /// 📄 docs/DESIGN_Terrain_Zones_And_Assets.md §9.2 U1, §9.1, §9.5, §5.3, §9.7.
    /// </summary>
    [GizmoProjector(typeof(TkbIdentity))]
    public sealed class TerrainZoneGizmo : IStatelessGizmo
    {
        // ⭐ Loaded: calm and solid — the "nothing to see here" case an operator should be able to skim.
        private static readonly Rgba32 LoadedColor  = new Rgba32(90, 190, 120, 230);
        // ⚠ Stale / never-loaded: same hue family, dashed — a zone whose data does not match its shape.
        private static readonly Rgba32 StaleColor   = new Rgba32(215, 180, 70, 230);
        // ⛔ Failed: loud. §8.3 N3 — a host that cannot make the coverage resident is BROKEN, not static.
        private static readonly Rgba32 FailedColor  = new Rgba32(225, 70, 60, 240);
        // In flight.
        private static readonly Rgba32 LoadingColor = new Rgba32(120, 170, 225, 230);

        public void Draw(ISimulationView view, Entity entity, IDebugDrawBuilder draw)
        {
            if (!view.HasComponent<TkbIdentity>(entity)) return;

            ref readonly var tkb = ref view.GetComponentRO<TkbIdentity>(entity);
            if (tkb.TkbType != TkbEntityTypes.TerrainZone) return;

            if (!view.HasManagedComponent<EditablePolyline>(entity)) return;

            var polyline = view.GetManagedComponentRO<EditablePolyline>(entity);
            if (polyline?.Points == null || polyline.Points.Count < 2) return;

            // ⭐ Points are RELATIVE offsets from SimTransform (§2.2) — the origin MUST be added.
            var origin3 = Vector3.Zero;
            if (view.HasComponent<SimTransform>(entity))
            {
                ref readonly var simTr = ref view.GetComponentRO<SimTransform>(entity);
                origin3 = simTr.Position;
            }
            var origin = new Vector2(origin3.X, origin3.Y);

            var (color, style) = ResolveStroke(view, entity, origin3, polyline);

            EntityPresentationGizmoShared.DrawClosedPolylineOutline(
                draw, polyline.Points, origin, color, thickness: 1.5f, style: style);

            // ⭐ The SAME origin the drawing used, or a click on the drawn outline misses by exactly the
            //   transform (the other half of BP-517).
            EntityPresentationGizmoShared.EmitPickSegments(
                draw, view, entity, polyline.Points, origin, isClosed: true);
        }

        /// <summary>
        /// The stroke policy, isolated so it can be read as a table rather than inferred from draw code.
        ///
        /// <para>⚠ <b>No marker at all reads as STALE, not as an error and not as loaded.</b> A freshly
        /// loaded scenario carries no marker — the component is <c>NoScenario</c> — so "absent" honestly
        /// means *"this node has not made it resident yet"*, which is the same thing the operator needs
        /// to act on as a stale one. ⛔ Showing it as loaded is the error §9.1 retracts.</para>
        /// </summary>
        private static (Rgba32 Color, LineStyle Style) ResolveStroke(
            ISimulationView view, Entity entity, in Vector3 origin, EditablePolyline polyline)
        {
            if (!view.HasComponent<TerrainAssetLoadState>(entity))
                return (StaleColor, LineStyle.Dashed);

            ref readonly var marker = ref view.GetComponentRO<TerrainAssetLoadState>(entity);

            switch (marker.Phase)
            {
                case LoadPhase.Failed:
                    return (FailedColor, LineStyle.Solid);   // ⛔ loud, not subtle

                case LoadPhase.Loading:
                    return (LoadingColor, LineStyle.Dotted);

                case LoadPhase.Loaded:
                    // ⭐ Resident FOR WHAT SHAPE? Recompute and compare — this is the staleness check.
                    ulong current = ZoneFootprint.Compute(origin, polyline.Points);
                    return marker.SourceHash == current
                        ? (LoadedColor, LineStyle.Solid)
                        : (StaleColor,  LineStyle.Dashed);

                default:
                    return (StaleColor, LineStyle.Dashed);   // NotLoaded
            }
        }
    }
}
