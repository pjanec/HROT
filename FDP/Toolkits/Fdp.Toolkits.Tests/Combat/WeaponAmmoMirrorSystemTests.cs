using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Combat.Systems;
using Xunit;

namespace Fdp.Toolkit.Combat.Tests
{
    /// <summary>⭐ CE-3174 (C-7; docs/DESIGN_Entity_Control.md §8d) — the brain mirrors every mount's ammunition into WeaponAmmo, the one
    /// component a station's HUD reads (the WeaponStates never leave the brain), and only for units whose brain runs here.</summary>
    public sealed class WeaponAmmoMirrorSystemTests
    {
        [Fact]
        public void CE3174_EveryMountsAmmunition_IsMirrored_AndFollowsAFiredRound()
        {
            using var w = new EntityRepository();
            w.RegisterComponent<WeaponState>();
            w.RegisterComponent<BehaviorState>();
            var unit = w.CreateEntity();
            w.AddComponent(unit, new BehaviorState());
            w.SetAuthority<BehaviorState>(unit, true);
            w.AddComponent(unit, new WeaponState { Ammo = 30, MaxAmmo = 30, MagazineSize = 10, MagazineRounds = 10 });
            var notMine = w.CreateEntity();
            w.AddComponent(notMine, new BehaviorState());
            w.AddComponent(notMine, new WeaponState { Ammo = 5, MaxAmmo = 5 });
            var mirror = new WeaponAmmoMirrorSystem();

            mirror.Execute(w, 0.016f);
            var a = w.GetComponentRO<WeaponAmmo>(unit);
            Assert.Equal((1, 30, 30, 10), (a.Count, a.Ammo0, a.Max0, a.Mag0));
            Assert.False(w.HasComponent<WeaponAmmo>(notMine));   // its brain runs elsewhere: that node mirrors it

            ref var ws = ref w.GetComponentRW<WeaponState>(unit);
            Magazine.Spend(ref ws);
            mirror.Execute(w, 0.016f);
            a = w.GetComponentRO<WeaponAmmo>(unit);
            Assert.Equal((29, 9), (a.Ammo0, a.Mag0));
        }

        /// <summary>
        /// ⭐ <c>CE-4659</c> (thermal drivers TD-5, §3d) — the mirror keeps each mount's LAST BURST: shots under a second apart are one burst (its rounds, when it
        /// began, its last shot, the heat just after it); while the barrel cools the record does NOT change (no sample — the retired percent sent one per
        /// step); the heat computed from it later equals the firing node's <see cref="WeaponState.Heat"/>; a shot after a pause opens a new burst.
        /// 🔴 Red-proofs: no burst gap (every shot a new burst) ⇒ rounds 1 ⇒ red; the record updated while cooling ⇒ the "unchanged" assert ⇒ red.
        /// </summary>
        [Fact]
        public void CE4659_TheMirrorKeepsEachMountsLastBurst_AndTheComputedHeatMatchesTheBarrel()
        {
            using var w = new EntityRepository();
            w.RegisterComponent<WeaponState>();
            w.RegisterComponent<BehaviorState>();
            var unit = w.CreateEntity();
            w.AddComponent(unit, new BehaviorState());
            w.SetAuthority<BehaviorState>(unit, true);
            w.AddComponent(unit, new WeaponState { Ammo = 100, MaxAmmo = 100, HeatPerRound = 0.1f, HeatCoolPerSecond = 0.05f });
            var mirror = new WeaponAmmoMirrorSystem();
            double t = 10.0;
            void Frame(bool shoot, float dt = 0.1f)
            {
                t += dt;
                w.SetSingletonUnmanaged(new GlobalTime { TotalTime = t });
                ref var ws = ref w.GetComponentRW<WeaponState>(unit);
                BarrelHeat.Tick(ref ws, dt);
                if (shoot) Magazine.Spend(ref ws);
                mirror.Execute(w, dt);
            }

            Frame(false);
            Assert.True(double.IsNegativeInfinity(w.GetComponentRO<WeaponAmmo>(unit).LastShotAt0));   // never fired
            Frame(true); double first = t;
            Frame(true); Frame(true); Frame(true);                                                   // 4 rounds, 0.1 s apart: one burst
            var a = w.GetComponentRO<WeaponAmmo>(unit);
            Assert.Equal(4, a.BurstRounds0);
            Assert.Equal(first, a.BurstStartedAt0, 6);
            Assert.Equal(t, a.LastShotAt0, 6);
            Assert.Equal(w.GetComponentRO<WeaponState>(unit).Heat, a.HeatAtLastShot0, 5);

            for (int i = 0; i < 30; i++)                                                              // 3 s of cooling
            {
                var before = w.GetComponentRO<WeaponAmmo>(unit);
                Frame(false);
                Assert.True(before.Equals(w.GetComponentRO<WeaponAmmo>(unit)), "no sample while the barrel cools");
            }
            Assert.Equal(w.GetComponentRO<WeaponState>(unit).Heat, w.GetComponentRO<WeaponAmmo>(unit).HeatAt(0, t), 3);   // computed = the barrel

            Frame(true);                                                                              // after a 3 s pause: a new burst
            a = w.GetComponentRO<WeaponAmmo>(unit);
            Assert.Equal((1, t), (a.BurstRounds0, a.BurstStartedAt0));
        }

