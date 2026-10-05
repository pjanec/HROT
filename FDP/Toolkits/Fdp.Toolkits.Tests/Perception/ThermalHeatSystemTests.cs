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
    }
}
