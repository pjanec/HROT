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
            _repo.RegisterComponent<IgHealthState>();
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
    }
}
