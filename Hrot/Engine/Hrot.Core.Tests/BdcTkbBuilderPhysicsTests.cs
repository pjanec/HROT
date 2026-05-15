using System;
using Hrot.Map.Common;
using Hrot.Map.Definitions.Tkb;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// Tests for BD1-P3T1: NedTkbBuilder.WithPhysics must store a VehicleParametersDto
    /// in the descriptor bag.
    /// </summary>
    public class NedTkbBuilderPhysicsTests
    {
        // ── Helpers ───────────────────────────────────────────────────────────

        private static TkbDatabase BuildDatabase() =>
            BuildDatabase(length: 6f, width: 2.5f);

        private static TkbDatabase BuildDatabase(float length, float width, float maxSpeed = 0f)
        {
            var db = new TkbDatabase();
            new NedTkbBuilder(db)
                .DefineVehicle(TestTkbId, "TestVehicle")
                .WithPhysics(TestTkbId, def =>
                {
                    def.Length   = length;
                    def.Width    = width;
                    def.MaxSpeed = maxSpeed;
                });
            return db;
        }

        private const long TestTkbId = 9901L;

        // ── Tests ─────────────────────────────────────────────────────────────

        [Fact]
        public void WithPhysics_StoresVehicleParametersDescriptor()
        {
            var template = BuildDatabase().GetByType(TestTkbId)!;
            Assert.True(template.HasDescriptor<VehicleParametersDto>());
        }

        [Fact]
        public void WithPhysics_VehicleParameters_HasExpectedLength()
        {
            var template = BuildDatabase().GetByType(TestTkbId)!;
            var dto = template.GetDescriptor<VehicleParametersDto>()!;
            Assert.Equal(6f, dto.Length);
        }

        [Fact]
        public void WithPhysics_VehicleParameters_HasExpectedWidth()
        {
            var template = BuildDatabase().GetByType(TestTkbId)!;
            var dto = template.GetDescriptor<VehicleParametersDto>()!;
            Assert.Equal(2.5f, dto.Width);
        }

        [Fact]
        public void WithPhysics_VehicleParameters_HasExpectedMaxSpeedFwd()
        {
            var db = BuildDatabase(length: 6f, width: 2.5f, maxSpeed: 20f);
            var template = db.GetByType(TestTkbId)!;
            var dto = template.GetDescriptor<VehicleParametersDto>()!;
            Assert.Equal(20f, dto.MaxSpeedFwd);
        }
    }
}

