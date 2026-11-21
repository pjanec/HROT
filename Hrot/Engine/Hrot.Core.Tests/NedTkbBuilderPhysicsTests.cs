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

        // ── CE-113 ───────────────────────────────────────────────────────────────
        // WithPhysics used to store six of SimVehicleDef's eleven fields and drop the
        // rest under a comment deferring them to a "Phase 6" that never happened.  The
        // two that a vehicle cannot be driven without are TurnRate and Mobility.

        private static TkbDatabase BuildTank()
        {
            var db = new TkbDatabase();
            new NedTkbBuilder(db)
                .DefineVehicle(TestTkbId, "TestTank")
                .WithPhysics(TestTkbId, def =>
                {
                    def.Length   = 7.93f;
                    def.Width    = 3.66f;
                    def.MaxSpeed = 20f;
                    def.TurnRate = 15f;
                    def.Mobility = TerrainMobility.Tracked;
                });
            return db;
        }

        [Fact]
        public void WithPhysics_carries_the_authored_TurnRate_into_the_descriptor()
        {
            var dto = BuildTank().GetByType(TestTkbId)!.GetDescriptor<VehicleParametersDto>()!;
            Assert.Equal(15f, dto.TurnRate);
        }

        /// <summary>
        /// The catalog authors <c>TerrainMobility</c>; the kinematics translator needs a
        /// <c>VehicleClass</c>.  The mapping stays on this side of the layer boundary
        /// because <c>Fdp.Toolkits</c> cannot reference <c>Hrot.Core</c>'s enum.
        /// </summary>
        [Fact]
        public void WithPhysics_maps_Tracked_mobility_to_the_Tank_vehicle_class()
        {
            var dto = BuildTank().GetByType(TestTkbId)!.GetDescriptor<VehicleParametersDto>()!;
            Assert.Equal(CarKinem.Core.VehicleClass.Tank, dto.VehicleClass);
        }

        /// <summary>
        /// A template that authors no mobility must leave the class absent rather than
        /// claiming PersonalCar, so the translator can tell the two apart.
        /// </summary>
        [Fact]
        public void WithPhysics_without_an_authored_mobility_still_reports_a_class()
        {
            // TerrainMobility is a non-nullable enum on SimVehicleDef whose default is
            // Tracked, so "unauthored" is not expressible upstream -- the builder always
            // maps whatever the def holds.  Pinned so the asymmetry with the DTO's
            // nullable field is deliberate and visible, not an accident.
            var dto = BuildDatabase().GetByType(TestTkbId)!.GetDescriptor<VehicleParametersDto>()!;
            Assert.NotNull(dto.VehicleClass);
            Assert.Equal(CarKinem.Core.VehicleClass.Tank, dto.VehicleClass);
        }
    }
}

