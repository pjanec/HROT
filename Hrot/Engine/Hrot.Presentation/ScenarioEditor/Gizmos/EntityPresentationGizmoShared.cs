using System.Numerics;
using CarKinem.Core;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;

namespace Hrot.ScenarioEditor.Gizmos
{
    public static class EntityPresentationGizmoShared
    {
        public static void DrawSpatialAnchorFromRotation(
            IDebugDrawBuilder draw,
            long networkId,
            in Vector3 position,
            in Quaternion rotation)
        {
            var (yawDeg, pitchDeg, rollDeg) = SimMath.ToYawPitchRollDeg(rotation);
            draw.DrawSpatialAnchor(networkId, position.X, position.Y, position.Z, yawDeg, pitchDeg, rollDeg);
        }

        /// <summary>
        /// ⭐ An invisible, hit-testable box at <paramref name="position"/>, identified by
        /// <paramref name="networkId"/>.
        /// ⛔ §6.7 — the <c>Entity entity</c> parameter is DELETED: it existed only to stamp the
        /// emitter's ECS index+generation into the primitive as a pick payload, which nothing reads now.
        /// 📄 <c>docs/DESIGN_Gizmo_Anchor_Identity.md</c> §6.7.
        /// </summary>
        /// <param name="extentMetres">
        /// ⭐⭐⭐ <c>CE-3147</c> — the clickable HALF-EXTENT in <b>world metres</b>, normally
        /// <c>EntityFootprint.InteractionRadiusMetres(view, entity)</c> so it tracks the drawn symbol at every zoom.
        /// ⛔ It used to be a hard-coded <c>8f</c> in <c>ScreenPixels</c>, which is correct at exactly one zoom —
        /// 🔒 the user's two reports, <i>"extremely small"</i> zoomed in and <i>"enormous in comparison to entity
        /// symbol"</i> zoomed out, are the two ends of that one mistake.
        /// </param>
        public static void EmitPickBox(IDebugDrawBuilder draw, long networkId, in Vector3 position,
                                       float extentMetres, byte layer = 0)
        {
            // ⭐⭐⭐ §6.8 — NO ID, NO PICK TARGET. Constraint C2 was enforced by CALLERS checking that a
            //   NetworkIdentity was PRESENT; a component present with Value 0 passed that and produced a
            //   pick box that swallows clicks and resolves to nothing. ⭐ The rule belongs on the seam
            //   that owns it, so every caller gets it. 📌 Prior art: ContextMenuProjectorGizmo.cs:102
            //   already did exactly this — the only emitter that did.
            if (networkId == 0) return;

            var pickBox = DebugPrimitive.MakeBox2D(
                new Vector2(position.X, position.Y),
                new Vector2(extentMetres, extentMetres),
                new Rgba32(0, 0, 0, 0),
                // ⭐ CE-3147 — WORLD METRES, because the symbol is in world metres: MakeBox2D's sizeMode DEFAULTS
                //   to ScreenPixels, so this must be passed explicitly or the area stops scaling with zoom.
                sizeMode: SizeMode.WorldMeters,
                target: PipelineTarget.Map2D,
                layer: layer,
                anchorId: networkId);
            draw.EmitRaw(in pickBox);
        }

