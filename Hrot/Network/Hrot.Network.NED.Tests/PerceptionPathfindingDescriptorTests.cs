using Hrot.NED.Messages;
using Hrot.NED.Common;
using Hrot.NED.Descriptors;
using Xunit;

namespace Hrot.DDS.DataModel.Tests
{
    /// <summary>
    /// Validates the perception and pathfinding DDS descriptor types introduced by MOD1-P6T2.
    /// These tests deliberately avoid reflection-heavy assertions; they confirm the types exist,
    /// compile, and carry the expected field names/types.
    /// </summary>
    public class PerceptionPathfindingDescriptorTests
    {
        // ── RelativeVector3 ───────────────────────────────────────────────────

        [Fact]
        public void RelativeVector3_HasExpectedFields()
        {
            var rv = new RelativeVector3 { East = 1f, North = 2f, Up = 3f };

            Assert.Equal(1f, rv.East);
            Assert.Equal(2f, rv.North);
            Assert.Equal(3f, rv.Up);
        }

        [Fact]
        public void RelativeVector3_FieldsAreFloat()
        {
            Assert.Equal(typeof(float), typeof(RelativeVector3).GetField("East")!.FieldType);
            Assert.Equal(typeof(float), typeof(RelativeVector3).GetField("North")!.FieldType);
            Assert.Equal(typeof(float), typeof(RelativeVector3).GetField("Up")!.FieldType);
        }

        // ── Raycast pipeline ──────────────────────────────────────────────────

        [Fact]
        public void DdsRaycastRequest_CanBeCreated()
        {
            var req = new DdsRaycastRequest
            {
                RayId          = 42L,
                Start          = new RelativeVector3 { East = 0f, North = 0f, Up = 0f },
                End            = new RelativeVector3 { East = 100f, North = 50f, Up = 0f },
                LayerMask      = 0xFF,
                IgnoreEntityId = -1L,
            };
            Assert.Equal(42L, req.RayId);
        }

        [Fact]
        public void DdsRaycastHit_DefaultIsNoHit()
        {
            var hit = default(DdsRaycastHit);
            Assert.False(hit.HasHit);
        }

        // ── Sensor pipeline ───────────────────────────────────────────────────

        [Fact]
        public void SensorConfig_CanBeCreated()
        {
            var cfg = new SensorConfig
            {
                EntityId     = 1001L,
                VisionRange  = 500f,
                HearingRange = 100f,
                FovRad       = System.MathF.PI / 3f,   // 60° — AQ107 I: radians on the wire
            };
            Assert.Equal(1001L, cfg.EntityId);
            Assert.Equal(500f, cfg.VisionRange);
        }

        /// <summary>
        /// ⭐ AQ107 I — the field of view is RADIANS on the wire. A 90° field of view (the receptor holds cos 45°) leaves the egress as π/2
        /// and the ingress turns it back into the same cosine. ✅ Red-proof: the old <c>* 180/π</c> left in the egress ⇒ 90 on the wire ⇒ red.
        /// </summary>
        [Fact]
        public void AQ107I_AFieldOfViewOf90Degrees_LeavesTheEgressAsHalfPiRadians_AndComesBackAsTheSameCosine()
        {
            float cos45 = System.MathF.Cos(System.MathF.PI / 4f);   // what PerceptionReceptor.FieldOfViewCos holds for a 90° field of view

            float onTheWire = global::Hrot.Network.NED.SimHost.SensorConfigEgressTranslator.FovRadOf(cos45);
            Assert.Equal(System.MathF.PI / 2f, onTheWire, 1e-5f);

            float back = global::Hrot.Network.NED.SimHost.SensorConfigIngressTranslator.FovCosOf(onTheWire);
            Assert.Equal(cos45, back, 1e-6f);
        }

        // ── Pathfinding pipeline ──────────────────────────────────────────────

        [Fact]
        public void DdsPathRequest_CanBeCreated()
        {
            var req = new DdsPathRequest
            {
                RequestId       = 99L,
                Start           = new RelativeVector3 { East = 0f, North = 0f, Up = 0f },
                End             = new RelativeVector3 { East = 200f, North = 300f, Up = 0f },
                MobilityProfile = 1,
            };
            Assert.Equal(99L, req.RequestId);
            Assert.Equal(1, req.MobilityProfile);
        }

        [Fact]
        public void DdsPathResult_DefaultIsNotReachable()
        {
            var result = default(DdsPathResult);
            Assert.False(result.IsReachable);
            Assert.Equal(0f, result.TotalDistanceMeters);
        }

        // ── SourceNodeId routing fields ───────────────────────────────────────

        /// <summary>
        /// Verifies that <see cref="PathRequestBatch"/> carries a
        /// <c>SourceNodeId</c> field for originating-node routing.
        /// </summary>
        [Fact]
        public void PathRequestBatch_HasSourceNodeId()
        {
            var batch = new PathRequestBatch
            {
                SourceNodeId = 3,
                BatchOrigin  = new Vec3d { X = 0, Y = 0, Z = 0 },
                Requests     = new System.Collections.Generic.List<DdsPathRequest>(),
            };
            Assert.Equal(3, batch.SourceNodeId);
        }

        /// <summary>
        /// Verifies that <see cref="PathResponseBatch"/> carries both
        /// <c>TargetNodeId</c> and <c>BatchOrigin</c> so the Brain-ingress
        /// translator can demultiplex responses and reconstruct absolute coordinates.
        /// </summary>
        [Fact]
        public void PathResponseBatch_HasTargetNodeIdAndBatchOrigin()
        {
            var origin = new Vec3d { X = 5250.5, Y = 1340.25, Z = 100 };   // AQ107 B: the anchor is local ENU metres
            var batch  = new PathResponseBatch
            {
                TargetNodeId = 7,
                BatchOrigin  = origin,
                Results      = new System.Collections.Generic.List<DdsPathResult>(),
            };
            Assert.Equal(7, batch.TargetNodeId);
            Assert.Equal(5250.5, batch.BatchOrigin.X);
        }

        /// <summary>
        /// Verifies that <see cref="RaycastRequestBatch"/> carries a
        /// <c>SourceNodeId</c> field for originating-node routing.
        /// </summary>
        [Fact]
        public void RaycastRequestBatch_HasSourceNodeId()
        {
            var batch = new RaycastRequestBatch
            {
                SourceNodeId       = 4,
                BatchCorrelationId = 12u,
                BatchOrigin        = new Vec3d(),
                Requests           = new System.Collections.Generic.List<DdsRaycastRequest>(),
            };
            Assert.Equal(4, batch.SourceNodeId);
        }
    }
}
