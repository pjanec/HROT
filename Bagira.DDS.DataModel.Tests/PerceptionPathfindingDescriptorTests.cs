using Bagira.BDC.SSTD;
using Xunit;

namespace Bagira.DDS.DataModel.Tests
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
                FovDegrees   = 60f,
            };
            Assert.Equal(1001L, cfg.EntityId);
            Assert.Equal(500f, cfg.VisionRange);
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
    }
}
