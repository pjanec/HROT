using System;
using System.Numerics;
using System.Reflection;
using CarKinem.Core;
using Fdp.Core;
using Fdp.Toolkit.Diagnostics.Gizmos;
using Fdp.Toolkit.Replication.Components;
using Hrot.SimHost.Gizmos;
using Xunit;

namespace Hrot.SimHost.Tests.Gizmos
{
    // SC-GZ057: tests for SimHostEntityPresentationGizmo.
    public sealed class SimHostEntityPresentationGizmoTests : IDisposable
    {
        private readonly EntityRepository _repo;

        public SimHostEntityPresentationGizmoTests()
        {
            _repo = new EntityRepository();
            _repo.RegisterComponent<SimTransform>();
            _repo.RegisterComponent<NetworkIdentity>();
            _repo.RegisterComponent<VehicleParams>();
        }

        public void Dispose() => _repo.Dispose();

        // SC_GZ057_1: [GizmoProjector] attribute declares SimTransform and NetworkIdentity.
        [Fact]
        public void SC_GZ057_1_GizmoProjectorAttribute_ContainsSimTransformAndNetworkIdentity()
        {
            var attr = typeof(SimHostEntityPresentationGizmo)
                .GetCustomAttribute<GizmoProjectorAttribute>();

            Assert.NotNull(attr);
            Assert.Contains(typeof(SimTransform),     attr!.RequiredComponents);
            Assert.Contains(typeof(NetworkIdentity),  attr!.RequiredComponents);
        }

        // SC_GZ057_2: Draw emits a SpatialAnchor primitive with the correct NetworkId.
        [Fact]
        public void SC_GZ057_2_Draw_EmitsSpatialAnchorWithCorrectNetworkId()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = new Vector3(100f, 200f, 5f) });
            _repo.AddComponent(entity, new NetworkIdentity(42L));

            var buffer = new DebugPrimitiveBuffer();
            var gizmo  = new SimHostEntityPresentationGizmo();
            gizmo.Draw(_repo, entity, buffer);

            var frame = buffer.GetFrame();
            Assert.True(frame.Length >= 1);

            var anchor = frame[0];
            Assert.Equal(DebugPrimitiveShape.SpatialAnchor, anchor.Shape);
            Assert.Equal(42L,   anchor.NetworkId);
            Assert.Equal(100f,  anchor.AnchorWorldX);
            Assert.Equal(200f,  anchor.AnchorWorldY);
        }

        // SC_GZ057_3: Draw emits a SemanticShape primitive with AnchorIndex matching networkId.
        // STABILITY(Broken): Expected DebugPrimitiveShape.SemanticShape but got Box2D — SimHostEntityPresentationGizmo emitting wrong shape type; investigate
        [Trait("Stability", "Broken")]
        [Fact]
        public void SC_GZ057_3_Draw_EmitsSemanticShapeWithMatchingAnchorIndex()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = new Vector3(50f, 60f, 0f) });
            _repo.AddComponent(entity, new NetworkIdentity(99L));

            var buffer = new DebugPrimitiveBuffer();
            var gizmo  = new SimHostEntityPresentationGizmo();
            gizmo.Draw(_repo, entity, buffer);

            var frame = buffer.GetFrame();
            Assert.True(frame.Length >= 2);

            var semantic = frame[1];
            Assert.Equal(DebugPrimitiveShape.SemanticShape, semantic.Shape);
            Assert.Equal(CoordinateSpace.EntityLocal, semantic.Space);
            Assert.Equal(99, semantic.AnchorIndex);
        }

        // SC_GZ057_4: Draw with VehicleParams emits non-zero dimensions in SemanticShape.
        // STABILITY(Broken): Expected 3 primitives but got 8 — SimHostEntityPresentationGizmo emitting extra primitives with VehicleParams; investigate
        [Trait("Stability", "Broken")]
        [Fact]
        public void SC_GZ057_4_Draw_WithVehicleParams_EmitsNonZeroDimensions()
        {
            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = new Vector3(0f, 0f, 0f) });
            _repo.AddComponent(entity, new NetworkIdentity(7L));
            _repo.AddComponent(entity, new VehicleParams { Length = 8f, Width = 3f });

            var buffer = new DebugPrimitiveBuffer();
            var gizmo  = new SimHostEntityPresentationGizmo();
            gizmo.Draw(_repo, entity, buffer);

            var frame = buffer.GetFrame();
            Assert.True(frame.Length >= 2);

            var semantic = frame[1];
            Assert.Equal(8f, semantic.LengthMeters);
            Assert.Equal(3f, semantic.WidthMeters);
        }
    }
}
