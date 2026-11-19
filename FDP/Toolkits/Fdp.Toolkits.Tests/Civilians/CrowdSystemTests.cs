using System.Numerics;
using Fdp.Core;
using Fdp.Toolkit.Civilians;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Creatures;
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
            // ⭐ CE-4628 — a civilian's fear, his crowd (GroupMember) and a crowd entity's header (Group) are the shared creature components
            if (!w.IsComponentTypeRegistered<Creatures.Fear>()) w.RegisterComponent<Creatures.Fear>();
            if (!w.IsComponentTypeRegistered<GroupMember>()) w.RegisterComponent<GroupMember>();
            if (!w.IsComponentTypeRegistered<Group>()) w.RegisterComponent<Group>();
            if (!w.IsComponentTypeRegistered<Mood>()) w.RegisterComponent<Mood>();   // ⭐ CE-4628 / CE-2250 — how he seems
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
            w.AddComponent(e, new CivilianState { Mode = mode });
            w.AddComponent(e, new Creatures.Fear { Value = fear });
            w.AddComponent(e, new GroupMember { Group = Entity.Null });
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
        private static ref Creatures.Fear Fr(EntityRepository w, Entity e) => ref w.GetComponentRW<Creatures.Fear>(e);
        /// <summary>The crowd entity a civilian stands in (Null = none).</summary>
        private static Entity CrowdOf(EntityRepository w, Entity e) => w.GetComponentRO<GroupMember>(e).Group;

        private static void Move(EntityRepository w, Entity e, float x, float y)
            => w.GetComponentRW<SimTransform>(e).Position = new Vector3(x, y, 0);

        /// <summary>
        /// ⭐⭐ <c>CE-2250</c> / <c>CE-4628</c> (docs/DESIGN_Civilians_And_Crowd_Control.md §5o; designs/animals/DESIGN_Animals.md §10a) — how a civilian SEEMS to
        /// every node: the crowd pass writes his mode, fear and anger in quarters on the replicated <c>Mood</c> (the face and the mood dot read it), and a
        /// frame in which no quarter moved leaves it untouched. 🔴 Red-proof: drop <c>WriteMoods</c> ⇒ no Mood ⇒ red.
        /// </summary>
        [Fact]
        public void CE4628_ACivilianSeems_HisModeFearAndAnger_OnTheMood()
        {
            var w = World();
            var man = Civ(w, 0, 0, CivilianMode.Protest, fear: 0.6f);
            Mind(w, man).Anger = 0.3f;
            var crowds = new CrowdSystem();
            Frame(w, crowds);
            Assert.True(Moods.Of(w, man, out var mood), "the crowd pass writes the civilian's Mood (CE-2250)");
            Assert.Equal(CivilianMode.Protest, mood.Mode);
            Assert.Equal(Moods.Quarter(w.GetComponentRO<Creatures.Fear>(man).Value), mood.Fear);
            Assert.Equal(Moods.Quarter(Mind(w, man).Anger), mood.Anger);
        }

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
            var id = CrowdOf(w, chain[0]);
            Assert.False(id.IsNull);

            var kf = Fdp.Toolkit.Replay.WorldKeyframes.Capture(w, Fdp.Toolkit.Replay.CaptureOptions.Recording);
            var fresh = World();
            Fdp.Toolkit.Replay.WorldKeyframes.Apply(fresh, kf);
            var crowds = new CrowdSystem();
            var other = new[] { Civ(fresh, 300, 0), Civ(fresh, 310, 0), Civ(fresh, 320, 0) };   // a second crowd forms after the restore
            Frame(fresh, crowds);

            int crowdsAlive = 0;
            foreach (var _ in fresh.Query().With<Crowd>().Build()) crowdsAlive++;
            Assert.Equal(2, crowdsAlive);
            Assert.All(chain, e => Assert.Equal(id, CrowdOf(fresh, e)));
            var second = CrowdOf(fresh, other[0]);
            Assert.False(second.IsNull);
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

            var ce = CrowdOf(w, chain[0]);
            Assert.False(ce.IsNull);
            Assert.All(chain, e => Assert.Equal(ce, CrowdOf(w, e)));
            Assert.All(pair, e => Assert.True(CrowdOf(w, e).IsNull));
            Assert.True(w.IsAlive(ce));
            var header = w.GetComponentRO<Group>(ce);   // ⭐ CE-4628 — the crowd's count and centre are its Group header
            Assert.Equal(GroupKind.Crowd, header.Kind);
            Assert.Equal(4, header.Count);
            Assert.Equal(15f, header.Centre.X, 3);
            Assert.True(header.Lead.IsNull);

            Move(w, chain[3], 33, 4);   // still within 15 m of the third
            Frame(w, crowds);
            Assert.All(chain, e => Assert.Equal(ce, CrowdOf(w, e)));

            Move(w, chain[2], 100, 100);   // the chain breaks: two and one and one
            Move(w, chain[3], 130, 100);
            Frame(w, crowds);
            Assert.All(chain, e => Assert.True(CrowdOf(w, e).IsNull));
            Assert.False(w.IsAlive(ce), "a crowd with no members is destroyed");
        }

        /// <summary>
        /// ⭐ <c>CE-4628</c> rail — when two crowds merge into one group with the SAME number of votes, the OLDER crowd (by <c>FormedAt</c>, then the entity index)
        /// keeps its entity and the other is destroyed: the int id's tie-break was "the lower id", which was the older one. 🔴 Red-proof: the tie-break
        /// reversed (the newer wins) ⇒ red.
        /// </summary>
        [Fact]
        public void CE4628_WhenTwoCrowdsMergeOnATie_TheOlderCrowdKeepsItsEntity()
        {
            var w = World();
            var crowds = new CrowdSystem();
            var olderMembers = new[] { Civ(w, 0, 0), Civ(w, 10, 0), Civ(w, 20, 0) };
            Frame(w, crowds);
            var older = CrowdOf(w, olderMembers[0]);
            Assert.False(older.IsNull);
            for (int i = 0; i < 5; i++) Frame(w, crowds);   // time passes: the next crowd forms LATER
            var newerMembers = new[] { Civ(w, 200, 0), Civ(w, 210, 0), Civ(w, 220, 0) };
            Frame(w, crowds);
            var newer = CrowdOf(w, newerMembers[0]);
            Assert.False(newer.IsNull);
            Assert.NotEqual(older, newer);
            Assert.True(w.GetComponentRO<Crowd>(older).FormedAt < w.GetComponentRO<Crowd>(newer).FormedAt);

            for (int i = 0; i < 3; i++) Move(w, newerMembers[i], 30 + i * 10, 0);   // the second crowd walks up to the first: one chain of six, three votes each
            Frame(w, crowds);
            Assert.All(olderMembers, e => Assert.Equal(older, CrowdOf(w, e)));
            Assert.All(newerMembers, e => Assert.Equal(older, CrowdOf(w, e)));
            Assert.Equal(6, w.GetComponentRO<Group>(older).Count);
            Assert.False(w.IsAlive(newer), "the newer crowd's entity is gone");
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
            var crowdEntity = CrowdOf(w, calm);
            var crowd = w.GetComponentRO<Crowd>(crowdEntity);
            Assert.Equal(3, w.GetComponentRO<Group>(crowdEntity).Count);   // the calm, the onlooker and the frightened one stand linked; the far one does not
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
            Fr(w, src).FrightFrom = new Vector3(-50, 0, 0);
            Fr(w, src).HasFrightFrom = 1;
            var near = Civ(w, 8, 0);
            Civ(w, 0, 10);   // a third, so the two are a crowd
            var calmSource = Civ(w, 300, 0, CivilianMode.Routine, 0.9f);   // frightened inside, but showing nothing
            var calmNear = Civ(w, 305, 0);
            Civ(w, 310, 0);
            for (int i = 0; i < 40; i++) Frame(w, crowds);   // 4 s

            Assert.True(Fr(w, near).Value > 0.6f, $"he caught it — past panic: {Fr(w, near).Value}");
            Assert.True(Fr(w, near).Value <= 0.9f + 1e-4f, $"never above 0.9 of his source (design §5f — a literal, so a changed share reddens it): {Fr(w, near).Value}");
            Assert.Equal(0.0, Fr(w, near).HeardAt);
            Assert.Equal(1, Fr(w, near).HasFrightFrom);
            Assert.Equal(new Vector3(-50, 0, 0), (Vector3)Fr(w, near).FrightFrom);   // he runs from where the others run from
            Assert.Equal(0f, Fr(w, calmNear).Value);   // a man who shows nothing spreads nothing
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
            Assert.True(Fr(w, a).Value < 0.4f && Fr(w, b).Value < 0.4f, $"both fade: {Fr(w, a).Value}, {Fr(w, b).Value}");
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
            w.AddComponent(struck, new Fdp.Toolkit.Combat.EffectState { KnockedDownUntil = (SimInstant)(w.SimulationTime + 3) });
            Frame(w, crowds);
            Assert.InRange(Mind(w, beside).Anger, 0.239f, 0.241f);           // 0.3 × (1 − 0.5 × 4/10)
            Assert.Equal(0f, Mind(w, struck).Anger);
            Assert.Equal(0f, Mind(w, back).Anger);
            float once = Mind(w, beside).Anger;
            for (int i = 0; i < 5; i++) Frame(w, crowds);
            Assert.True(Mind(w, beside).Anger <= once, $"one blow spikes it once: {Mind(w, beside).Anger}");

            w.GetComponentRW<Fdp.Toolkit.Combat.EffectState>(struck).KnockedDownUntil = (SimInstant)(w.SimulationTime + 3);   // a second blow
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
