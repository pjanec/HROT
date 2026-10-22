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

        private static void At(EntityRepository w, double t) => w.SetSingletonUnmanaged(new GlobalTime { TotalTime = t, DeltaTime = 0.1f, TimeScale = 1f });

        /// <summary>
        /// ⭐⭐ <c>CE-3315</c> (restore design §1k) — the system keeps the world's count of sounds (<see cref="SoundTimeline"/>, which a checkpoint
        /// carries) and, when the world is PUT BACK, numbers on from it: a carried count from the restored moment wins; a count newer than the
        /// restored clock (a dry run puts no singleton back) gives way to what the ring had made before it. 🔴 Red-proof: drop the restore
        /// branch ⇒ the ring numbers on from 3 (the abandoned future's) — and in a fresh process from 0.
        /// </summary>
        [Fact]
        public void CE3315_AWorldPutBack_NumbersItsNextSoundWhereTheRestoredWorldStood()
        {
            var (w, e) = World();
            var s = new SoundEmissionSystem();
            for (int i = 1; i <= 3; i++) { At(w, i); w.Bus.Publish(new WeaponFireNotification { Shooter = e }); w.Bus.SwapBuffers(); Step(w, s); }
            var ring = SoundEmissionRing.Of(w);
            Assert.Equal(3, ring.Places);
            Assert.Equal(3, w.GetSingleton<SoundTimeline>().Places);           // the world keeps the count beside the ring

            // a checkpoint restore at 1.5 s: the keyframe brought back the count of that moment
            w.SetSingletonUnmanaged(new SoundTimeline { Places = 1, At = 1.0 });
            At(w, 1.5);
            Fdp.Toolkit.Replication.Services.WorldEpoch.Restored(w);
            Step(w, s);
            Assert.Equal(1, ring.Places);                                        // numbers on from 1, the abandoned 2 and 3 left behind

            // a dry-run exit at 1.2 s: no singleton put back — the count (1, at 1.0) is still the restored moment's; then one that is not
            At(w, 2.0); w.Bus.Publish(new WeaponFireNotification { Shooter = e }); w.Bus.SwapBuffers(); Step(w, s);
            Assert.Equal(2, w.GetSingleton<SoundTimeline>().Places);
            At(w, 1.8);                                                         // the world goes back to 1.8 s; the count says 2 at 2.0 s
            Fdp.Toolkit.Replication.Services.WorldEpoch.Restored(w);
            Step(w, s);
            Assert.Equal(1, ring.Places);                                        // ⇒ the ring's own count before 1.8 s (place 1)
        }

        /// <summary>⭐ <c>CE-3063</c> (R-207) — the TKB authors what each sound sounds LIKE; a detonation with no class is an explosion.</summary>
        [Fact]
        public void TheTkbAuthorsTheSourceClass_AndADetonationDefaultsToAnExplosion_CE3063()
        {
            var w = new EntityRepository();
            w.RegisterComponent<AcousticEmitter>();
            var e = w.CreateEntity();
            var template = new Fdp.Interfaces.TkbTemplate("Tank", 3063);
            template.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.SignaturesDto
            {
                Acoustic = new Fdp.Toolkit.Tkb.Domain.AcousticSignatureDto
                {
                    MovingAudibleRange = 400f,
                    MovingClass = Fdp.Toolkit.Tkb.Domain.SoundSourceClass.TrackedEngine,
                    FiringClass = Fdp.Toolkit.Tkb.Domain.SoundSourceClass.HeavyWeapon,
                },
            });
            new SignatureTkbTranslator().Inject(w, e, template);

            var a = w.GetComponentRO<AcousticEmitter>(e);
            Assert.Equal((byte)Fdp.Toolkit.Tkb.Domain.SoundSourceClass.TrackedEngine, a.MovingClass);
            Assert.Equal((byte)Fdp.Toolkit.Tkb.Domain.SoundSourceClass.HeavyWeapon, a.FiringClass);
            Assert.Equal((byte)Fdp.Toolkit.Tkb.Domain.SoundSourceClass.Explosion, a.DetonationClass);
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

        /// <summary>⭐ Q94 A-2 (R-3018) — a shot the muscle REFUSED never flew: nobody hears it. ✅ Red-proof: the <c>Fired</c> skip removed ⇒
        /// the refused shot is heard ⇒ red.</summary>
        [Fact]
        public void Q94_ARefusedShot_IsNotHeard()
        {
            var (w, e) = World();
            var s = new SoundEmissionSystem();
            w.Bus.Publish(new WeaponFireNotification { Shooter = e, Refusal = Fdp.Toolkit.Combat.FireRefusal.Masked });
            w.Bus.SwapBuffers();
            Step(w, s);
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
    
        /// <summary>⭐ M7 (DESIGN_FPV_Drone §2, §5) — a flyer is heard at its full moving range while AIRBORNE, even hovering (a source
        /// below the audible speed was silent); once down (Airborne 0) the speed rule applies again.</summary>
        [Fact]
        public void M7_AHoveringDrone_IsHeard_AsRotor()
        {
            var (w, e) = World();
            w.RegisterComponent<Fdp.Toolkit.Motion.FlightState>();
            w.AddComponent(e, new Fdp.Toolkit.Motion.FlightState { Airborne = 1 });
            var s = new SoundEmissionSystem();
            Step(w, s); Step(w, s);   // not moving
            Assert.Equal(200f, w.GetComponentRO<AcousticEmitter>(e).CurrentMovingRange);

            w.SetComponent(e, new Fdp.Toolkit.Motion.FlightState { Airborne = 0 });
            Step(w, s);
            Assert.Equal(0f, w.GetComponentRO<AcousticEmitter>(e).CurrentMovingRange);
            Assert.Equal((byte)7, (byte)Fdp.Toolkit.Tkb.Domain.SoundSourceClass.Rotor);
        }

}
}