        /// <summary>
        /// ⭐⭐⭐ <b><c>CE-259ae</c> — MAKE A POLYLINE'S EDGES CLICKABLE.</b> Emits one invisible, oriented
        /// pick box per edge so the operator can left-click an area boundary or a route to SELECT the
        /// entity, and right-click it to get that entity's CONTEXT MENU.
        ///
        /// <para>🔒 <b>User requirement, <c>2026-09-11</c>:</b> <i>"polygon areas and routes entities
        /// should be selectable by clicking on their lines, also context menu by right clicking
        /// them."</i></para>
        ///
        /// <para>⭐⭐ <b>Both halves then work with no further wiring, which is why this is the whole
        /// change.</b> Measured before building:</para>
        /// <list type="bullet">
        ///   <item><description><b>Selection</b> — <c>SelectionInteractionSystem.Tick</c> selects the
        ///   entity the token's <c>AnchorId</c> resolves to, with no <c>GizmoTypeId</c> filter, so any
        ///   pick primitive carrying this entity's network id selects it. ⚠ Updated for §6.7: it used to
        ///   read a forwarded ECS payload.</description></item>
        ///   <item><description><b>Context menu</b> — the AREA and ROUTE menus <b>already exist and are
        ///   already bound by network id</b> (<c>ContextMenuProjectorGizmo</c>: <c>MenuJsonArea</c> when
        ///   the entity has an <c>EditablePolyline</c>, <c>MenuJsonRoute</c> for a <c>RoutePlan</c>), and
        ///   the terminal resolves the menu from the HIT primitive's <c>BoxAnchorId</c>. ⇒ they were
        ///   simply UNREACHABLE, because there was nothing on the lines to right-click.</description></item>
        /// </list>
        ///
        /// <para>⛔⛔ <b><c>SubElementId</c> IS DELIBERATELY 0, and this is load-bearing.</b> An
        /// <c>EditablePolyline</c> entity may have a <c>VertexEditGizmo</c> INJECTED
        /// (<c>IgApplication</c>), and <c>DataDrivenGizmoSystem.FindGizmo</c> gives injected gizmos
        /// <b>strict priority, ignoring <c>GizmoTypeId</c> entirely</b>. With a non-zero sub-element a
        /// click on edge <c>i</c> would reach <c>VertexEditGizmo.OnInteractionStarted</c>, whose
        /// <c>idx = SubElementId - 1</c> would make it <b>start dragging vertex i</b>. With 0 it falls
        /// through that gizmo's <c>idx &lt; 0</c> guard and is safely ignored — and 0 is anyway the right
        /// meaning here: "the whole entity", which is what clicking a boundary selects.</para>
        ///
        /// <para>⭐ <b>Z-order is already correct and needs nothing.</b> The hit-test walks the buffer in
        /// REVERSE, so a layer-0 tie goes to the LAST-emitted primitive. These come from the STATELESS
        /// group, which runs before the arbiters, so an active tool's HANDLES still win — the
        /// <c>CE-259r</c> / §4.7i ruling holds unchanged.</para>
        ///
        /// <para>⚠ <b>No network id, no pick target</b> — same rule as <see cref="EmitPickBox"/> and
        /// constraint <c>C2</c>: identity is the network id, so an unreplicated overlay has nothing to be
        /// identified by.</para>
        /// </summary>
        /// <param name="points">Vertices. Added to <paramref name="origin"/>, so pass
        /// <c>Vector2.Zero</c> when they are already absolute.</param>
        /// <param name="isClosed">true closes the loop (an area); false leaves it open (a route).</param>
        /// <summary>
        /// ⭐⭐ Draws a CLOSED polyline outline from RELATIVE points plus an origin — the one
        /// implementation, shared by <c>TacticalAreaGizmo</c> and <c>TerrainZoneGizmo</c>.
        ///
        /// <para>⛔ <b>Extracted deliberately, not for tidiness.</b> <c>BP-517</c> was exactly this loop
        /// with <c>Vector2.Zero</c> for the origin, which drew every area at the wrong place and put
        /// picking off by the same distance. ⇒ a second copy of the loop is a second place that bug can
        /// come back. ⭐ The <paramref name="style"/> parameter is what lets the zone gizmo say
        /// "loaded / stale / loading" without forking the geometry (design §9.2 U1).</para>
        /// </summary>
        /// <summary>
        /// ⭐ CE-1033 M22 — the height a ground-anchored shape (an area, an overlay) is drawn at: its position's Z when that is on
        /// a roof or an upper floor (level ≥ 1), else 0 = the ground, which the 3-D map DRAPES so the shape follows relief (M18).
        /// ⛔ Not the raw Z: an area spawned at the ground's height on a slope would otherwise float as one flat plane (R-248).
        /// </summary>
        public static float LevelHeightOf(ISimulationView view, Vector3 position)
        {
            if (position.Z == 0f) return 0f;
            var world = Fdp.Toolkit.World.WorldQuery.Of(view);
            return world != null && Fdp.Toolkit.World.Levels.LevelOf(world, position) >= 1 ? position.Z : 0f;
        }

        public static void DrawClosedPolylineOutline(
            IDebugDrawBuilder draw,
            System.Collections.Generic.IReadOnlyList<Vector2> points,
            Vector2 origin,
            Rgba32 color,
            float thickness = 1.5f,
            LineStyle style = LineStyle.Solid,
            float z = 0f)
        {
            if (points == null || points.Count < 2) return;

            // ⭐ CE-1033 M22 — z is the level the shape is on (an area on a roof); 0 = the ground, draped in 3-D (M18).
            int n = points.Count;
            for (int i = 0; i < n; i++)
            {
                var pa = origin + points[i];
                var pb = origin + points[(i + 1) % n];
                draw.DrawLine(
                    new Vector3(pa.X, pa.Y, z),
                    new Vector3(pb.X, pb.Y, z),
                    color, thickness, SizeMode.ScreenPixels,
                    PipelineTarget.All, layer: 0, style: style);
            }
        }

