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
    }
}
