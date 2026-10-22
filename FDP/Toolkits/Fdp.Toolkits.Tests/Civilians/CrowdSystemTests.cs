using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Perception.Tests;
using Xunit;

namespace Fdp.Toolkit.Civilians.Tests
{
    /// <summary>
    /// ⭐ <c>CE-2237</c> (docs/DESIGN_Civilians_And_Crowd_Control.md §5f) — the crowd system: civilians standing linked are one crowd with
    /// one id that survives their moving about, a lone one is in none; a person hurt draws the calm civilians within 80 m, not a frightened
    /// one nor a farther one; fear spreads to a member who heard nothing, never above 0.9 of what he caught it from, and two frightened
    /// neighbours do not hold each other frightened.
    /// </summary>
    public class CrowdSystemTests
    {
        private static EntityRepository World()
        {
            var w = PerceptionTestWorldFactory.Create();
            if (!w.IsComponentTypeRegistered<CivilianState>()) w.RegisterComponent<CivilianState>();
            if (!w.IsComponentTypeRegistered<Crowd>()) w.RegisterComponent<Crowd>();
            if (!w.IsComponentTypeRegistered<Health>()) w.RegisterComponent<Health>();
            if (!w.IsComponentTypeRegistered<InjuryThresholds>()) w.RegisterComponent<InjuryThresholds>();
            if (!w.Bus.IsRegistered<SoundContactEvent>()) w.Bus.Register<SoundContactEvent>();
            if (!w.Bus.IsRegistered<SensorChangedEvent>()) w.Bus.Register<SensorChangedEvent>();
            if (!w.IsComponentTypeRegistered<Fdp.Toolkit.Combat.EffectState>()) w.RegisterComponent<Fdp.Toolkit.Combat.EffectState>();
            return w;
        }

