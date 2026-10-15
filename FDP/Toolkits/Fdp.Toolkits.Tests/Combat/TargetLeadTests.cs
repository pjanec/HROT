using System.Numerics;
using Fdp.Toolkit.Combat;
using Xunit;

namespace Fdp.Toolkits.Tests.Combat
{
    /// <summary>
    /// ⭐ <c>CE-2173</c> slice 6c (B13 / AT2; docs/designs/air-attack/DESIGN_Air_Operations.md §9e) — <see cref="TargetLead"/>: the one
    /// predictor the air attack and the aim path share.
    /// </summary>
    public sealed class TargetLeadTests
    {
        [Fact]
        public void B13_AStillTarget_IsAimedAtWhereItIs()
        {
            var t = new Vector3(300f, 0f, 60f);
            Assert.Equal(t, TargetLead.Point(Vector3.Zero, Vector3.Zero, 900f, t, Vector3.Zero));
        }

        /// <summary>A helicopter crossing at 60 m/s, 300 m out, a 900 m/s round from the ground: the aim point is ahead along its track by
        /// its velocity × the flight time, and a round flown to it at 900 m/s gets there exactly when the helicopter does.
        /// ✅ Red-proof: aiming where it is ⇒ the round arrives ~20 m behind.</summary>
        [Fact]
        public void B13_ACrossingHelicopter_IsLedByItsFlightTime_AndTheRoundMeetsIt()
        {
            var target = new Vector3(300f, 0f, 60f);
            var v = new Vector3(0f, 60f, 0f);
            var aim = TargetLead.Point(Vector3.Zero, Vector3.Zero, 900f, target, v);
            float t = TargetLead.FlightTime(Vector3.Zero, Vector3.Zero, 900f, target, v);
            Assert.InRange(t, 0.33f, 0.35f);
            Assert.Equal(target.X, aim.X, 3);
            Assert.Equal(60f * t, aim.Y, 3);                                   // ~20 m ahead
            Assert.Equal(900f * t, aim.Length(), 1);                           // the round reaches the aim point in t
        }

        /// <summary>An aircraft firing at a target flying alongside at its own velocity: the round keeps the carrier's velocity (A4), so the
        /// aim is straight at the target — the aircraft's velocity comes off the lead.</summary>
        [Fact]
        public void B13_TheShootersOwnVelocity_ComesOffTheLead()
        {
            var v = new Vector3(150f, 0f, 0f);
            var target = new Vector3(0f, 800f, 0f);
            var aim = TargetLead.Point(Vector3.Zero, v, 700f, target, v);
            Assert.Equal(target, aim);
        }

        /// <summary>An aircraft at 150 m/s firing a 700 m/s rocket at a still point 1 km ahead and 100 m below: the round keeps the
        /// carrier's velocity, so the aim is SHORT of the point — and a round flown from the aim's direction at 700 m/s plus the aircraft's
        /// velocity passes through the point. ✅ Red-proof: the flight time solved without the shooter's velocity ⇒ the round misses by metres.</summary>
        [Fact]
        public void B13_AMovingShooter_AimsShort_AndItsRoundMeetsAStillPoint()
        {
            var sv = new Vector3(150f, 0f, 0f);
            var point = new Vector3(1000f, 0f, -100f);
            var aim = TargetLead.Point(Vector3.Zero, sv, 700f, point, Vector3.Zero);
            Assert.True(aim.X < point.X - 100f, $"aim {aim} — short of the point by the aircraft's own travel");
            var round = Vector3.Normalize(aim) * 700f + sv;
            float miss = float.MaxValue;
            for (float t = 0f; t < 3f; t += 0.0005f) miss = System.MathF.Min(miss, Vector3.Distance(round * t, point));
            Assert.True(miss < 0.5f, $"the round passed {miss:F2} m from the point");
        }

        [Fact]
        public void B13_ATargetTheRoundCanNeverCatch_IsAimedAtWhereItIs()
        {
            var target = new Vector3(100f, 0f, 0f);
            Assert.Equal(0f, TargetLead.FlightTime(Vector3.Zero, Vector3.Zero, 50f, target, new Vector3(80f, 0f, 0f)));
            Assert.Equal(target, TargetLead.Point(Vector3.Zero, Vector3.Zero, 50f, target, new Vector3(80f, 0f, 0f)));
        }
    }
}
