using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Tests;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Civilians.Tests
{
    /// <summary>
    /// ⭐ <c>CE-2235</c> (docs/DESIGN_Civilians_And_Crowd_Control.md §5d) — a civilian's fear: what he hears raises it, less the farther it
    /// seemed; a round past him raises it more; it fades when nothing new frightens him; and a unit that is not a civilian is untouched.
    /// </summary>
    public class CivilianFearSystemTests
    {
        private static EntityRepository World()
        {
            var w = PerceptionTestWorldFactory.Create();
            if (!w.IsComponentTypeRegistered<CivilianState>()) w.RegisterComponent<CivilianState>();
            if (!w.Bus.IsRegistered<SoundContactEvent>()) w.Bus.Register<SoundContactEvent>();
            if (!w.Bus.IsRegistered<SensorChangedEvent>()) w.Bus.Register<SensorChangedEvent>();
            return w;
        }

        private static Entity Person(EntityRepository w, Vector3 at, bool civilian = true)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            if (civilian) w.AddComponent(e, new CivilianState());
            return e;
        }

        private static void Frame(EntityRepository w, float dt = 0.1f)
        {
            w.SetSimulationTime(w.SimulationTime + dt);   // ⭐ the fade skips a fright of THIS instant — time must move
            w.Bus.SwapBuffers();
            new CivilianFearSystem().Execute(w, dt);
        }

        private static float Fear(EntityRepository w, Entity e) => w.GetComponentRO<CivilianState>(e).Fear;

        [Fact]
        public void CE2235_AShotFrightens_ANearOneMoreThanAFarOne_AndFootstepsNot()
        {
            var w = World();
            var near = Person(w, new Vector3(0, 0, 0));
            var far = Person(w, new Vector3(0, 0, 0));
            var walker = Person(w, new Vector3(0, 0, 0));
            w.Bus.Publish(new SoundContactEvent { Observer = near, X = 20, Y = 0, SourceClass = (byte)SoundSourceClass.SmallArms });
            w.Bus.Publish(new SoundContactEvent { Observer = far, X = 95, Y = 0, SourceClass = (byte)SoundSourceClass.SmallArms });
            w.Bus.Publish(new SoundContactEvent { Observer = walker, X = 5, Y = 0, SourceClass = (byte)SoundSourceClass.Footsteps });
            Frame(w);

            Assert.Equal(CivilianFear.Impulse(SoundSourceClass.SmallArms, 20f), Fear(w, near), 3);
            Assert.True(Fear(w, near) > Fear(w, far), $"a shot 20 m away frightens more than one 95 m away: {Fear(w, near)} vs {Fear(w, far)}");
            Assert.True(Fear(w, far) > 0f, "a shot at the edge of hearing still frightens");
            Assert.Equal(0f, Fear(w, walker));
            Assert.True(Fear(w, near) >= 0.3f, "one shot 20 m away alarms him (the HSM's default AlarmAt)");
        }

        [Fact]
        public void CE2235_ABlastFrightensMoreThanAShot_ANearMissMore_AndFearIsCappedAtOne()
        {
            Assert.True(CivilianFear.Impulse(SoundSourceClass.Explosion, 50f) > CivilianFear.Impulse(SoundSourceClass.SmallArms, 50f));
            var w = World();
            var man = Person(w, Vector3.Zero);
            w.Bus.Publish(new SensorChangedEvent { Unit = man, What = SensorChange.NearMiss });
            Frame(w);
            Assert.Equal(CivilianFear.NearMissImpulse, Fear(w, man), 3);
            for (int i = 0; i < 3; i++) w.Bus.Publish(new SensorChangedEvent { Unit = man, What = SensorChange.Hit });
            Frame(w);
            Assert.Equal(1f, Fear(w, man), 3);
        }

        [Fact]
        public void CE2235_FearFades_WhenNothingNewFrightensHim()
        {
            var w = World();
            var man = Person(w, Vector3.Zero);
            w.Bus.Publish(new SoundContactEvent { Observer = man, X = 10, Y = 0, SourceClass = (byte)SoundSourceClass.Explosion });
            Frame(w);
            float frightened = Fear(w, man);
            for (int i = 0; i < 10; i++) Frame(w, 0.1f);   // one quiet second
            Assert.Equal(frightened - CivilianFear.DecayPerSecond, Fear(w, man), 3);
            for (int i = 0; i < 400; i++) Frame(w, 0.1f);
            Assert.Equal(0f, Fear(w, man));
        }

        [Fact]
        public void CE2235_ASoldierWhoHearsTheShotIsUntouched()
        {
            var w = World();
            var soldier = Person(w, Vector3.Zero, civilian: false);
            w.Bus.Publish(new SoundContactEvent { Observer = soldier, X = 10, Y = 0, SourceClass = (byte)SoundSourceClass.SmallArms });
            Frame(w);
            Assert.False(w.HasComponent<CivilianState>(soldier));
        }
    }
}
