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

        public static void EmitPickBox(IDebugDrawBuilder draw, Entity entity, long networkId, in Vector3 position, byte layer = 0)
        {
            var pickBox = DebugPrimitive.MakeBox2D(
                new Vector2(position.X, position.Y),
                new Vector2(8f, 8f),
                new Rgba32(0, 0, 0, 0),
                entity.Index,
                (ushort)entity.Generation,
                networkId,
                target: PipelineTarget.Map2D,
                layer: layer);
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
        ///   <item><description><b>Selection</b> — <c>SelectionInteractionSystem.Tick</c> selects
        ///   <c>evt.Token.Target</c> with no <c>GizmoTypeId</c> filter, so any pick primitive carrying
        ///   this entity's ECS payload selects it.</description></item>
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
            if (!view.HasComponent<NetworkIdentity>(entity)) return;

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
                    anchorIndex: entity.Index,
                    anchorGeneration: (ushort)entity.Generation,
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

        public static void DrawSemanticShape(
            IDebugDrawBuilder draw,
            Entity entity,
            long networkId,
            ulong profileId,
            float length,
            float width,
            uint conditionMask,
            byte layer = 0)
        {
            var prim = DebugPrimitive.MakeSemanticShape(
                (int)networkId,
                (ushort)entity.Generation,
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
