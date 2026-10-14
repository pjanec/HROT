using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Squad;
using Fdp.Toolkit.Squad.Maneuvers;
using Hrot.AI.Behaviors.Brains;
using Hrot.CGF.Configuration;
using Hrot.Map.Definitions.Behavior;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3160</c> / <c>CE-507</c> D2 (a) (R-264) — the first squad manoeuvre that RUNS, on the commander:
    /// <see cref="BoundingOverwatchBehavior"/>. 📄 docs/designs/group-maneuvers/DESIGN_Squad_Wiring.md §6.
    /// </summary>
    public class SquadManeuverTests : IDisposable
    {
        // Every world a test builds is disposed after it: an undisposed repository keeps its native maps, and the
        // suite's leaked worlds once reached the process's map limit (`OutOfMemoryException` in later classes).
        private static readonly List<Squad> Built = new();

        public void Dispose()
        {
            lock (Built) { foreach (var s in Built) s.Repo.Dispose(); Built.Clear(); }
        }

        private sealed class Squad
        {
            public readonly EntityRepository Repo = new();
            public readonly BehaviorRegistry Registry = new();
            public readonly Entity Commander;
            public readonly Entity[] Members;
            private readonly BehaviorIngressSystem _ingress;
            private readonly BrainTickSystem _brain;
            private readonly RecentSensesSystem _senses = new();   // ⭐ CE-2123 S2 — the squad edges reach the leader's SOP
            private double _time;
            private long _frame;
            public readonly List<AssignTacticalIntentEvent> Orders = new();
            /// <summary>⭐ CE-2141 — the CLEAR intents the commander's ended runs sent (the release, AQ90 A5).</summary>
            public readonly List<AssignTacticalIntentEvent> Clears = new();

            public Squad(int members)
            {
                lock (Built) Built.Add(this);
                SimHostComponentRegistry.RegisterAll(Repo);
                CognitiveComponentRegistry.RegisterAll(Repo);
                BlueprintTierTable.RegisterAll(Repo);
                if (!Repo.Bus.IsRegistered<Fdp.Toolkit.Perception.Events.SensorChangedEvent>())
                    Repo.Bus.Register<Fdp.Toolkit.Perception.Events.SensorChangedEvent>();
                var geo = new WGS84Transform();
                geo.SetOrigin(0.0, 0.0, 0.0);
                Repo.SetSingletonManaged<IGeographicTransform>(geo);
                if (!Repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.NetworkIdentity>()) Repo.RegisterComponent<Fdp.Toolkit.Replication.Components.NetworkIdentity>();
                if (!Repo.IsComponentTypeRegistered<UnitEntity>()) Repo.RegisterComponent<UnitEntity>();
                CgfBehaviorSetup.LoadFromAiAssembly(Registry);
                Fdp.Toolkit.Utility.StandardInputs.RegisterAll();
                Fdp.Toolkit.Utility.SquadInputs.RegisterAll();          // ⭐ CE-2123 S2 — the squad's contact response reads them
                Fdp.Toolkit.Utility.UtilityDecisionCatalog.EnsureRegistered();
                _ingress = new BehaviorIngressSystem(Registry);
                _brain   = new BrainTickSystem(Registry);

                Commander = Repo.CreateEntity();
                Repo.AddComponent(Commander, new BehaviorState());
                Repo.AddComponent(Commander, new SquadCognitiveState());
                var roster = new UnitRoster();
                Members = new Entity[members];
                for (int i = 0; i < members; i++)
                {
                    var m = Repo.CreateEntity();
                    Repo.AddComponent(m, new SimTransform { Position = new Vector3(i * 4f, 0f, 0f), Rotation = Quaternion.Identity });
                    Repo.AddComponent(m, new UnitSubordinate { Commander = Commander });
                    UnitRoster.Add(ref roster, m);
                    Members[i] = m;
                }
                Repo.AddComponent(Commander, roster);
                Tick();
            }

            public void Tick()
            {
                _time += 0.016;
                Repo.SetSingleton(new GlobalTime { TotalTime = _time, DeltaTime = 0.016f, TimeScale = 1f, FrameNumber = ++_frame });
                Repo.SetSimulationTime((float)_time);
                Repo.Bus.SwapBuffers();
                foreach (var o in Repo.Bus.ReadManaged<AssignTacticalIntentEvent>()) (o.Kind == TacticalIntentKind.Clear ? Clears : Orders).Add(o);
                _senses.Execute(Repo, 0.016f);
                _ingress.Execute(Repo, 0.016f);
                _brain.Execute(Repo, 0.016f);
                Repo.FlushCommandBuffers();
            }

            public void Order(string json, string behaviour = BehaviorNames.BoundingOverwatch)
                => Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Commander, BehaviorName = behaviour, JsonParams = json, Origin = BehaviorOrigin.Superior });

            /// <summary>⭐ CE-2123 — time passes with the commander's task not ticked (a pause).</summary>
            public void Pause(double seconds) => _time += seconds;

            /// <summary>⭐ CE-2123 — the last order each member got: which behaviour, and where.</summary>
            public Dictionary<Entity, (string Intent, Vector3 Objective)> LastOrders()
            {
                var d = new Dictionary<Entity, (string, Vector3)>();
                var opts = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
                foreach (var o in Orders)
                {
                    Assert.Equal(BehaviorOrigin.Superior, o.Origin);
                    d[o.Entity] = o.IntentId switch
                    {
                        BehaviorNames.CombatPosture => (o.IntentId, System.Text.Json.JsonSerializer.Deserialize<CombatPostureParamsJsonDto>(o.JsonParams, opts).Advance.Objective),
                        BehaviorNames.AdvanceTo     => (o.IntentId, System.Text.Json.JsonSerializer.Deserialize<AdvanceToParamsJsonDto>(o.JsonParams, opts).Objective),
                        _ => throw new Xunit.Sdk.XunitException("unexpected order " + o.IntentId),
                    };
                }
                return d;
            }

            /// <summary>⭐ CE-2123 — the squad's merged pool holds <paramref name="enemy"/> at <paramref name="at"/>, reported by the
            /// roster slots in <paramref name="reporters"/> (what <c>SquadPerceptionMergeSystem</c> writes).</summary>
            public void Pool(Entity enemy, Vector3 at, ushort reporters)
            {
                ref var squad = ref Repo.GetComponentRW<SquadCognitiveState>(Commander);
                var span = System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
                    ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(ref squad.Contacts.Contacts), 16);
                span[0] = new SquadContact { EntityId = (long)enemy.PackedValue, PositionX = at.X, PositionY = at.Y, PositionZ = at.Z,
                                             ThreatScore = 1f, SourceMembersMask = reporters };
                squad.Contacts.Count = 1;
            }

            /// <summary>⭐ CE-2123 S2 — <paramref name="enemies"/> pooled contacts, all reported by slot 0.</summary>
            public Entity[] PoolMany(int enemies, Vector3 at)
            {
                var es = new Entity[enemies];
                ref var squad = ref Repo.GetComponentRW<SquadCognitiveState>(Commander);
                var span = System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
                    ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(ref squad.Contacts.Contacts), 16);
                for (int i = 0; i < enemies; i++)
                {
                    es[i] = Repo.CreateEntity();
                    Repo.AddComponent(es[i], new SimTransform { Position = at, Rotation = Quaternion.Identity });
                    span[i] = new SquadContact { EntityId = (long)es[i].PackedValue, PositionX = at.X, PositionY = at.Y, PositionZ = at.Z,
                                                 ThreatScore = 1f - i * 0.01f, SourceMembersMask = 1 };
                }
                Repo.GetComponentRW<SquadCognitiveState>(Commander).Contacts.Count = enemies;
                return es;
            }

            public void ClearPool() => Repo.GetComponentRW<SquadCognitiveState>(Commander).Contacts.Count = 0;

            /// <summary>⭐ CE-2123 S2 — what the commander's standing orders do on a squad edge: a REACTION (it pauses the task).</summary>
            public void React(string behaviour, string json = "{}")
                => Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Commander, BehaviorName = behaviour, JsonParams = json,
                                                                     Origin = BehaviorOrigin.Reaction, Urgency = ReactionUrgency.Contact });

            /// <summary>⭐ CE-2123 S2 — the leader's standing orders, and a squad edge on the leader.</summary>
            public void StartSop(string sop)
                => Repo.Bus.PublishManaged(new AssignSopEvent { Entity = Commander, BehaviorName = sop, JsonParams = "{}", Origin = BehaviorOrigin.Superior });
            public void Sense(Fdp.Toolkit.Perception.Events.SensorChange what)
                => Repo.Bus.Publish(new Fdp.Toolkit.Perception.Events.SensorChangedEvent { Unit = Commander, What = what });
            public string? TaskName => Registry.TryGetName(Repo.GetComponent<BehaviorState>(Commander).ActiveBehaviorHash, out var n) ? n : null;

            public ushort Kind => Repo.GetComponentRO<SquadCognitiveState>(Commander).ManeuverKind;
            public Entity RetreatQuery() => Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(Repo, Commander, BreakContactPlay.RetreatSite);

            /// <summary>⭐ CE-2123 — the commander's flank query, and SimHost's answer to it.</summary>
            public Entity FlankQuery() => Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(Repo, Commander, SuppressAndManeuverPlay.FlankSite);

            public void Answer(Entity sensor, Vector3? best)
            {
                var buf = new Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer { Count = best.HasValue ? 1 : 0, LastUpdateTick = 7 };
                if (best.HasValue) buf.GetSpanRW()[0] = new Fdp.Toolkit.Spatial.Eqs.EqsResult { PositionX = best.Value.X, PositionY = best.Value.Y, Score = 1f };
                Repo.SetComponent(sensor, buf);
            }

            /// <summary>The last order each member got: a HOLD is CombatPosture at its own position, a MOVE the library AdvanceTo
            /// (⭐ CE-2123). Either way from the commander (Superior).</summary>
            public Dictionary<Entity, Vector3> LastObjectives() => LastOrders().ToDictionary(kv => kv.Key, kv => kv.Value.Objective);
            public Vector3 At(Entity m) => Repo.GetComponentRO<SimTransform>(m).Position;
            public void Teleport(Entity m, Vector3 p) => Repo.GetComponentRW<SimTransform>(m).Position = p;
            public ushort Phase => Repo.GetComponentRO<SquadCognitiveState>(Commander).PhaseId;

            public bool Finished(out Fbt.NodeStatus result)
            {
                result = default;
                foreach (var evt in Repo.Bus.Read<BehaviorFinishedEvent>())
                    if (evt.Entity.Equals(Commander)) { result = evt.Result; return true; }
                return false;
            }
        }

        /// <summary>
        /// ⭐ The squad bounds: element Alpha advances <c>BoundMetres</c> toward the objective while Bravo covers where it stands; when
        /// every Alpha mover is at its point the library shell flips the phase and Bravo bounds; with both elements at the objective the
        /// commander's behaviour ends in Success. Every order is the library CombatPosture, from the commander (Superior).
        /// 🔴 Red before: no squad manoeuvre ran anywhere (CE-507: <c>SquadHsmShell</c> had no production caller).
        /// </summary>
        [Fact]
        public void CE507_BoundingOverwatch_OneElementMovesWhileTheOtherCovers_ThenTheyLeapfrog_ToTheObjective()
        {
            var s = new Squad(members: 4);
            s.Order("{\"Objective\":[0,100,0],\"BoundMetres\":30,\"ArrivalRadius\":15}");
            s.Tick(); s.Tick();

            var first = s.LastObjectives();
            Assert.Equal(4, first.Count);
            Assert.Equal(BoundingOverwatchManeuver.PhaseElement0Moving, s.Phase);
            var alpha = s.Members.Take(2).ToArray();
            var bravo = s.Members.Skip(2).ToArray();
            foreach (var m in alpha) Assert.Equal(30f, first[m].Y - s.At(m).Y, 1);   // a 30 m bound north
            foreach (var m in bravo) Assert.Equal(s.At(m), first[m]);                 // covering where it stands

            foreach (var m in alpha) s.Teleport(m, first[m]);                         // Alpha arrives
            s.Orders.Clear();
            s.Tick(); s.Tick();
            Assert.Equal(BoundingOverwatchManeuver.PhaseElement1Moving, s.Phase);
            var second = s.LastObjectives();
            foreach (var m in bravo) Assert.True(second[m].Y > s.At(m).Y + 25f, "Bravo now bounds");
            foreach (var m in alpha) Assert.Equal(s.At(m), second[m]);               // Alpha covers

            foreach (var m in s.Members) s.Teleport(m, new Vector3(s.At(m).X, 100f, 0f));
            bool done = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 4 && !done; i++) { s.Tick(); done = s.Finished(out result); }
            Assert.True(done, "both elements at the objective ⇒ the manoeuvre ends");
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }

        /// <summary>⭐ A mover that never arrives (pinned, lost) does not stall the squad: the bound times out and the other element goes.</summary>
        /// <summary>
        /// ⭐ <c>CE-2157</c> (O-a, Squad Tactics §7.8.1) — the drill trace the debug route reads: the drill and its phase, the history
        /// (Start, then each phase change with the event that caused it — a finished bound vs a timed-out one), each member's order.
        /// 🔴 Red before: nothing recorded a squad drill outside the behaviour block, so a debug route could not show it.
        /// </summary>
        [Fact]
        public void CE2157_TheDrillTrace_ShowsThePhase_TheHistoryWithItsCause_AndEachMembersOrder()
        {
            var s = new Squad(members: 4);
            s.Order("{\"Objective\":[0,100,0],\"BoundMetres\":30,\"ArrivalRadius\":15,\"BoundTimeoutSeconds\":5}");
            s.Tick(); s.Tick();

            var t = SquadDrillTrace.Of(s.Repo, s.Commander);
            Assert.True(t.Active);
            Assert.Equal(BoundingOverwatchManeuver.ManeuverKind, t.Kind);
            Assert.Equal(BoundingOverwatchManeuver.PhaseElement0Moving, t.Phase);
            Assert.Equal(SquadDrillTrace.EventStart, Assert.Single(t.History).Event);
            Assert.Equal(4, t.Orders.Length);
            Assert.Equal((byte)MemberOrderKind.AdvanceTo, t.Orders[0].Kind);
            Assert.Equal((byte)MemberOrderKind.Hold, t.Orders[3].Kind);
            Assert.Equal(s.Members[0], t.Orders[0].Member);
            Assert.Equal(s.LastObjectives()[s.Members[0]], t.Orders[0].Point);

            foreach (var m in s.Members.Take(2)) s.Teleport(m, s.LastObjectives()[m]);   // Alpha arrives: the bound is complete
            s.Tick(); s.Tick();
            t = SquadDrillTrace.Of(s.Repo, s.Commander);
            Assert.Equal(BoundingOverwatchManeuver.PhaseElement1Moving, t.Phase);
            Assert.Equal(2, t.History.Length);
            // ⭐ CE-2158 F-c — an arrived bound is "set" (DefiladeReached); BoundComplete is now only the timed-out one
            Assert.Equal((byte)Fdp.Toolkit.Squad.Primitives.PhaseEventKind.DefiladeReached, t.History[1].Event);
            Assert.Equal((byte)MemberOrderKind.AdvanceTo, t.Orders[3].Kind);   // Bravo now bounds

            for (int i = 0; i < 400; i++) s.Tick();                              // Bravo never arrives: the bound times out
            t = SquadDrillTrace.Of(s.Repo, s.Commander);
            Assert.Equal(SquadDrillTrace.EventTimeout, t.History[2].Event);
            Assert.Equal(BoundingOverwatchManeuver.PhaseElement0Moving, t.History[2].Phase);
        }

        /// <summary>
        /// ⭐ <c>CE-2152</c> C-c (R-2012 K6, R-a) — a man who is DOWN gets no order, and a bound does not wait for him: the runner's live
        /// test is "in the fight". 🔴 Red before: <c>SquadPlayContext.Live</c> tested <c>CombatLife.IsAlive</c>, so a down mover got
        /// orders he could not carry out and held the bound open until it timed out.
        /// </summary>
        [Fact]
        public void CE2152_ADownMember_GetsNoOrder_AndTheBoundDoesNotWaitForHim()
        {
            var s = new Squad(members: 4);
            var down = s.Members[1];
            s.Repo.AddComponent(down, new Fdp.Toolkit.Combat.Components.Health { Current = 25f, Max = 100f });
            s.Repo.AddComponent(down, new Fdp.Toolkit.Combat.Components.InjuryThresholds { WoundedBelow = 0.6f, IncapacitatedBelow = 0.3f });
            s.Order("{\"Objective\":[0,100,0],\"BoundMetres\":30,\"ArrivalRadius\":15,\"BoundTimeoutSeconds\":60}");
            s.Tick(); s.Tick();

            var orders = s.LastObjectives();
            Assert.False(orders.ContainsKey(down));
            Assert.Equal(3, orders.Count);
            Assert.Equal(255, SquadDrillTrace.Of(s.Repo, s.Commander).Orders[1].Kind);

            s.Teleport(s.Members[0], orders[s.Members[0]]);   // the one Alpha mover in the fight arrives
            s.Tick(); s.Tick();
            Assert.Equal(BoundingOverwatchManeuver.PhaseElement1Moving, s.Phase);
        }

        // ── CE-2152 C-c R-c — the casualty-care play ───────────────────────────────────────────────────────────────────────

        /// <summary>A squad of 4: member 1 is DOWN (25 of 100 HP), member 2 the designated Medic, everyone armed; one pooled enemy.</summary>
        private static (Squad s, Entity casualty, Entity medic, Entity enemy) CareSquad()
        {
            var s = new Squad(members: 4);
            foreach (var m in s.Members)
                s.Repo.AddComponent(m, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 100, MaxAmmo = 100, MagazineSize = 30, MagazineRounds = 30 });
            var casualty = s.Members[1];
            s.Repo.AddComponent(casualty, new Fdp.Toolkit.Combat.Components.Health { Current = 25f, Max = 100f });
            s.Repo.AddComponent(casualty, new Fdp.Toolkit.Combat.Components.InjuryThresholds { WoundedBelow = 0.6f, IncapacitatedBelow = 0.3f });
            var medic = s.Members[2];
            s.Repo.GetComponentRW<Fdp.Core.CommandHierarchy.UnitSubordinate>(medic).Designation = Fdp.Core.CommandHierarchy.TacticalDesignation.Medic;
            var enemy = s.Repo.CreateEntity();
            s.Repo.AddComponent(enemy, new SimTransform { Position = new Vector3(0, 80, 0), Rotation = Quaternion.Identity });
            s.Pool(enemy, new Vector3(0, 80, 0), reporters: 1);
            return (s, casualty, medic, enemy);
        }

        private static string LastIntent(Squad s, Entity m) => s.Orders.Last(o => o.Entity.Equals(m)).IntentId;
        private static void Fire(Squad s, Entity m) => s.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.WeaponState>(m).Ammo -= 1;
        private static Entity CoverQuery(Squad s) => Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(s.Repo, s.Commander, SquadCasualtyCarePlay.CoverSite);

        /// <summary>
        /// ⭐ <c>CE-2152</c> C-c (R-c, Squad Tactics §7.5.3) — the coverers suppress the enemy while the MEDIC holds; he goes only once every
        /// coverer has fired ("covering"); he is sent to bring the man to the cover point the terrain answered; the man lying there ends
        /// the drill (Success) and the squad remembers him as cared for. 🔴 Red before: no play cared for a casualty.
        /// </summary>
        [Fact]
        public void CE2152_CasualtyCare_TheOthersCover_TheMedicGoesOnlyWhenTheyFired_AndBringsHimToCover()
        {
            var (s, casualty, medic, enemy) = CareSquad();
            s.Order("{}", BehaviorNames.CasualtyCare);
            s.Tick(); s.Tick();

            Assert.Equal(CasualtyCareManeuver.ManeuverKind, s.Kind);
            Assert.Equal(CasualtyCareManeuver.PhaseSuppress, s.Phase);
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[0]));
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[3]));
            Assert.Equal(BehaviorNames.CombatPosture, LastIntent(s, medic));      // he holds in cover
            Assert.DoesNotContain(s.Orders, o => o.Entity.Equals(casualty));     // the man down gets nothing
            var query = CoverQuery(s);
            Assert.False(query.IsNull);                                          // the cover query, slot 0 the casualty
            var cover = new Vector3(6, -4, 0);
            s.Answer(query, cover);
            for (int i = 0; i < 20; i++) s.Tick();
            Assert.Equal(CasualtyCareManeuver.PhaseSuppress, s.Phase);            // nobody has fired yet: he waits

            Fire(s, s.Members[0]); s.Tick();
            Assert.Equal(CasualtyCareManeuver.PhaseSuppress, s.Phase);            // one coverer is not "covering"
            Fire(s, s.Members[3]); s.Tick(); s.Tick();
            Assert.Equal(CasualtyCareManeuver.PhaseGo, s.Phase);
            Assert.Equal(BehaviorNames.CarryCasualty, LastIntent(s, medic));
            var trace = SquadDrillTrace.Of(s.Repo, s.Commander);
            Assert.Equal(cover, trace.Orders[2].Point);
            Assert.Equal(casualty, trace.Orders[2].Target);
            Assert.Equal(medic, trace.Care.Medic);
            Assert.Equal((byte)Fdp.Toolkit.Squad.Primitives.PhaseEventKind.ShotFired, trace.History[^1].Event);

            s.Teleport(casualty, cover + new Vector3(1, 0, 0));                  // he lies at the cover point
            s.Tick(); s.Tick();
            Assert.True(s.Finished(out var result));
            Assert.Equal(Fbt.NodeStatus.Success, result);
            Assert.Equal(0, CasualtyCare.UncaredCount(s.Repo, s.Commander));      // cared for: not again
        }

        /// <summary>⭐ R-c — the covering never comes (nobody fires): after <c>CoveringSeconds</c> the medic goes anyway, and the trace says
        /// it was a Timeout, not the gate. 🔴 Red before: as above.</summary>
        [Fact]
        public void CE2152_CasualtyCare_NoCoveringFire_TheMedicGoesAfterAWhile_AndTheTraceSaysTimeout()
        {
            var (s, _, medic, _) = CareSquad();
            s.Order("{}", BehaviorNames.CasualtyCare);
            s.Tick(); s.Tick();
            s.Answer(CoverQuery(s), new Vector3(6, -4, 0));
            for (int i = 0; i < 250; i++) s.Tick();                               // 4 s > CoveringSeconds (3 s)
            Assert.Equal(CasualtyCareManeuver.PhaseGo, s.Phase);
            Assert.Equal(BehaviorNames.CarryCasualty, LastIntent(s, medic));
            Assert.Equal(SquadDrillTrace.EventTimeout, SquadDrillTrace.Of(s.Repo, s.Commander).History[^1].Event);
        }

        /// <summary>⭐ R-c (§7.5.4 mid-rescue) — fire picks up while the medic is still WALKING to him: he is called back to where he waited,
        /// and the drill fails once he is back (the reaction decides again). 🔴 Red before: as above.</summary>
        [Fact]
        public void CE2152_CasualtyCare_FireWhileTheMedicIsOnTheWay_CallsHimBack()
        {
            var (s, _, medic, _) = CareSquad();
            var waited = s.At(medic);
            s.Order("{}", BehaviorNames.CasualtyCare);
            s.Tick(); s.Tick();
            s.Answer(CoverQuery(s), new Vector3(6, -4, 0));
            Fire(s, s.Members[0]); Fire(s, s.Members[3]); s.Tick(); s.Tick();
            Assert.Equal(CasualtyCareManeuver.PhaseGo, s.Phase);

            s.Teleport(medic, waited + new Vector3(0, 5, 0));                      // on his way
            s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.SquadUnderFire);
            s.Tick(); s.Tick(); s.Tick();
            Assert.Equal(CasualtyCareManeuver.PhaseRecall, s.Phase);
            Assert.Equal(BehaviorNames.AdvanceTo, LastIntent(s, medic));

            s.Teleport(medic, waited);
            s.Tick(); s.Tick();
            Assert.True(s.Finished(out var result));
            Assert.Equal(Fbt.NodeStatus.Failure, result);
        }

        /// <summary>⭐ <c>CE-2152</c> R-e — the medic's carry ENDED without the man (📐 measured live: he could not reach him, and the drill
        /// waited in Go while the man bled out): the play sends him again. 🔴 Red before: Go only waited for the man to lie in cover.</summary>
        [Fact]
        public void CE2152_CasualtyCare_AMedicWhoStoppedCarrying_IsSentAgain()
        {
            var (s, _, medic, _) = CareSquad();
            s.Order("{}", BehaviorNames.CasualtyCare);
            s.Tick(); s.Tick();
            s.Answer(CoverQuery(s), new Vector3(6, -4, 0));
            Fire(s, s.Members[0]); Fire(s, s.Members[3]); s.Tick(); s.Tick();
            Assert.Equal(CasualtyCareManeuver.PhaseGo, s.Phase);
            int sent = s.Orders.Count(o => o.Entity.Equals(medic) && o.IntentId == BehaviorNames.CarryCasualty);
            Assert.Equal(1, sent);
            s.Repo.AddComponent(medic, new BehaviorState());                                     // his brain runs nothing: the carry is over
            for (int i = 0; i < 200; i++) { s.Tick(); s.Repo.GetComponentRW<BehaviorState>(medic).ActiveBehaviorHash = 0; }   // 3.2 s
            Assert.Equal(CasualtyCareManeuver.PhaseGo, s.Phase);
            Assert.True(s.Orders.Count(o => o.Entity.Equals(medic) && o.IntentId == BehaviorNames.CarryCasualty) > sent);
        }

        // ── CE-2152 C-c R-d — the rescue decision inside the contact reaction ────────────────────────────────────────────────

        /// <summary>The care squad, everyone at full health but the casualty, the enemy pooled at <paramref name="enemyAt"/>; the casualty
        /// bleeds at <paramref name="bleed"/> of max per second.</summary>
        private static (Squad s, Entity casualty) RescueSquad(Vector3 enemyAt, float bleed)
        {
            var (s, casualty, _, enemy) = CareSquad();
            foreach (var m in s.Members)
                if (!m.Equals(casualty)) s.Repo.AddComponent(m, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
            s.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.InjuryThresholds>(casualty).BleedPerSecond = bleed;
            s.Teleport(enemy, enemyAt);
            s.Pool(enemy, enemyAt, reporters: 1);
            return (s, casualty);
        }

        private static void Seconds(Squad s, double seconds) { for (int i = 0; i < (int)(seconds / 0.016); i++) s.Tick(); }

        /// <summary>
        /// ⭐ <c>CE-2152</c> C-c (R-d, §7.5.4) — a QUIET squad whose casualty no contact can see: after returning fire the reaction scores
        /// Rescue above Assault and Hold and runs the care drill. 🔴 Red before: the decision had no Rescue option.
        /// </summary>
        [Fact]
        public void CE2152_Rescue_AQuietSquad_HiddenCasualty_RescueWins()
        {
            var (s, _) = RescueSquad(new Vector3(0, 400, 0), bleed: 0.001f);   // beyond the no-terrain sight fallback (150 m)
            s.React(BehaviorNames.ReactToContact);
            Seconds(s, 4);
            Assert.Equal(CasualtyCareManeuver.ManeuverKind, s.Kind);
        }

        /// <summary>
        /// ⭐ R-d — HEAVY fire (under fire now, the enemy close enough to see the casualty): Rescue loses and the casualty waits; once the
        /// enemy is out of sight of him and the squad has been quiet, the reaction's re-decide (every 5 s) picks Rescue.
        /// 🔴 Red before: as above.
        /// </summary>
        [Fact]
        public void CE2152_Rescue_UnderHeavyFire_TheManWaits_ThenWhenQuiet_TheReDecideGoesForHim()
        {
            var (s, _) = RescueSquad(new Vector3(0, 60, 0), bleed: 0.001f);
            s.React(BehaviorNames.ReactToContact);
            for (int k = 0; k < 8; k++) { s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.SquadUnderFire); Seconds(s, 1); }
            Assert.NotEqual(CasualtyCareManeuver.ManeuverKind, s.Kind);

            var enemy = s.Repo.CreateEntity();                                   // the fight moves off: a contact far from him
            s.Repo.AddComponent(enemy, new SimTransform { Position = new Vector3(0, 400, 0), Rotation = Quaternion.Identity });
            s.Pool(enemy, new Vector3(0, 400, 0), reporters: 1);
            Seconds(s, 16);                                                      // 10 s quiet + a 5 s re-decide
            Assert.Equal(CasualtyCareManeuver.ManeuverKind, s.Kind);
        }

        /// <summary>⭐ R-d — a man about to BLEED OUT under light fire (shot at 3 s ago, nobody sees him): the squad accepts the risk.
        /// 🔴 Red before: as above.</summary>
        [Fact]
        public void CE2152_Rescue_AManAboutToBleedOut_UnderLightFire_RescueWins()
        {
            var (s, _) = RescueSquad(new Vector3(0, 400, 0), bleed: 0.012f);    // 25 % health ⇒ ~20 s left
            s.React(BehaviorNames.ReactToContact);
            s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.SquadUnderFire);
            Seconds(s, 4);
            Assert.Equal(CasualtyCareManeuver.ManeuverKind, s.Kind);
        }

        /// <summary>⭐ R-d — the same man, NOT dying, under the same light fire: holding (or assaulting) wins — the contrast that makes
        /// the bleed-out case mean something. 🔴 Red before: as above.</summary>
        [Fact]
        public void CE2152_Rescue_AFreshCasualty_UnderLightFire_TheSquadFightsFirst()
        {
            var (s, _) = RescueSquad(new Vector3(0, 400, 0), bleed: 0.001f);
            s.React(BehaviorNames.ReactToContact);
            s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.SquadUnderFire);
            Seconds(s, 3.2);
            Assert.NotEqual(CasualtyCareManeuver.ManeuverKind, s.Kind);
        }

        /// <summary>⭐ R-d — a man down and NOTHING to fight (the pool is empty): the reaction does not decide, it brings him in.
        /// 🔴 Red before: as above.</summary>
        [Fact]
        public void CE2152_Rescue_NothingToFight_TheReactionBringsHimInStraightAway()
        {
            var (s, _) = RescueSquad(new Vector3(0, 400, 0), bleed: 0.001f);
            s.ClearPool();
            s.React(BehaviorNames.ReactToContact);
            Seconds(s, 0.5);
            Assert.Equal(CasualtyCareManeuver.ManeuverKind, s.Kind);
        }

        /// <summary>⭐ R-d — the reaction is HOLDING (the casualty waited under fire) when the enemy is gone: the pool emptying must not
        /// end the reaction with the man left lying — it goes for him at once. 🔴 Red before this rail's fix: the hold ended "clear"
        /// before the 5-s re-decide and the casualty was abandoned (found while writing the `-heavy` demo).</summary>
        [Fact]
        public void CE2152_Rescue_TheEnemyGoneWhileHolding_DoesNotLeaveTheMan()
        {
            var (s, _) = RescueSquad(new Vector3(0, 60, 0), bleed: 0.001f);   // close enough to see him: no rescue while he is there
            s.React(BehaviorNames.ReactToContact);
            for (int k = 0; k < 5; k++) { s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.SquadUnderFire); Seconds(s, 1); }
            Assert.NotEqual(CasualtyCareManeuver.ManeuverKind, s.Kind);
            s.ClearPool();
            Seconds(s, 0.5);
            Assert.Equal(CasualtyCareManeuver.ManeuverKind, s.Kind);
        }

        /// <summary>⭐ R-e — the enemy is KILLED but the squad still remembers him (the pool is rebuilt from the members' memories, which
        /// keep him until they fade — measured live on <c>ua-casualty-care-heavy</c>): there is nothing left to fight, so the squad goes
        /// for the man now, not when the memory fades. 🔴 Red before: "nothing to fight" was "the pool is empty".</summary>
        [Fact]
        public void CE2152_Rescue_TheEnemyKilledButStillRemembered_TheSquadGoesForHim()
        {
            var (s, _) = RescueSquad(new Vector3(0, 60, 0), bleed: 0.001f);
            s.React(BehaviorNames.ReactToContact);
            for (int k = 0; k < 5; k++) { s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.SquadUnderFire); Seconds(s, 1); }
            Assert.NotEqual(CasualtyCareManeuver.ManeuverKind, s.Kind);
            var enemy = PooledEnemy(s);
            s.Repo.AddComponent(enemy, new Fdp.Toolkit.Combat.Components.Health { Current = 0f, Max = 100f });   // dead, still pooled
            Seconds(s, 0.5);
            Assert.Equal(CasualtyCareManeuver.ManeuverKind, s.Kind);
        }

        private static Entity PooledEnemy(Squad s)
        {
            ref var squad = ref s.Repo.GetComponentRW<SquadCognitiveState>(s.Commander);
            var span = System.Runtime.InteropServices.MemoryMarshal.CreateSpan(
                ref System.Runtime.CompilerServices.Unsafe.As<SquadContactPoolSlots, SquadContact>(ref squad.Contacts.Contacts), 16);
            return new Entity((ulong)span[0].EntityId);
        }

        [Fact]
        public void CE507_BoundingOverwatch_ABoundThatNeverCompletes_TimesOut_AndTheOtherElementMoves()
        {
            var s = new Squad(members: 4);
            s.Order("{\"Objective\":[0,100,0],\"BoundTimeoutSeconds\":0.1}");
            for (int i = 0; i < 4; i++) s.Tick();
            Assert.Equal(BoundingOverwatchManeuver.PhaseElement0Moving, s.Phase);
            for (int i = 0; i < 10; i++) s.Tick();                                    // 0.16 s, nobody moved
            Assert.Equal(BoundingOverwatchManeuver.PhaseElement1Moving, s.Phase);
        }

        // ── ⭐ CE-2123 (S1) — suppress and manoeuvre on the generic runner ─────────────────────────────────────────────────────────

        private static Entity Enemy(Squad s, Vector3 at)
        {
            var e = s.Repo.CreateEntity();
            s.Repo.AddComponent(e, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            s.Repo.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
            return e;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2123</c> S1 — the members who REPORTED the enemy hold and fire (base of fire); the rest hold until the COMMANDER's
        /// own flank query answers (measured from a base member, aimed at the enemy), then go there as an assault; at the flank the
        /// table's Assaulting phase sends them through the enemy's position, and reaching it ends the drill. 📄 DESIGN_Squad_Tactics §3.3.
        /// </summary>
        [Fact]
        public void CE2123_SuppressAndManeuver_TheSeersHold_TheRestFlankWhereTheLeaderAsks_ThenAssaultThrough()
        {
            var s = new Squad(members: 4);
            var enemy = Enemy(s, new Vector3(0f, 60f, 0f));
            s.Pool(enemy, new Vector3(0f, 60f, 0f), reporters: 0b1100);          // members 2 and 3 see him (not the first half)
            s.Order("{}", BehaviorNames.SuppressAndManeuver);
            s.Tick(); s.Tick();

            Assert.Equal(SuppressAndManeuverManeuver.PhaseSuppressing, s.Phase);
            var first = s.LastOrders();
            Assert.Equal(4, first.Count);
            foreach (var m in s.Members) Assert.Equal((BehaviorNames.CombatPosture, s.At(m)), first[m]);   // everyone holds until the terrain answers

            var query = s.FlankQuery();
            Assert.False(query.IsNull, "the commander owns a flank query");
            var cfg = s.Repo.GetComponentRO<Fdp.Toolkit.Spatial.Eqs.EqsSensor>(query);
            Assert.Equal(Fdp.Toolkit.Spatial.Eqs.FindFlankingPosition.BlueprintId, cfg.BlueprintId);
            Assert.Equal(s.Members[2], cfg.ContextSlot0);                            // measured from a member that sees the enemy
            Assert.Equal(enemy, cfg.ContextSlot1);                                   // aimed at the enemy

            s.Orders.Clear();
            s.Answer(query, new Vector3(40f, 60f, 0f));
            s.Tick(); s.Tick();
            var flank = s.LastOrders();
            Assert.Equal((BehaviorNames.AdvanceTo, new Vector3(40f, 60f, 0f)), flank[s.Members[0]]);   // the two who did NOT see him MOVE
            Assert.Equal((BehaviorNames.AdvanceTo, new Vector3(40f, 60f, 0f)), flank[s.Members[1]]);
            Assert.Equal((BehaviorNames.CombatPosture, s.At(s.Members[2])), flank[s.Members[2]]);     // the base (the seers) keeps holding
            Assert.True(s.FlankQuery().IsNull, "the answered query goes");

            s.Orders.Clear();
            s.Teleport(s.Members[0], new Vector3(39f, 60f, 0f));
            s.Teleport(s.Members[1], new Vector3(41f, 61f, 0f));
            s.Tick(); s.Tick();
            Assert.Equal(SuppressAndManeuverManeuver.PhaseAssaulting, s.Phase);
            var through = s.LastOrders();
            Assert.Equal((BehaviorNames.AdvanceTo, new Vector3(0f, 60f, 0f)), through[s.Members[0]]);   // onto the enemy's position

            s.Teleport(s.Members[0], new Vector3(2f, 60f, 0f));
            s.Teleport(s.Members[1], new Vector3(1f, 58f, 0f));
            bool done = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 4 && !done; i++) { s.Tick(); done = s.Finished(out result); }
            Assert.True(done, "the assault through the position ends the drill");
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }

        /// <summary>⭐ <c>CE-2123</c> — no flank to be had (an empty answer): the assault goes straight through.</summary>
        [Fact]
        public void CE2123_SuppressAndManeuver_NoFlankPoint_TheAssaultGoesStraightThrough()
        {
            var s = new Squad(members: 4);
            var enemy = Enemy(s, new Vector3(0f, 60f, 0f));
            s.Pool(enemy, new Vector3(0f, 60f, 0f), reporters: 0b0001);
            s.Order("{}", BehaviorNames.SuppressAndManeuver);
            s.Tick(); s.Tick();
            s.Orders.Clear();
            s.Answer(s.FlankQuery(), null);
            s.Tick(); s.Tick();
            Assert.Equal(SuppressAndManeuverManeuver.PhaseAssaulting, s.Phase);
            Assert.Equal((BehaviorNames.AdvanceTo, new Vector3(0f, 60f, 0f)), s.LastOrders()[s.Members[3]]);
        }

        /// <summary>⭐ <c>CE-2123</c> — the enemy down ends the drill in Success; ordered before any contact, the commander WAITS (no
        /// orders) and starts the moment the squad pools an identified enemy.</summary>
        [Fact]
        public void CE2123_SuppressAndManeuver_EndsWhenTheEnemyIsDown_AndWaitsForContact()
        {
            var s = new Squad(members: 4);
            var enemy = Enemy(s, new Vector3(0f, 60f, 0f));
            s.Pool(enemy, new Vector3(0f, 60f, 0f), reporters: 0b0011);
            s.Order("{}", BehaviorNames.SuppressAndManeuver);
            s.Tick(); s.Tick();
            s.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(enemy).Current = 0f;
            bool done = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 4 && !done; i++) { s.Tick(); done = s.Finished(out result); }
            Assert.True(done);
            Assert.Equal(Fbt.NodeStatus.Success, result);

            var early = new Squad(members: 4);
            early.Order("{}", BehaviorNames.SuppressAndManeuver);
            done = false;
            for (int i = 0; i < 6 && !done; i++) { early.Tick(); done = early.Finished(out result); }
            Assert.False(done, "no contact yet: the commander waits");
            Assert.Empty(early.Orders);
            var later = Enemy(early, new Vector3(0f, 60f, 0f));
            early.Pool(later, new Vector3(0f, 60f, 0f), reporters: 0b0011);
            early.Tick(); early.Tick();
            Assert.Equal(4, early.LastOrders().Count);                               // contact: the drill starts
        }

        /// <summary>⭐ <c>CE-2123</c> S1-G — a resumed task keeps its state but sends nothing again (Decision Layer §4.9); the runner
        /// notices the gap in its own ticks and re-sends every member's order, so a reaction's orders do not stand.</summary>
        [Fact]
        public void CE2123_AfterAPause_TheRunnerReSendsEveryOrder()
        {
            var s = new Squad(members: 4);
            s.Order("{\"Objective\":[0,100,0],\"BoundMetres\":30}");
            s.Tick(); s.Tick();
            Assert.Equal(4, s.LastOrders().Count);
            s.Orders.Clear();
            s.Tick();
            Assert.Empty(s.Orders);                                                  // nothing new while running
            s.Pause(1.0);
            s.Tick(); s.Tick();
            Assert.Equal(4, s.LastOrders().Count);                                   // back from the pause: every order again
        }

        // ── ⭐ CE-2123 (S2) — react to contact ─────────────────────────────────────────────────────────────────────────────────

        /// <summary>⭐ S2-G — a drill paused under a reaction finds the squad state ANOTHER drill left (here a suppress drill that
        /// ended Aborted, whose id 2 is bounding's own abort id, and a swapped partition): it restores its own kind, phase and
        /// partition, re-sends every order and carries on. ✅ Red-proof: skip the re-take ⇒ the bounding ends in Failure.</summary>
        [Fact]
        public void CE2123_S2G_ADrillResumedAfterAnotherDrill_RestoresItsOwnSquadState_AndCarriesOn()
        {
            var s = new Squad(members: 4);
            s.Order("{\"Objective\":[0,100,0],\"BoundMetres\":30}");
            s.Tick(); s.Tick();
            Assert.Equal(BoundingOverwatchManeuver.ManeuverKind, s.Kind);
            var alpha = s.Members.Take(2).ToArray();

            ref var squad = ref s.Repo.GetComponentRW<SquadCognitiveState>(s.Commander);   // another drill ran meanwhile
            squad.ManeuverKind = SuppressAndManeuverManeuver.ManeuverKind;
            squad.PhaseId = SuppressAndManeuverManeuver.PhaseAborted;
            for (int i = 0; i < 4; i++) squad.Elements.MemberElements[i] = (byte)(i < 2 ? 1 : 0);
            s.Pause(1.0);
            s.Orders.Clear();
            s.Tick(); s.Tick();

            Assert.False(s.Finished(out _), "the bounding keeps going");
            Assert.Equal(BoundingOverwatchManeuver.ManeuverKind, s.Kind);
            Assert.Equal(BoundingOverwatchManeuver.PhaseElement0Moving, s.Phase);
            var orders = s.LastOrders();
            Assert.Equal(4, orders.Count);                                            // every order sent again
            foreach (var m in alpha) Assert.Equal(BehaviorNames.AdvanceTo, orders[m].Intent);   // its OWN movers move
        }

        /// <summary>⭐ S2 §4.3 — the reaction holds and returns fire (every member CombatPosture where it stands), then a squad that
        /// outmatches the enemy ASSAULTS: the suppress-and-manoeuvre drill starts and the leader asks for a flank.</summary>
        [Fact]
        public void CE2123_ReactToContact_HoldsAndReturnsFire_ThenAStrongSquadAssaults()
        {
            var s = new Squad(members: 4);
            var enemy = Enemy(s, new Vector3(0f, 60f, 0f));
            s.Pool(enemy, new Vector3(0f, 60f, 0f), reporters: 0b0011);
            s.Order("{\"DecideAfterSeconds\":0.1}", BehaviorNames.ReactToContact);
            s.Tick(); s.Tick();

            Assert.Equal(SquadHoldManeuver.ManeuverKind, s.Kind);
            var hold = s.LastOrders();
            Assert.Equal(4, hold.Count);
            foreach (var m in s.Members) Assert.Equal((BehaviorNames.CombatPosture, s.At(m)), hold[m]);

            for (int i = 0; i < 10; i++) s.Tick();                                   // past DecideAfterSeconds
            Assert.Equal(SuppressAndManeuverManeuver.ManeuverKind, s.Kind);
            Assert.False(s.FlankQuery().IsNull, "the leader asks the terrain for a flank");
        }

        /// <summary>⭐⭐ <c>CE-2158</c> F-e (§7.6.3) — a FAR contact is CLOSED on first: the assault starts as fire and movement toward
        /// where the contact stands; once MOST of the squad is within <c>CloseMetres</c> (one man left behind does not hold it back) the
        /// flank (suppress and manoeuvre) takes over and the leader asks the terrain for it. 🔴 Red before: the assault went straight to
        /// the flank at any range; red-proved also with the "most" rule removed (the stuck man's half never arrives).</summary>
        [Fact]
        public void CE2158_ReactToContact_AFarContact_IsClosedByFireAndMovement_ThenFlanked()
        {
            var s = new Squad(members: 4);
            var far = new Vector3(0f, 150f, 0f);
            var enemy = Enemy(s, far);
            s.Pool(enemy, far, reporters: 0b0011);
            s.Order("{\"DecideAfterSeconds\":0.1}", BehaviorNames.ReactToContact);
            for (int i = 0; i < 12; i++) s.Tick();                                   // past DecideAfterSeconds

            Assert.Equal(FireAndMovementManeuver.ManeuverKind, s.Kind);              // closing, not flanking from 150 m
            Assert.True(s.FlankQuery().IsNull, "no flank asked while the squad closes");

            foreach (var m in s.Members.Skip(1)) s.Teleport(m, s.At(m) + new Vector3(0f, 110f, 0f));   // MOST of the squad within 50 m;
            for (int i = 0; i < 4; i++) s.Tick();                                                       // member 0 stays behind (stuck)
            Assert.Equal(SuppressAndManeuverManeuver.ManeuverKind, s.Kind);
            Assert.False(s.FlankQuery().IsNull, "closed: the leader asks the terrain for a flank");
        }

        /// <summary>⭐ S2-F — an outmatched squad BREAKS CONTACT: the leader asks for a point the enemy cannot see; element 0 goes there
        /// while element 1 holds and fires, then element 1 goes; both there ⇒ Success.</summary>
        [Fact]
        public void CE2123_ReactToContact_AnOutmatchedSquadBreaksContact_ElementByElement_ToTheLeadersRetreatPoint()
        {
            var s = new Squad(members: 2);
            s.PoolMany(6, new Vector3(0f, 60f, 0f));
            s.Order("{\"DecideAfterSeconds\":0.1}", BehaviorNames.ReactToContact);
            for (int i = 0; i < 12; i++) s.Tick();
            Assert.Equal(BreakContactManeuver.ManeuverKind, s.Kind);
            var query = s.RetreatQuery();
            Assert.False(query.IsNull, "the leader asks the terrain for a hidden point");
            var cfg = s.Repo.GetComponentRO<Fdp.Toolkit.Spatial.Eqs.EqsSensor>(query);
            Assert.Equal(Fdp.Toolkit.Spatial.Eqs.FindSafeRetreatPoint.BlueprintId, cfg.BlueprintId);

            var hidden = new Vector3(-40f, -30f, 0f);
            s.Answer(query, hidden);
            s.Orders.Clear();
            s.Tick(); s.Tick();
            var first = s.LastOrders();
            Assert.Equal((BehaviorNames.AdvanceTo, hidden), first[s.Members[0]]);
            Assert.Equal(BehaviorNames.CombatPosture, first[s.Members[1]].Intent);   // covering

            s.Teleport(s.Members[0], hidden);
            s.Orders.Clear();
            s.Tick(); s.Tick();
            Assert.Equal((BehaviorNames.AdvanceTo, hidden), s.LastOrders()[s.Members[1]]);

            s.Teleport(s.Members[1], hidden);
            bool done = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 4 && !done; i++) { s.Tick(); done = s.Finished(out result); }
            Assert.True(done);
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }

        /// <summary>⭐ S2-F — no hidden point anywhere ⇒ break contact fails and the reaction HOLDS until the squad knows no contact.</summary>
        [Fact]
        public void CE2123_ReactToContact_NoRetreatPoint_HoldsUntilClear()
        {
            var s = new Squad(members: 2);
            s.PoolMany(6, new Vector3(0f, 60f, 0f));
            s.Order("{\"DecideAfterSeconds\":0.1}", BehaviorNames.ReactToContact);
            for (int i = 0; i < 12; i++) s.Tick();
            s.Answer(s.RetreatQuery(), null);
            s.Tick(); s.Tick();
            Assert.Equal(SquadHoldManeuver.ManeuverKind, s.Kind);
            Assert.False(s.Finished(out _));

            s.ClearPool();
            bool done = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 4 && !done; i++) { s.Tick(); done = s.Finished(out result); }
            Assert.True(done, "no contact left ⇒ the reaction ends");
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }

        /// <summary>⭐⭐ S2 end to end on the commander: bounding (its task) → contact ⇒ the reaction PAUSES it, holds, assaults → the
        /// enemy is down ⇒ the reaction ends ⇒ the bounding RESUMES with its own squad state and re-sends its orders (S1-G + S2-G).</summary>
        [Fact]
        public void CE2123_ABoundingSquadReactsToContact_ThenResumesItsBound()
        {
            var s = new Squad(members: 4);
            s.Order("{\"Objective\":[0,100,0],\"BoundMetres\":30}");
            s.Tick(); s.Tick();
            Assert.Equal(BoundingOverwatchManeuver.ManeuverKind, s.Kind);

            var enemy = Enemy(s, new Vector3(0f, 60f, 0f));
            s.Pool(enemy, new Vector3(0f, 60f, 0f), reporters: 0b0011);
            s.React(BehaviorNames.ReactToContact, "{\"DecideAfterSeconds\":0.1}");
            s.Tick(); s.Tick();
            Assert.Equal(SquadHoldManeuver.ManeuverKind, s.Kind);                   // the bound is paused, the squad holds
            for (int i = 0; i < 10; i++) s.Tick();
            Assert.Equal(SuppressAndManeuverManeuver.ManeuverKind, s.Kind);         // decided: assault

            s.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(enemy).Current = 0f;
            s.Orders.Clear();
            for (int i = 0; i < 6; i++) s.Tick();
            Assert.Equal(BoundingOverwatchManeuver.ManeuverKind, s.Kind);           // the bound resumed, its own squad state back
            Assert.Equal(BoundingOverwatchManeuver.PhaseElement0Moving, s.Phase);
            var orders = s.LastOrders();
            foreach (var m in s.Members.Take(2)) Assert.Equal(BehaviorNames.AdvanceTo, orders[m].Intent);
        }

        /// <summary>⭐ S2-C — the shipped leader SOP (<c>BasicSquadSop</c>, compiled from its asset): a fresh <c>SquadContact</c> on the
        /// leader ⇒ React <c>ReactToContact</c>, which PAUSES the leader's task (here a bound); a <c>SquadUnderFire</c> while it runs
        /// does not restart it (<c>SopActions.React</c>: already running as a reaction ⇒ nothing published).</summary>
        [Fact]
        public void CE2123_BasicSquadSop_ASquadContact_PausesTheLeadersTask_WithReactToContact()
        {
            var s = new Squad(members: 4);
            Assert.True(s.Registry.TryGetId("BasicSquadSop", out _), "the shipped squad SOP must be compiled and registered");
            s.Order("{\"Objective\":[0,100,0],\"BoundMetres\":30}");
            s.StartSop("BasicSquadSop");
            for (int i = 0; i < 20; i++) s.Tick();
            Assert.Equal(BehaviorNames.BoundingOverwatch, s.TaskName);
            Assert.Equal(0, s.Repo.GetComponent<SopState>(s.Commander).SopFaulted);

            var enemy = Enemy(s, new Vector3(0f, 60f, 0f));
            s.Pool(enemy, new Vector3(0f, 60f, 0f), reporters: 0b0011);
            s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.SquadContact);
            for (int i = 0; i < 4; i++) s.Tick();

            Assert.Equal(BehaviorNames.ReactToContact, s.TaskName);
            var task = s.Repo.GetComponent<BehaviorState>(s.Commander);
            Assert.Equal(BehaviorOrigin.Reaction, task.Origin);
            Assert.Equal(ReactionUrgency.Contact, task.Urgency);
            Assert.Equal(BehaviorNames.BoundingOverwatch, BehaviorIngressSystem.PausedTaskOf(s.Repo, s.Commander)!.BehaviorName);
            uint run = task.InstanceId;

            s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.SquadUnderFire);
            for (int i = 0; i < 20; i++) s.Tick();
            Assert.Equal(run, s.Repo.GetComponent<BehaviorState>(s.Commander).InstanceId);   // not restarted
            Assert.Equal(0, s.Repo.GetComponent<SopState>(s.Commander).SopFaulted);
        }

        // ── ⭐ CE-2125 — the leader falls: a lull, then the squad goes on ─────────────────────────────────────────────────

        private static void Silence(Squad s, double seconds)
        {
            var now = s.Repo.GetSingleton<GlobalTime>().TotalTime;
            var lead = new Fdp.Toolkit.Squad.SquadLeadership { SilentUntil = now + seconds };
            if (s.Repo.HasComponent<Fdp.Toolkit.Squad.SquadLeadership>(s.Commander)) s.Repo.SetComponent(s.Commander, lead);
            else s.Repo.AddComponent(s.Commander, lead);
        }

        /// <summary>⭐ §7.2 L2 — while the commander is silent it sends NO order (members keep theirs); when the lull ends every order goes
        /// out again. ✅ Red-proof: drop the silence check in the runner ⇒ red.</summary>
        [Fact]
        public void CE2125_TheSilentCommander_SendsNoOrders_ThenReSendsThemAll()
        {
            var s = new Squad(members: 4);
            s.Order("{\"Objective\":[0,100,0],\"BoundMetres\":30}");
            s.Tick(); s.Tick();
            Assert.Equal(4, s.LastOrders().Count);

            Silence(s, 0.2);
            s.Orders.Clear();
            foreach (var m in s.Members.Take(2)) s.Teleport(m, new Vector3(s.At(m).X, 30f, 0f));   // a phase would complete …
            for (int i = 0; i < 6; i++) s.Tick();
            Assert.Empty(s.Orders);                                                               // … but nobody gives the order
            Assert.Equal(BoundingOverwatchManeuver.PhaseElement0Moving, s.Phase);

            for (int i = 0; i < 12; i++) s.Tick();                                               // the lull is over
            Assert.Equal(4, s.LastOrders().Count);
        }

        /// <summary>⭐ §7.2 L3 — the leader falls during the reaction's drill ⇒ the squad holds and the commander decides again.</summary>
        [Fact]
        public void CE2125_TheLeaderFallsDuringTheDrill_TheSquadHoldsAndDecidesAgain()
        {
            var s = new Squad(members: 4);
            var enemy = Enemy(s, new Vector3(0f, 60f, 0f));
            s.Pool(enemy, new Vector3(0f, 60f, 0f), reporters: 0b0011);
            s.Order("{\"DecideAfterSeconds\":0.1}", BehaviorNames.ReactToContact);
            for (int i = 0; i < 12; i++) s.Tick();
            Assert.Equal(SuppressAndManeuverManeuver.ManeuverKind, s.Kind);

            s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.SquadLeaderDown);
            s.Tick(); s.Tick();
            Assert.Equal(SquadHoldManeuver.ManeuverKind, s.Kind);                                 // back to holding …
            for (int i = 0; i < 12; i++) s.Tick();
            Assert.Equal(SuppressAndManeuverManeuver.ManeuverKind, s.Kind);                       // … and decided again
        }

        /// <summary>⭐ L3 — the shipped leader SOP reacts to <c>SquadLeaderDown</c> even with no contact (an unseen sniper).</summary>
        [Fact]
        public void CE2125_BasicSquadSop_TheLeaderDown_PausesTheTask_WithReactToContact()
        {
            var s = new Squad(members: 4);
            s.Order("{\"Objective\":[0,100,0],\"BoundMetres\":30}");
            s.StartSop("BasicSquadSop");
            for (int i = 0; i < 20; i++) s.Tick();
            s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.SquadLeaderDown);
            for (int i = 0; i < 4; i++) s.Tick();

            Assert.Equal(BehaviorNames.ReactToContact, s.TaskName);
            Assert.Equal(ReactionUrgency.UnderFire, s.Repo.GetComponent<BehaviorState>(s.Commander).Urgency);
            Assert.Equal(0, s.Repo.GetComponent<SopState>(s.Commander).SopFaulted);
        }

        // ── ⭐ CE-2123 (S3) — the squad march (DESIGN_Squad_Tactics §6.1) ─────────────────────────────────────────────────────────────

        private static void Walk(Squad s, Entity m, Vector3 to, float step = 2f)
        {
            while (true)
            {
                var d = to - s.At(m);
                if (d.Length() <= step) { s.Teleport(m, to); s.Tick(); return; }
                s.Teleport(m, s.At(m) + Vector3.Normalize(d) * step);
                s.Tick();
            }
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2123</c> S3 — a WEDGE march: the lead (the first member) goes to the objective; the others go to their wedge slots
        /// turned to the lead's heading; when the lead walks on, each follower is re-ordered ONCE per 3 m its slot moved — never every
        /// tick, and never the whole squad for one member's slot. 🔴 Red-proved: re-ordering through the runner's issue-all (Reissue)
        /// sends the lead again and fails the per-member count.
        /// </summary>
        [Fact]
        public void CE2123_SquadMarch_Wedge_TheLeadGoesToTheObjective_TheOthersToTheirSlots_ReorderedOnlyWhenTheirSlotMoves()
        {
            var s = new Squad(members: 4);
            foreach (var (m, i) in s.Members.Select((m, i) => (m, i))) s.Teleport(m, new Vector3(i * 2f, -i * 2f, 0f));
            s.Order("{\"Objective\":[0,100,0],\"Shape\":1,\"MaxLagMetres\":50}", BehaviorNames.SquadMarch);   // no halting in this rail
            s.Tick(); s.Tick();

            Assert.Equal(SquadMarchManeuver.ManeuverKind, s.Kind);
            var first = s.LastOrders();
            Assert.Equal((BehaviorNames.AdvanceTo, new Vector3(0f, 100f, 0f)), first[s.Members[0]]);
            // heading north (lead → objective): forward (0,1), right (1,0); the wedge's rows 4 m back, 3 m out (× Spacing 5 / 5)
            Assert.Equal((BehaviorNames.AdvanceTo, new Vector3(3f, -4f, 0f)), first[s.Members[1]]);
            Assert.Equal((BehaviorNames.AdvanceTo, new Vector3(-3f, -4f, 0f)), first[s.Members[2]]);
            Assert.Equal((BehaviorNames.AdvanceTo, new Vector3(6f, -8f, 0f)), first[s.Members[3]]);

            s.Orders.Clear();
            Walk(s, s.Members[0], new Vector3(0f, 10f, 0f), step: 1f);
            var perMember = s.Orders.GroupBy(o => o.Entity).ToDictionary(g => g.Key, g => g.Count());
            Assert.False(perMember.ContainsKey(s.Members[0]), "the lead keeps its order to the objective");
            foreach (var m in s.Members.Skip(1))
                Assert.Equal(3, perMember.GetValueOrDefault(m));                                      // 10 m walked ⇒ one order per 3 m (at 3, 6, 9)
            var now = s.LastOrders();
            Assert.Equal(new Vector3(3f, 5f, 0f), now[s.Members[1]].Objective);                    // the slot rides on the lead (ordered at y 9)

            s.Orders.Clear();
            s.Teleport(s.Members[0], new Vector3(0f, 11f, 0f));
            s.Tick(); s.Tick();
            Assert.Empty(s.Orders);                                                                // a 1 m step re-orders nobody
        }

        /// <summary>
        /// ⭐ <c>CE-2123</c> S3 — a FILE march walks on the lead's own trail: a follower holds until the lead has walked its distance,
        /// then goes to the point (rank+1) × Spacing behind the lead ON THE PATH the lead took — round the corner it turned, not a
        /// straight line behind it.
        /// </summary>
        [Fact]
        public void CE2123_SquadMarch_File_FollowersWalkOnTheLeadsTrail_RoundItsCorner()
        {
            var s = new Squad(members: 3);
            s.Order("{\"Objective\":[0,100,0],\"Shape\":0}", BehaviorNames.SquadMarch);   // member 0 is nearest: it leads
            s.Tick(); s.Tick();
            var first = s.LastOrders();
            Assert.Equal((BehaviorNames.CombatPosture, s.At(s.Members[1])), first[s.Members[1]]);      // the trail is not 5 m long yet: hold

            Walk(s, s.Members[0], new Vector3(0f, 20f, 0f));                                            // north 20 m …
            Walk(s, s.Members[0], new Vector3(8f, 20f, 0f));                                            // … then east 8 m
            // the walked path: (0,0) → (0,20) → (8,20). 5 m behind the lead is (3,20) on the east leg; 10 m is (0,18), round the corner.
            static bool OnTrail(Vector3 p) => (MathF.Abs(p.X) < 0.1f && p.Y is >= -0.1f and <= 20.1f) || (MathF.Abs(p.Y - 20f) < 0.1f && p.X is >= -0.1f and <= 8.1f);
            var orders = s.LastOrders();
            foreach (var (m, behind) in new[] { (s.Members[1], new Vector3(3f, 20f, 0f)), (s.Members[2], new Vector3(0f, 18f, 0f)) })
            {
                Assert.Equal(BehaviorNames.AdvanceTo, orders[m].Intent);
                Assert.True(OnTrail(orders[m].Objective), $"a file slot lies on the lead's trail: {orders[m].Objective}");
                Assert.True(Vector3.Distance(orders[m].Objective, behind) < SquadMarchPlay.ReorderMetres, $"{orders[m].Objective} vs {behind}");
            }
        }

        /// <summary>⭐ <c>CE-2159</c> (§6.1b) — a follower far behind his slot is sent FASTER than the march (a slot on the trail moves at
        /// the march speed, so the march speed never closes the gap); one near his slot at the march speed. 🔴 Red before: always the
        /// march speed.</summary>
        [Fact]
        public void CE2159_SquadMarch_AFollowerFarBehindHisSlot_IsSentFaster_ANearOneAtTheMarchSpeed()
        {
            var s = new Squad(members: 3);
            s.Order("{\"Objective\":[0,100,0],\"Shape\":0,\"Speed\":1.5}", BehaviorNames.SquadMarch);
            s.Tick(); s.Tick();
            Walk(s, s.Members[0], new Vector3(0f, 20f, 0f));                                            // slots 5 and 10 m behind the lead
            s.Teleport(s.Members[1], new Vector3(0f, 19f, 0f));                                         // where his slot will be (5 m behind)
            s.Orders.Clear();
            Walk(s, s.Members[0], new Vector3(0f, 26f, 0f));                                            // the slots move on: both re-ordered
            float Speed(Entity m)
            {
                var opts = Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed;
                var o = s.Orders.Last(x => x.Entity == m && x.IntentId == BehaviorNames.AdvanceTo);
                return System.Text.Json.JsonSerializer.Deserialize<AdvanceToParamsJsonDto>(o.JsonParams, opts).Speed;
            }
            Assert.Equal(1.5f * SquadMarchPlay.CatchUpFactor, Speed(s.Members[2]), 3);                 // ~16 m behind: catches up
            Assert.Equal(1.5f, Speed(s.Members[1]), 3);                                                 // ≤ 3 m from it: the march speed
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2123</c> S3 — a slot that does not work: followers that make no progress for 3 s walk ON FILE on the lead's trail
        /// (a point the lead stood on — walkable); and while they lag more than MaxLagMetres the lead HALTS (a hold), going on once they
        /// close up. 🔴 Red-proved: without the stuck fallback the followers' orders stay on their wedge slots.
        /// </summary>
        [Fact]
        public void CE2123_SquadMarch_AStuckFollowerFallsBackToFile_AndTheLeadWaitsForStragglers()
        {
            var s = new Squad(members: 3);
            s.Order("{\"Objective\":[0,200,0],\"Shape\":2,\"MaxLagMetres\":10}", BehaviorNames.SquadMarch);
            s.Tick(); s.Tick();
            Walk(s, s.Members[0], new Vector3(0f, 16f, 0f));                                           // the followers never move
            for (int i = 0; i < 220; i++) s.Tick();                                                   // > 3 s with no progress

            var orders = s.LastOrders();
            Assert.Equal(BehaviorNames.CombatPosture, orders[s.Members[0]].Intent);                    // the lead waits …
            foreach (var m in s.Members.Skip(1))
            {
                var o = orders[m].Objective;
                Assert.Equal(BehaviorNames.AdvanceTo, orders[m].Intent);
                Assert.Equal(0f, o.X, 1);                                                             // … and they walk on the trail (x = 0)
            }

            s.Orders.Clear();
            foreach (var m in s.Members.Skip(1)) s.Teleport(m, orders[m].Objective);                   // they close up
            s.Tick(); s.Tick();
            Assert.Equal((BehaviorNames.AdvanceTo, new Vector3(0f, 200f, 0f)), s.LastOrders()[s.Members[0]]);   // the lead goes on
        }

        /// <summary>⭐ <c>CE-2123</c> S3 — the lead falls: the next live member leads (to the objective), and the march ends in Success
        /// when the lead is there and everyone stands at its slot.</summary>
        [Fact]
        public void CE2123_SquadMarch_TheLeadFalls_TheNextMemberLeads_ThenEveryoneArrives()
        {
            var s = new Squad(members: 3);
            foreach (var m in s.Members) s.Repo.AddComponent(m, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
            s.Order("{\"Objective\":[0,30,0],\"Shape\":1}", BehaviorNames.SquadMarch);
            s.Tick(); s.Tick();
            s.Orders.Clear();
            s.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(s.Members[0]).Current = 0f;
            s.Tick(); s.Tick();
            Assert.Equal((BehaviorNames.AdvanceTo, new Vector3(0f, 30f, 0f)), s.LastOrders()[s.Members[1]]);

            Walk(s, s.Members[1], new Vector3(0f, 30f, 0f));
            var slot = s.LastOrders()[s.Members[2]].Objective;
            s.Teleport(s.Members[2], slot);
            bool done = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 6 && !done; i++) { s.Tick(); done = s.Finished(out result); }
            Assert.True(done, "the lead at the objective and the follower at its slot ⇒ the march ends");
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }

        // ── ⭐ CE-2141 (D2, docs/DESIGN_Orders_Params_Entity_Classes.md §7) — the orders a squad fulfils, and the release ──

        private static string MoveJson(Squad s, Vector3 to, string roadUse = "Unspecified")
        {
            var (lat, lon, _) = s.Repo.GetSingletonManaged<IGeographicTransform>().ToGeodetic(to);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            return "{\"Location\":[" + lat.ToString("R", inv) + "," + lon.ToString("R", inv) + "],\"RoadUse\":\"" + roadUse + "\"}";   // enums are names (strict)
        }

        private static void Ticks(Squad s, int n) { for (int i = 0; i < n; i++) s.Tick(); }

        /// <summary>⭐ D2-E/F — MOVE: a geodetic Location becomes the march's local objective; the squad chooses its shape — a Wedge
        /// (every follower goes to its slot at once), or File when the order prefers roads (followers wait for the lead's trail).</summary>
        [Fact]
        public void CE2141_InfantrySquadMove_AGeodeticPoint_TheSquadMarchesThere_InAWedge_OrInFileOnARoad()
        {
            var s = new Squad(members: 4);
            s.Order(MoveJson(s, new Vector3(0f, 80f, 0f)), BehaviorNames.InfantrySquadMove);
            Ticks(s, 2);
            Assert.True(SquadMarchManeuver.ManeuverKind == s.Kind, $"task={s.TaskName} kind={s.Kind} orders={s.Orders.Count} json={MoveJson(s, new Vector3(0f, 80f, 0f))}");
            var o = s.LastOrders();
            Assert.Equal(BehaviorNames.AdvanceTo, o[s.Members[0]].Intent);                              // the lead: nearest the point
            Assert.InRange(Vector2.Distance(new Vector2(o[s.Members[0]].Objective.X, o[s.Members[0]].Objective.Y), new Vector2(0f, 80f)), 0f, 0.5f);
            Assert.All(s.Members.Skip(1), m => Assert.Equal(BehaviorNames.AdvanceTo, o[m].Intent));       // wedge slots

            var road = new Squad(members: 4);
            road.Order(MoveJson(road, new Vector3(0f, 80f, 0f), roadUse: nameof(Fdp.Toolkit.Navigation.RoadUse.Prefer)), BehaviorNames.InfantrySquadMove);
            Ticks(road, 2);
            var r = road.LastOrders();
            Assert.Equal(BehaviorNames.AdvanceTo, r[road.Members[0]].Intent);
            Assert.All(road.Members.Skip(1), m => Assert.Equal(BehaviorNames.CombatPosture, r[m].Intent)); // file: wait for the trail
        }

        /// <summary>⭐ HOLD: every member holds and fights where it stands, and the order never ends by itself.</summary>
        [Fact]
        public void CE2141_InfantrySquadHold_EveryoneHolds_AndItNeverEndsByItself()
        {
            var s = new Squad(members: 3);
            s.Order("{}", BehaviorNames.InfantrySquadHold);
            for (int i = 0; i < 300; i++) { s.Tick(); Assert.False(s.Finished(out _), "a hold order ends only when replaced or released"); }
            Assert.Equal(SquadHoldManeuver.ManeuverKind, s.Kind);
            var o = s.LastOrders();
            Assert.All(s.Members, m => Assert.Equal(BehaviorNames.CombatPosture, o[m].Intent));
        }

        /// <summary>⭐ D2-G — ATTACK on an enemy the squad never saw: the designation is the knowledge — the drill starts at once, the
        /// leader's flank query is aimed at that enemy, and its death ends the drill.</summary>
        [Fact]
        public void CE2141_InfantrySquadAttack_ANamedTargetTheSquadNeverSaw_IsAttacked_ItsDeathEndsTheDrill()
        {
            var s = new Squad(members: 4);
            var enemy = Enemy(s, new Vector3(0f, 60f, 0f));
            s.Repo.AddComponent(enemy, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 2141 });
            s.Order("{\"Target\":2141}", BehaviorNames.InfantrySquadAttack);
            Ticks(s, 2);
            Assert.Equal(SuppressAndManeuverManeuver.ManeuverKind, s.Kind);
            Assert.Equal(4, s.LastOrders().Count);
            var query = s.FlankQuery();
            Assert.False(query.IsNull, "the leader asks the terrain for a flank on the designated enemy");
            Assert.Equal(enemy, s.Repo.GetComponentRO<Fdp.Toolkit.Spatial.Eqs.EqsSensor>(query).ContextSlot1);

            s.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(enemy).Current = 0f;
            bool done = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 4 && !done; i++) { s.Tick(); done = s.Finished(out result); }
            Assert.True(done);
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }

        /// <summary>⭐⭐ D2-B (AQ90 A5) — a drill that ENDS clears each member's order, naming what it was told (so a member's own
        /// reaction or a newer order is left alone); a drill REPLACED from outside does the same.</summary>
        [Fact]
        public void CE2141_ADrillThatEnds_OrIsReplaced_ClearsTheOrdersItGave_NamingEach()
        {
            var s = new Squad(members: 3);
            s.Order("{\"DurationSeconds\":0.5}", BehaviorNames.SquadHold);
            bool done = false;
            for (int i = 0; i < 60 && !done; i++) { s.Tick(); done = s.Finished(out _); }
            Assert.True(done);
            s.Tick();                                                                          // the release went out with the finish
            Assert.Equal(s.Members.OrderBy(m => m.Index), s.Clears.Select(c => c.Entity).OrderBy(m => m.Index));
            Assert.All(s.Clears, c => { Assert.Equal(BehaviorNames.CombatPosture, c.IntentId); Assert.Equal(BehaviorOrigin.Superior, c.Origin); });

            var r = new Squad(members: 3);
            r.Order("{}", BehaviorNames.InfantrySquadHold);
            Ticks(r, 3);
            Assert.Empty(r.Clears);
            r.Order(MoveJson(r, new Vector3(0f, 80f, 0f)), BehaviorNames.InfantrySquadMove);   // a new order from outside
            Ticks(r, 3);
            Assert.Equal(3, r.Clears.Count);
            Assert.All(r.Clears, c => Assert.Equal(BehaviorNames.CombatPosture, c.IntentId));
        }

        /// <summary>⭐ D2-B — a drill PAUSED under the leader's reaction releases nothing; the reaction's own orders are released when
        /// it ends, and the resumed drill sends its orders again (S1-G).</summary>
        [Fact]
        public void CE2141_APausedDrill_ReleasesNothing_TheReactionsOrdersGoWhenItEnds()
        {
            var s = new Squad(members: 3);
            s.Order("{}", BehaviorNames.InfantrySquadHold);
            Ticks(s, 3);
            s.React(BehaviorNames.SquadHold, "{\"DurationSeconds\":0.5}");
            Ticks(s, 3);
            Assert.Equal(BehaviorNames.SquadHold, s.TaskName);
            Assert.Empty(s.Clears);
            Ticks(s, 60);
            Assert.Equal(BehaviorNames.InfantrySquadHold, s.TaskName);                       // resumed
            Assert.Equal(3, s.Clears.Count);                                                  // the reaction's three orders
            int before = s.Orders.Count;
            Ticks(s, 2);
            Assert.True(s.Orders.Count >= before, "the resumed drill's orders stand");
        }

        /// <summary>⭐ D2-H (AQ90 A4) — a member that is itself a UNIT gets an ORDER (<c>Order.Hold</c>), not a behaviour, and its
        /// release is an unnamed clear (the commander cannot know what it resolved to).</summary>
        [Fact]
        public void CE2141_AMemberThatIsAUnit_GetsAnOrder_AndAnUnnamedRelease()
        {
            var s = new Squad(members: 3);
            s.Repo.AddComponent(s.Members[1], new UnitEntity());
            s.Order("{\"DurationSeconds\":0.5}", BehaviorNames.SquadHold);
            bool done = false;
            for (int i = 0; i < 60 && !done; i++) { s.Tick(); done = s.Finished(out _); }
            Assert.True(done);
            var toUnit = s.Orders.Where(o => o.Entity.Equals(s.Members[1])).ToList();
            Assert.NotEmpty(toUnit);
            Assert.All(toUnit, o => Assert.Equal(Hrot.Map.Definitions.Behavior.Orders.OrderAttribute.IntentFor("Hold"), o.IntentId));
            Assert.All(s.Orders.Where(o => !o.Entity.Equals(s.Members[1])), o => Assert.Equal(BehaviorNames.CombatPosture, o.IntentId));

            s.Tick(); s.Tick();
            Assert.Equal(string.Empty, s.Clears.Single(c => c.Entity.Equals(s.Members[1])).IntentId);
            Assert.Equal(BehaviorNames.CombatPosture, s.Clears.First(c => c.Entity.Equals(s.Members[0])).IntentId);
        }

        /// <summary>⭐ D2-H — a unit member told to MOVE gets <c>Order.Move</c> with the point as a geodetic Location.</summary>
        [Fact]
        public void CE2141_AUnitMemberToldToMove_GetsOrderMove_WithAGeodeticLocation()
        {
            var s = new Squad(members: 2);
            s.Repo.AddComponent(s.Members[0], new UnitEntity());
            SquadManeuverRunner.Move(s.Repo, s.Commander, s.Members[0], new Vector3(30f, 40f, 0f), 2f, 5f);
            s.Tick();
            var o = s.Orders.Single();
            Assert.Equal("Order.Move", o.IntentId);
            var move = System.Text.Json.JsonSerializer.Deserialize<Hrot.AI.Behaviors.Orders.MoveOrder>(o.JsonParams, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
            var back = s.Repo.GetSingletonManaged<IGeographicTransform>().ToCartesian(move.Location.Latitude, move.Location.Longitude, 0.0);
            Assert.InRange(Vector2.Distance(new Vector2(back.X, back.Y), new Vector2(30f, 40f)), 0f, 0.1f);
            Assert.Equal(2f, move.Speed);
        }

        // ── CE-1056 I-2d — the squad Dismount drill (Squad Tactics §5a) ──────────────────────────────────────────────────────

        private static Entity Carrier(Squad s, Vector3 at, params int[] aboard)
        {
            var v = s.Repo.CreateEntity();
            s.Repo.AddComponent(v, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            s.Repo.AddComponent(v, new PassengerBuffer());
            foreach (int i in aboard) Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(s.Repo, s.Members[i], v));
            return v;
        }

        private static Fbt.NodeStatus? RunUntilFinished(Squad s, int ticks)
        {
            for (int i = 0; i < ticks; i++) { s.Tick(); if (s.Finished(out var r)) return r; }
            return null;
        }

        [Fact]
        public void CE1056_SquadDismount_EveryMemberAboardIsToldToDisembark_TheRestHold_DoneWhenNobodyIsAboard()
        {
            var s = new Squad(members: 4);
            var apc = Carrier(s, new Vector3(0f, 0f, 0f), 0, 1, 2);
            s.Order("{}", BehaviorNames.SquadDismount);
            Ticks(s, 3);

            Assert.Equal(SquadDismountManeuver.ManeuverKind, s.Kind);
            var last = s.Orders.GroupBy(o => o.Entity).ToDictionary(g => g.Key, g => g.Last());
            for (int i = 0; i < 3; i++) Assert.Equal(BehaviorNames.Disembark, last[s.Members[i]].IntentId);
            Assert.Equal(BehaviorNames.CombatPosture, last[s.Members[3]].IntentId);                  // already out: holds
            Assert.All(last.Values, o => Assert.Equal(BehaviorOrigin.Superior, o.Origin));

            Embarkation.Disembark(s.Repo, s.Members[0]);
            Embarkation.Disembark(s.Repo, s.Members[1]);
            Assert.Null(RunUntilFinished(s, 5));                                                     // one still aboard
            Embarkation.Disembark(s.Repo, s.Members[2]);
            Assert.Equal(Fbt.NodeStatus.Success, RunUntilFinished(s, 5));
            Assert.Equal(0, s.Repo.GetComponentRO<PassengerBuffer>(apc).Count);
            Ticks(s, 2);
            Assert.Contains(s.Clears, c => c.Entity == s.Members[0]);                                // its orders released (A5)
        }

        [Fact]
        public void CE1056_SquadDismount_SomebodyStuckAboard_FailsAfterItsTimeout()
        {
            var s = new Squad(members: 3);
            Carrier(s, new Vector3(0f, 0f, 0f), 0, 1, 2);
            s.Order("{\"timeoutSeconds\":1}", BehaviorNames.SquadDismount);
            Assert.Equal(Fbt.NodeStatus.Failure, RunUntilFinished(s, 120));                          // 1 s at 16 ms a tick
        }

        [Fact]
        public void CE1056_BasicSquadSop_UnderFireWhileMounted_Dismounts_ThenReactsToContactOnceOut()
        {
            var s = new Squad(members: 4);
            s.Order("{\"Objective\":[0,100,0],\"BoundMetres\":30}");
            s.StartSop("BasicSquadSop");
            Ticks(s, 20);
            Carrier(s, new Vector3(0f, 0f, 0f), 0, 1, 2, 3);

            s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.SquadUnderFire);
            Ticks(s, 4);
            Assert.Equal(BehaviorNames.SquadDismount, s.TaskName);                                   // row 1: mounted ⇒ get out
            Assert.Equal(BehaviorOrigin.Reaction, s.Repo.GetComponent<BehaviorState>(s.Commander).Origin);

            foreach (var m in s.Members) Embarkation.Disembark(s.Repo, m);
            Ticks(s, 10);
            Assert.NotEqual(BehaviorNames.SquadDismount, s.TaskName);                                // done once nobody is aboard

            s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.SquadUnderFire);
            Ticks(s, 4);
            Assert.Equal(BehaviorNames.ReactToContact, s.TaskName);                                  // out: the battle drill
            Assert.Equal(0, s.Repo.GetComponent<SopState>(s.Commander).SopFaulted);
        }

        // ── CE-2158 F-c — bounding overwatch: suppress on contact, cover-point bounds ───────────────────────────────────────────────

        private static Entity BoQuery(Squad s) => Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(s.Repo, s.Commander, BoundingOverwatchPlay.CoverSite);

        /// <summary>⭐ <c>CE-2158</c> F-c (§7.6) — before contact the covering element WATCHES (holds); once the squad's pool holds a contact it
        /// SUPPRESSES it. 🔴 Red before: the coverers only ever held, contact or not.</summary>
        [Fact]
        public void CE2158_BoundingOverwatch_TheWatchersSuppressOnContact()
        {
            var s = new Squad(members: 4);
            s.Order("{\"Objective\":[0,100,0],\"BoundMetres\":30,\"ArrivalRadius\":15,\"BoundTimeoutSeconds\":60}");
            s.Tick(); s.Tick();
            Assert.Equal(BehaviorNames.CombatPosture, LastIntent(s, s.Members[2]));     // watching
            var enemy = s.Repo.CreateEntity();
            s.Repo.AddComponent(enemy, new SimTransform { Position = new Vector3(0, 90, 0), Rotation = Quaternion.Identity });
            s.Pool(enemy, new Vector3(0, 90, 0), reporters: 1);
            s.Tick(); s.Tick();
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[2]));
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[3]));
            Assert.Equal(BehaviorNames.AdvanceTo, LastIntent(s, s.Members[0]));         // the bound goes on
        }

        /// <summary>⭐ <c>CE-2158</c> F-c — while Alpha bounds, the commander asks for cover for BRAVO's next bound (before contact aimed at the
        /// objective, where the enemy is expected); when Alpha is set (DefiladeReached), Bravo bounds to the cover points ahead.
        /// 🔴 Red before: the bound point was the formation shifted 30 m, cover or not.</summary>
        [Fact]
        public void CE2158_BoundingOverwatch_TheNextBoundGoesToCoverAhead()
        {
            var s = new Squad(members: 4);
            s.Order("{\"Objective\":[0,100,0],\"BoundMetres\":30,\"ArrivalRadius\":15,\"BoundTimeoutSeconds\":60}");
            s.Tick(); s.Tick();
            var query = BoQuery(s);
            Assert.False(query.IsNull);
            var sensor = s.Repo.GetComponentRO<Fdp.Toolkit.Spatial.Eqs.EqsSensor>(query);
            Assert.Equal(s.Members[2], sensor.ContextSlot0);                             // around Bravo's lead
            Assert.Equal(Fdp.Toolkit.Spatial.Eqs.EqsSensor.Point1Bit, sensor.ContextPointMask & Fdp.Toolkit.Spatial.Eqs.EqsSensor.Point1Bit);
            Assert.Equal(new Vector3(0, 100, 0), sensor.ContextPoint1);                   // hidden from where the enemy is expected
            var b0 = new Vector3(9, 26, 0); var b1 = new Vector3(13, 25, 0);
            AnswerAll(s, query, new Vector3(8, -10, 0), b0, b1);
            s.Tick();

            var first = s.LastObjectives();
            foreach (var m in s.Members.Take(2)) s.Teleport(m, first[m]);                // Alpha is set
            s.Tick(); s.Tick();
            Assert.Equal(BoundingOverwatchManeuver.PhaseElement1Moving, s.Phase);
            var t = SquadDrillTrace.Of(s.Repo, s.Commander);
            Assert.Equal((byte)Fdp.Toolkit.Squad.Primitives.PhaseEventKind.DefiladeReached, t.History[^1].Event);
            Assert.Equal(b0, t.Orders[2].Point);
            Assert.Equal(b1, t.Orders[3].Point);
            Assert.True(t.Orders[2].Cover && t.Orders[3].Cover);

            // ⭐ a man sent to COVER is set in it (1.5 m), not 3 m short of it — measured live: the swap came 2.6–3.9 m short
            s.Teleport(s.Members[2], b0 - new Vector3(0f, 2.5f, 0f));
            s.Teleport(s.Members[3], b1);
            s.Tick(); s.Tick();
            Assert.Equal(BoundingOverwatchManeuver.PhaseElement1Moving, s.Phase);
            s.Teleport(s.Members[2], b0);
            s.Tick(); s.Tick();
            Assert.Equal(BoundingOverwatchManeuver.PhaseElement0Moving, s.Phase);
        }

        // ── CE-2158 F-b — fire and movement (Squad Tactics §7.6.1) ──────────────────────────────────────────────────────────────

        private const string FmOrder = "{\"Objective\":[6,80,0],\"RushMetres\":15}";
        private static readonly Vector3 AheadA = new(1f, 12f, 0f), AheadB = new(5f, 11f, 0f);

        /// <summary>A squad of 4 in a row at y 0, everyone armed, one enemy pooled ~80 m north. Half 0 = members 0 and 1, half 1 = 2 and 3.</summary>
        private static Squad FmSquad()
        {
            var s = new Squad(members: 4);
            foreach (var m in s.Members)
                s.Repo.AddComponent(m, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 100, MaxAmmo = 100, MagazineSize = 30, MagazineRounds = 30 });
            var enemy = s.Repo.CreateEntity();
            s.Repo.AddComponent(enemy, new SimTransform { Position = new Vector3(6, 80, 0), Rotation = Quaternion.Identity });
            s.Pool(enemy, new Vector3(6, 80, 0), reporters: 1);
            return s;
        }

        private static Entity FmQuery(Squad s) => Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(s.Repo, s.Commander, FireAndMovementPlay.CoverSite);

        /// <summary>SimHost's answer to a cover query: these points, best first.</summary>
        private static void AnswerAll(Squad s, Entity sensor, params Vector3[] points)
        {
            var buf = new Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer { Count = points.Length, LastUpdateTick = 7 };
            for (int i = 0; i < points.Length; i++)
                buf.GetSpanRW()[i] = new Fdp.Toolkit.Spatial.Eqs.EqsResult { PositionX = points[i].X, PositionY = points[i].Y, PositionZ = points[i].Z, Score = 1f - i * 0.1f };
            s.Repo.SetComponent(sensor, buf);
        }

        /// <summary>Order, answer the first bound's cover query (one point behind, one out of reach, two ahead), both coverers fire.</summary>
        private static Squad FmRushing()
        {
            var s = FmSquad();
            s.Order(FmOrder, BehaviorNames.FireAndMovement);
            s.Tick(); s.Tick();
            AnswerAll(s, FmQuery(s), new Vector3(0, -5, 0), new Vector3(0, 40, 0), AheadA, AheadB);
            s.Tick();
            Fire(s, s.Members[2]); Fire(s, s.Members[3]); s.Tick(); s.Tick();
            return s;
        }

        /// <summary>
        /// ⭐ <c>CE-2158</c> F-b (§7.6.1) — in contact, half 1 SUPPRESSES while half 0 holds; half 0 rushes only once every base-of-fire man
        /// has fired ("covering"), each mover to the cover point that brings him furthest toward the objective within one rush (never the
        /// point behind him, never the one 40 m off, never two men on one point). 🔴 Red before: no fire-and-movement play.
        /// </summary>
        [Fact]
        public void CE2158_FireAndMovement_TheBaseOfFireSuppresses_TheMoversGoOnlyWhenCovered_ToCoverAhead()
        {
            var s = FmSquad();
            s.Order(FmOrder, BehaviorNames.FireAndMovement);
            s.Tick(); s.Tick();

            Assert.Equal(FireAndMovementManeuver.ManeuverKind, s.Kind);
            Assert.Equal(FireAndMovementManeuver.PhaseCover0, s.Phase);
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[2]));
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[3]));
            Assert.Equal(BehaviorNames.CombatPosture, LastIntent(s, s.Members[0]));   // the movers wait down
            Assert.Equal(BehaviorNames.CombatPosture, LastIntent(s, s.Members[1]));
            var query = FmQuery(s);
            Assert.False(query.IsNull);
            AnswerAll(s, query, new Vector3(0, -5, 0), new Vector3(0, 40, 0), AheadA, AheadB);
            for (int i = 0; i < 20; i++) s.Tick();
            Assert.Equal(FireAndMovementManeuver.PhaseCover0, s.Phase);              // nobody fired: nobody moves

            Fire(s, s.Members[2]); s.Tick();
            Assert.Equal(FireAndMovementManeuver.PhaseCover0, s.Phase);              // one is not "covering"
            Fire(s, s.Members[3]); s.Tick(); s.Tick();
            Assert.Equal(FireAndMovementManeuver.PhaseRush0, s.Phase);
            Assert.Equal(BehaviorNames.AdvanceTo, LastIntent(s, s.Members[0]));
            Assert.Equal(BehaviorNames.AdvanceTo, LastIntent(s, s.Members[1]));
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[2]));       // still firing while they run
            var trace = SquadDrillTrace.Of(s.Repo, s.Commander);
            Assert.Equal(AheadA, trace.Orders[0].Point);
            Assert.Equal(AheadB, trace.Orders[1].Point);
            Assert.True(trace.Orders[0].Cover);
            Assert.Equal((byte)Fdp.Toolkit.Squad.Primitives.PhaseEventKind.ShotFired, trace.History[^1].Event);
        }

        /// <summary>⭐ F-b — "set": the halves swap only when EVERY mover is at his point; then the movers are the base of fire and the
        /// other half waits for its own cover. 🔴 Red before: as above.</summary>
        [Fact]
        public void CE2158_FireAndMovement_SetSwapsTheHalves_TheMoversBecomeTheBaseOfFire()
        {
            var s = FmRushing();
            Assert.Equal(FireAndMovementManeuver.PhaseRush0, s.Phase);
            s.Teleport(s.Members[0], AheadA);
            s.Tick(); s.Tick();
            Assert.Equal(FireAndMovementManeuver.PhaseRush0, s.Phase);               // one man is still up
            s.Teleport(s.Members[1], AheadB + new Vector3(0.5f, 0, 0));
            s.Tick(); s.Tick();
            Assert.Equal(FireAndMovementManeuver.PhaseCover1, s.Phase);
            Assert.Equal((byte)Fdp.Toolkit.Squad.Primitives.PhaseEventKind.DefiladeReached, SquadDrillTrace.Of(s.Repo, s.Commander).History[^1].Event);
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[0]));
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[1]));
            Assert.Equal(BehaviorNames.CombatPosture, LastIntent(s, s.Members[2]));
            Assert.False(FmQuery(s).IsNull);                                         // cover for the next half
        }

        /// <summary>⭐ <c>CE-2158</c> F-d (§7.6.4) — in PAIRS the moving half rushes one buddy at a time: the A man (member 0) goes while
        /// his buddy B (member 1) fires with the other half; A set ⇒ B goes (still the same phase); B set ⇒ the halves swap.
        /// 🔴 Red before: the whole half went at once.</summary>
        [Fact]
        public void CE2158_FireAndMovement_InPairs_OneBuddyUpWhileTheOtherFires_ThenTheOther()
        {
            var s = FmSquad();
            s.Order("{\"Objective\":[6,80,0],\"RushMetres\":15,\"Pairs\":true}", BehaviorNames.FireAndMovement);
            s.Tick(); s.Tick();
            AnswerAll(s, FmQuery(s), new Vector3(0, -5, 0), new Vector3(0, 40, 0), AheadA, AheadB);
            s.Tick();
            Fire(s, s.Members[2]); Fire(s, s.Members[3]); s.Tick(); s.Tick();
            Assert.Equal(FireAndMovementManeuver.PhaseRush0, s.Phase);
            Assert.Equal(BehaviorNames.AdvanceTo, LastIntent(s, s.Members[0]));     // A is up …
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[1]));      // … his buddy fires
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[2]));

            s.Teleport(s.Members[0], AheadA);
            s.Tick(); s.Tick();
            Assert.Equal(FireAndMovementManeuver.PhaseRush0, s.Phase);               // the same phase: B's turn
            Assert.Equal(BehaviorNames.AdvanceTo, LastIntent(s, s.Members[1]));
            Assert.Equal(BehaviorNames.Suppress, LastIntent(s, s.Members[0]));      // A, down, now covers B

            s.Teleport(s.Members[1], SquadDrillTrace.Of(s.Repo, s.Commander).Orders[1].Point);
            s.Tick(); s.Tick();
            Assert.Equal(FireAndMovementManeuver.PhaseCover1, s.Phase);              // both set: the halves swap
        }

        /// <summary>⭐ F-b — no cover ahead and no covering fire: after <c>CoveringSeconds</c> the movers go anyway (traced Timeout), each
        /// one rush (≤ RushMetres) straight toward the objective, and the trace says that point is NOT cover. 🔴 Red before: as above.</summary>
        [Fact]
        public void CE2158_FireAndMovement_NoCoverAhead_NoFire_AShortRushTowardTheObjective_NotCover()
        {
            var s = FmSquad();
            s.Order(FmOrder, BehaviorNames.FireAndMovement);
            s.Tick(); s.Tick();
            AnswerAll(s, FmQuery(s), new Vector3(0, -5, 0));                          // only cover behind
            Seconds(s, 4);                                                            // > CoveringSeconds (3 s)
            Assert.Equal(FireAndMovementManeuver.PhaseRush0, s.Phase);
            var trace = SquadDrillTrace.Of(s.Repo, s.Commander);
            Assert.Equal(SquadDrillTrace.EventTimeout, trace.History[^1].Event);
            var rush = trace.Orders[0].Point;
            Assert.False(trace.Orders[0].Cover);
            Assert.InRange(rush.Length(), 14.9f, 15.01f);                             // from (0,0): one rush
            Assert.True(rush.Y > 14f);                                                // toward the objective
        }

        /// <summary>⭐ F-b — both halves within <c>ArrivalRadius</c> of the objective ⇒ Success. 🔴 Red before: as above.</summary>
        [Fact]
        public void CE2158_FireAndMovement_BothHalvesNearTheObjective_Success()
        {
            var s = FmRushing();
            for (int i = 0; i < 4; i++) s.Teleport(s.Members[i], new Vector3(i * 3f, 65f, 0f));
            s.Tick(); s.Tick();
            Assert.True(s.Finished(out var result));
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }

        // ── CE-2159 X-a…X-c — the squad danger crossing (Squad Tactics §7.7.6) ──────────────────────────────────────────────────

        private static readonly Vector3 NearSide = new(10f, 20f, 0f), FarSide = new(10f, 40f, 0f);

        /// <summary>The commander's danger-area sensor (what the march owns live), holding one area: a 20 m strip crossed northward.</summary>
        private static Entity AreaAhead(Squad s, float distanceAlongRoute = 10f, Vector3? near = null, Vector3? far = null,
                                        Fdp.Toolkit.Squad.DangerArea.DangerAreaKind kind = Fdp.Toolkit.Squad.DangerArea.DangerAreaKind.StreetCrossing,
                                        float threat = 0f)
        {
            var r = s.Repo;
            if (!r.IsComponentTypeRegistered<Fdp.Toolkit.Perception.Components.SensorTag>()) r.RegisterComponent<Fdp.Toolkit.Perception.Components.SensorTag>();
            if (!r.IsComponentTypeRegistered<Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor>()) r.RegisterComponent<Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor>();
            if (!r.IsComponentTypeRegistered<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>()) r.RegisterComponent<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>();
            // the unit's sensor of the kind, whoever holds it (live: the paused march's run) — here held by the unit itself, so the
            // order that starts the drill does not end it with the previous run
            var child = Fdp.Toolkit.Perception.Sensors.UnitSensors.Of(r, s.Commander, Fdp.Toolkit.Perception.Components.SensorModality.DangerArea);
            if (child.IsNull)
            {
                child = Fdp.Toolkit.Squad.DangerArea.DangerAreaChildSensor.Ensure(r, s.Commander, SquadMarchPlay.DangerSensorSite,
                    Fdp.Toolkit.Squad.DangerArea.DangerAreaSettings.ToPoint(new Vector3(10f, 100f, 0f)));
                r.FlushCommandBuffers();
                if (r.HasComponent<BehaviorOwnedPart>(child)) r.RemoveComponent<BehaviorOwnedPart>(child);
            }
            ref var buffer = ref r.GetComponentRW<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>(child);
            buffer.Count = 1;
            buffer.LastUpdateTick = 1;
            buffer.GetSpanRW()[0] = new Fdp.Toolkit.Squad.DangerArea.DangerAreaDescriptor
            {
                FeatureId = 7, Kind = kind, ThreatRating = threat, Center = new Vector3(10f, 30f, 0f),
                ExtentsXY = new Vector2(30f, 10f), NearSideHandle = near ?? NearSide, FarSideHandle = far ?? FarSide,
                DistanceAlongRoute = distanceAlongRoute,
            };
            return child;
        }

        private static Squad Crossing(int members = 6)
        {
            var s = new Squad(members);
            AreaAhead(s);
            s.Orders.Clear();
            s.Order("{}", BehaviorNames.SquadDangerCrossing);
            s.Tick(); s.Tick();
            return s;
        }

        /// <summary>⭐⭐ <c>CE-2159</c> D3 (§7.7.7) — the leader decides HOW to cross: a street is crossed by the drill (above); OPEN
        /// GROUND by bounding overwatch to beyond the far side. 🔴 Red before: every area ran the crossing drill (no choice).</summary>
        [Fact]
        public void CE2159_D3_OpenGround_IsCrossedByBoundingOverwatch_ToBeyondTheFarSide()
        {
            var s = new Squad(6);
            AreaAhead(s, kind: Fdp.Toolkit.Squad.DangerArea.DangerAreaKind.OpenGround);
            s.Order("{}", BehaviorNames.SquadDangerCrossing);
            s.Tick(); s.Tick();
            Assert.Equal(BoundingOverwatchManeuver.ManeuverKind, s.Kind);
            Assert.Equal((uint)7, s.Repo.GetComponentRO<SquadCognitiveState>(s.Commander).ActiveFeatureId);   // the area it decided on
            var movers = s.LastOrders().Where(o => o.Value.Intent == BehaviorNames.AdvanceTo).ToList();
            Assert.NotEmpty(movers);
            foreach (var m in movers) Assert.True(m.Value.Objective.Y > s.At(m.Key).Y, "the bound goes toward the far side");
        }

        /// <summary>⭐ <c>CE-2159</c> D3 — a threatened area with the squad low on ammunition: HOLD, then decide again once it has
        /// quietened; a street then gets the crossing drill. 🔴 Red before: as above.</summary>
        [Fact]
        public void CE2159_D3_AThreatenedArea_LowOnAmmo_Holds_ThenDecidesAgain()
        {
            var s = new Squad(6);
            var sensor = AreaAhead(s, threat: 1f);
            if (!s.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.WeaponState>()) s.Repo.RegisterComponent<Fdp.Toolkit.Combat.Components.WeaponState>();
            foreach (var m in s.Members)
            {
                var low = new Fdp.Toolkit.Combat.Components.WeaponState { MaxAmmo = 30, Ammo = 3 };
                if (s.Repo.HasComponent<Fdp.Toolkit.Combat.Components.WeaponState>(m)) s.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.WeaponState>(m) = low;
                else s.Repo.AddComponent(m, low);
            }
            s.Order("{}", BehaviorNames.SquadDangerCrossing);
            s.Tick(); s.Tick();
            Assert.Equal(SquadHoldManeuver.ManeuverKind, s.Kind);

            s.Repo.GetComponentRW<Fdp.Toolkit.Squad.DangerArea.DangerAreaCognitiveBuffer>(sensor).GetSpanRW()[0].ThreatRating = 0f;
            Seconds(s, SquadDangerCrossingBehavior.HoldSeconds + 1);
            Assert.Equal(DangerAreaCrossingManeuver.ManeuverKind, s.Kind);
        }

        private static void Arrive(Squad s, params int[] members)
        {
            var at = s.LastObjectives();
            foreach (int i in members) s.Teleport(s.Members[i], at[s.Members[i]]);
            s.Tick(); s.Tick(); s.Tick();   // the phase moves · the next pair is picked · its orders are read off the bus
        }

        /// <summary>⭐ X-b — the drill starts on the commander's area: security (the back half) WATCHES beside the near side, the
        /// crossers watch short of it; nobody crosses before security is set. 🔴 Red before: no play ran the crossing table.</summary>
        [Fact]
        public void CE2159_DangerCrossing_SecurityWatchesTheNearSide_BeforeAnyoneCrosses()
        {
            var s = Crossing();
            Assert.Equal(DangerAreaCrossingManeuver.ManeuverKind, s.Kind);
            Assert.Equal(DangerAreaCrossingManeuver.PhaseSetSecurity, s.Phase);
            var orders = s.LastOrders();
            foreach (var m in s.Members) Assert.Equal(BehaviorNames.CombatPosture, orders[m].Intent);     // Watch = hold at a point
            foreach (var m in s.Members.Skip(3))
            {
                Assert.True(orders[m].Objective.Y < NearSide.Y, "security stands short of the near side");
                Assert.True(MathF.Abs(orders[m].Objective.X - NearSide.X) >= 4f, "beside the crossing point, watching along the area");
            }
            foreach (var m in s.Members.Take(3)) Assert.True(orders[m].Objective.Y < NearSide.Y);
            Assert.Equal(BehaviorNames.SquadDangerCrossing, s.TaskName);
        }

        /// <summary>⭐ X-b (D1) — once security is set the crossers go ONE PAIR AT A TIME, each pair on the next lane (≥ 4 m apart), and
        /// a pair is across when it is past the far handle. 🔴 Red before: as above.</summary>
        [Fact]
        public void CE2159_DangerCrossing_PairsCrossOneAtATime_OnNewLanes()
        {
            var s = Crossing();
            Arrive(s, 3, 4, 5);                                                        // security set
            Assert.Equal(DangerAreaCrossingManeuver.PhaseCrossElement, s.Phase);
            var orders = s.LastOrders();
            Assert.Equal(BehaviorNames.AdvanceTo, orders[s.Members[0]].Intent);
            Assert.Equal(BehaviorNames.AdvanceTo, orders[s.Members[1]].Intent);
            Assert.Equal(BehaviorNames.CombatPosture, orders[s.Members[2]].Intent);   // the third waits
            var lane0 = orders[s.Members[0]].Objective;
            Assert.True(lane0.Y > FarSide.Y, "the pair crosses to beyond the far side");

            Arrive(s, 0, 1);                                                           // the first pair is across
            orders = s.LastOrders();
            Assert.Equal(BehaviorNames.AdvanceTo, orders[s.Members[2]].Intent);
            Assert.True(MathF.Abs(orders[s.Members[2]].Objective.X - lane0.X) >= 4f, "the next pair takes the next lane");
            Assert.Equal(BehaviorNames.CombatPosture, orders[s.Members[0]].Intent);   // across: they watch from the far side
            Assert.Equal(DangerAreaCrossingManeuver.PhaseCrossElement, s.Phase);
        }

        /// <summary>⭐ X-b (D2) — the whole drill: the far side is SET before security leaves, security crosses last by pairs, then the
        /// squad reforms beyond and the drill succeeds; the trace carries each phase's cause. 🔴 Red before: as above.</summary>
        [Fact]
        public void CE2159_DangerCrossing_TheFarSideSetsBeforeSecurityCollapses_ThenTheSquadReforms()
        {
            var s = Crossing();
            Arrive(s, 3, 4, 5);
            Arrive(s, 0, 1);
            Arrive(s, 2);
            Assert.Equal(DangerAreaCrossingManeuver.PhaseFarSideCover, s.Phase);
            var orders = s.LastOrders();
            foreach (var m in s.Members.Skip(3)) Assert.Equal(BehaviorNames.CombatPosture, orders[m].Intent);   // security still watches
            Arrive(s, 0, 1, 2);                                                        // the far side is set
            Assert.Equal(DangerAreaCrossingManeuver.PhaseCollapseSecurity, s.Phase);
            orders = s.LastOrders();
            Assert.Equal(BehaviorNames.AdvanceTo, orders[s.Members[3]].Intent);
            Assert.Equal(BehaviorNames.AdvanceTo, orders[s.Members[4]].Intent);
            Assert.Equal(BehaviorNames.CombatPosture, orders[s.Members[5]].Intent);
            Arrive(s, 3, 4);
            Arrive(s, 5);
            Assert.Equal(DangerAreaCrossingManeuver.PhaseReform, s.Phase);
            var reform = s.LastObjectives();
            foreach (var m in s.Members) s.Teleport(m, reform[m]);                     // reformed beyond the far side
            bool finished = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 3 && !finished; i++) { s.Tick(); finished = s.Finished(out result); }
            Assert.True(finished);
            Assert.Equal(Fbt.NodeStatus.Success, result);
            var t = SquadDrillTrace.Of(s.Repo, s.Commander);
            var events = t.History.Skip(1).Select(h => (Fdp.Toolkit.Squad.Primitives.PhaseEventKind)h.Event).ToArray();
            Assert.Equal(new[]
            {
                Fdp.Toolkit.Squad.Primitives.PhaseEventKind.DefiladeReached, Fdp.Toolkit.Squad.Primitives.PhaseEventKind.FarSideReached,
                Fdp.Toolkit.Squad.Primitives.PhaseEventKind.DefiladeReached, Fdp.Toolkit.Squad.Primitives.PhaseEventKind.FarSideReached,
            }, events.Take(4).ToArray());
        }

        /// <summary>⭐ X-b — no area on the commander's sensor ⇒ nothing to cross: the drill fails at once (the SOP row never asks then).</summary>
        [Fact]
        public void CE2159_DangerCrossing_WithNoAreaAhead_Fails()
        {
            var s = new Squad(4);
            s.Order("{}", BehaviorNames.SquadDangerCrossing);
            s.Tick(); s.Tick();
            Assert.True(s.Finished(out var result));
            Assert.Equal(Fbt.NodeStatus.Failure, result);
        }

        /// <summary>⭐ X-c — the SOP row's condition reads STATE: an area within <c>WithinMetres</c> along the route, and the squad not yet
        /// past its far side (a buffer not refreshed after a crossing must not start it again). 🔴 Red before: no such condition.</summary>
        [Fact]
        public void CE2159_DangerAreaAhead_IsNearAndNotYetCrossed()
        {
            var s = new Squad(4);
            s.Repo.AddComponent(s.Commander, new SimTransform { Position = new Vector3(10f, 5f, 0f), Rotation = Quaternion.Identity });
            var p = new SopAreaParams { WithinMetres = 30f };
            Assert.False(SquadSopConditions.DangerAreaAhead(ref p, s.Commander, s.Repo));             // no sensor
            AreaAhead(s, distanceAlongRoute: 50f);
            Assert.False(SquadSopConditions.DangerAreaAhead(ref p, s.Commander, s.Repo));             // too far along the route
            AreaAhead(s, distanceAlongRoute: 15f);
            Assert.True(SquadDangerAhead(s, ref p));
            s.Repo.GetComponentRW<SimTransform>(s.Commander).Position = new Vector3(10f, 45f, 0f);    // past the far side
            Assert.False(SquadSopConditions.DangerAreaAhead(ref p, s.Commander, s.Repo));
        }

        private static bool SquadDangerAhead(Squad s, ref SopAreaParams p) => SquadSopConditions.DangerAreaAhead(ref p, s.Commander, s.Repo);

        /// <summary>⭐ X-a — a march told to watch for danger areas owns the commander's route sensor (to its objective); one not told
        /// does not. 🔴 Red before: no squad behaviour spawned a danger-area sensor.</summary>
        [Fact]
        public void CE2159_TheMarch_OwnsTheCommandersRouteSensor_WhenAsked()
        {
            var s = new Squad(4);
            s.Order("{\"Objective\":[0,100,0]}", BehaviorNames.SquadMarch);
            s.Tick(); s.Tick();
            Assert.True(Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(s.Repo, s.Commander, SquadMarchPlay.DangerSensorSite).IsNull);

            var w = new Squad(4);
            w.Order("{\"Objective\":[0,100,0],\"DangerAreas\":true}", BehaviorNames.SquadMarch);
            w.Tick(); w.Tick(); w.Repo.FlushCommandBuffers();
            var sensor = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(w.Repo, w.Commander, SquadMarchPlay.DangerSensorSite);
            Assert.False(sensor.IsNull);
            var settings = w.Repo.GetComponentRO<Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor>(sensor).Settings;
            Assert.Equal(Fdp.Toolkit.Squad.DangerArea.DangerRouteSource.ToPoint, settings.RouteSource);
            Assert.Equal(new Vector3(0f, 100f, 0f), settings.RoutePoint);
        }

        /// <summary>⭐ X-c (D4, D6) — the shipped squad SOP: a danger area ahead of a marching squad starts the crossing as a reaction
        /// (<c>Alert</c>) that PAUSES the march; a contact during it is more urgent and replaces it with <c>ReactToContact</c>.
        /// 🔴 Red before: the SOP had no danger-area row.</summary>
        [Fact]
        public void CE2159_BasicSquadSop_ADangerAreaAhead_CrossesIt_AndAContactReplacesTheCrossing()
        {
            var s = new Squad(members: 6);
            s.Repo.AddComponent(s.Commander, new SimTransform { Position = new Vector3(10f, 5f, 0f), Rotation = Quaternion.Identity });
            s.Order("{\"Objective\":[10,100,0]}", BehaviorNames.SquadMarch);
            s.StartSop("BasicSquadSop");
            for (int i = 0; i < 10; i++) s.Tick();
            Assert.Equal(BehaviorNames.SquadMarch, s.TaskName);

            AreaAhead(s, distanceAlongRoute: 12f);
            for (int i = 0; i < 6; i++) s.Tick();
            Assert.Equal(BehaviorNames.SquadDangerCrossing, s.TaskName);
            var task = s.Repo.GetComponent<BehaviorState>(s.Commander);
            Assert.Equal(BehaviorOrigin.Reaction, task.Origin);
            Assert.Equal(ReactionUrgency.Alert, task.Urgency);
            Assert.Equal(BehaviorNames.SquadMarch, BehaviorIngressSystem.PausedTaskOf(s.Repo, s.Commander)!.BehaviorName);
            Assert.Equal(DangerAreaCrossingManeuver.ManeuverKind, s.Kind);
            Assert.Equal(0, s.Repo.GetComponent<SopState>(s.Commander).SopFaulted);

            var enemy = Enemy(s, new Vector3(10f, 60f, 0f));
            s.Pool(enemy, new Vector3(10f, 60f, 0f), reporters: 0b0011);
            s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.SquadContact);
            for (int i = 0; i < 4; i++) s.Tick();
            Assert.Equal(BehaviorNames.ReactToContact, s.TaskName);
        }
    }
}
