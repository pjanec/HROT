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

        // SC_GZ057_5: IgEntityPresentationGizmo [GizmoProjector] declares CullingState.
        [Fact]
        public void SC_GZ057_5_IgGizmoProjectorAttribute_ContainsCullingState()
        {
            var attr = typeof(IgEntityPresentationGizmo)
                .GetCustomAttribute<GizmoProjectorAttribute>();

            Assert.NotNull(attr);
            Assert.Contains(typeof(CullingState),    attr!.RequiredComponents);
            Assert.Contains(typeof(SimTransform),    attr!.RequiredComponents);
            Assert.Contains(typeof(NetworkIdentity), attr!.RequiredComponents);
        }

        // SC_GZ057_7: IgEntityPresentationGizmo sets Damaged condition mask when health damage >= 50.
        [Fact]
        public void SC_GZ057_7_IgGizmo_WithHighDamage_SetsDamagedConditionMask()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = new Vector3(10f, 20f, 0f) });
            _repo.AddComponent(entity, new NetworkIdentity(5L));
            _repo.AddComponent(entity, new CullingState { IsVisible = true });
            _repo.AddComponent(entity, new IgHealthState { Damage = 75f });

            var buffer = new DebugPrimitiveBuffer();
            var gizmo  = new IgEntityPresentationGizmo();
            gizmo.Draw(_repo, entity, buffer);

            var frame = buffer.GetFrame();
            // Phase 5: IgEntityPresentationGizmo now emits a pick sphere (DrawEntitySphere) between
            // the SpatialAnchor and the SemanticShape, so frame[0]=SpatialAnchor,
            // frame[1]=Sphere (pick sphere), frame[2]=SemanticShape.
            Assert.True(frame.Length >= 3);

            var semantic = frame[2];
            Assert.Equal(DebugPrimitiveShape.SemanticShape, semantic.Shape);
            // Damage 75f >= 50 → Damaged bit set; < 90 → Immobile bit NOT set.
            Assert.NotEqual(0u, semantic.ConditionMask & ConditionDamaged);
            Assert.Equal(0u, semantic.ConditionMask & ConditionImmobile);
        }

        // SC_GZ057_6: Draw skips entity when CullingState.IsVisible == false.
        [Fact]
        public void SC_GZ057_6_IgGizmo_Draw_SkipsEntityWhenNotVisible()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = new Vector3(10f, 20f, 0f) });
            _repo.AddComponent(entity, new NetworkIdentity(1L));
            _repo.AddComponent(entity, new CullingState { IsVisible = false });

            var buffer = new DebugPrimitiveBuffer();
            var gizmo  = new IgEntityPresentationGizmo();
            gizmo.Draw(_repo, entity, buffer);

            Assert.Equal(0, buffer.GetFrame().Length);
        }

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