        public static void EmitPickSegments(
            IDebugDrawBuilder draw,
            ISimulationView view,
            Entity entity,
            System.Collections.Generic.IReadOnlyList<Vector2> points,
            Vector2 origin,
            bool isClosed,
            byte layer = 0)
        {
            if (points == null || points.Count < 2) return;
            // ⭐⭐ §6.8 — the VALUE, not just the component: `HasComponent` alone let a NetworkIdentity
            //   with Value 0 through, which emits pick segments that cannot be resolved.
            // ⭐⭐⭐ THROUGH THE ONE RESOLVER (R-77 / BP-508), not an inline read — and that is not
            //   style: `ThereIsOneNetworkIdResolverTests.NoNewInlineNetworkIdLookupAppears` is a
            //   tripwire on the inline-lookup TEXT SHAPE, and my first version of this guard reddened
            //   it. ⭐ The rail was right; the resolver also covers liveness and component presence, so
            //   this one call replaces both checks.
            // ⚠⚠ AND A TRAP WORTH KNOWING: that rail scans TEXT, so even a COMMENT quoting the shape
            //   trips it — my second attempt reddened it by DESCRIBING the pattern in prose. ⛔ Do not
            //   "fix" the rail for that; describe the shape without writing it.
            if (Fdp.Toolkit.Replication.Services.NetworkIdResolver
                    .RuntimeNetworkIdOf(view as EntityRepository, entity) == 0) return;

            ref readonly var netId = ref view.GetComponentRO<NetworkIdentity>(entity);
            long networkId = netId.Value;
            if (networkId == 0) return;

            int n = points.Count;
            int segCount = isClosed ? n : n - 1;

            for (int i = 0; i < segCount; i++)
            {
                var a = origin + points[i];
                var b = origin + points[(i + 1) % n];

                var prim = DebugPrimitive.MakePickSegment(
                    a, b,
                    networkId: networkId,
                    // ⭐ 0 world thickness: the corridor is the terminal's own ~5px grace radius, which
                    //   with ScreenPixels stays 5px at every zoom. A world-unit thickness would get
                    //   unclickably thin when zoomed out — the opposite of what an operator wants.
                    pickThickness: 0f,
                    subElementId: 0,          // ⛔ see the note above — NOT the edge index
                    color: default,           // fully transparent, exactly like EmitPickBox
                    sizeMode: SizeMode.ScreenPixels,
                    target: PipelineTarget.Map2D,
                    layer: layer);

                draw.EmitRaw(in prim);
            }
        }

        public static void TryGetVehicleDimensions(ISimulationView view, Entity entity, out float length, out float width)
        {
            length = 0f;
            width = 0f;
            if (!view.HasComponent<VehicleParams>(entity)) return;

            ref readonly var vp = ref view.GetComponentRO<VehicleParams>(entity);
            length = vp.Length;
            width = vp.Width;
        }

        public static ulong ResolveProfileId(ISimulationView view, Entity entity)
        {
            if (view is not EntityRepository repo)
                return 0UL;

            var dis = repo.GetDisType(entity);
            if (dis.Value != 0UL)
                return dis.Value;

            // Header DisType is unset (e.g. the entity was spawned via TKB, or loaded without
            // the DisEntityType translator reaching this repository). Fall back to the TKB
            // template keyed by TkbType — mirroring DisEntityTypeTranslator's extract-time
            // fallback — so the shape still resolves to the correct DIS profile.
            if (repo.HasComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>(entity)
                && repo.HasSingletonManaged<Fdp.Interfaces.ITkbDatabase>())
            {
                var tkb = repo.GetSingletonManaged<Fdp.Interfaces.ITkbDatabase>();
                ref readonly var tkbId = ref repo.GetComponentRO<Fdp.Toolkit.Replication.Components.TkbIdentity>(entity);
                if (tkb != null && tkb.TryGetByType(tkbId.TkbType, out var template))
                    return template.DisType.Value;
            }
            return 0UL;
        }

        /// <summary>
        /// ⛔ §6.7 — the <c>Entity entity</c> parameter is DELETED; it supplied only the ECS generation
        /// that <c>MakeSemanticShape</c> stamped into offset 12 for nothing to read.
        /// </summary>
        public static void DrawSemanticShape(
            IDebugDrawBuilder draw,
            long networkId,
            ulong profileId,
            float length,
            float width,
            uint conditionMask,
            byte layer = 0)
        {
            var prim = DebugPrimitive.MakeSemanticShape(
                (int)networkId,          // the SpatialAnchor cache key at offset 8 (C7 narrowing)
                networkId,
                profileId,
                length,
                width,
                conditionMask,
                layer: layer);
            // MakeSemanticShape builds from default(DebugPrimitive), which leaves Color at
            // (0,0,0,0) — fully transparent, so the avatar would draw invisibly. Set an
            // explicit opaque color so the shape (and the magenta fallback) is visible.
            prim.Color = new Rgba32(100, 220, 255, 255);
            draw.EmitRaw(in prim);
        }
    }
}