        /// <summary>⭐ CE-1079 (docs/DESIGN_Entity_Control.md §8e) — the HUD's breech: the loaded round, how many of it are left, and the
        /// LOADING bit while a reload or a round switch runs; a mount with no loadout says so (0, −1).</summary>
        [Fact]
        public void CE1079_TheMirror_CarriesTheBreech_ItsRounds_AndTheLoadingBit()
        {
            using var w = new EntityRepository();
            w.RegisterComponent<WeaponState>();
            w.RegisterComponent<BehaviorState>();
            w.RegisterComponent<AmmoLoadout>();
            var unit = w.CreateEntity();
            w.AddComponent(unit, new BehaviorState());
            w.SetAuthority<BehaviorState>(unit, true);
            w.AddComponent(unit, new WeaponState { Ammo = 9, MaxAmmo = 9 });
            w.AddComponent(unit, new AmmoLoadout { Count = 2, Ammo0 = 6006, Rounds0 = 5, Ammo1 = 6018, Rounds1 = 4, Loaded = 1, SwitchRemaining = 3f });
            var mirror = new WeaponAmmoMirrorSystem();

            mirror.Execute(w, 0.016f);
            var a = w.GetComponentRO<WeaponAmmo>(unit);
            Assert.Equal((6018L, 4, true), (a.LoadedOf(0), a.LoadedRoundsOf(0), a.IsBusy(0)));

            w.GetComponentRW<AmmoLoadout>(unit).SwitchRemaining = 0f;
            mirror.Execute(w, 0.016f);
            Assert.False(w.GetComponentRO<WeaponAmmo>(unit).IsBusy(0));
            w.GetComponentRW<WeaponState>(unit).ReloadSecondsRemaining = 2f;
            mirror.Execute(w, 0.016f);
            Assert.True(w.GetComponentRO<WeaponAmmo>(unit).IsBusy(0));   // a reload is LOADING too

            w.RemoveComponent<AmmoLoadout>(unit);
            mirror.Execute(w, 0.016f);
            a = w.GetComponentRO<WeaponAmmo>(unit);
            Assert.Equal((0L, -1), (a.LoadedOf(0), a.LoadedRoundsOf(0)));
        }

        /// <summary>⭐ CE-4366 (L-4, docs/DESIGN_Scale_Rig.md §5) — a steady frame's garbage does not grow with the units: the unchanged-ammo test
        /// compared through <c>Equals(object)</c> (two boxes per unit per frame — 2.3 MB per frame at 1 000 men on the scale rig).</summary>
        [Fact]
        public void CE4366_ASteadyFrame_AllocatesNothingPerUnit()
        {
            long PerFrame(int units)
            {
                using var w = new EntityRepository();
                w.RegisterComponent<WeaponState>();
                w.RegisterComponent<BehaviorState>();
                for (int i = 0; i < units; i++)
                {
                    var u = w.CreateEntity();
                    w.AddComponent(u, new BehaviorState());
                    w.SetAuthority<BehaviorState>(u, true);
                    w.AddComponent(u, new WeaponState { Ammo = 30, MaxAmmo = 30, MagazineSize = 10, MagazineRounds = 10 });
                }
                var mirror = new WeaponAmmoMirrorSystem();
                mirror.Execute(w, 0.016f); mirror.Execute(w, 0.016f);   // the components exist, the lists are grown
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                for (int f = 0; f < 10; f++) mirror.Execute(w, 0.016f);
                return (System.GC.GetAllocatedBytesForCurrentThread() - before) / 10;
            }
            long few = PerFrame(10), many = PerFrame(500);
            Assert.True(many - few <= 64, $"10 units: {few} B per frame, 500 units: {many} B per frame — the per-unit garbage is back");
        }
    }
}
