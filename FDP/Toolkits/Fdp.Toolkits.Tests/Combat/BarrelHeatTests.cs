using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>
    /// ⭐ <c>CE-1358</c> (docs/DESIGN_Weapon_Layers.md §5k) — the BARREL HEAT rule: every round heats, it cools linearly, too hot latches
    /// until half. The executors' rails (<c>AimAndFireExecutorTests.CE1358_*</c>) prove the fire paths obey it.
    /// </summary>
    public class BarrelHeatTests
    {
        private static WeaponState Loaded(WeaponMountDto mount, int ammo = 1000)
        {
            var w = new WeaponState { Ammo = ammo, MaxAmmo = ammo };
            Magazine.Load(ref w, 0, 0f);
            BarrelHeat.Load(ref w, mount);
            return w;
        }

        /// <summary>The defaults by category: an automatic weapon is too hot after ~4 s of its cyclic rate (an M134 at 3000/min: 200
        /// rounds) and cools in 12 s; a single-shot gun 0.15 a round, 60 s; a grenade, a dispenser, a person's hands have no barrel.</summary>
        [Fact]
        public void CE1358_TheDefaults_ByCategory()
        {
            var minigun = BarrelHeat.For(new WeaponMountDto { Category = WeaponCategory.MachineGun, RateOfFire = 3000f });
            Assert.Equal(1f / 200f, minigun.PerRound, 5);
            Assert.Equal(BarrelHeat.AutomaticCoolSeconds, minigun.CoolSeconds);

            var tank = BarrelHeat.For(new WeaponMountDto { Category = WeaponCategory.TankGun, RateOfFire = 6f });
            Assert.Equal(BarrelHeat.SingleShotHeatPerRound, tank.PerRound);
            Assert.Equal(BarrelHeat.SingleShotCoolSeconds, tank.CoolSeconds);

            Assert.Equal(0f, BarrelHeat.For(new WeaponMountDto { Category = WeaponCategory.Grenade }).PerRound);
            Assert.Equal(0f, BarrelHeat.For(new WeaponMountDto { Category = WeaponCategory.Countermeasure, RateOfFire = 60f }).PerRound);
            Assert.Equal(0f, BarrelHeat.For(new WeaponMountDto { Category = WeaponCategory.SmallArms, Hands = true }).PerRound);

            var stated = BarrelHeat.For(new WeaponMountDto { Category = WeaponCategory.MachineGun, RateOfFire = 3000f, HeatPerRound = 0.1f, BarrelCoolSeconds = 5f });
            Assert.Equal((0.1f, 5f), stated);
        }

        /// <summary>Every round spent heats; at 1 the barrel latches too hot — <see cref="Magazine.Ready"/> refuses — and opens again only
        /// once it has cooled to half. 🔴 Red-proof: drop the latch (open the moment it is below 1) ⇒ red at the 0.9 check.</summary>
        [Fact]
        public void CE1358_TooHot_Latches_UntilItCoolsToHalf()
        {
            var w = Loaded(new WeaponMountDto { Category = WeaponCategory.MachineGun, HeatPerRound = 0.25f, BarrelCoolSeconds = 10f });
            Assert.Equal(4, BarrelHeat.RoundsLeft(w));
            for (int i = 0; i < 4; i++) { Assert.True(Magazine.Ready(w)); Magazine.Spend(ref w); }
            Assert.True(BarrelHeat.IsOverheated(w));
            Assert.False(Magazine.Ready(w));
            Assert.Equal(0, BarrelHeat.RoundsLeft(w));
            Assert.Equal(996, w.Ammo);

            BarrelHeat.Tick(ref w, 1f);   // 1.0 → 0.9: still too hot
            Assert.Equal(0.9f, w.Heat, 3);
            Assert.False(Magazine.Ready(w));
            BarrelHeat.Tick(ref w, 3.5f);   // → 0.55: still too hot
            Assert.False(Magazine.Ready(w));
            BarrelHeat.Tick(ref w, 1f);     // → 0.45: below half, open
            Assert.True(Magazine.Ready(w));
            Assert.Equal(3, BarrelHeat.RoundsLeft(w));   // 0.45 + 3 × 0.25 ≥ 1
            BarrelHeat.Tick(ref w, 10f);
            Assert.Equal(0f, w.Heat);
        }

        /// <summary>A single-shot gun's barrel is warm after a round (the thermal sensor reads it) and never locks at its catalogue rate:
        /// a tank gun at 6/min cools more between rounds than one round adds.</summary>
        [Fact]
        public void CE1358_ASingleShotGun_IsWarm_ButNeverLocks_AtItsOwnRate()
        {
            var w = Loaded(new WeaponMountDto { Category = WeaponCategory.TankGun, RateOfFire = 6f });
            float peak = 0f;
            for (int round = 0; round < 40; round++)
            {
                Assert.True(Magazine.Ready(w));
                Magazine.Spend(ref w);
                peak = System.MathF.Max(peak, w.Heat);
                for (int s = 0; s < 100; s++) BarrelHeat.Tick(ref w, 0.1f);   // 10 s to the next round
            }
            Assert.InRange(peak, BarrelHeat.SingleShotHeatPerRound - 1e-3f, 0.3f);
            Assert.False(BarrelHeat.IsOverheated(w));
        }

        /// <summary>No barrel (a dispenser, a grenade): nothing heats, nothing blocks.</summary>
        [Fact]
        public void CE1358_NoBarrel_NeverHeats()
        {
            var w = Loaded(new WeaponMountDto { Category = WeaponCategory.Countermeasure });
            for (int i = 0; i < 500; i++) Magazine.Spend(ref w);
            Assert.Equal(0f, w.Heat);
            Assert.True(Magazine.Ready(w));
            Assert.Equal(int.MaxValue, BarrelHeat.RoundsLeft(w));
        }
    }
}
