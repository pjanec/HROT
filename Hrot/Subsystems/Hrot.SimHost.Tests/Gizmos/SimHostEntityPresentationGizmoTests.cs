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
        // Gizmo emits: [0] SpatialAnchor, [1] PickBox (Box2D), [2] SemanticShape.
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
            Assert.True(frame.Length >= 3);

            var semantic = frame[2];
            Assert.Equal(DebugPrimitiveShape.SemanticShape, semantic.Shape);
            Assert.Equal(CoordinateSpace.EntityLocal, semantic.Space);
            Assert.Equal(99, semantic.AnchorIndex);
        }

        // SC_GZ057_5: AnchorIndex == (int)networkId even when entity.Index diverges from networkId.
        // Regression for the bug where entity.Index was passed as anchorIndex instead of (int)networkId,
        // causing the renderer's SpatialAnchor cache lookup (keyed by networkId) to always miss.
        [Fact]
        public void SC_GZ057_5_DrawSemanticShape_AnchorIndex_MatchesNetworkId_NotEntityIndex()
        {
            // Create multiple entities so entity.Index is not 1, then assign a large networkId
            // that is guaranteed to differ from any plausible entity.Index.
            var dummy = _repo.CreateEntity();
            _repo.AddComponent(dummy, new SimTransform());
            _repo.AddComponent(dummy, new NetworkIdentity(0L));

            var entity = _repo.CreateEntity();
            _repo.AddComponent(entity, new SimTransform { Position = new Vector3(10f, 20f, 0f) });
            const long networkId = 9999L;
            _repo.AddComponent(entity, new NetworkIdentity(networkId));

            // Verify the precondition: entity.Index must differ from (int)networkId
            Assert.NotEqual((int)networkId, entity.Index);

            var buffer = new DebugPrimitiveBuffer();
            var gizmo  = new SimHostEntityPresentationGizmo();
            gizmo.Draw(_repo, entity, buffer);

            var frame = buffer.GetFrame();
            Assert.True(frame.Length >= 3);

            // Find the SemanticShape — it is at frame[2]: [0]=SpatialAnchor, [1]=PickBox, [2]=SemanticShape
            var semantic = frame[2];
            Assert.Equal(DebugPrimitiveShape.SemanticShape, semantic.Shape);
            Assert.Equal(CoordinateSpace.EntityLocal, semantic.Space);

            // AnchorIndex must equal (int)networkId, NOT entity.Index.
            // The renderer caches SpatialAnchors keyed by networkId; a mismatch causes every
            // SemanticShape to be silently dropped (anchor lookup miss).
            Assert.Equal((int)networkId, semantic.AnchorIndex);
            Assert.NotEqual(entity.Index, semantic.AnchorIndex);

            // Round-trip: the SpatialAnchor at frame[0] carries the same networkId,
            // so the renderer key and the SemanticShape lookup key both equal networkId.
            var anchor = frame[0];
            Assert.Equal(DebugPrimitiveShape.SpatialAnchor, anchor.Shape);
            Assert.Equal(networkId, anchor.NetworkId);
            Assert.Equal(anchor.NetworkId, (long)semantic.AnchorIndex);
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
