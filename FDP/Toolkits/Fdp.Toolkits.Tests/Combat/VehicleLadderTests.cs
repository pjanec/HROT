using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat;
using Xunit;

namespace Fdp.Toolkits.Tests.Combat
{
    /// <summary>⭐ <c>CE-4622</c> K-5 (docs/designs/vehicle-tactics/DESIGN_Checkpoint_And_Ramming.md CP-3, §4) — the vehicle ladder by TIME, a 15 s warning line.</summary>
    public sealed class VehicleLadderTests
    {
        private static VehicleRung Due(float s, bool bike = false, RoeForceLevel cap = RoeForceLevel.Lethal, bool self = false,
                                       VehicleRung current = VehicleRung.Quiet, ApproachVerdict v = ApproachVerdict.Threat)
            => VehicleLadder.Due(v, s, 15f, bike, cap, self, current);

        /// <summary>⭐ CP-3 — a threat climbs by time: signal at 14 s, the warning shot at 9, disabling at 5, lethal at 2.
        /// ✅ Red-proof: the disabling rung dropped ⇒ red.</summary>
        [Fact]
        public void CE4622_K5_AThreatClimbsTheLadderByTime()
        {
            Assert.Equal(VehicleRung.Signal, Due(14f));
            Assert.Equal(VehicleRung.WarningShot, Due(9f));
            Assert.Equal(VehicleRung.Disabling, Due(5f));
            Assert.Equal(VehicleRung.Lethal, Due(2f));
        }

        /// <summary>⭐ CP-3 — rungs SKIP when time is short (first seen 5 s out ⇒ straight to disabling, never a late warning shot) and never step down
        /// while it stays a threat. ✅ Red-proof: the never-down rule dropped ⇒ red.</summary>
        [Fact]
        public void CE4622_K5_RungsSkipWhenTimeIsShort_AndNeverStepDown()
        {
            Assert.Equal(VehicleRung.Disabling, Due(5f, current: VehicleRung.Quiet));
            Assert.Equal(VehicleRung.Disabling, Due(8f, current: VehicleRung.Disabling));   // it slowed a little: still disabling
        }

        /// <summary>⭐ KV-14 — a bike has no engine rung: the warning shot, then lethal at the disabling time. ✅ Red-proof: the bike table dropped ⇒ red.</summary>
        [Fact]
        public void CE4622_K5_ABikeHasNoEngineRung()
        {
            Assert.Equal(VehicleRung.WarningShot, Due(9f, bike: true));
            Assert.Equal(VehicleRung.Lethal, Due(5f, bike: true));
        }

        /// <summary>⭐ CP-3 — the ROE caps it: Warn ⇒ the signal only; NonLethal ⇒ up to the warning shot; self-defence (the car rammed: a lethal act)
        /// lifts the cap. ✅ Red-proof: the NonLethal cap dropped ⇒ red.</summary>
        [Fact]
        public void CE4622_K5_TheRoeCapsTheLadder_SelfDefenceLiftsIt()
        {
            Assert.Equal(VehicleRung.Signal, Due(2f, cap: RoeForceLevel.Warn));
            Assert.Equal(VehicleRung.WarningShot, Due(2f, cap: RoeForceLevel.NonLethal));
            Assert.Equal(VehicleRung.Lethal, Due(2f, cap: RoeForceLevel.NonLethal, self: true));
        }

        /// <summary>⭐ CP-3 — a routine car, or one approaching inside the warning line, is signalled; one beyond it, or nothing, is quiet.</summary>
        [Fact]
        public void CE4622_K5_EveryVehicleInsideTheWarningLineIsSignalled_NothingMore()
        {
            Assert.Equal(VehicleRung.Signal, Due(12f, v: ApproachVerdict.Approaching));
            Assert.Equal(VehicleRung.Signal, Due(float.PositiveInfinity, v: ApproachVerdict.Routine));
            Assert.Equal(VehicleRung.Quiet, Due(20f, v: ApproachVerdict.Approaching));
            Assert.Equal(VehicleRung.Quiet, Due(5f, v: ApproachVerdict.None));
        }
    }
}
