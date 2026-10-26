using System;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Hrot.CGF.Configuration;
using Hrot.Map.Definitions.Behavior;
using Fdp.Toolkit.Perception.Events;
using Fdp.Toolkit.Spatial.Eqs;
using Hrot.AI.Behaviors.Brains;
using Hrot.MuscleCharacter.Animation.Components;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-2270</c> TS-B (R-2034 TS-4) — a combatant's stress: each stimulus, the nerve scaling, the calming by time, by cover and by
    /// his leader; and who has none (TS-7). 📄 docs/DESIGN_Temperament_And_Stress.md §4.
    /// </summary>
    public sealed class StressSystemTests : IDisposable
    {
        private readonly EntityRepository _w = new();

        public StressSystemTests()
        {
            SimHostComponentRegistry.RegisterAll(_w);
            CognitiveComponentRegistry.RegisterAll(_w);
        }

        public void Dispose() => _w.Dispose();

        /// <summary>A soldier standing at <paramref name="x"/>, healthy, with a brain.</summary>
        private Entity Man(float x = 0f, float? nerve = null, float? obedience = null, float? resolve = null)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new BehaviorState());
            _w.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
            _w.AddComponent(e, new StanceIntent());
            _w.AddComponent(e, new SimTransform { Position = new Vector3(x, 0f, 0f), Rotation = Quaternion.Identity });
            _w.AddComponent(e, new EntityInfo { ForceId = ForceId.Friend });
            var t = new CombatTemperament();
            if (nerve is { } n) { t.Nerve = n; t.Set |= CombatTemperament.NerveSet; }
            if (obedience is { } o) { t.Obedience = o; t.Set |= CombatTemperament.ObedienceSet; }
            if (resolve is { } r) { t.Resolve = r; t.Set |= CombatTemperament.ResolveSet; }
            if (t.Set != 0) _w.AddComponent(e, t);
            return e;
        }

        private float Level(Entity e) => StressModel.LevelOf(_w, e);

        private void SetLevel(Entity e, float level, double at)
        {
            StressModel.Step(_w, e, at);   // attaches it
            _w.GetComponentRW<Stress>(e).Level = level;
        }

        /// <summary>TS-4 — a near miss 0.08, a hit 0.25, each × (1 − 0.5 × effective nerve): an ordinary man (nerve 0.5, obedience 0.8 ⇒
        /// effective 0.70, R-2076) takes 0.65 of it.</summary>
        [Fact]
        public void CE2270_ANearMissAndAHit_RaiseHim_ByTheirAmount_TimesHisNerve()
        {
            var e = Man();
            StressModel.Add(_w, e, StressModel.NearMiss, 1.0);
            Assert.Equal(0.052f, Level(e), 4);
            StressModel.Add(_w, e, StressModel.Hit, 1.0);
            Assert.Equal(0.052f + 0.1625f, Level(e), 4);
            Assert.Equal(1.0, _w.GetComponentRO<Stress>(e).LastStimulusAt);
        }

        /// <summary>TS-4 × TS-2 — his nerve, as obedience tempers it, sets how much reaches him: iron nerve with no discipline to temper it
        /// halves a stimulus, none takes it whole; the same coward under strict discipline takes nearly the trained man's share (R-2076).</summary>
        [Theory]
        [InlineData(1.0f, 0.0f, 0.5f)]
        [InlineData(0.0f, 0.0f, 1.0f)]
        [InlineData(0.0f, 0.9f, 0.6625f)]  // effective nerve 0.675 (0 + 0.9 × 0.75)
        public void CE2270_HisNerve_SetsHowMuchReachesHim(float nerve, float obedience, float reach)
        {
            var e = Man(nerve: nerve, obedience: obedience);
            Assert.Equal(reach, StressModel.Reach(_w, e), 4);
            StressModel.Add(_w, e, 0.2f, 0.0);
            Assert.Equal(0.2f * reach, Level(e), 4);
        }

        /// <summary>TS-4 — the system turns last frame's edges into stimuli: a near miss on the man, and a squad member down raises the men
        /// within 15 m of him (not the one at 20 m, not the fallen man).</summary>
        [Fact]
        public void CE2270_TheSystem_ReadsTheEdges_NearMiss_AndAFriendDownWithin15m()
        {
            var a = Man(0f);
            var near = Man(10f);
            var far = Man(30f);
            var down = Man(5f);
            var commander = _w.CreateEntity();
            var roster = new UnitRoster();
            roster.Count = 4;
            roster.SubordinateEntities[0] = a;
            roster.SubordinateEntities[1] = near;
            roster.SubordinateEntities[2] = far;
            roster.SubordinateEntities[3] = down;
            _w.AddComponent(commander, roster);

            _w.Bus.Publish(new SensorChangedEvent { Unit = a, What = SensorChange.NearMiss });
            _w.Bus.Publish(new SensorChangedEvent { Unit = commander, What = SensorChange.SquadMemberDown, Target = down });
            _w.Bus.SwapBuffers();
            _w.SetSingleton(new GlobalTime { TotalTime = 2.0, DeltaTime = 0.016f, TimeScale = 1f });
            new StressSystem().Execute(_w, 0.016f);

            Assert.Equal(0.052f + 0.13f, Level(a), 3);  // his own near miss + the friend 5 m from him (each × 0.65)
            Assert.Equal(0.13f, Level(near), 3);         // 5 m from the fallen
            Assert.Equal(0f, Level(far), 3);             // 25 m: too far to feel it
            Assert.Equal(0f, Level(down), 3);            // not himself
        }

        /// <summary>TS-4 — time calms him 0.03 / s; ⭐ cover doubles it and his leader within 20 m doubles it again.</summary>
        [Fact]
        public void CE2270_TimeCalmsHim_CoverDoublesIt_HisLeaderNearDoublesItAgain()
        {
            var e = Man(0f);
            Man(5f);                                   // a friend near: not alone
            SetLevel(e, 0.8f, 0.0);
            StressModel.Step(_w, e, 1.0);
            Assert.Equal(0.8f - 0.03f, Level(e), 4);

            _w.SetSingletonManaged<ICoverProvider>(new CoverAtFeet());
            StressModel.Step(_w, e, 2.0);
            Assert.Equal(0.77f - 0.06f, Level(e), 4);

            var commander = _w.CreateEntity();
            var leader = Man(15f);
            _w.AddComponent(commander, new Fdp.Toolkit.Squad.SquadLeadership { Leader = leader });
            _w.AddComponent(e, new UnitSubordinate { Commander = commander });
            StressModel.Step(_w, e, 3.0);
            Assert.Equal(0.71f - 0.12f, Level(e), 4);

            _w.GetComponentRW<SimTransform>(leader).Position = new Vector3(25f, 0f, 0f);   // he moves off
            StressModel.Step(_w, e, 4.0);
            Assert.Equal(0.59f - 0.06f, Level(e), 4);
        }

        /// <summary>TS-4 — alone (no living friend within 20 m) he takes 0.01 / s × his nerve against the calm; outnumbered (the enemy's share
        /// over 0.6) 0.02 / s more. A paused clock moves nothing.</summary>
        [Fact]
        public void CE2270_AloneAndOutnumbered_RaiseHimOverTime()
        {
            var e = Man(0f);
            SetLevel(e, 0.5f, 0.0);
            StressModel.Step(_w, e, 1.0);
            Assert.Equal(0.5f + 0.01f * 0.65f - 0.03f, Level(e), 4);    // alone

            for (int i = 0; i < 3; i++) Contact(e, armed: true);
            Assert.True(StressModel.EnemyShare(_w, e) > StressModel.OutnumberedShare, $"share {StressModel.EnemyShare(_w, e)}");
            float before = Level(e);
            StressModel.Step(_w, e, 2.0);
            Assert.Equal(before + 0.03f * 0.65f - 0.03f, Level(e), 4);  // alone and outnumbered

            before = Level(e);
            StressModel.Step(_w, e, 2.0);
            Assert.Equal(before, Level(e));                              // no time passed
        }

        /// <summary>TS-7 — a civilian, a body with no stance (a vehicle), and a man who is down have no stress.</summary>
        [Fact]
        public void CE2270_ACivilian_AVehicle_AndAManDown_HaveNone()
        {
            var civ = Man(0f);
            _w.AddComponent(civ, new Fdp.Toolkit.Civilians.CivilianState());
            var vehicle = Man(10f);
            _w.RemoveComponent<StanceIntent>(vehicle);
            var down = Man(20f);
            _w.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(down).Current = 0f;
            foreach (var e in new[] { civ, vehicle, down })
            {
                StressModel.Add(_w, e, StressModel.Hit, 1.0);
                StressModel.Step(_w, e, 2.0);
                Assert.False(_w.HasComponent<Stress>(e));
            }
        }

        // ── ⭐ CE-2272 (TS-6) — the break ───────────────────────────────────────────────────────────────────────────────────────

        /// <summary>A brain world: the ingress, the brain tick and the stress pass, on <see cref="_w"/>.</summary>
        private sealed class Brains
        {
            private readonly EntityRepository _w;
            public readonly BehaviorRegistry Registry = new();
            private readonly BehaviorIngressSystem _ingress;
            private readonly BrainTickSystem _brain;
            private readonly StressSystem _stress = new();
            public double Time;
            private long _frame;

            public Brains(EntityRepository w)
            {
                _w = w;
                BlueprintTierTable.RegisterAll(w);
                if (!w.IsComponentTypeRegistered<UnitSubordinate>()) w.RegisterComponent<UnitSubordinate>();
                if (!w.Bus.IsRegistered<SensorChangedEvent>()) w.Bus.Register<SensorChangedEvent>();
                var geo = new WGS84Transform();
                geo.SetOrigin(0.0, 0.0, 0.0);
                w.SetSingletonManaged<IGeographicTransform>(geo);
                CgfBehaviorSetup.LoadFromAiAssembly(Registry);
                _ingress = new BehaviorIngressSystem(Registry);
                _brain = new BrainTickSystem(Registry);
                Tick();
            }

            public void Tick()
            {
                Time += 0.016;
                _w.SetSingleton(new GlobalTime { TotalTime = Time, DeltaTime = 0.016f, TimeScale = 1f, FrameNumber = ++_frame });
                _w.SetSimulationTime((float)Time);
                _w.Bus.SwapBuffers();
                _ingress.Execute(_w, 0.016f);
                _stress.Execute(_w, 0.016f);
                _brain.Execute(_w, 0.016f);
            }

            public void Run(double seconds) { for (double t = 0; t < seconds; t += 0.016) Tick(); }

            public string? TaskOf(Entity m) => Registry.TryGetName(_w.GetComponent<BehaviorState>(m).ActiveBehaviorHash, out var n) ? n : null;
        }

        /// <summary>A soldier on an ORDERED task, his ROE StayOnTask — the order he would never leave for a reaction.</summary>
        private Entity Ordered(Brains b, float x = 0f, float? nerve = null, float? obedience = null, float? resolve = null)
        {
            var m = Man(x, nerve, obedience);
            if (resolve is { } r)
            {
                var t = _w.HasComponent<CombatTemperament>(m) ? _w.GetComponentRO<CombatTemperament>(m) : new CombatTemperament();
                t.Resolve = r;
                t.Set |= CombatTemperament.ResolveSet;
                if (_w.HasComponent<CombatTemperament>(m)) _w.SetComponent(m, t); else _w.AddComponent(m, t);
            }
            _w.AddComponent(m, new LocomotionChannel());
            _w.AddComponent(m, new WeaponChannel());
            _w.AddComponent(m, new Roe { Reactions = RoeReactions.StayOnTask });
            _w.SetAuthority<BehaviorState>(m, true);
            _w.Bus.PublishManaged(new AssignBehaviorEvent { Entity = m, BehaviorName = BehaviorNames.Idle, JsonParams = "{}", Origin = BehaviorOrigin.Superior });
            b.Run(0.1);
            Assert.Equal(BehaviorNames.Idle, b.TaskOf(m));
            return m;
        }

        /// <summary>
        /// ⭐⭐ TS-6 — at his nerve he BREAKS: the edge on the bus, the <c>Break</c> reaction pausing his ordered task — ⭐ even under ROE
        /// StayOnTask (men break whatever the order) — down where he is (no retreat known), no aimed fire; under 0.6 × nerve for 10 s he
        /// recovers and his task resumes. 🔒 User: <i>"brave, cautious or cowardly"</i> (R-2034).
        /// </summary>
        [Fact]
        public void CE2272_AtHisNerve_HeBreaks_EvenUnderStayOnTask_GoesToGround_AndRecovers()
        {
            var b = new Brains(_w);
            var m = Ordered(b);
            _w.GetComponentRW<Stress>(m).Level = 0.75f;   // nerve 0.5, obedience 0.8 ⇒ he breaks at 0.70 (R-2076)
            b.Tick();
            Assert.True(StressModel.IsBroken(_w, m));
            b.Tick();
            Assert.Equal(BehaviorNames.Break, b.TaskOf(m));
            var task = _w.GetComponent<BehaviorState>(m);
            Assert.Equal(BehaviorOrigin.Reaction, task.Origin);
            Assert.Equal(ReactionUrgency.Break, task.Urgency);
            Assert.Equal(BehaviorNames.Idle, BehaviorIngressSystem.PausedTaskOf(_w, m)!.BehaviorName);
            b.Run(0.2);
            Assert.Equal(Fdp.Toolkit.Tkb.Domain.StanceId.Prone, _w.GetComponentRO<StanceIntent>(m).TargetStance);

            // the fire he was asked for is ended
            _w.GetComponentRW<WeaponChannel>(m).ActiveAction = Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire;
            b.Tick();
            Assert.NotEqual(Fdp.Toolkit.Combat.CombatConstants.ActionIdAimAndFire, _w.GetComponentRO<WeaponChannel>(m).ActiveAction);

            _w.GetComponentRW<Stress>(m).Level = 0.2f;     // under 0.6 × 0.70
            b.Run(StressModel.RecoverSeconds - 1.0);
            Assert.True(StressModel.IsBroken(_w, m));      // not yet: 10 s calm
            b.Run(1.5);
            Assert.False(StressModel.IsBroken(_w, m));
            b.Run(0.1);
            Assert.Equal(BehaviorNames.Idle, b.TaskOf(m));  // his task resumed
        }

        /// <summary>TS-6 — the edges are published once each, on the man.</summary>
        [Fact]
        public void CE2272_TheBreakAndTheRecovery_AreEdges_OnTheMan()
        {
            var b = new Brains(_w);
            var m = Ordered(b);
            int breaks = 0, recoveries = 0;
            _w.GetComponentRW<Stress>(m).Level = 0.9f;
            for (int i = 0; i < 30; i++)
            {
                b.Tick();
                foreach (var e in _w.ReadEvents<SensorChangedEvent>())
                {
                    if (e.Unit != m) continue;
                    if (e.What == SensorChange.StressBreak) breaks++;
                    if (e.What == SensorChange.StressRecovered) recoveries++;
                }
            }
            _w.GetComponentRW<Stress>(m).Level = 0f;
            for (int i = 0; i < 700; i++)
            {
                b.Tick();
                foreach (var e in _w.ReadEvents<SensorChangedEvent>())
                {
                    if (e.Unit != m) continue;
                    if (e.What == SensorChange.StressBreak) breaks++;
                    if (e.What == SensorChange.StressRecovered) recoveries++;
                }
            }
            Assert.Equal(1, breaks);
            Assert.Equal(1, recoveries);
        }

        /// <summary>TS-2 × TS-6 — obedience tempers the nerve he breaks at: a coward (nerve 0.1) with no discipline breaks at 0.1, the same coward
        /// under strict discipline (0.9) at 0.685 — near the trained 0.75 (R-2076) — and never under <see cref="StressModel.BreakFloor"/>.</summary>
        [Theory]
        [InlineData(0.1f, 0.0f, 0.1f)]
        [InlineData(0.1f, 0.9f, 0.685f)]
        [InlineData(0.0f, 0.0f, 0.05f)]
        public void CE2272_HeBreaksAtHisEffectiveNerve_NeverUnderTheFloor(float nerve, float obedience, float breakAt)
        {
            var m = Man(0f, nerve, obedience);
            Assert.Equal(breakAt, StressModel.BreakLevel(_w, m), 4);
            StressModel.Step(_w, m, 0.0);
            _w.GetComponentRW<Stress>(m).Level = breakAt - 0.01f;
            StressModel.Judge(_w, m, 0.0);
            Assert.False(StressModel.IsBroken(_w, m));
            _w.GetComponentRW<Stress>(m).Level = breakAt;
            StressModel.Judge(_w, m, 0.0);
            Assert.True(StressModel.IsBroken(_w, m));
        }

        /// <summary>TS-6 — a broken man with a low resolve whom an enemy rifle covers SURRENDERS (for good); one with resolve does not.</summary>
        [Fact]
        public void CE2272_LowResolve_AndCovered_HeSurrenders_ElseHeBreaks()
        {
            var b = new Brains(_w);
            var waverer = Ordered(b, 0f, obedience: 0f, resolve: 0.1f);
            var fighter = Ordered(b, 40f, obedience: 0f, resolve: 0.9f);
            var alone = Ordered(b, 80f, obedience: 0f, resolve: 0.1f);   // a waverer no rifle covers
            _w.GetComponentRW<EntityInfo>(alone).ForceId = ForceId.Hostile;
            foreach (var man in new[] { waverer, fighter })
            {
                _w.GetComponentRW<EntityInfo>(man).ForceId = ForceId.Hostile;
                Rifle(new Vector3(_w.GetComponentRO<SimTransform>(man).Position.X, 20f, 0f), man);
            }
            Assert.True(StressModel.Surrenders(_w, waverer));
            Assert.False(StressModel.Surrenders(_w, fighter));
            Assert.False(StressModel.Surrenders(_w, alone));
            foreach (var man in new[] { waverer, fighter, alone }) _w.GetComponentRW<Stress>(man).Level = 0.9f;
            b.Run(0.1);
            Assert.Equal(BehaviorNames.Surrender, b.TaskOf(waverer));
            Assert.Equal(BehaviorNames.Break, b.TaskOf(fighter));
            Assert.Equal(BehaviorNames.Break, b.TaskOf(alone));
        }

        /// <summary>TS-6 — with a threat known and a cover point farther from it within 30 m, he RUNS there (upright, flat out), not down where
        /// he is; a cover point nearer the threat is no retreat.</summary>
        [Fact]
        public void CE2272_AWayOut_IsACoverPointFartherFromTheThreat()
        {
            var m = Man(0f);
            Contact(m, armed: true, x: 50f);   // remembered at (50, 0)
            _w.SetSingletonManaged<ICoverProvider>(new CoverAt(new Vector2(-15f, 0f), new Vector2(1f, 0f)));
            Assert.True(StressModel.Retreat(_w, m, out var to));
            Assert.Equal(-15f, to.X, 2);

            _w.SetSingletonManaged<ICoverProvider>(new CoverAt(new Vector2(10f, 0f), new Vector2(1f, 0f)));
            Assert.True(UnderFire.CoverNear(_w, m, Vector3.Zero, StressModel.RetreatMetres, StressModel.RetreatGainMetres, out _, out _));   // it IS cover from him …
            Assert.False(StressModel.Retreat(_w, m, out _));   // … but toward him: no way out
        }

        // ── ⭐ CE-2376 (AQ104 C + F) — the enemy heard behind the wall, the near miss in cover, panic flees ──────────────────────

        /// <summary>A memory slot for someone HEARD (acoustic) just now at (<paramref name="x"/>, 0), anonymous — an unknown sound.</summary>
        private unsafe void Heard(Entity self, float x, bool acoustic = true)
        {
            if (!_w.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(self)) _w.AddComponent(self, new Fdp.Toolkit.Perception.Components.TargetMemory());
            ref var mem = ref _w.GetComponentRW<Fdp.Toolkit.Perception.Components.TargetMemory>(self);
            int i = mem.Count++;
            mem.EntityIds[i] = -1 - i;
            mem.Anonymous[i] = 1;
            mem.Freshness[i] = Fdp.Toolkit.Perception.PerceptionConstants.FreshnessSaturation;
            mem.Modalities[i] = (byte)(acoustic ? Fdp.Toolkit.Perception.Components.SensorModality.Acoustic : Fdp.Toolkit.Perception.Components.SensorModality.Visual);
            mem.PositionsX[i] = x;
            mem.LastSeenTick[i] = Fdp.Toolkit.Perception.PerceptionClock.Now(_w);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2376</c> (AQ104 F, Temperament §7.6 DF-1) — someone HEARD within 10 m whom he cannot see stops his calming and raises him
        /// 0.03 / s × (1 − effective nerve): "walking around behind a wall". The same sound 20 m off, or a man only SEEN (no sound), lets him calm
        /// as before. 🔴 Red-proof: the term dropped ⇒ he calms.
        /// </summary>
        [Fact]
        public void CE2376_SomeoneHeardBehindTheWall_RaisesHim_FarOrSeenOnlyDoesNot()
        {
            _w.SetSingleton(new GlobalTime { TotalTime = 0.0, DeltaTime = 0.016f, TimeScale = 1f, FrameNumber = 600 });
            var e = Man(0f);
            Man(5f);   // a friend near: not alone
            SetLevel(e, 0.2f, 0.0);
            Heard(e, 8f);
            StressModel.Step(_w, e, 1.0);
            Assert.True(Level(e) > 0.2f, $"heard behind the wall: rises ({Level(e)})");   // ⚠ the sound also counts toward 'outnumbered'

            var far = Man(40f); Man(45f);
            SetLevel(far, 0.2f, 0.0);
            Heard(far, 60f);   // 20 m from him
            StressModel.Step(_w, far, 1.0);
            Assert.True(Level(far) < 0.2f, $"20 m off: he calms ({Level(far)})");

            var seen = Man(80f); Man(85f);
            SetLevel(seen, 0.2f, 0.0);
            Heard(seen, 88f, acoustic: false);
            StressModel.Step(_w, seen, 1.0);
            Assert.True(Level(seen) < 0.2f, $"seen only: he calms ({Level(seen)})");
        }

        /// <summary>
        /// ⭐ <c>CE-2376</c> (DF-2, the measured re-tune) — a near miss on a man IN COVER counts half (0.04 before his nerve); in the open the
        /// whole 0.08. 📐 Live <c>ia-area-defend</c>: a regular behind a wall under two rifles reached 1.0 in ~10 s and never recovered.
        /// 🔴 Red-proof: the cover share dropped ⇒ 0.052 in cover too.
        /// </summary>
        [Fact]
        public void CE2376_ANearMissInCover_CountsHalf()
        {
            var e = Man(0f);
            _w.SetSingleton(new GlobalTime { TotalTime = 2.0, DeltaTime = 0.016f, TimeScale = 1f });
            _w.Bus.Publish(new SensorChangedEvent { Unit = e, What = SensorChange.NearMiss });
            _w.Bus.SwapBuffers();
            new StressSystem().Execute(_w, 0.016f);
            float open = Level(e);
            Assert.True(open > 0.05f, $"in the open {open}");

            var c = Man(100f);
            _w.SetSingletonManaged<ICoverProvider>(new CoverAtFeet());
            Assert.Equal(0.04f, StressModel.NearMissIn(_w, c), 4);   // half of TS-4's 0.08 — the number itself, not the constant
            Assert.Equal(0.026f, StressModel.NearMissIn(_w, c) * StressModel.Reach(_w, c), 4);   // × his reach 0.65
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2376</c> (AQ104 C, DS-3) — panic FLEES: a man of low resolve (0.2, no discipline to temper it) who breaks runs to cover up to
        /// 60 m off that gains ≥ 15 m on the threat — out of the house — not to the nearest; an ordinary man's resolve does not flee; a point
        /// gaining only 10 m is no flight. 🔴 Red-proof: Flees always false ⇒ the break takes the near retreat.
        /// </summary>
        [Fact]
        public void CE2376_ALowResolveMan_FleesFar_AnOrdinaryManDoesNot()
        {
            var coward = Man(0f, obedience: 0f, resolve: 0.2f);
            Contact(coward, armed: true, x: 50f);
            Assert.True(StressModel.Flees(_w, coward));
            Assert.False(StressModel.Flees(_w, Man(5f)));   // effective resolve ≥ 0.4

            _w.SetSingletonManaged<ICoverProvider>(new CoverAt(new Vector2(-30f, 0f), new Vector2(1f, 0f)));
            Assert.True(StressModel.Flee(_w, coward, out var to));
            Assert.Equal(-30f, to.X, 2);

            _w.SetSingletonManaged<ICoverProvider>(new CoverAt(new Vector2(-10f, 0f), new Vector2(1f, 0f)));
            Assert.False(StressModel.Flee(_w, coward, out _));   // only 10 m farther: no flight
            Assert.True(StressModel.Retreat(_w, coward, out _));  // … the near retreat still is one
        }

        // ── ⭐ CE-2365 (AQ103 C/H) — the nervous man: the fidget and the head ──────────────────────────────────────────────────

        /// <summary>A nervous man: his level set so <see cref="StressModel.Nervousness"/> reads <paramref name="nervous"/> (nerve 0.5).</summary>
        private Entity Nervous(float nervous, float x = 0f)
        {
            var m = Man(x);
            StressModel.Step(_w, m, 0.0);
            float b = StressModel.BreakLevel(_w, m);
            _w.GetComponentRW<Stress>(m).Level = b * (StressModel.NervousFrom + nervous * (1f - StressModel.NervousFrom));
            return m;
        }

        /// <summary>TS-6's band — nervous from 0.4 × his break level (0) to it (1); none without stress.</summary>
        [Fact]
        public void CE2365_Nervousness_Is0Below04OfHisBreakLevel_1AtIt()
        {
            var calm = Man(0f);
            Assert.Equal(0f, StressModel.Nervousness(_w, calm));
            StressModel.Step(_w, calm, 0.0);
            _w.GetComponentRW<Stress>(calm).Level = 0.19f;               // under 0.4 × 0.5
            Assert.Equal(0f, StressModel.Nervousness(_w, calm));
            Assert.Equal(0.5f, StressModel.Nervousness(_w, Nervous(0.5f, 5f)), 3);
            Assert.Equal(1f, StressModel.Nervousness(_w, Nervous(1f, 10f)), 3);
        }

        /// <summary>⭐ CE-2365 — a nervous man shifts SOONER (waits × (1 − 0.75 × nervousness)) and FURTHER (a standing step at least 1 m).</summary>
        [Fact]
        public void CE2365_ANervousMan_ShiftsSoonerAndFurther()
        {
            var calm = Man(0f);
            var jumpy = Nervous(1f, 30f);
            foreach (var m in new[] { calm, jumpy }) { _w.AddComponent(m, new Fdp.Toolkit.Navigation.NavigationIntent()); }
            double c = Restlessness.Interval(_w, calm, 0, 100.0), j = Restlessness.Interval(_w, jumpy, 0, 100.0);
            Assert.InRange(c, Restlessness.StandMinSeconds, Restlessness.StandMaxSeconds);
            Assert.InRange(j, Restlessness.StandMinSeconds * 0.25 - 1e-6, Restlessness.StandMaxSeconds * 0.25 + 1e-6);
            for (int i = 0; i < 20; i++)
            {
                var to = Restlessness.Shift(jumpy, Vector3.Zero, Vector3.Zero, prone: false, 0, i * 7.0, nervous: 1f);
                Assert.InRange(new Vector2(to.X, to.Y).Length(), Restlessness.NervousStepMinMetres - 1e-4f, Restlessness.StepMaxMetres + 1e-4f);
            }
        }

        /// <summary>⭐ AQ103 C (step 3) — IN COVER his step stays behind it: to the cover point nearest where it would go; with no other
        /// cover near, no step at all.</summary>
        [Fact]
        public void CE2365_InCover_TheStepStaysBehindIt_OrIsNoStep()
        {
            var m = Man(0f);
            _w.AddComponent(m, new Fdp.Toolkit.Navigation.NavigationIntent());
            _w.SetSingletonManaged<ICoverProvider>(new CoverAt(new Vector2(0f, 0f), new Vector2(1f, 0f)));   // only where he stands
            Restlessness.Step(_w, m, 0.0);                         // settles
            Restlessness.Step(_w, m, 30.0);                        // due
            Assert.Equal(0, _w.GetComponentRO<Restless>(m).Moving);   // nowhere else behind cover: no step

            var n = Man(20f);
            _w.AddComponent(n, new Fdp.Toolkit.Navigation.NavigationIntent());
            _w.SetSingletonManaged<ICoverProvider>(new TwoCover(new Vector2(20f, 0f), new Vector2(21f, 0f)));
            Restlessness.Step(_w, n, 0.0);
            Restlessness.Step(_w, n, 30.0);
            Assert.Equal(1, _w.GetComponentRO<Restless>(n).Moving);
            var to = _w.GetComponentRO<Fdp.Toolkit.Navigation.NavigationIntent>(n).FinalDestination;
            Assert.Equal(21f, to.X, 2);                           // the other cover point, never the open
            Assert.Equal(0f, to.Y, 2);
        }

        /// <summary>⭐ CE-2365 — a calm man with his weapon up is held still; a NERVOUS one fidgets all the same.</summary>
        [Fact]
        public void CE2365_WeaponUp_HoldsACalmMan_NotANervousOne()
        {
            var calm = Man(0f);
            var jumpy = Nervous(1f, 30f);
            foreach (var m in new[] { calm, jumpy })
            {
                _w.AddComponent(m, new Fdp.Toolkit.Navigation.NavigationIntent());
                _w.AddComponent(m, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed });
                Restlessness.Step(_w, m, 0.0);
                Restlessness.Step(_w, m, 30.0);
            }
            Assert.Equal(0, _w.GetComponentRO<Restless>(calm).Moving);
            Assert.Equal(1, _w.GetComponentRO<Restless>(jumpy).Moving);
        }

        /// <summary>⭐ AQ103 H — a nervous man's eyes: each fixation held 0.4 s at full nervousness (2 s calm), the five points in a seeded,
        /// irregular order (not the calm sweep), and the head snapping (0.06 s blend, 0.2 s calm).</summary>
        [Fact]
        public void AQ103H_ANervousMansScan_QuickensJerks_AndSnaps()
        {
            Assert.Equal(2f, LookAt.ScanStepSeconds(2f, 0f), 4);
            Assert.Equal(LookAt.NervousStepSeconds, LookAt.ScanStepSeconds(2f, 1f), 4);
            Assert.Equal(1.2f, LookAt.ScanStepSeconds(2f, 0.5f), 4);
            Assert.Equal(0.2f, LookAt.BlendSeconds(0f), 4);
            Assert.Equal(LookAt.NervousBlendSeconds, LookAt.BlendSeconds(1f), 4);

            var self = Man(0f);
            int changes = 0, sweepSteps = 0, repeats = 0;
            float prev = LookAt.NervousScanBearing(self, 90f, 30f, 0.4f, 0.2);
            float calmPrev = LookAt.ScanBearing(90f, 30f, 0.4f, 0.2);
            for (int k = 1; k < 40; k++)
            {
                double t = k * 0.4 + 0.2;
                float b = LookAt.NervousScanBearing(self, 90f, 30f, 0.4f, t);
                Assert.InRange(b, 60f - 1e-3f, 120f + 1e-3f);
                if (MathF.Abs(b - prev) > 1e-3f) changes++; else repeats++;
                float calm = LookAt.ScanBearing(90f, 30f, 0.4f, t);
                if (MathF.Abs(MathF.Abs(b - prev) - 15f) < 1e-3f) sweepSteps++;   // a calm sweep moves one point (15°) a step
                prev = b;
                calmPrev = calm;
            }
            Assert.True(changes >= 30, $"he jerks from point to point: {changes} changes, {repeats} repeats of 39");
            Assert.True(sweepSteps < 30, $"not the calm sweep: {sweepSteps} one-point steps of 39");
        }

        /// <summary>Two cover points, both facing +X.</summary>
        private sealed class TwoCover : ICoverProvider
        {
            private readonly Vector2 _a, _b;
            public TwoCover(Vector2 a, Vector2 b) { _a = a; _b = b; }
            public int GetCoverPointsInRadius(Vector2 center, float radius, Span<CoverPoint> results)
            {
                int n = 0;
                foreach (var p in new[] { _a, _b })
                    if (Vector2.Distance(center, p) <= radius) results[n++] = new CoverPoint { PositionX = p.X, PositionY = p.Y, DirectionX = 1f };
                return n;
            }
        }

        /// <summary>A forces rifleman at <paramref name="at"/> with his rifle on <paramref name="target"/> (<see cref="Fdp.Toolkit.Combat.Covering"/>).</summary>
        private Entity Rifle(Vector3 at, Entity target)
        {
            var e = _w.CreateEntity();
            _w.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            _w.AddComponent(e, new EntityInfo { ForceId = ForceId.Friend });
            _w.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
            _w.AddComponent(e, new Fdp.Toolkit.Combat.Components.WeaponPosture { Kind = Fdp.Toolkit.Combat.Components.WeaponPostureKind.Aimed, Target = target });
            _w.AddComponent(e, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 30, MaxAmmo = 30 });
            return e;
        }

        /// <summary>One cover point at <paramref name="at"/> whose cover faces <paramref name="facing"/>.</summary>
        private sealed class CoverAt : ICoverProvider
        {
            private readonly Vector2 _at, _facing;
            public CoverAt(Vector2 at, Vector2 facing) { _at = at; _facing = facing; }
            public int GetCoverPointsInRadius(Vector2 center, float radius, Span<CoverPoint> results)
            {
                if (Vector2.Distance(center, _at) > radius) return 0;
                results[0] = new CoverPoint { PositionX = _at.X, PositionY = _at.Y, DirectionX = _facing.X, DirectionY = _facing.Y };
                return 1;
            }
        }

        private unsafe void Contact(Entity self, bool armed, float x = 0f)
        {
            var c = _w.CreateEntity();
            _w.AddComponent(c, new SimTransform { Position = new Vector3(50f, 0f, 0f), Rotation = Quaternion.Identity });
            if (armed) _w.AddComponent(c, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 30, MaxAmmo = 30 });
            if (!_w.HasComponent<Fdp.Toolkit.Perception.Components.TargetMemory>(self)) _w.AddComponent(self, new Fdp.Toolkit.Perception.Components.TargetMemory());
            ref var mem = ref _w.GetComponentRW<Fdp.Toolkit.Perception.Components.TargetMemory>(self);
            mem.EntityIds[mem.Count] = (long)c.PackedValue;
            mem.Freshness[mem.Count] = Fdp.Toolkit.Perception.PerceptionConstants.FreshnessSaturation;
            mem.Modalities[mem.Count] = (byte)Fdp.Toolkit.Perception.Components.SensorModality.Visual;
            mem.PositionsX[mem.Count] = x;
            mem.Count++;
        }

        /// <summary>A cover point under any asker's feet, facing +X.</summary>
        private sealed class CoverAtFeet : ICoverProvider
        {
            public int GetCoverPointsInRadius(Vector2 center, float radius, Span<CoverPoint> results)
            {
                results[0] = new CoverPoint { PositionX = center.X, PositionY = center.Y, DirectionX = 1f };
                return 1;
            }
        }
    }
}
