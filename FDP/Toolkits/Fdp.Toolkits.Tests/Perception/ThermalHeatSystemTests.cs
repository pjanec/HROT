using System.Numerics;
using Fdp.Core;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Perception.Signatures;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3061</c> — heat builds while an entity runs or fires and cools when it stops (R-205,
    /// docs/DESIGN_Thermal_And_Acoustic_Sensing.md §5.2).
    /// </summary>
    public class ThermalHeatSystemTests
    {
        private static (EntityRepository w, Entity e) World()
        {
            var w = new EntityRepository();
            w.RegisterComponent<SimTransform>();
            w.RegisterComponent<ThermalState>();
            w.RegisterEvent<WeaponFireNotification>();
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            w.AddComponent(e, new ThermalState
            {
                BaseSignature = 0.3f, RunningHeatPerSecond = 0.5f, FiringHeatPerShot = 0.1f, CooldownPerSecond = 0.2f, ReferenceSpeed = 5f,
            });
            return (w, e);
        }

        private static void Step(EntityRepository w, ThermalHeatSystem sys, float dt = 0.1f)
        {
            sys.Execute(w, dt);
            w.Bus.SwapBuffers();
        }

        [Fact]
        public void AtRest_TheSignatureIsTheBase()
        {
            var (w, e) = World();
            var sys = new ThermalHeatSystem();
            for (int i = 0; i < 10; i++) Step(w, sys);
            Assert.Equal(0f, w.GetComponentRO<ThermalState>(e).Heat);
            Assert.Equal(0.3f, w.GetComponentRO<ThermalState>(e).Signature, 3);
        }

        [Fact]
        public void Running_BuildsHeat_ThenItCoolsWhenStopped()
        {
            var (w, e) = World();
            var sys = new ThermalHeatSystem();
            Step(w, sys);                                                 // first sample: no speed yet
            for (int i = 0; i < 20; i++)                                  // 2 s at 5 m/s (0.5 m per 0.1 s)
            {
                ref var t = ref w.GetComponentRW<SimTransform>(e);
                t.Position += new Vector3(0.5f, 0f, 0f);
                Step(w, sys);
            }
            float hot = w.GetComponentRO<ThermalState>(e).Heat;
            Assert.InRange(hot, 0.6f, 1f);                                // ≈ 0.5/s for 2 s, less what cooled
            Assert.True(w.GetComponentRO<ThermalState>(e).Signature > 0.3f + 0.6f * 0.7f - 1e-3f);

            for (int i = 0; i < 50; i++) Step(w, sys);                    // 5 s at rest
            float cooled = w.GetComponentRO<ThermalState>(e).Heat;
            Assert.True(cooled < hot * 0.5f, $"heat should cool (exponential): {hot} → {cooled}");
        }

        [Fact]
        public void EachShot_AddsHeat_ForTheShooterOnly()
        {
            var (w, e) = World();
            var other = w.CreateEntity();
            w.AddComponent(other, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            w.AddComponent(other, new ThermalState { BaseSignature = 0.3f, FiringHeatPerShot = 0.1f });
            var sys = new ThermalHeatSystem();
            w.Bus.Publish(new WeaponFireNotification { Shooter = e });
            w.Bus.Publish(new WeaponFireNotification { Shooter = e });
            w.Bus.SwapBuffers();
            sys.Execute(w, 0f);
            Assert.Equal(0.2f, w.GetComponentRO<ThermalState>(e).Heat, 3);
            Assert.Equal(0f, w.GetComponentRO<ThermalState>(other).Heat);
        }

        /// <summary>
        /// ⭐ <c>CE-1358</c> (DESIGN_Weapon_Layers §5k) — a hot BARREL warms its unit for the thermal sensor: its own mount, a mount child, a
        /// station weapon embarked in it (a door gun on its helicopter) — the hottest one; the signature is the hotter of body and barrel.
        /// 🔴 Red-proof: drop the barrel pass ⇒ the child's and the door gun's heat are never seen.
        /// </summary>
        [Fact]
        public void CE1358_AHotBarrel_WarmsItsUnit_OwnMountChildOrStationWeapon()
        {
            var (w, e) = World();
            w.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponState>();
            w.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponMountInfo>();
            w.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
            w.RegisterComponent<Fdp.Toolkit.Behavior.Components.IsEmbarkedTag>();
            var sys = new ThermalHeatSystem();

            w.AddComponent(e, new Fdp.Toolkit.Combat.Components.WeaponState { Heat = 0.2f });
            Step(w, sys, 0f);
            Assert.Equal(0.2f, w.GetComponentRO<ThermalState>(e).BarrelHeat, 3);

            var child = w.CreateEntity();
            w.AddComponent(child, new Fdp.Toolkit.Combat.Components.WeaponState { Heat = 0.5f });
            w.AddComponent(child, new Fdp.Toolkit.Combat.Components.WeaponMountInfo { MountIndex = 1 });
            w.AddComponent(child, new Fdp.Toolkit.Replication.Components.PartMetadata { ParentEntity = e });
            Step(w, sys, 0f);
            Assert.Equal(0.5f, w.GetComponentRO<ThermalState>(e).BarrelHeat, 3);

            var doorGun = w.CreateEntity();
            w.AddComponent(doorGun, new Fdp.Toolkit.Combat.Components.WeaponState { Heat = 0.9f });
            w.AddComponent(doorGun, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = e });
            Step(w, sys, 0f);
            var t = w.GetComponentRO<ThermalState>(e);
            Assert.Equal(0.9f, t.BarrelHeat, 3);
            Assert.Equal(0.3f + 0.9f * 0.7f, t.Signature, 3);   // the barrel is hotter than the cold body

            w.GetComponentRW<Fdp.Toolkit.Combat.Components.WeaponState>(doorGun).Heat = 0f;   // it cooled
            Step(w, sys, 0f);
            Assert.Equal(0.5f, w.GetComponentRO<ThermalState>(e).BarrelHeat, 3);
        }
    }
}
