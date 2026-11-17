using System.Numerics;
using Fdp.Toolkit.Combat;
using Xunit;

namespace Fdp.Toolkits.Tests.Combat
{
    /// <summary>
    /// ⭐ <c>CE-4622</c> K-4 (docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md CP-2, §4) — the approach watch judges a vehicle by TIME to the stand-off:
    /// the stop line at the origin, traffic coming in eastward along +x, a 40 m stand-off, a 15 s warning line.
    /// </summary>
    public sealed class VehicleApproachWatchTests
    {
        private static readonly ApproachSite Site = new() { Stop = Vector3.Zero, Inbound = Vector2.UnitX, StandOffM = 40f, WarningSeconds = 15f };

        /// <summary>⭐ CP-2 — at 18 m/s a car 360 m out is 20 s out: approaching (beyond the 15 s line), its time right.
        /// ✅ Red-proof: a car beyond the warning line not watched ⇒ red.</summary>
        [Fact]
        public void CE4622_K4_AFarCar_IsApproaching_WithItsSecondsToTheStandOff()
        {
            var v = VehicleApproachWatch.Judge(in Site, new Vector3(-360f, 0f, 0f), new Vector3(18f, 0f, 0f), out float s);
            Assert.Equal(ApproachVerdict.Approaching, v);
            Assert.Equal(20f, s, 2);
        }

        /// <summary>⭐ CP-2 — past the warning line still at 18 m/s with 120 m to go: it needs 1.35 m/s² — it can still stop, so it is watched, not
        /// a threat; at 60 m it needs 2.7; at 30 m 5.4 — more than an ordinary brake: a THREAT.
        /// ✅ Red-proof: the brake test dropped (fast past the line ⇒ threat at once) ⇒ red.</summary>
        [Fact]
        public void CE4622_K4_AFastCarPastTheWarningLine_IsAThreat_OnlyWhenItCanNoLongerStop()
        {
            Assert.Equal(ApproachVerdict.Approaching, VehicleApproachWatch.Judge(in Site, new Vector3(-120f, 0f, 0f), new Vector3(18f, 0f, 0f), out _));
            Assert.Equal(ApproachVerdict.Approaching, VehicleApproachWatch.Judge(in Site, new Vector3(-60f, 0f, 0f), new Vector3(14f, 0f, 0f), out _));
            Assert.Equal(ApproachVerdict.Threat, VehicleApproachWatch.Judge(in Site, new Vector3(-30f, 0f, 0f), new Vector3(18f, 0f, 0f), out float s));
            Assert.InRange(s, 1.6f, 1.7f);
        }

        /// <summary>⭐ CP-2 — a car slowed under 3 m/s near the line, or standing at it, is ROUTINE; standing 200 m out is nothing; one through the
        /// stop line at speed is a THREAT. ✅ Red-proof: the routine branch dropped (a slow car near the line read as approaching) ⇒ red.</summary>
        [Fact]
        public void CE4622_K4_ASlowOrStoppedCar_AtTheLine_IsRoutine()
        {
            Assert.Equal(ApproachVerdict.Routine, VehicleApproachWatch.Judge(in Site, new Vector3(-20f, 0f, 0f), new Vector3(2f, 0f, 0f), out _));
            Assert.Equal(ApproachVerdict.Routine, VehicleApproachWatch.Judge(in Site, new Vector3(-3f, 1f, 0f), Vector3.Zero, out float s));
            Assert.True(float.IsPositiveInfinity(s));
            Assert.Equal(ApproachVerdict.None, VehicleApproachWatch.Judge(in Site, new Vector3(-200f, 0f, 0f), Vector3.Zero, out _));
            Assert.Equal(ApproachVerdict.Threat, VehicleApproachWatch.Judge(in Site, new Vector3(5f, 0f, 0f), new Vector3(15f, 0f, 0f), out float past));
            Assert.Equal(0f, past);
        }

        /// <summary>⭐ CP-2 — a car on a PARALLEL road 300 m to the side, eastward at 15 m/s, or one driving away, is nothing.
        /// ✅ Red-proof: the side test dropped ⇒ the parallel road's car is a threat ⇒ red.</summary>
        [Fact]
        public void CE4622_K4_ACarOffTheApproach_OrLeaving_IsNothing()
        {
            Assert.Equal(ApproachVerdict.None, VehicleApproachWatch.Judge(in Site, new Vector3(-100f, 300f, 0f), new Vector3(15f, 0f, 0f), out _));
            Assert.Equal(ApproachVerdict.None, VehicleApproachWatch.Judge(in Site, new Vector3(-100f, 0f, 0f), new Vector3(-15f, 0f, 0f), out _));
        }
    

        /// <summary>⭐ CP-2 (IM-8) — two men on one bike raise the watch; one man on it, or two in a car, do not.
        /// ✅ Red-proof: the two-wheeler test dropped ⇒ the car with two is raised ⇒ red.</summary>
        [Fact]
        public void CE4622_K4_TwoMenOnOneBike_IsAnIndicator()
        {
            using var w = new Fdp.Core.EntityRepository();
            w.RegisterComponent<Fdp.Toolkit.Replication.Components.TkbIdentity>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.PassengerBuffer>();
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var bike = new Fdp.Interfaces.TkbTemplate("Bike", 9115L);
            bike.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.CarrierDefDto { Seats = new() { new() { Pose = Fdp.Toolkit.Tkb.Domain.SeatPose.Ride }, new() { Pose = Fdp.Toolkit.Tkb.Domain.SeatPose.Ride } } });
            var car = new Fdp.Interfaces.TkbTemplate("Car", 9102L);
            car.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.CarrierDefDto { Seats = new() { new() { Pose = Fdp.Toolkit.Tkb.Domain.SeatPose.Sit }, new() { Pose = Fdp.Toolkit.Tkb.Domain.SeatPose.Sit } } });
            db.Register(bike); db.Register(car);
            w.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            Fdp.Core.Entity Make(long type, int aboard)
            {
                var e = w.CreateEntity();
                w.AddComponent(e, new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = (Fdp.Core.TkbTypeId)type });
                w.AddComponent(e, new Fdp.Toolkit.Behavior.Components.PassengerBuffer { Count = aboard });
                return e;
            }
            Assert.Equal(ApproachIndicator.TwoUpBike, VehicleApproachWatch.Indicators(w, Make(9115L, 2)));
            Assert.Equal(ApproachIndicator.Plain, VehicleApproachWatch.Indicators(w, Make(9115L, 1)));
            Assert.Equal(ApproachIndicator.Plain, VehicleApproachWatch.Indicators(w, Make(9102L, 2)));
        }
    }
}
