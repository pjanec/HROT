using System.Linq;
using Fdp.Interfaces;
using Fdp.Toolkit.Tkb;
using Fdp.Toolkit.Tkb.Domain;
using Hrot.Core.Tkb;
using Hrot.Map.Common;
using Fdp.Toolkit.Behavior.Components;
using Xunit;

namespace Hrot.Map.Common.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-4634</c> D-1 (docs/designs/fun/DESIGN_Dragon.md DR-1…DR-7, R-3112) — the DRAGON of the built-in catalogue (type 411): a ROTORCRAFT (so it
    /// hovers, lands on its legs and is flown by hand like a helicopter), an attack helicopter to the air orders (DIS 1.2.*.20), its breath a flamethrower
    /// on the NECK turret leaving from the mouth, a rider seat and a firing pillion, a beast's roar. 🔴 Red-proof: the breath's turret index dropped ⇒ the
    /// neck rail red.
    /// </summary>
    public class DragonTkbTests
    {
        private static TkbDatabase BuiltIn()
        {
            var db = new TkbDatabase();
            HrotEnvironment.RegisterBuiltInTkb(db);
            return db;
        }

        [Fact]
        public void CE4634_TheDragon_FliesAsARotorcraft_OnItsLegs_AndIsAnAttackHelicopterToTheOrders()
        {
            var t = BuiltIn().GetByType(TkbEntityTypes.Dragon);
            Assert.Equal("Dragon", t.Name);
            Assert.Equal(LocomotionClass.Rotorcraft, t.GetDescriptor<LocomotionDto>()!.Class);
            var flight = t.GetDescriptor<FlightParamsDto>()!;
            Assert.Equal((35f, 25f), (flight.MaxSpeedMps, flight.HoverHeightM));
            var body = t.GetDescriptor<BodyGeometryDto>()!;
            Assert.Equal((20f, 26f), (body.Length, body.Width));
            Assert.Equal(4, body.GroundContacts.Count);   // four legs: it lands and takes off as a wheeled helicopter does
            var dis = t.DisType;
            Assert.Equal((1, 2, 20, FunTkbCatalog.DragonDisSubcategory), (dis.Kind, dis.Domain, dis.Category, dis.Subcategory));
        }

        [Fact]
        public void CE4634_ItsBreath_IsAFlamethrowerOnItsNeck_LeavingFromTheMouth_DragonFire50m()
        {
            var db = BuiltIn();
            var t = db.GetByType(TkbEntityTypes.Dragon);
            var neck = Assert.Single(t.GetDescriptor<TurretSuiteDto>()!.Turrets);
            Assert.Equal((-70f, 70f, -60f, 15f), (neck.TraverseMinDeg, neck.TraverseMaxDeg, neck.ElevationMinDeg, neck.ElevationMaxDeg));
            var breath = t.GetDescriptor<WeaponSuiteDto>()!.Mounts[0];
            Assert.Equal(WeaponCategory.Flamethrower, breath.Category);
            Assert.Equal(0, breath.TurretIndex);
            Assert.True(breath.MuzzleOffsetX > 0f, "the mouth is ahead of the neck's trunnion");
            Assert.Equal((ulong)MunitionTkbCatalog.TkbDragonFire, breath.AmmoGuid);
            var fire = db.GetByType(MunitionTkbCatalog.TkbDragonFire).GetDescriptor<WarheadDto>()!;
            Assert.Equal((WarheadKind.Incendiary, 50f, 9f), (fire.Kind, fire.FlameRangeM, fire.FlameHalfAngleDeg));
        }

        [Fact]
        public void CE4634_ItCarriesARider_AndAFiringPillion_AndRoars()
        {
            var t = BuiltIn().GetByType(TkbEntityTypes.Dragon);
            var seats = t.GetDescriptor<CarrierDefDto>()!.Seats;
            Assert.Equal(2, seats.Count);
            Assert.Equal((SeatRole.Driver, SeatPose.Ride), (seats[0].Role, seats[0].Pose));
            Assert.Equal((SeatRole.Passenger, SeatPose.Ride), (seats[1].Role, seats[1].Pose));
            Assert.True(seats[1].TraverseMaxDeg > seats[1].TraverseMinDeg, "the pillion is a firing seat (an arc)");
            var sound = t.GetDescriptor<SignaturesDto>()!;
            Assert.Equal(SoundSourceClass.Roar, sound.Acoustic!.MovingClass);
            Assert.Equal(SoundSourceClass.Flame, sound.Acoustic.FiringClass);
            Assert.True(sound.Radar!.RcsM2 > 0f, "an aircraft to radar (DR-2)");
        }
    }
}
