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
    }
}
