using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Combat.Contracts;
using Fdp.Toolkit.Combat.Events;
using Fdp.Toolkit.Perception.Signatures;
using Xunit;

namespace Fdp.Toolkit.Perception.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3062</c> — what an entity sounds like right now (R-205: "Moving entity also makes sound"):
    /// docs/DESIGN_Thermal_And_Acoustic_Sensing.md §6 B–C.
    /// </summary>
    public class SoundEmissionSystemTests
    {
        private static (EntityRepository w, Entity e) World()
        {
            var w = new EntityRepository();
            w.RegisterComponent<SimTransform>();
            w.RegisterComponent<AcousticEmitter>();
            w.RegisterEvent<WeaponFireNotification>();
            w.RegisterEvent<DetonationNotification>();
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = new Vector3(5f, 6f, 0f), Rotation = Quaternion.Identity });
            w.AddComponent(e, new AcousticEmitter
            {
                MovingAudibleRange = 200f, ReferenceSpeed = 10f, FiringAudibleRange = 800f, DetonationAudibleRange = 1000f,
            });
            return (w, e);
        }

        private static void Step(EntityRepository w, SoundEmissionSystem s, float dt = 0.1f)
        {
            s.Execute(w, dt);
            w.Bus.SwapBuffers();
        }

        [Fact]
        public void Movement_CarriesInProportionToSpeed_AndStillIsSilent()
        {
            var (w, e) = World();
            var s = new SoundEmissionSystem();
            Step(w, s);
            Assert.Equal(0f, w.GetComponentRO<AcousticEmitter>(e).CurrentMovingRange);

            ref var t = ref w.GetComponentRW<SimTransform>(e);
            t.Position += new Vector3(0.5f, 0f, 0f);                 // 5 m/s = half the reference speed
            Step(w, s);
            Assert.Equal(100f, w.GetComponentRO<AcousticEmitter>(e).CurrentMovingRange, 2);

            Step(w, s);                                               // stopped
            Assert.Equal(0f, w.GetComponentRO<AcousticEmitter>(e).CurrentMovingRange);
        }

        [Fact]
        public void AShot_StaysAudibleBriefly_FromWhereItWasFired()
        {
            var (w, e) = World();
            var s = new SoundEmissionSystem();
            w.Bus.Publish(new WeaponFireNotification { Shooter = e });
            w.Bus.SwapBuffers();
            Step(w, s);
            var a = w.GetComponentRO<AcousticEmitter>(e);
            Assert.Equal(SoundEmissionSystem.SoundLingerSeconds, a.ShotTimeLeft, 3);
            Assert.Equal(5f, a.ShotX);
            for (int i = 0; i < 6; i++) Step(w, s);                  // 0.6 s later
            Assert.Equal(0f, w.GetComponentRO<AcousticEmitter>(e).ShotTimeLeft);
        }

        [Fact]
        public void ADetonation_IsHeardWhereItBurst()
        {
            var (w, e) = World();
            var s = new SoundEmissionSystem();
            w.Bus.Publish(new DetonationNotification { Shooter = e, HitX = 300f, HitY = 40f, HitZ = 1f });
            w.Bus.SwapBuffers();
            Step(w, s);
            var a = w.GetComponentRO<AcousticEmitter>(e);
            Assert.True(a.DetonationTimeLeft > 0f);
            Assert.Equal(300f, a.DetonationX);
            Assert.Equal(40f, a.DetonationY);
        }
    }
}
