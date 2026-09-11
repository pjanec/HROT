using Fdp.Toolkit.Combat.Components;
using System;
using System.Numerics;
using System.Reflection;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.IG.Components;
using Hrot.IG.Gizmos;
using Hrot.ScenarioEditor.Gizmos;
using Hrot.Map.Common.Components;
using Xunit;

namespace Hrot.IG.Tests.Gizmos
{
    // SC-GZ057/GZ058: tests for IgEntityPresentationGizmo, EffectPresentationGizmo, RouteGizmo.
    public sealed class PresentationGizmoTests : IDisposable
    {
        private const uint ConditionDamaged = 1u << 0;
        private const uint ConditionImmobile = 1u << 1;

        private readonly EntityRepository _repo;

        public PresentationGizmoTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<SimTransform>();
            _repo.RegisterComponent<NetworkIdentity>();
            _repo.RegisterComponent<CullingState>();
            _repo.RegisterComponent<Health>();
            _repo.RegisterComponent<VehicleParams>();
            _repo.RegisterComponent<VisualEffectState>();
            _repo.RegisterComponent<TracerTarget>();
            _repo.RegisterComponent<TkbIdentity>();
        }

        public void Dispose() => _repo.Dispose();

        // ── UXI-23 S2: three tests RE-HOMED, not deleted ──────────────────────────────────────
        //
        // SC_GZ057_5 / _6 / _7 asserted claims about IgEntityPresentationGizmo, which S2 merged into the
        // shared Hrot.ScenarioEditor.Gizmos.EntityPresentationGizmo. All three claims still hold and are
        // now asserted ONCE, over the shared projector, in:
        //
        //     Hrot/Engine/Hrot.Presentation.Tests/Gizmos/EntityPresentationGizmoTests.cs
        //
        // ⚠ SC_GZ057_5 is deliberately INVERTED there. It asserted that the query CONTAINS CullingState;
        // the merged query must NOT, because a [GizmoProjector] requirement is a hard mask filter and
        // keeping it would make the rule match nothing on SimHost and CGF — neither produces
        // CullingState — silently emptying their maps. Culling did not go away: it is presence-decided
        // inside Draw, so IG keeps it and the other hosts gain it (R-137).
        //
        // 📄 docs/UX/UX_Feature_Map_Parity.md §3.9j.