        /// <summary>⭐ <c>CE-2241</c> ② — a policeman (the <c>SecurityForce</c> class, alive) at (x, y) facing <paramref name="headingDeg"/>.</summary>
        private static Entity Policeman(EntityRepository w, float x, float y, float headingDeg = 0f)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0),
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, headingDeg * System.MathF.PI / 180f) });
            w.AddComponent(e, new Health { Current = 100, Max = 100 });
            w.SetDisType(e, new DISEntityType { Kind = 3, Domain = 1, Category = Fdp.Toolkit.Combat.Armament.SecurityForceCategory });
            return e;
        }

        private static Entity Civ(EntityRepository w, float x, float y, CivilianMode mode = CivilianMode.Routine, float fear = 0f)
        {
            var e = w.CreateEntity();
            w.AddComponent(e, new SimTransform { Position = new Vector3(x, y, 0), Rotation = Quaternion.Identity });
            w.AddComponent(e, new CivilianState { Mode = mode, Fear = fear });
            return e;
        }

        private static void Frame(EntityRepository w, CrowdSystem crowds, float dt = 0.1f, bool fade = false)
        {
            w.SetSimulationTime(w.SimulationTime + dt);
            w.Bus.SwapBuffers();
            if (fade) new CivilianFearSystem().Execute(w, dt);
            crowds.Execute(w, dt);
        }

        private static ref CivilianState Mind(EntityRepository w, Entity e) => ref w.GetComponentRW<CivilianState>(e);

        private static void Move(EntityRepository w, Entity e, float x, float y)
            => w.GetComponentRW<SimTransform>(e).Position = new Vector3(x, y, 0);

        /// <summary>
        /// ⭐⭐ <c>R-3039</c> S1 (docs/DESIGN_System_State_Inventory.md §6 #9) — a crowd survives a checkpoint restored into a FRESH system as ONE
        /// crowd: formed, keyframe, applied to a new world with a NEW system, a frame on ⇒ still one crowd entity with the same id, and a NEW crowd
        /// formed after it gets a fresh id. 🔴 Red-proof: the id → entity map back in a system field ⇒ the fresh system makes a second crowd
        /// entity for the same id ⇒ red; the counter back ⇒ the new crowd reuses id 1 ⇒ red.
        /// </summary>
        [Fact]
        public void R3039_ACrowd_SurvivesACheckpointRestoredIntoAFreshSystem_AsOneCrowd()
        {
            var w = World();
            var chain = new[] { Civ(w, 0, 0), Civ(w, 10, 0), Civ(w, 20, 0) };
            Frame(w, new CrowdSystem());
            int id = Mind(w, chain[0]).CrowdId;
            Assert.NotEqual(0, id);

            var kf = Fdp.Toolkit.Replay.WorldKeyframes.Capture(w, Fdp.Toolkit.Replay.CaptureOptions.Recording);
            var fresh = World();
            Fdp.Toolkit.Replay.WorldKeyframes.Apply(fresh, kf);
            var crowds = new CrowdSystem();
            var other = new[] { Civ(fresh, 300, 0), Civ(fresh, 310, 0), Civ(fresh, 320, 0) };   // a second crowd forms after the restore
            Frame(fresh, crowds);

            int crowdsAlive = 0;
            foreach (var _ in fresh.Query().With<Crowd>().Build()) crowdsAlive++;
            Assert.Equal(2, crowdsAlive);
            Assert.All(chain, e => Assert.Equal(id, Mind(fresh, e).CrowdId));
            int second = Mind(fresh, other[0]).CrowdId;
            Assert.NotEqual(0, second);
            Assert.NotEqual(id, second);
        }

        [Fact]
        public void CE2237_CiviliansStandingLinked_AreOneCrowd_ALoneOneIsNot_TheIdSurvivesAMove_AndABrokenCrowdIsGone()
        {
            var w = World();
            var crowds = new CrowdSystem();
            var chain = new[] { Civ(w, 0, 0), Civ(w, 10, 0), Civ(w, 20, 0), Civ(w, 30, 0) };   // 10 m apart: linked one to the next
            var pair = new[] { Civ(w, 200, 0), Civ(w, 205, 0) };                                  // two are not a crowd
            Frame(w, crowds);

            int id = Mind(w, chain[0]).CrowdId;
            Assert.NotEqual(0, id);
            Assert.All(chain, e => Assert.Equal(id, Mind(w, e).CrowdId));
            Assert.All(pair, e => Assert.Equal(0, Mind(w, e).CrowdId));
            var ce = crowds.EntityOf(id);
            Assert.True(w.IsAlive(ce));
            var crowd = w.GetComponentRO<Crowd>(ce);
            Assert.Equal(4, crowd.Count);
            Assert.Equal(15f, crowd.Centroid.X, 3);

            Move(w, chain[3], 33, 4);   // still within 15 m of the third
            Frame(w, crowds);
            Assert.All(chain, e => Assert.Equal(id, Mind(w, e).CrowdId));

            Move(w, chain[2], 100, 100);   // the chain breaks: two and one and one
            Move(w, chain[3], 130, 100);
            Frame(w, crowds);
            Assert.All(chain, e => Assert.Equal(0, Mind(w, e).CrowdId));
            Assert.False(w.IsAlive(ce), "a crowd with no members is destroyed");
            Assert.Equal(Entity.Null, crowds.EntityOf(id));
        }

        [Fact]
        public void CE2237_APersonHurt_DrawsTheCalmWithin80m_NotTheFrightened_NorTheFarther_AndTheCrowdTakesHisFocus()
        {
            var w = World();
            var crowds = new CrowdSystem();
            var hurt = w.CreateEntity();
            w.AddComponent(hurt, new SimTransform { Position = new Vector3(0, 0, 0), Rotation = Quaternion.Identity });
            w.AddComponent(hurt, new Health { Current = 40f, Max = 100f });
            w.AddComponent(hurt, new InjuryThresholds { WoundedBelow = 0.6f, IncapacitatedBelow = 0.3f });
            var calm = Civ(w, 50, 0);
            var looking = Civ(w, 60, 0, CivilianMode.Gather);
            var frightened = Civ(w, 55, 5, CivilianMode.Alarmed, 0.4f);
            var far = Civ(w, 120, 0);
            Frame(w, crowds);

            Assert.Equal(1, Mind(w, calm).HasFocus);
            Assert.Equal(Vector3.Zero, Mind(w, calm).Focus);
            Assert.Equal(1, Mind(w, looking).HasFocus);
            Assert.Equal(0, Mind(w, frightened).HasFocus);
            Assert.Equal(0, Mind(w, far).HasFocus);
            var crowd = w.GetComponentRO<Crowd>(crowds.EntityOf(Mind(w, calm).CrowdId));
            Assert.Equal(3, crowd.Count);   // the calm, the onlooker and the frightened one stand linked; the far one does not
            Assert.Equal(1, crowd.HasFocus);   // two of three are drawn to it

            w.GetComponentRW<Health>(hurt).Current = 100f;   // no longer hurt (a test's shortcut; nothing heals in play)
            Frame(w, crowds);
            Assert.Equal(0, Mind(w, calm).HasFocus);
        }

        [Fact]
        public void CE2237_FearSpreads_ToAMemberWhoHeardNothing_NeverAbove09OfItsSource_AndOnlyFromOneWhoShowsIt()
        {
            var w = World();
            var crowds = new CrowdSystem();
            var src = Civ(w, 0, 0, CivilianMode.Panic, 1f);
            Mind(w, src).FrightFrom = new Vector3(-50, 0, 0);
            Mind(w, src).HasFrightFrom = 1;
            var near = Civ(w, 8, 0);
            Civ(w, 0, 10);   // a third, so the two are a crowd
            var calmSource = Civ(w, 300, 0, CivilianMode.Routine, 0.9f);   // frightened inside, but showing nothing
            var calmNear = Civ(w, 305, 0);
            Civ(w, 310, 0);
            for (int i = 0; i < 40; i++) Frame(w, crowds);   // 4 s

            Assert.True(Mind(w, near).Fear > 0.6f, $"he caught it — past panic: {Mind(w, near).Fear}");
            Assert.True(Mind(w, near).Fear <= 0.9f + 1e-4f, $"never above 0.9 of his source (design §5f — a literal, so a changed share reddens it): {Mind(w, near).Fear}");
            Assert.Equal(0.0, Mind(w, near).HeardAt);
            Assert.Equal(1, Mind(w, near).HasFrightFrom);
            Assert.Equal(new Vector3(-50, 0, 0), Mind(w, near).FrightFrom);   // he runs from where the others run from
            Assert.Equal(0f, Mind(w, calmNear).Fear);   // a man who shows nothing spreads nothing
        }

        [Fact]
        public void CE2237_TwoFrightenedNeighbours_DoNotHoldEachOtherFrightened_TheirFearStillFades()
        {
            var w = World();
            var crowds = new CrowdSystem();
            var a = Civ(w, 0, 0, CivilianMode.Cower, 0.8f);
            var b = Civ(w, 5, 0, CivilianMode.Cower, 0.8f);
            Civ(w, 10, 0, CivilianMode.Cower, 0.8f);
            for (int i = 0; i < 100; i++) Frame(w, crowds, fade: true);   // 10 s of the fade at 0.05/s
            Assert.True(Mind(w, a).Fear < 0.4f && Mind(w, b).Fear < 0.4f, $"both fade: {Mind(w, a).Fear}, {Mind(w, b).Fear}");
        }

        /// <summary>⭐ <c>CE-2238</c> (§5i) — an INSTIGATOR raises the anger of his crowd near him (past the 0.6 riot line in a few seconds
        /// beside him), not of a member beyond <c>ContagionMetres</c> nor of a civilian in another crowd; his own anger is 1; with him
        /// gone, anger fades.</summary>
        [Fact]
        public void CE2238_AnInstigator_RaisesHisCrowdsAngerNearHim_NotFarOrOtherCrowds_AndItFadesWithoutHim()
        {
            var w = World();
            var crowds = new CrowdSystem();
            var inst = Civ(w, 0, 0, CivilianMode.Protest);
            Mind(w, inst).Instigator = 1;
            var near = Civ(w, 3, 0, CivilianMode.Protest);
            var mid = Civ(w, 14, 0, CivilianMode.Protest);
            var far = Civ(w, 28, 0, CivilianMode.Protest);                  // linked through mid, 28 m from the instigator
            var other = new[] { Civ(w, 300, 0), Civ(w, 305, 0), Civ(w, 310, 0) };
            for (int i = 0; i < 60; i++) Frame(w, crowds);                  // 6 s
            Assert.Equal(1f, Mind(w, inst).Anger);
            Assert.True(Mind(w, near).Anger >= 0.6f, $"beside him he is past the riot line: {Mind(w, near).Anger}");
            Assert.True(Mind(w, mid).Anger > 0f && Mind(w, mid).Anger < Mind(w, near).Anger, $"farther, slower: {Mind(w, mid).Anger}");
            Assert.Equal(0f, Mind(w, far).Anger);
            Assert.All(other, o => Assert.Equal(0f, Mind(w, o).Anger));

            Mind(w, inst).Instigator = 0;
            Move(w, inst, 500, 500);
            float before = Mind(w, near).Anger;
            for (int i = 0; i < 100; i++) Frame(w, crowds);                 // 10 s at 0.02/s
            Assert.InRange(Mind(w, near).Anger, before - 0.21f, before - 0.19f);
        }
    
        /// <summary>⭐ <c>CE-2241</c> ② (§5m PV-1) — a policeman 15 m off FACING the crowd raises its anger slowly (0.01/s, half at 30 m ⇒
        /// 0.0075/s at 15 m); one facing AWAY, or one beyond 30 m, does not; once he is gone the anger fades.</summary>
        [Fact]
        public void CE2241_AFacingPoliceman_RaisesTheCrowdsAngerSlowly_NotFacingAwayOrFar_AndItFadesWhenHeGoes()
        {
            var w = World();
            var crowds = new CrowdSystem();
            var a = Civ(w, 0, 0, CivilianMode.Protest);
            var b = Civ(w, 3, 0, CivilianMode.Protest);
            var c = Civ(w, 6, 0, CivilianMode.Protest);
            var police = Policeman(w, -15, 0, headingDeg: 0f);              // X-forward: facing the crowd
            for (int i = 0; i < 100; i++) Frame(w, crowds);                  // 10 s
            Assert.InRange(Mind(w, a).Anger, 0.074f, 0.076f);               // 0.01 × (1 − 0.5 × 15/30) × 10 s
            Assert.True(Mind(w, c).Anger < Mind(w, a).Anger && Mind(w, c).Anger > 0f, $"farther, slower: {Mind(w, c).Anger}");

            float before = Mind(w, a).Anger;
            w.GetComponentRW<SimTransform>(police).Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, System.MathF.PI);   // he turns away
            for (int i = 0; i < 20; i++) Frame(w, crowds);                   // 2 s
            Assert.InRange(Mind(w, a).Anger, before - 0.041f, before - 0.039f);   // not provoked: it fades 0.02/s

            var w2 = World();
            var crowds2 = new CrowdSystem();
            var far = new[] { Civ(w2, 0, 0, CivilianMode.Protest), Civ(w2, 3, 0, CivilianMode.Protest), Civ(w2, 6, 0, CivilianMode.Protest) };
            Policeman(w2, -31, 0);                                           // facing, beyond 30 m
            for (int i = 0; i < 100; i++) Frame(w2, crowds2);
            Assert.All(far, f => Assert.Equal(0f, Mind(w2, f).Anger));
        }

        /// <summary>⭐ <c>CE-2241</c> ② (§5m PV-2) — force on a member (a new knock-down) spikes the anger of the OTHER members of his crowd
        /// within 10 m (0.3, half at 10 m) — not his own, not a member beyond 10 m — ONCE per effect; a longer effect spikes again.</summary>
        [Fact]
        public void CE2241_ForceOnAMember_SpikesHisNeighboursAnger_OncePerEffect_NotHisOwnNorFarther()
        {
            var w = World();
            var crowds = new CrowdSystem();
            var struck = Civ(w, 0, 0, CivilianMode.Protest);
            var beside = Civ(w, 4, 0, CivilianMode.Protest);
            var back = Civ(w, 12, 0, CivilianMode.Protest);
            Frame(w, crowds);
            w.AddComponent(struck, new Fdp.Toolkit.Combat.EffectState { KnockedDownUntil = w.SimulationTime + 3 });
            Frame(w, crowds);
            Assert.InRange(Mind(w, beside).Anger, 0.239f, 0.241f);           // 0.3 × (1 − 0.5 × 4/10)
            Assert.Equal(0f, Mind(w, struck).Anger);
            Assert.Equal(0f, Mind(w, back).Anger);
            float once = Mind(w, beside).Anger;
            for (int i = 0; i < 5; i++) Frame(w, crowds);
            Assert.True(Mind(w, beside).Anger <= once, $"one blow spikes it once: {Mind(w, beside).Anger}");

            w.GetComponentRW<Fdp.Toolkit.Combat.EffectState>(struck).KnockedDownUntil = w.SimulationTime + 3;   // a second blow
            Frame(w, crowds);
            Assert.True(Mind(w, beside).Anger > once + 0.2f, $"a new blow spikes it again: {Mind(w, beside).Anger}");
        }

        /// <summary>⭐ <c>CE-2241</c> ② (§5m PV-3) — his attitude moves HIS riot threshold: the design's literals.</summary>
        [Fact]
        public void CE2241_HisAttitude_MovesHisRiotThreshold()
        {
            Assert.Equal(0.6f, CrowdRules.RiotAtFor(0.6f, 0f), 4);
            Assert.Equal(1.1f, CrowdRules.RiotAtFor(0.6f, -1f), 4);          // friendly: above 1 — he never riots
            Assert.Equal(0.1f, CrowdRules.RiotAtFor(0.6f, 1f), 4);
            Assert.Equal(1.1f, CrowdRules.RiotAtFor(0.6f, -5f), 4);          // clamped
        }
    }
}
