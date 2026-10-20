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
            if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Combat.EffectState>()) w.RegisterComponent<Fdp.Toolkit.Combat.EffectState>();
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

        /// <summary>
        /// ⭐⭐ <c>CE-2322</c> (AQ97 D1, R-2062) — the fear of a civilian on ANY task: a person of the Civilian class with no
        /// <see cref="CivilianState"/> yet (his HSM never ran — he was ordered somewhere) is given one by the first fright, and feels it; a
        /// soldier is not. 🔴 Red before: only his HSM created the state, so an ordered civilian heard gunfire and felt nothing.
        /// ✅ Red-proof: the creation removed ⇒ no state ⇒ red.
        /// </summary>
        [Fact]
        public void CE2322_ACivilianOnAnotherTask_IsFrightened_ASoldierIsNot()
        {
            var w = World();
            var civilian = Person(w, Vector3.Zero, civilian: false);
            w.SetDisType(civilian, new DISEntityType { Kind = 3, Domain = 1, Category = 2 });   // the Civilian class (3.1.*.2)
            var soldier = Person(w, Vector3.Zero, civilian: false);
            w.SetDisType(soldier, new DISEntityType { Kind = 3, Domain = 1, Category = 1 });
            w.Bus.Publish(new SoundContactEvent { Observer = civilian, X = 20, Y = 0, SourceClass = (byte)SoundSourceClass.SmallArms });
            w.Bus.Publish(new SoundContactEvent { Observer = soldier, X = 20, Y = 0, SourceClass = (byte)SoundSourceClass.SmallArms });
            w.Bus.Publish(new SensorChangedEvent { Unit = civilian, What = SensorChange.NearMiss });
            Frame(w);
            Assert.True(w.HasComponent<CivilianState>(civilian));
            Assert.True(Fear(w, civilian) > 0.3f, $"heard and near-missed, he is frightened: {Fear(w, civilian)}");
            Assert.False(w.HasComponent<CivilianState>(soldier));
        }

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

        /// <summary>⭐ <c>CE-2239</c> part 2 (FL-6, §5j) — a stun grenade or tear gas hurts nobody and is not heard, yet it is force used on
        /// him: a NEW stun frightens him as a hit does, ONCE (his fear then fades while the stun runs), and a new irritation that lasts
        /// longer frightens him again. ✅ Red-proof: the effect block removed from <c>CivilianFearSystem</c> ⇒ red; the "already
        /// frightened by it" check removed ⇒ red (the fear never fades while the stun runs).</summary>
        [Fact]
        public void CE2239_AStunOrGas_FrightensHimOnce_ANewOneAgain()
        {
            var w = World();
            var man = Person(w, Vector3.Zero);
            w.AddComponent(man, new Fdp.Toolkit.Combat.EffectState { StunnedUntil = 1000 });
            Frame(w);
            Assert.Equal(CivilianFear.EffectImpulse, Fear(w, man), 3);
            for (int i = 0; i < 10; i++) Frame(w, 0.1f);
            Assert.Equal(CivilianFear.EffectImpulse - CivilianFear.DecayPerSecond, Fear(w, man), 3);
            w.GetComponentRW<Fdp.Toolkit.Combat.EffectState>(man).IrritatedUntil = 2000;   // the gas reaches him
            Frame(w);
            Assert.Equal(1f, Fear(w, man), 3);
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