        // SC_GZ058_1: EffectPresentationGizmo emits a Sphere for Explosion effects.
        [Fact]
        public void SC_GZ058_1_EffectGizmo_Explosion_EmitsSphere()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = new Vector3(100f, 200f, 0f) });
            _repo.AddComponent(entity, new VisualEffectState
            {
                Type      = EffectType.Explosion,
                ColorR    = 255,
                ColorG    = 100,
                ColorB    = 0,
                ColorA    = 255,
                Duration  = 1f,
                ElapsedTime = 0f,
                Scale     = 5f,
            });

            var draw  = new FullCapturingDrawBuilder();
            var gizmo = new EffectPresentationGizmo();
            gizmo.Draw(_repo, entity, draw);

            Assert.Single(draw.SphereCalls);
            var sphere = draw.SphereCalls[0];
            Assert.Equal(100f, sphere.Center.X);
            Assert.Equal(200f, sphere.Center.Y);
            Assert.Equal(5f,   sphere.Radius);
        }

        // SC_GZ058_2: EffectPresentationGizmo emits a Line for Tracer effects.
        [Fact]
        public void SC_GZ058_2_EffectGizmo_Tracer_EmitsLine()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = new Vector3(0f, 0f, 0f) });
            _repo.AddComponent(entity, new VisualEffectState
            {
                Type      = EffectType.Tracer,
                ColorR    = 255,
                ColorG    = 255,
                ColorB    = 0,
                ColorA    = 255,
                Duration  = 0.5f,
                ElapsedTime = 0f,
                Scale     = 1f,
            });
            _repo.AddComponent(entity, new TracerTarget { EndX = 500f, EndY = 600f });

            var draw  = new FullCapturingDrawBuilder();
            var gizmo = new EffectPresentationGizmo();
            gizmo.Draw(_repo, entity, draw);

            Assert.Single(draw.LineCalls);
            var line = draw.LineCalls[0];
            Assert.Equal(500f, line.End.X);
            Assert.Equal(600f, line.End.Y);
        }

        // SC_GZ058_3: RouteGizmo emits N-1 lines for N waypoints in a non-loop route.
        [Fact]
        public void SC_GZ058_3_RouteGizmo_EmitsLinesForWaypoints()
        {
            _repo.RegisterManagedComponent<RoutePlan>();

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new TkbIdentity
            {
                TkbType = Hrot.Map.Common.TkbEntityTypes.TacGraphic_Route,
            });

            var plan = new RoutePlan();
            plan.Mutate(list =>
            {
                list.Add(new RouteWaypoint { Position = new Vector3(0f, 0f, 0f) });
                list.Add(new RouteWaypoint { Position = new Vector3(10f, 0f, 20f) });
                list.Add(new RouteWaypoint { Position = new Vector3(20f, 0f, 30f) });
            });

            var ecb = (Fdp.Core.EntityCommandBuffer)((Fdp.ModuleHost.Abstractions.ISimulationView)_repo).GetCommandBuffer();
            ecb.AddManagedComponent(entity, plan);
            ecb.Playback(_repo);

            var draw  = new FullCapturingDrawBuilder();
            var gizmo = new RouteGizmo();
            gizmo.Draw(_repo, entity, draw);

            // 3 waypoints, not a loop → 2 line segments.
            Assert.Equal(2, draw.LineCalls.Count);
        }

        // SC_GZ058_4: DrawSpatialAnchor via DebugPrimitiveBuffer emits correct primitive.
        [Fact]
        public void SC_GZ058_4_DrawSpatialAnchor_EmitsCorrectPrimitive()
        {
            var buffer = new DebugPrimitiveBuffer();
            buffer.DrawSpatialAnchor(networkId: 42L, worldX: 100f, worldY: 200f, worldZ: 5f, headingDeg: 45f);

            var frame = buffer.GetFrame();
            Assert.Equal(1, frame.Length);

            var prim = frame[0];
            Assert.Equal(DebugPrimitiveShape.SpatialAnchor, prim.Shape);
            Assert.Equal(42L,  prim.NetworkId);
            Assert.Equal(100f, prim.AnchorWorldX);
            Assert.Equal(200f, prim.AnchorWorldY);
            Assert.Equal(5f,   prim.AnchorWorldZ);
            Assert.Equal(45f,  prim.Heading);
        }

        // SC_GZ058_5: MapOverlayGizmo emits N-1 line segments for N points (open polyline).
        [Fact]
        public void SC_GZ058_5_MapOverlayGizmo_EmitsLinesForOpenPolyline()
        {
            _repo.RegisterComponent<MapOverlayStyle>();
            _repo.RegisterManagedComponent<EditablePolyline>();

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = new Vector3(10f, 20f, 0f) });
            _repo.AddComponent(entity, new MapOverlayStyle
            {
                BorderR       = 255,
                BorderG       = 255,
                BorderB       = 255,
                BorderA       = 255,
                LineThickness = 2f,
                IsClosed      = false,
            });

            var polyline = new EditablePolyline();
            polyline.Points.Add(new Vector2(0f, 0f));
            polyline.Points.Add(new Vector2(5f, 0f));
            polyline.Points.Add(new Vector2(5f, 5f));

            var ecb = (Fdp.Core.EntityCommandBuffer)((Fdp.ModuleHost.Abstractions.ISimulationView)_repo).GetCommandBuffer();
            ecb.AddManagedComponent(entity, polyline);
            ecb.Playback(_repo);

            var draw  = new FullCapturingDrawBuilder();
            var gizmo = new MapOverlayGizmo();
            gizmo.Draw(_repo, entity, draw);

            // 3 points, not closed → 2 segments.
            Assert.Equal(2, draw.LineCalls.Count);
        }

        // =====================================================================
        // CE-259ae — polygon areas and routes are selectable / right-clickable BY THEIR LINES
        // 🔒 User, 2026-09-11: "polygon areas and routes entities should be selectable by clicking on
        //    their lines, also context menu by right clicking them."
        // 📄 EntityPresentationGizmoShared.EmitPickSegments · DESIGN_Gizmo_Anchor_Identity.md §6.4
        // =====================================================================

        private (Entity entity, EditablePolyline poly) MakeOverlay(bool isClosed, long networkId = 90210L)
        {
            _repo.RegisterComponent<MapOverlayStyle>();
            _repo.RegisterManagedComponent<EditablePolyline>();
            _repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = new Vector3(10f, 20f, 0f) });
            _repo.AddComponent(entity, new MapOverlayStyle
            {
                BorderR = 255, BorderG = 255, BorderB = 255, BorderA = 255,
                LineThickness = 2f, IsClosed = isClosed,
            });
            _repo.AddComponent(entity, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = networkId });

            var poly = new EditablePolyline();
            poly.Points.Add(new Vector2(0f, 0f));
            poly.Points.Add(new Vector2(100f, 0f));
            poly.Points.Add(new Vector2(100f, 100f));

            var ecb = (Fdp.Core.EntityCommandBuffer)((Fdp.ModuleHost.Abstractions.ISimulationView)_repo).GetCommandBuffer();
            ecb.AddManagedComponent(entity, poly);
            ecb.Playback(_repo);
            return (entity, poly);
        }

        // SC-GZ058-6: a CLOSED overlay (an area) emits one pick box per edge, including the closing one.
        [Fact]
        public void SC_GZ058_6_ClosedArea_EmitsAPickSegmentPerEdge()
        {
            var (entity, _) = MakeOverlay(isClosed: true);
            var buffer = new DebugPrimitiveBuffer(64);

            new MapOverlayGizmo().Draw(_repo, entity, buffer);

            var picks = CollectPickBoxes(buffer);
            Assert.Equal(3, picks.Count);   // 3 points, closed → 3 edges
        }

        // SC-GZ058-6b: an OPEN overlay (a route) emits n-1 — it must not close the loop.
        [Fact]
        public void SC_GZ058_6b_OpenRoute_EmitsOneFewerPickSegment()
        {
            var (entity, _) = MakeOverlay(isClosed: false);
            var buffer = new DebugPrimitiveBuffer(64);

            new MapOverlayGizmo().Draw(_repo, entity, buffer);

            Assert.Equal(2, CollectPickBoxes(buffer).Count);
        }

        // SC-GZ058-7: ⭐⭐ THE REQUIREMENT — clicking ON a line yields this entity, so
        // SelectionInteractionSystem selects it and the terminal finds its context menu.
        // ⛔ RED-PROOF SHAPE: remove the EmitPickSegments call from MapOverlayGizmo and this misses.
        [Fact]
        public void SC_GZ058_7_ClickingOnALine_PicksTheEntity()
        {
            var (entity, _) = MakeOverlay(isClosed: true);
            var buffer = new DebugPrimitiveBuffer(64);
            new MapOverlayGizmo().Draw(_repo, entity, buffer);

            // Mid-way along the first edge, which runs from (10,20) to (110,20) in world space
            // (points are RELATIVE to the SimTransform origin).
            var hit = GizmoMap.Presentation.DebugGizmoLayer.PickTopmostEntityAnchor(
                buffer.GetFrame(), new Vector2(60f, 20f), zoom: 1f);

            Assert.NotNull(hit);
            Assert.Equal(entity.Index, hit!.Value.Index);
            Assert.Equal((ushort)entity.Generation, hit.Value.Generation);
        }

        // SC-GZ058-7b: ...and the token carries the NETWORK id, which is what the right-click path
        // looks the CONTEXT MENU up by (menuBindings keyed on ContextMenuBinding.StructNetworkId).
        [Fact]
        public void SC_GZ058_7b_ClickingOnALine_CarriesTheNetworkIdForTheContextMenu()
        {
            var (entity, _) = MakeOverlay(isClosed: true, networkId: 4242L);
            var buffer = new DebugPrimitiveBuffer(64);
            new MapOverlayGizmo().Draw(_repo, entity, buffer);

            var picks = CollectPickBoxes(buffer);

            // ⭐ EVERY edge carries the SAME entity id — that is the point: whichever edge you click,
            //   the terminal resolves the same entity and therefore the same context menu.
            Assert.Equal(3, picks.Count);
            Assert.All(picks, p => Assert.Equal(4242L, p.BoxAnchorId));

            var pick  = picks[0];
            var token = GizmoMap.Presentation.DebugGizmoLayer.MakePickToken(in pick);

            Assert.Equal(4242L, token.AnchorId);
            // ⛔ 0, NOT the edge index: an injected VertexEditGizmo has strict routing priority and
            //   would read a non-zero SubElementId as "drag vertex i". See EmitPickSegments' note.
            Assert.Equal(0u, token.SubElementId);
        }

        // SC-GZ058-7c: a click well AWAY from every edge misses — the interior of an area is not
        // clickable, only its boundary. ⭐ Paired with 7 so neither always-hit nor always-miss passes.
        [Fact]
        public void SC_GZ058_7c_ClickingInsideTheAreaButOffTheLines_IsMiss()
        {
            var (entity, _) = MakeOverlay(isClosed: true);
            var buffer = new DebugPrimitiveBuffer(64);
            new MapOverlayGizmo().Draw(_repo, entity, buffer);

            // Well inside the triangle's bounding box but far from all three edges.
            Assert.Null(GizmoMap.Presentation.DebugGizmoLayer.PickTopmostEntityAnchor(
                buffer.GetFrame(), new Vector2(40f, 80f), zoom: 1f));
        }

        // SC-GZ058-7d: an overlay with NO NetworkIdentity gets no pick target — identity is the network
        // id (constraint C2), the same rule EmitPickBox follows.
        [Fact]
        public void SC_GZ058_7d_UnreplicatedOverlay_EmitsNoPickSegments()
        {
            var (entity, _) = MakeOverlay(isClosed: true, networkId: 0L);
            var buffer = new DebugPrimitiveBuffer(64);

            new MapOverlayGizmo().Draw(_repo, entity, buffer);

            Assert.Empty(CollectPickBoxes(buffer));
        }

        /// <summary>The Box2D pick targets in a frame — the visual edges are Line primitives.</summary>
        private static System.Collections.Generic.List<DebugPrimitive> CollectPickBoxes(
            DebugPrimitiveBuffer buffer)
        {
            var list = new System.Collections.Generic.List<DebugPrimitive>();
            foreach (ref readonly var p in buffer.GetFrame())
                if (p.Shape == DebugPrimitiveShape.Box2D) list.Add(p);
            return list;
        }
    }
}
