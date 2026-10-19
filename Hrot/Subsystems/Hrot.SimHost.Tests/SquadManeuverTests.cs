using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.RegularExpressions;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.Modules.Geographic;
using Fdp.Modules.Geographic.Transforms;
using Fdp.Toolkit.Navigation;
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
            private readonly Fdp.Toolkit.Behavior.Systems.EngagementAreaSystem _sectors = new();   // ⭐ CE-2302 — the runner's arcs land
            private readonly Fdp.Toolkit.Behavior.Systems.RoeSystem _roe = new();   // ⭐ CE-2275 DA-35 — a squad's own hold-fire lift lands
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
                _sectors.Execute(Repo, 0.016f);
                _roe.Execute(Repo, 0.016f);
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

        /// <summary>
        /// ⭐ <c>CE-2173</c> slice 3c (Air Operations §9j) — <c>SquadRecover</c>: a downed pilot who is NOT of the squad is brought to the
        /// named point (the LZ — no cover query) and KEPT on his carrier: the medic's <c>CarryCasualty</c> says <c>keep</c>, and the drill
        /// is done when the medic carries him there (not when he lies there). 🔴 Red-proof: the outsider branch off ⇒ the play fails at
        /// start (no member is down); <c>Keep</c> off ⇒ carried there is not done.
        /// </summary>
        [Fact]
        public void CE2173_SquadRecover_ADownedPilotNotOfTheSquad_IsCarriedToTheLz_AndKept()
        {
            var s = new Squad(members: 4);
            foreach (var m in s.Members)
                s.Repo.AddComponent(m, new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 100, MaxAmmo = 100, MagazineSize = 30, MagazineRounds = 30 });
            var medic = s.Members[2];
            s.Repo.GetComponentRW<Fdp.Core.CommandHierarchy.UnitSubordinate>(medic).Designation = Fdp.Core.CommandHierarchy.TacticalDesignation.Medic;
            var pilot = s.Repo.CreateEntity();
            s.Repo.AddComponent(pilot, new SimTransform { Position = new Vector3(20, 20, 0), Rotation = Quaternion.Identity });
            s.Repo.AddComponent(pilot, new Fdp.Toolkit.Combat.Components.Health { Current = 20f, Max = 100f });
            s.Repo.AddComponent(pilot, new Fdp.Toolkit.Combat.Components.InjuryThresholds { WoundedBelow = 0.6f, IncapacitatedBelow = 0.3f });
            s.Repo.AddComponent(pilot, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 777 });
            var map = new Fdp.Toolkit.Replication.Services.NetworkEntityMap();
            map.Register(777, pilot);
            s.Repo.SetSingletonManaged(map);
            var lz = new Vector3(50, 0, 0);
            s.Order("{\"casualty\":777,\"to\":[50,0,0]}", BehaviorNames.SquadRecover);
            s.Tick(); s.Tick();
            Assert.Equal(CasualtyCareManeuver.ManeuverKind, s.Kind);
            Assert.True(CoverQuery(s).IsNull, "a named point: no cover query");
            for (int i = 0; i < 370; i++) s.Tick();                               // nobody to suppress: the medic goes after CoveringSeconds (5 s)
            Assert.Equal(CasualtyCareManeuver.PhaseGo, s.Phase);
            var carry = s.Orders.Last(o => o.Entity.Equals(medic));
            Assert.Equal(BehaviorNames.CarryCasualty, carry.IntentId);
            var dto = System.Text.Json.JsonSerializer.Deserialize<CarryCasualtyParamsJsonDto>(carry.JsonParams, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
            Assert.True(dto.Keep, "he keeps him");
            Assert.Equal((lz.X, lz.Y), (dto.X, dto.Y));
            Assert.Equal(pilot, SquadDrillTrace.Of(s.Repo, s.Commander).Orders[2].Target);

            s.Repo.AddComponent(pilot, new Fdp.Toolkit.Behavior.Components.IsEmbarkedTag { VehicleEntity = medic });   // he has him
            s.Tick(); s.Tick();
            Assert.False(s.Finished(out _), "carried, but not there yet");
            s.Teleport(medic, lz + new Vector3(1, 0, 0));
            s.Tick(); s.Tick();
            Assert.True(s.Finished(out var result));
            Assert.Equal(Fbt.NodeStatus.Success, result);
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
            for (int i = 0; i < 250; i++) s.Tick();                               // 4 s: the covering may still come (CE-2227 — 5 s)
            Assert.Equal(CasualtyCareManeuver.PhaseSuppress, s.Phase);
            for (int i = 0; i < 120; i++) s.Tick();                               // 6 s > CoveringSeconds (5 s)
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

        /// <summary>⭐ <c>CE-2281</c> DA-19 — the squad's MACHINE GUNNER goes to the base of fire whoever saw the enemy: members 2 and 3
        /// report him, member 1 carries the MG ⇒ he holds and fires with them, and only member 0 flanks. ✅ Red-proof: the role pass
        /// removed ⇒ member 1 flanks.</summary>
        [Fact]
        public void CE2281_SuppressAndManeuver_TheMachineGunnerJoinsTheBaseOfFire()
        {
            var s = new Squad(members: 4);
            ref var sub = ref s.Repo.GetComponentRW<UnitSubordinate>(s.Members[1]);
            sub.Designation = TacticalDesignation.MachineGunner;
            var enemy = Enemy(s, new Vector3(0f, 60f, 0f));
            s.Pool(enemy, new Vector3(0f, 60f, 0f), reporters: 0b1100);
            s.Order("{}", BehaviorNames.SuppressAndManeuver);
            s.Tick(); s.Tick();
            s.Orders.Clear();
            s.Answer(s.FlankQuery(), new Vector3(40f, 60f, 0f));
            s.Tick(); s.Tick();
            var flank = s.LastOrders();
            Assert.Equal((BehaviorNames.AdvanceTo, new Vector3(40f, 60f, 0f)), flank[s.Members[0]]);   // the one rifleman who did not see him
            Assert.Equal((BehaviorNames.CombatPosture, s.At(s.Members[1])), flank[s.Members[1]]);     // the MG holds with the base
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
            // ⭐ CE-2297 slice 2a (R-2055) — a holder STAYS: arriving at his own spot does not end his posture
            Assert.All(s.Orders.Where(o => o.IntentId == BehaviorNames.CombatPosture), o => Assert.True(
                System.Text.Json.JsonSerializer.Deserialize<CombatPostureParamsJsonDto>(o.JsonParams, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed).Stay));

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

        /// <summary>⭐ <c>CE-2276</c> E-3a (DA-68) — <c>BreakContact {To}</c>: told where to go, the squad asks no terrain and needs no contact;
        /// element 0 goes there while element 1 covers, then element 1; both there ⇒ Success. ✅ Red-proof: the <c>To</c> arm dropped ⇒
        /// no contact, so the play fails at once.</summary>
        [Fact]
        public void CE2276_E3a_BreakContactTo_GoesThere_ElementByElement_WithNoContactAndNoTerrainQuery()
        {
            var s = new Squad(members: 2);
            var to = new Vector3(20f, -60f, 0f);
            s.Order("{\"To\":[20,-60,0]}", BehaviorNames.BreakContact);
            s.Tick(); s.Tick(); s.Tick();
            Assert.True(s.RetreatQuery().IsNull, "no terrain asked");
            var first = s.LastOrders();
            Assert.Equal((BehaviorNames.AdvanceTo, to), first[s.Members[0]]);
            Assert.Equal(BehaviorNames.CombatPosture, first[s.Members[1]].Intent);
            s.Teleport(s.Members[0], to);
            s.Orders.Clear();
            s.Tick(); s.Tick();
            Assert.Equal((BehaviorNames.AdvanceTo, to), s.LastOrders()[s.Members[1]]);
            s.Teleport(s.Members[1], to);
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

        // ── CE-2262 SL-0 — the squad layer one echelon up: a platoon whose members are squads ─────────────────────────────

        /// <summary>Makes the platoon's member <paramref name="i"/> a SQUAD: a body-less unit with <paramref name="men"/> men of its own
        /// standing around it, each with <paramref name="health"/>.</summary>
        private static Entity[] MakeSquad(Squad platoon, int i, int men, float health)
        {
            var squad = platoon.Members[i];
            platoon.Repo.AddComponent(squad, new UnitEntity());
            var roster = new UnitRoster();
            var them = new Entity[men];
            var at = platoon.At(squad);
            for (int k = 0; k < men; k++)
            {
                var m = platoon.Repo.CreateEntity();
                platoon.Repo.AddComponent(m, new SimTransform { Position = at + new Vector3(k, 0f, 0f), Rotation = Quaternion.Identity });
                platoon.Repo.AddComponent(m, new Fdp.Toolkit.Combat.Components.Health { Current = health, Max = 100f });
                platoon.Repo.AddComponent(m, new UnitSubordinate { Commander = squad });
                UnitRoster.Add(ref roster, m);
                them[k] = m;
            }
            platoon.Repo.AddComponent(squad, roster);
            return them;
        }

        /// <summary>⭐⭐ <c>CE-2262</c> SL-0 (CB-16) — the first NESTED hierarchy run: a platoon commander marching three SQUADS orders each
        /// effective squad an ORDER (it decides how — <c>Order.Move</c> / <c>Order.Hold</c>, D3) and a wiped-out squad NOTHING. 🔴 Before:
        /// a squad has no Health, so <c>Live(i)</c> read the dead squad as in the fight and kept ordering it. ✅ Red-proof: <c>Live(i)</c>
        /// back to <c>Injury.IsInTheFight</c> ⇒ the dead squad gets an order.</summary>
        [Fact]
        public void CE2262_APlatoonMarchingItsSquads_OrdersEachEffectiveSquad_AndAWipedOutSquadNothing()
        {
            var p = new Squad(members: 3);
            MakeSquad(p, 0, men: 4, health: 100f);
            MakeSquad(p, 1, men: 4, health: 100f);
            MakeSquad(p, 2, men: 4, health: 0f);                    // every man dead
            Assert.False(CombatEffective.IsInTheFight(p.Repo, p.Members[2]));

            p.Order("{\"Objective\":[0,100,0],\"Shape\":1,\"MaxLagMetres\":50}", BehaviorNames.SquadMarch);
            Ticks(p, 3);
            Assert.Equal(SquadMarchManeuver.ManeuverKind, p.Kind);
            for (int i = 0; i < 2; i++)
            {
                var toSquad = p.Orders.Where(o => o.Entity.Equals(p.Members[i])).ToList();
                Assert.NotEmpty(toSquad);
                Assert.All(toSquad, o => Assert.StartsWith("Order.", o.IntentId));   // an order, never a soldier's behaviour
            }
            Assert.DoesNotContain(p.Orders, o => o.Entity.Equals(p.Members[2]));
        }

        /// <summary>⭐ <c>CE-2291</c> CC-A (R-2042) — the commander's picture is the UNIT's knowledge: a task switch and a reaction pausing the
        /// task (through the real ingress) leave it exactly as the staff wrote it — a guard against any future admit step that resets unit
        /// state.</summary>
        [Fact]
        public void CE2291_ThePicture_SurvivesATaskSwitch_AndAReaction()
        {
            var s = new Squad(members: 3);
            var picture = new Fdp.Toolkit.Squad.Command.CommandPicture();
            picture.Elements.Items[0] = new Fdp.Toolkit.Squad.Command.ReportedElement { At = new Vector3(10, 200, 0), Count = 3, FromNetId = 5002, Seq = 7 };
            picture.Elements.Count = 1;
            picture.Subordinates.Items[0] = new Fdp.Toolkit.Squad.Command.SubordinateRow { UnitNetId = 5002, Progress = Fdp.Toolkit.Squad.Command.ProgressKind.Set };
            picture.Subordinates.Count = 1;
            s.Repo.AddComponent(s.Commander, picture);

            s.Order("{\"DurationSeconds\":0}", BehaviorNames.SquadHold);
            Ticks(s, 2);
            s.Order("{\"Objective\":[0,100,0],\"Shape\":1,\"MaxLagMetres\":50}", BehaviorNames.SquadMarch);   // a task switch
            Ticks(s, 2);
            Assert.Equal(BehaviorNames.SquadMarch, s.TaskName);
            s.React(BehaviorNames.SquadHold);                                                                       // a reaction pauses it
            Ticks(s, 2);
            Assert.Equal(BehaviorNames.SquadHold, s.TaskName);

            var after = s.Repo.GetComponentRO<Fdp.Toolkit.Squad.Command.CommandPicture>(s.Commander);
            Assert.Equal(1, after.Elements.Count);
            Assert.Equal((5002L, 7u, (byte)3), (after.Elements.Items[0].FromNetId, after.Elements.Items[0].Seq, after.Elements.Items[0].Count));
            Assert.Equal(Fdp.Toolkit.Squad.Command.ProgressKind.Set, after.Subordinates.Items[0].Progress);
        }

        // ── CE-2292 CC-B — the command actions on a real commander ─────────────────────────────────────────────────────────

        /// <summary>⭐⭐ <c>CE-2292</c> CC-B — <c>OrderSubordinate</c> sends the squad in roster slot 1 an ABSTRACT order (<c>Order.Move</c>,
        /// the point geodetic), records it on the commander's run like the runner's own orders, and makes the picture forget that squad's
        /// last progress; the runner's re-send (the same helper) does NOT. ✅ Red-proof: the node's <c>newOrder: true</c> dropped ⇒ the
        /// progress stays Set.</summary>
        [Fact]
        public void CE2292_OrderSubordinate_SendsAnAbstractOrder_RecordsIt_AndResetsTheProgress()
        {
            var s = new Squad(members: 2);
            foreach (var m in s.Members) s.Repo.AddComponent(m, new UnitEntity());
            s.Order("{\"DurationSeconds\":0}", BehaviorNames.SquadHold);   // the commander's run that the orders belong to
            Ticks(s, 2);
            int row = Fdp.Toolkit.Squad.Command.CommandPictureOps.EnsureRow(s.Repo, s.Commander,
                Fdp.Toolkit.Squad.Command.CommandPictureOps.NetIdOf(s.Repo, s.Members[1]));
            s.Repo.GetComponentRW<Fdp.Toolkit.Squad.Command.CommandPicture>(s.Commander).Subordinates.Items[row].Progress
                = Fdp.Toolkit.Squad.Command.ProgressKind.Set;
            s.Orders.Clear();

            var p = new OrderSubordinateParams { Slot = 1, Order = Hrot.AI.Behaviors.Orders.OrderKind.Move, Point = new Vector3(30f, 40f, 0f), Speed = 2f };
            Assert.Equal(Fbt.NodeStatus.Success, CommandNodes.OrderSubordinate(ref p, s.Commander, s.Repo));
            s.Tick();
            var o = Assert.Single(s.Orders.Where(x => x.Entity.Equals(s.Members[1])));
            Assert.Equal("Order.Move", o.IntentId);
            var move = System.Text.Json.JsonSerializer.Deserialize<Hrot.AI.Behaviors.Orders.MoveOrder>(o.JsonParams, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
            var back = s.Repo.GetSingletonManaged<IGeographicTransform>().ToCartesian(move.Location.Latitude, move.Location.Longitude, 0.0);
            Assert.InRange(Vector2.Distance(new Vector2(back.X, back.Y), new Vector2(30f, 40f)), 0f, 0.1f);
            Assert.Contains(SubordinateOrders.Of(s.Repo, s.Commander)!.Orders, e => e.Member.Equals(s.Members[1]));
            var slot1 = new Fdp.Toolkit.Squad.Command.ProgressReachedParams { Slot = 1, Progress = Fdp.Toolkit.Squad.Command.ProgressKind.Set };
            Assert.False(Fdp.Toolkit.Squad.Command.PictureConditions.ProgressReached(ref slot1, s.Commander, s.Repo));

            s.Repo.GetComponentRW<Fdp.Toolkit.Squad.Command.CommandPicture>(s.Commander).Subordinates.Items[row].Progress
                = Fdp.Toolkit.Squad.Command.ProgressKind.Set;
            SquadManeuverRunner.Move(s.Repo, s.Commander, s.Members[1], new Vector3(30f, 40f, 0f), 2f, 5f);   // the runner's re-send
            Assert.True(Fdp.Toolkit.Squad.Command.PictureConditions.ProgressReached(ref slot1, s.Commander, s.Repo));

            var none = new OrderSubordinateParams { Slot = 5, Order = Hrot.AI.Behaviors.Orders.OrderKind.Hold };
            Assert.Equal(Fbt.NodeStatus.Failure, CommandNodes.OrderSubordinate(ref none, s.Commander, s.Repo));   // no such slot
        }

        /// <summary>⭐ <c>CE-2292</c> CC-B — <c>SetSubordinateReporting</c> reaches the subordinate's <c>ReportingRequirements</c> through
        /// its system (Superior); <c>SendReport</c> queues the play's milestone, due 3 s later; a SPOT is the staff's, not the play's.</summary>
        [Fact]
        public void CE2292_SetSubordinateReporting_AndSendReport()
        {
            var s = new Squad(members: 2);
            var p = new SetSubordinateReportingParams { Slot = 0, Pir = Fdp.Toolkit.Squad.Command.PirFlags.AnyElement, SitrepEverySeconds = 15f };
            Assert.Equal(Fbt.NodeStatus.Success, CommandNodes.SetSubordinateReporting(ref p, s.Commander, s.Repo));
            s.Repo.Bus.SwapBuffers();
            new Fdp.Toolkit.Squad.Command.ReportingRequirementsSystem().Execute(s.Repo, 0.016f);
            var req = s.Repo.GetComponentRO<Fdp.Toolkit.Squad.Command.ReportingRequirements>(s.Members[0]);
            Assert.Equal((Fdp.Toolkit.Squad.Command.PirFlags.AnyElement, 15f, BehaviorOrigin.Superior), (req.Pir, req.SitrepEverySeconds, req.SetBy));

            double now = s.Repo.GetSingletonUnmanaged<GlobalTime>().TotalTime;
            var phase = new SendReportParams { Kind = Fdp.Toolkit.Squad.Command.ReportKind.Progress, Subtype = (byte)Fdp.Toolkit.Squad.Command.ProgressKind.PhaseLine, Count = 2 };
            Assert.Equal(Fbt.NodeStatus.Success, CommandNodes.SendReport(ref phase, s.Commander, s.Repo));
            var q = s.Repo.GetComponentRO<Fdp.Toolkit.Squad.Command.ReportOutbox>(s.Commander).Queued;
            Assert.Equal(1, q.Count);
            Assert.Equal((Fdp.Toolkit.Squad.Command.ReportKind.Progress, (byte)2, 1u), (q.Items[0].Report.Kind, q.Items[0].Report.Count, q.Items[0].Report.Seq));
            Assert.Equal((float)now + CommandNodes.ReportDelaySeconds, q.Items[0].DueAt, 3);
            var spot = new SendReportParams { Kind = Fdp.Toolkit.Squad.Command.ReportKind.Spot };
            Assert.Equal(Fbt.NodeStatus.Failure, CommandNodes.SendReport(ref spot, s.Commander, s.Repo));
        }

        // ── CE-2293 CC-C — the staff: a squad reports to its platoon, on one node (the BR-3 event, looped back) ──────────────

        /// <summary>The squad <paramref name="s"/> under a body-less platoon commander; the staff's two systems, ticked with it.</summary>
        private sealed class Chain
        {
            public readonly Squad S;
            public readonly Entity Platoon;
            public readonly Fdp.Toolkit.Squad.Command.StaffComposerSystem Composer = new();
            public readonly Fdp.Toolkit.Squad.Command.StaffInboxSystem Inbox = new();
            public readonly List<Fdp.Toolkit.Perception.Events.SensorChange> PlatoonSensed = new();

            public Chain(int members)
            {
                S = new Squad(members);
                Platoon = S.Repo.CreateEntity();
                S.Repo.AddComponent(Platoon, new UnitEntity());
                var roster = new UnitRoster();
                UnitRoster.Add(ref roster, S.Commander);
                S.Repo.AddComponent(Platoon, roster);
                S.Repo.AddComponent(S.Commander, new UnitSubordinate { Commander = Platoon });
            }

            /// <summary>One frame: the squad's own systems, then the staff (as the pack orders them), the bus swapped between.</summary>
            public void Tick()
            {
                S.Tick();
                Composer.Execute(S.Repo, 0.016f);
                Inbox.Execute(S.Repo, 0.016f);
                foreach (var e in S.Repo.Bus.Read<Fdp.Toolkit.Perception.Events.SensorChangedEvent>())
                    if (e.Unit.Equals(Platoon)) PlatoonSensed.Add(e.What);
            }

            public void Ticks(int n) { for (int i = 0; i < n; i++) Tick(); }

            public Fdp.Toolkit.Squad.Command.CommandPicture Picture
                => S.Repo.HasComponent<Fdp.Toolkit.Squad.Command.CommandPicture>(Platoon)
                    ? S.Repo.GetComponentRO<Fdp.Toolkit.Squad.Command.CommandPicture>(Platoon) : default;

            public Fdp.Toolkit.Squad.Command.ProgressKind SquadProgress
                => Fdp.Toolkit.Squad.Command.CommandPictureOps.ProgressOf(S.Repo, Platoon, 0);
        }

        /// <summary>⭐⭐ <c>CE-2293</c> CC-C (CC-5, cc-platoon-bound ①) — the squad's TASK ends and its staff, with no play changing, reports
        /// PROGRESS Set: 3 s later (not before) the platoon's picture shows it, the platoon senses <c>ReportReceived</c>, and its
        /// <c>ProgressReached</c> guard turns true. ✅ Red-proof: the composer's BehaviorFinishedEvent read removed ⇒ nothing arrives.</summary>
        [Fact]
        public void CE2293_ATaskThatEnds_ReportsSet_ThreeSecondsLater_IntoThePlatoonsPicture()
        {
            var c = new Chain(members: 3);
            c.S.Order("{\"DurationSeconds\":0.5}", BehaviorNames.SquadHold);
            bool done = false;
            for (int i = 0; i < 80 && !done; i++) { c.Tick(); done = c.S.Finished(out _); }
            Assert.True(done);
            c.Ticks(2);
            Assert.Equal(Fdp.Toolkit.Squad.Command.ProgressKind.NoProgress, c.SquadProgress);   // held for the delay
            c.Ticks(60);                                                                        // ~1 s more: still held
            Assert.Equal(Fdp.Toolkit.Squad.Command.ProgressKind.NoProgress, c.SquadProgress);
            c.S.Pause(2.5);
            c.Ticks(3);
            Assert.Equal(Fdp.Toolkit.Squad.Command.ProgressKind.Set, c.SquadProgress);
            Assert.Contains(Fdp.Toolkit.Perception.Events.SensorChange.ReportReceived, c.PlatoonSensed);
            var set = new Fdp.Toolkit.Squad.Command.ProgressReachedParams { Slot = 0, Progress = Fdp.Toolkit.Squad.Command.ProgressKind.Set };
            Assert.True(Fdp.Toolkit.Squad.Command.PictureConditions.ProgressReached(ref set, c.Platoon, c.S.Repo));
            Assert.Equal(1, c.Composer.SentCount);
        }

        /// <summary>⭐⭐ <c>CE-2293</c> CC-C (CC-7, cc-platoon-bound ③) — three contacts in the squad's pool are ONE element: one SPOT (size
        /// Team, count 3), not one per contact; the element moving 10 m sends nothing new, 40 m sends an update that REPLACES it; the pool
        /// emptying sends a lost SPOT that removes it. ✅ Red-proof: the size/move check removed ⇒ a SPOT every frame.</summary>
        [Fact]
        public void CE2293_ThePoolIsOneElement_OneSpot_MovedOnlyWhenItMoves_LostWhenItEmpties()
        {
            var c = new Chain(members: 3);
            c.S.PoolMany(3, new Vector3(0f, 200f, 0f));
            c.Ticks(2);
            c.S.Pause(3.1);
            c.Ticks(2);
            var pic = c.Picture;
            Assert.Equal(1, pic.Elements.Count);
            Assert.Equal((Fdp.Toolkit.Squad.Command.ElementSize.Team, (byte)3), (pic.Elements.Items[0].Size, pic.Elements.Items[0].Count));
            int sent = c.Composer.SentCount;

            MoveAll(c.S, new Vector3(0f, 210f, 0f));
            c.Ticks(2); c.S.Pause(3.1); c.Ticks(2);
            Assert.Equal(sent, c.Composer.SentCount);                                   // 10 m: not news

            MoveAll(c.S, new Vector3(0f, 250f, 0f));
            c.Ticks(2); c.S.Pause(3.1); c.Ticks(2);
            pic = c.Picture;
            Assert.Equal(1, pic.Elements.Count);                                         // the update replaced it
            Assert.InRange(pic.Elements.Items[0].At.Y, 249f, 251f);

            c.S.ClearPool();
            c.Ticks(2); c.S.Pause(3.1); c.Ticks(2);
            Assert.Equal(0, c.Picture.Elements.Count);                                   // lost
        }

        private static void MoveAll(Squad s, Vector3 at)
        {
            ref var squad = ref s.Repo.GetComponentRW<SquadCognitiveState>(s.Commander);
            for (int i = 0; i < squad.Contacts.Count; i++)
            {
                var c = squad.Contacts.Contacts[i];
                c.PositionX = at.X; c.PositionY = at.Y; c.PositionZ = at.Z;
                squad.Contacts.Contacts[i] = c;
            }
        }

        /// <summary>⭐ <c>CE-2293</c> CC-C — a SITREP at the superior's requested rate (none without a request), its strength a colour from
        /// the men in the fight; lost comms (mute) and a Silent posture hold the outbox, CONTACT excepted under Silent.</summary>
        [Fact]
        public void CE2293_SitrepAtTheRequestedRate_MuteAndSilentHoldTheOutbox()
        {
            var c = new Chain(members: 4);
            c.Ticks(2); c.S.Pause(3.1); c.Ticks(2);
            Assert.Equal(0, c.Composer.SentCount);                                       // no request ⇒ no routine SITREP
            c.S.Repo.AddComponent(c.S.Commander, new Fdp.Toolkit.Squad.Command.ReportingRequirements { SitrepEverySeconds = 15f });
            c.Ticks(2); c.S.Pause(3.1); c.Ticks(2);
            Assert.Equal(1, c.Composer.SentCount);
            Assert.Equal(Fdp.Toolkit.Squad.Command.StatusColour.Green, c.Picture.Subordinates.Items[0].Strength);
            c.S.Pause(5); c.Ticks(2);
            Assert.Equal(1, c.Composer.SentCount);                                       // not yet 15 s

            ref var outbox = ref c.S.Repo.GetComponentRW<Fdp.Toolkit.Squad.Command.ReportOutbox>(c.S.Commander);
            outbox.MutedUntil = (float)c.S.Repo.GetSingletonUnmanaged<GlobalTime>().TotalTime + 60f;
            c.S.Pause(11); c.Ticks(2); c.S.Pause(3.1); c.Ticks(2);
            Assert.Equal(1, c.Composer.SentCount);                                       // muted: queued, not sent
            Assert.True(c.S.Repo.GetComponentRO<Fdp.Toolkit.Squad.Command.ReportOutbox>(c.S.Commander).Queued.Count > 0);
        }

        /// <summary>⭐ <c>CE-2293</c> CC-C (CB-32, CC-14) — the inbox keeps the newest per sender: a late OLDER report is dropped, a
        /// Synthetic one (the HTTP fake) is always taken.</summary>
        [Fact]
        public void CE2293_TheInbox_DropsALateOlderReport_TakesASyntheticOne()
        {
            var c = new Chain(members: 2);
            float now = 10f;
            Fdp.Toolkit.Squad.Command.CommandReport R(Fdp.Toolkit.Squad.Command.ProgressKind p, uint seq, byte synthetic = 0)
                => new() { Kind = Fdp.Toolkit.Squad.Command.ReportKind.Progress, Subtype = (byte)p, Seq = seq, Synthetic = synthetic };
            Assert.True(Fdp.Toolkit.Squad.Command.StaffInboxSystem.Fold(c.S.Repo, c.S.Commander, c.Platoon, R(Fdp.Toolkit.Squad.Command.ProgressKind.Foothold, 5), now));
            Assert.False(Fdp.Toolkit.Squad.Command.StaffInboxSystem.Fold(c.S.Repo, c.S.Commander, c.Platoon, R(Fdp.Toolkit.Squad.Command.ProgressKind.Set, 4), now));
            Assert.Equal(Fdp.Toolkit.Squad.Command.ProgressKind.Foothold, c.SquadProgress);
            Assert.True(Fdp.Toolkit.Squad.Command.StaffInboxSystem.Fold(c.S.Repo, c.S.Commander, c.Platoon, R(Fdp.Toolkit.Squad.Command.ProgressKind.Set, 0, synthetic: 1), now));
            Assert.Equal(Fdp.Toolkit.Squad.Command.ProgressKind.Set, c.SquadProgress);
        }

        // ── CE-2295 CC-E — faking reports and cutting the radio (the service the debug HTTP calls) ──────────────────────────

        /// <summary>⭐⭐ <c>CE-2295</c> CC-E (CC-13, CC-14) — a SPOT faked as the squad's (<c>ReportInjection.Inject</c>) reaches the platoon's
        /// picture marked synthetic, aged from its <c>observedAgoSeconds</c>, logged; the squad's next REAL SPOT of the same element replaces
        /// it. ✅ Red-proof: the inbox dropping a <c>Seq</c>-0 synthetic as stale ⇒ the fake never arrives.</summary>
        [Fact]
        public void CE2295_AFakeSpot_ReachesThePlatoon_Synthetic_AndTheSquadsRealOneReplacesIt()
        {
            var c = new Chain(members: 3);
            c.Ticks(2);
            var fake = new Fdp.Toolkit.Squad.Command.CommandReport
            {
                Kind = Fdp.Toolkit.Squad.Command.ReportKind.Spot, At = new Vector3(0f, 300f, 0f), Count = 9,
                Size = Fdp.Toolkit.Squad.Command.ElementSize.SquadSize,
            };
            Assert.Null(Fdp.Toolkit.Squad.Command.ReportInjection.Inject(c.S.Repo, c.S.Commander, fake, observedAgoSeconds: 4f));
            c.Ticks(2);
            var e = c.Picture.Elements.Items[0];
            Assert.Equal(1, c.Picture.Elements.Count);
            Assert.Equal((byte)1, e.Synthetic);
            double now = c.S.Repo.GetSingletonUnmanaged<GlobalTime>().TotalTime;
            Assert.InRange(now - e.ObservedAt, 3.9, 4.2);
            var logged = Fdp.Toolkit.Squad.Command.SquadReportLog.Peek(c.S.Repo)!;
            Assert.Contains(logged.Recent(10), x => x.Report.Synthetic == 1 && x.Verdict == "folded");

            c.S.PoolMany(2, new Vector3(5f, 305f, 0f));   // the squad's own eyes: the same place, two men
            c.Ticks(2); c.S.Pause(3.1); c.Ticks(2);
            e = c.Picture.Elements.Items[0];
            Assert.Equal(1, c.Picture.Elements.Count);
            Assert.Equal(((byte)0, (byte)2), (e.Synthetic, e.Count));

            Assert.NotNull(Fdp.Toolkit.Squad.Command.ReportInjection.Inject(c.S.Repo, c.Platoon, fake));   // the platoon has no superior
        }

        /// <summary>⭐ <c>CE-2295</c> CC-E (CC-15) — a muted squad sends nothing, its PROGRESS waits in the outbox; restored, it goes.</summary>
        [Fact]
        public void CE2295_AMutedSquad_SendsNothing_UntilItsRadioIsBack()
        {
            var c = new Chain(members: 3);
            Assert.Null(Fdp.Toolkit.Squad.Command.ReportInjection.Mute(c.S.Repo, c.S.Commander, 120f));
            c.S.Order("{\"DurationSeconds\":0.5}", BehaviorNames.SquadHold);
            for (int i = 0; i < 80 && !c.S.Finished(out _); i++) c.Tick();
            c.Ticks(2); c.S.Pause(4); c.Ticks(3);
            Assert.Equal(Fdp.Toolkit.Squad.Command.ProgressKind.NoProgress, c.SquadProgress);
            Assert.Equal(0, c.Composer.SentCount);
            Assert.Null(Fdp.Toolkit.Squad.Command.ReportInjection.Mute(c.S.Repo, c.S.Commander, 0f));
            c.Ticks(3);
            Assert.Equal(Fdp.Toolkit.Squad.Command.ProgressKind.Set, c.SquadProgress);
        }

        // ── CE-2301 ① (R-2058) — the report log on every node (Command Chain §8b) ──────────────────────────────────────────────

        /// <summary>A brainless node's world: the report event (the shared ingress registers it), one SPOT from a squad to its commander,
        /// readable this frame; <paramref name="withPicture"/> = the picture's type is here too (a replica's, once CE-2301 ② lands).</summary>
        private static (EntityRepository World, Entity To) BrainlessNodeWithOneReport(bool withPicture)
        {
            var world = new EntityRepository();
            world.Bus.RegisterManaged<Fdp.Toolkit.Squad.Command.SquadReportNotification>();
            if (withPicture) world.RegisterComponent<Fdp.Toolkit.Squad.Command.CommandPicture>();
            Entity from = world.CreateEntity(), to = world.CreateEntity();
            world.Bus.PublishManaged(new Fdp.Toolkit.Squad.Command.SquadReportNotification
            {
                FromUnit = from, ToUnit = to,
                Report = new Fdp.Toolkit.Squad.Command.CommandReport { Kind = Fdp.Toolkit.Squad.Command.ReportKind.Spot, At = new Vector3(0f, 300f, 0f), Count = 2 },
            });
            world.Bus.SwapBuffers();
            return (world, to);
        }

        /// <summary>⭐⭐ <c>CE-2301</c> ① (R-2058; 🔒 user, 2026-10-19: <i>"Have the report inbox fill the log on every node."</i>) — a node with
        /// no brain (SimHost, IG) LOGS every report it sees, though it has no <c>CommandPicture</c>; and where the picture's type IS
        /// present it still never folds one (the commander's brain is not here), while the brain pack's inbox would — so a replicated
        /// picture keeps its one writer. ✅ Red-proof: the old picture guard first ⇒ no log; the owned-brain gate removed ⇒ a picture
        /// appears on the brainless node.</summary>
        [Fact]
        public void CE2301_ANodeWithNoBrain_LogsEveryReport_AndNeverFoldsAPicture()
        {
            var (bare, _) = BrainlessNodeWithOneReport(withPicture: false);
            using (bare)
            {
                var inbox = Fdp.Toolkit.Squad.Command.StaffInboxSystem.ForNodeWithoutBrain();
                inbox.Execute(bare, 0.016f);
                var logged = Fdp.Toolkit.Squad.Command.SquadReportLog.Peek(bare);
                Assert.NotNull(logged);
                var entry = Assert.Single(logged!.Recent(10));
                Assert.Equal((Fdp.Toolkit.Squad.Command.ReportKind.Spot, "not-owned-here"), (entry.Report.Kind, entry.Verdict));
                Assert.Equal(0, inbox.FoldedCount);
            }

            var (replica, to) = BrainlessNodeWithOneReport(withPicture: true);
            using (replica)
            {
                Fdp.Toolkit.Squad.Command.StaffInboxSystem.ForNodeWithoutBrain().Execute(replica, 0.016f);
                Assert.False(replica.HasComponent<Fdp.Toolkit.Squad.Command.CommandPicture>(to));
                Assert.Equal("not-owned-here", Assert.Single(Fdp.Toolkit.Squad.Command.SquadReportLog.Peek(replica)!.Recent(10)).Verdict);
            }

            var (brainPack, to2) = BrainlessNodeWithOneReport(withPicture: true);   // the contrast: the brain pack's inbox folds it
            using (brainPack)
            {
                new Fdp.Toolkit.Squad.Command.StaffInboxSystem(gateOnAuthority: true).Execute(brainPack, 0.016f);
                Assert.True(brainPack.HasComponent<Fdp.Toolkit.Squad.Command.CommandPicture>(to2));
            }
        }

        /// <summary>⭐ <c>CE-2301</c> ① — ONE inbox per node: the report-inbox capability is declared by the plans with no brain (SimHost,
        /// IG) and by no brain plan (CGF, the editor's two arms, Stride — whose plan is fused into the editor's in mode 1), which keep
        /// <c>CgfLogicPack</c>'s own, placed before the brain tick. A source rail, like the other plan rails (the plans are built inline).</summary>
        [Fact]
        public void CE2301_TheReportInbox_IsDeclaredOnlyByThePlansWithNoBrain()
        {
            string? root = AppContext.BaseDirectory;
            while (root is not null && !File.Exists(Path.Combine(root, "HROT.sln"))) root = Directory.GetParent(root)?.FullName;
            Assert.NotNull(root);
            int Declarations(string rel) => Regex.Matches(File.ReadAllText(Path.Combine(root!, rel)), @"CoreInfrastructureCapabilities\.ReportInbox\s*\(").Count;

            Assert.Equal(1, Declarations("Hrot/Subsystems/Hrot.SimHost/SimHostNodeBootstrapper.cs"));
            Assert.Equal(1, Declarations("Hrot/Subsystems/Hrot.IG/IgNodeBootstrapper.cs"));
            foreach (string brainPlan in new[] { "Hrot/Subsystems/Hrot.CGF/CgfSubsystem.cs", "Hrot/Subsystems/Hrot.Editor/EditorCapabilities.cs",
                                                 "Stride/HrotStrideApp.Game/StrideCapabilities.cs" })
                Assert.Equal(0, Declarations(brainPlan));
            Assert.Contains("StaffInboxSystem(", File.ReadAllText(Path.Combine(root!, "Hrot/Subsystems/Hrot.CGF/CgfLogicPack.cs")));
        }

        // ── CE-2275 D-1 — SquadDefend: posts, arcs, PROGRESS, displace (Defend & Attack §8c) ────────────────────────────────────

        /// <summary>⭐ <c>CE-2275</c> D-1 (DA-34) — the picker spaces posts in 3-D, so the same window on two storeys is two posts; a point
        /// within 2.5 m of a held post is skipped; a man's alternate is the nearest free point that keeps the spacing from every OTHER post.
        /// ✅ Red-proof: a 2-D spacing ⇒ the upper storey's post is merged away and a man is left without one.</summary>
        [Fact]
        public void CE2275_DefencePostPicker_SpacesPostsIn3D_AndGivesTheNearestFreeAlternate()
        {
            var candidates = new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 3), new Vector3(1, 0, 0), new Vector3(10, 0, 0) };
            var men = new[] { new Vector3(0, 0, 0), new Vector3(0, 0, 3), new Vector3(10, 0, 0) };
            var live = new[] { true, true, true };
            var postOf = new[] { -1, -1, -1 };
            DefencePostPicker.Pick(candidates, men, live, postOf);
            Assert.Equal(new[] { 0, 1, 3 }, postOf);   // the 1 m neighbour of post 0 is skipped
            Assert.Equal(2, DefencePostPicker.Alternate(candidates, postOf, 0));
            Assert.Equal(-1, DefencePostPicker.Alternate(candidates, new[] { 0, 2, 3, 1 }, 0));   // every point is held
        }

        private static Entity DefendQuery(Squad s, int site) => Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(s.Repo, s.Commander, site);

        /// <summary>A squad of four at the position (6, 20) whose own sector looks north (an arc from (0, 20), 330°–30°, out to 200 m ⇒ its
        /// point is (0, 120)), ordered to defend it; the window ask answered with three posts (two storeys of one window, one more), the roof
        /// ask (D-2g) with none, the cover ask with the fourth.</summary>
        private static Squad Defending(out Vector3[] posts)
        {
            var s = new Squad(members: 4);
            s.Repo.AddComponent(s.Commander, new Fdp.Toolkit.Behavior.Components.EngagementArea
            {
                ArcSet = 1, ArcOrigin = new Vector3(0, 20, 0), LeftBearing = 330f, RightBearing = 30f, MaxRange = 200f, SetBy = BehaviorOrigin.Operator,
            });
            s.Order("{\"Position\":[6,20,0],\"SearchRadius\":25}", BehaviorNames.SquadDefend);
            s.Tick(); s.Tick();
            for (int i = 0; i < 4; i++) s.Teleport(s.Members[i], new Vector3(4f + i, 20f, 0f));
            s.Tick(); s.Tick(); s.Tick();
            posts = new[] { new Vector3(2, 25, 0), new Vector3(2, 25, 3), new Vector3(10, 25, 0), new Vector3(14, 24, 0) };
            AnswerAll(s, DefendQuery(s, SquadDefendPlay.WindowSite), posts[0], posts[1], posts[2]);
            s.Tick(); s.Tick();
            AnswerAll(s, DefendQuery(s, SquadDefendPlay.RoofSite));
            s.Tick(); s.Tick();
            AnswerAll(s, DefendQuery(s, SquadDefendPlay.CoverSite), posts[3]);
            s.Orders.Clear();
            s.Tick(); s.Tick();
            return s;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2275</c> D-1 (DA-34…DA-37) — the squad moves to the position, then asks for WINDOW posts aimed at its sector's point and,
        /// for the man left over, COVER; each man gets a post (both storeys of one window are two posts) with an arc toward the sector, and
        /// PROGRESS Set goes up once every man is at his. ✅ Red-proofs: no cover ask ⇒ the fourth man has no post; the arc dropped ⇒ no
        /// sector on the men; the manned check skipped ⇒ no Set.
        /// </summary>
        [Fact]
        public void CE2275_SquadDefend_MovesThenMansWindowAndCoverPosts_WithArcsTowardItsSector_AndReportsSet()
        {
            var s = new Squad(members: 4);
            s.Repo.AddComponent(s.Commander, new Fdp.Toolkit.Behavior.Components.EngagementArea
            {
                ArcSet = 1, ArcOrigin = new Vector3(0, 20, 0), LeftBearing = 330f, RightBearing = 30f, MaxRange = 200f, SetBy = BehaviorOrigin.Operator,
            });
            s.Order("{\"Position\":[6,20,0],\"SearchRadius\":25}", BehaviorNames.SquadDefend);
            s.Tick(); s.Tick();
            Assert.Equal(SquadDefendManeuver.ManeuverKind, s.Kind);
            Assert.Equal(SquadDefendManeuver.PhaseMoving, s.Phase);
            foreach (var m in s.Members) Assert.Equal(new Vector3(6, 20, 0), s.LastOrders()[m].Objective);   // first, to the position
            Assert.True(DefendQuery(s, SquadDefendPlay.WindowSite).IsNull, "no post is asked for before the squad is there");

            s = Defending(out var posts);
            Assert.Equal(SquadDefendManeuver.PhasePosting, s.Phase);
            var given = s.LastOrders();
            Assert.Equal(4, given.Count);
            Assert.Equal(posts.OrderBy(p => p.X).ThenBy(p => p.Z), given.Values.Select(v => v.Objective).OrderBy(p => p.X).ThenBy(p => p.Z));
            foreach (var m in s.Members)
            {
                Assert.Equal(BehaviorNames.CombatPosture, given[m].Intent);
                ref readonly var arc = ref s.Repo.GetComponentRO<Fdp.Toolkit.Behavior.Components.EngagementArea>(m);
                Assert.Equal((byte)1, arc.ArcSet);
                Assert.True(Fdp.Toolkit.Behavior.Components.EngagementAreas.InArc(given[m].Objective, arc.LeftBearing, arc.RightBearing, 0f, 0f,
                    new Vector3(0, 120, 0)), "his arc faces the squad's sector point");
                Assert.InRange(Fdp.Toolkit.Behavior.Components.EngagementAreas.InArc(given[m].Objective, arc.LeftBearing, arc.RightBearing, 0f, 0f,
                    given[m].Objective - new Vector3(0, 50, 0)) ? 1 : 0, 0, 0);   // not behind him
            }

            Assert.False(HasProgress(s, Fdp.Toolkit.Squad.Command.ProgressKind.Set), "not yet at the posts");
            // ⭐ D-2d (DA-44) — "at his post" is within its radius (his room): 3 m off the point, fighting between its points, he mans it
            foreach (var m in s.Members) s.Teleport(m, given[m].Objective + new Vector3(3f, 0f, 0f));
            s.Tick(); s.Tick();
            Assert.True(HasProgress(s, Fdp.Toolkit.Squad.Command.ProgressKind.Set), "every man at his post ⇒ PROGRESS Set");
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2275</c> D-2a (PC-14, DA-40) — each man is sent his POST SET with his post: a window man fights it the window way (the
        /// window column, prone under the sill), a cover man from his cover — both anchored to the post (4 m) — and a man with a free point
        /// near him has it as his alternate. ✅ Red-proofs: the post dropped from the order ⇒ an unanchored posture; every post marked cover
        /// ⇒ the window men get the street column; no alternate passed ⇒ none.
        /// </summary>
        [Fact]
        public void CE2275_SquadDefend_EachManIsSentHisPostSet_AWindowManFightsItTheWindowWay_AnchoredToIt()
        {
            var s = Defending(out var posts);
            foreach (var m in s.Members)
            {
                var dto = PostureOf(s, m);
                var post = dto.Advance.Objective;
                bool window = post != posts[3];
                Assert.Equal(SquadDefendPlay.PostRadius, dto.Post.Radius);
                Assert.Equal(post, dto.Post.Point);
                Assert.Equal(window, dto.Post.Window);
                Assert.False(dto.Post.HasAlternate, "four men, four points: nothing is left over");

                var block = default(CombatPostureBehavior.CombatPostureBlackboard);
                CombatPostureBehavior.Resolve(in dto, ref block, s.Repo, m);
                Assert.Equal(post, block.Peek.Anchor);
                Assert.Equal(SquadDefendPlay.PostRadius, block.Peek.AnchorRadius);
                Assert.Equal(window, dto.Post.SetBack);   // ⭐ D-2b — no proficiency ⇒ Regular ⇒ the windows are set back
                Assert.Equal(window ? Fdp.Toolkit.Spatial.Eqs.FindWindowSetBackPosition.BlueprintId : 0u, block.Peek.HideTemplate);
                Assert.Equal(window ? (byte)(Fdp.Toolkit.Tkb.Domain.StanceId.Prone + 1) : (byte)0, block.Peek.HideStanceOverride);
            }

            // three men, four windows: the point left over is each man's alternate
            var t = new Squad(members: 3);
            t.Order("{\"Position\":[4,0,0],\"SearchRadius\":25}", BehaviorNames.SquadDefend);
            t.Tick(); t.Tick(); t.Tick();
            AnswerAll(t, DefendQuery(t, SquadDefendPlay.WindowSite),
                new Vector3(0, 5, 0), new Vector3(4, 5, 0), new Vector3(8, 5, 0), new Vector3(0, 9, 0));
            t.Orders.Clear();
            t.Tick(); t.Tick();
            var first = t.Members.Single(m => PostureOf(t, m).Advance.Objective == new Vector3(0, 5, 0));
            var spare = PostureOf(t, first).Post;
            Assert.True(spare.HasAlternate);
            Assert.Equal(new Vector3(0, 9, 0), spare.Alternate);
            Assert.True(spare.Window);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2275</c> D-2b (DA-6, DA-16, DA-42) — the squad's MEDIAN man decides where its windows are held: a Regular squad asks for
        /// SET-BACK window posts and each window man hides set back; a Militia squad asks for the sill and its men hide there; one Elite
        /// among two militia does not move the median. ✅ Red-proofs: the tier read from the commander (no component ⇒ Regular) ⇒ the
        /// militia set back; the set-back flag not passed to the man ⇒ he hides at the sill.
        /// </summary>
        [Theory]
        [InlineData(ProficiencyOf.Regular, ProficiencyOf.Regular, true)]
        [InlineData(ProficiencyOf.Militia, ProficiencyOf.Militia, false)]
        [InlineData(ProficiencyOf.Militia, ProficiencyOf.Elite, false)]   // two militia and one elite: the median is militia
        public void CE2275_SquadDefend_TheMedianManDecides_ARegularSquadSetsBack_AMilitiaSquadHoldsTheSill(float most, float first, bool setBack)
        {
            var s = new Squad(members: 3);
            if (!s.Repo.IsComponentTypeRegistered<CombatProficiency>()) s.Repo.RegisterComponent<CombatProficiency>();
            for (int i = 0; i < 3; i++)
                s.Repo.AddComponent(s.Members[i], new CombatProficiency { Authored = i == 0 ? first : most, Set = CombatProficiency.AuthoredSet });
            s.Order("{\"Position\":[4,0,0],\"SearchRadius\":25}", BehaviorNames.SquadDefend);
            s.Tick(); s.Tick(); s.Tick();
            var ask = DefendQuery(s, SquadDefendPlay.WindowSite);
            Assert.False(ask.IsNull);
            Assert.Equal(setBack ? Fdp.Toolkit.Spatial.Eqs.FindWindowSetBackPostsAllRound.BlueprintId : Fdp.Toolkit.Spatial.Eqs.FindWindowPostsAllRound.BlueprintId,
                s.Repo.GetComponentRO<Fdp.Toolkit.Spatial.Eqs.EqsSensor>(ask).BlueprintId);

            AnswerAll(s, ask, new Vector3(0, 5, 0), new Vector3(4, 5, 0), new Vector3(8, 5, 0), new Vector3(12, 5, 0));
            s.Orders.Clear();
            s.Tick(); s.Tick();
            foreach (var m in s.Members)
            {
                var dto = PostureOf(s, m);
                Assert.True(dto.Post.Window);
                Assert.Equal(setBack, dto.Post.SetBack);
                var block = default(CombatPostureBehavior.CombatPostureBlackboard);
                CombatPostureBehavior.Resolve(in dto, ref block, s.Repo, m);
                Assert.Equal(setBack ? Fdp.Toolkit.Spatial.Eqs.FindWindowSetBackPosition.BlueprintId : Fdp.Toolkit.Spatial.Eqs.FindWindowFiringPosition.BlueprintId,
                    block.Peek.HideTemplate);
            }
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2275</c> D-2g (DA-5, DA-22, DA-47) — the ROOF is the second ask: only after the windows, only for the men they left, aimed
        /// at the sector's point; a man given a roof post fights it prone behind the parapet (<c>FindRoofPosts</c> as his hide ask, a stance
        /// peek up to the point's crouch), never the window way; with every man posted the cover ask is not made. ✅ Red-proofs: the roof
        /// stage skipped ⇒ two men without a post and the cover ask runs; the roof flag not passed ⇒ his hide ask is cover.
        /// </summary>
        [Fact]
        public void CE2275_SquadDefend_TheRoofIsAskedAfterTheWindows_ForTheMenLeft_AndARoofManHidesBehindTheParapet()
        {
            var s = new Squad(members: 4);
            s.Repo.AddComponent(s.Commander, new Fdp.Toolkit.Behavior.Components.EngagementArea
            {
                ArcSet = 1, ArcOrigin = new Vector3(0, 20, 0), LeftBearing = 330f, RightBearing = 30f, MaxRange = 200f, SetBy = BehaviorOrigin.Operator,
            });
            s.Order("{\"Position\":[6,20,0],\"SearchRadius\":25}", BehaviorNames.SquadDefend);
            s.Tick(); s.Tick();
            for (int i = 0; i < 4; i++) s.Teleport(s.Members[i], new Vector3(4f + i, 20f, 0f));
            s.Tick(); s.Tick(); s.Tick();
            Assert.True(DefendQuery(s, SquadDefendPlay.RoofSite).IsNull, "the windows are asked first");
            AnswerAll(s, DefendQuery(s, SquadDefendPlay.WindowSite), new Vector3(2, 25, 0), new Vector3(10, 25, 3));
            s.Tick(); s.Tick();
            var roofAsk = DefendQuery(s, SquadDefendPlay.RoofSite);
            Assert.False(roofAsk.IsNull, "two men are left: the roof is asked");
            ref readonly var sensor = ref s.Repo.GetComponentRO<Fdp.Toolkit.Spatial.Eqs.EqsSensor>(roofAsk);
            Assert.Equal(Fdp.Toolkit.Spatial.Eqs.FindRoofPosts.BlueprintId, sensor.BlueprintId);
            var roof = new[] { new Vector3(3, 23, 6), new Vector3(9, 23, 6) };
            AnswerAll(s, roofAsk, roof);
            s.Orders.Clear();
            s.Tick(); s.Tick();
            Assert.True(DefendQuery(s, SquadDefendPlay.CoverSite).IsNull, "every man holds a post: no cover ask");
            var given = s.LastOrders();
            Assert.Equal(4, given.Count);
            int onRoof = 0;
            foreach (var m in s.Members)
            {
                var dto = PostureOf(s, m);
                bool isRoof = roof.Contains(dto.Post.Point);
                Assert.Equal(isRoof, dto.Post.Roof);
                Assert.Equal(!isRoof, dto.Post.Window);
                var block = default(CombatPostureBehavior.CombatPostureBlackboard);
                CombatPostureBehavior.Resolve(in dto, ref block, s.Repo, m);
                if (!isRoof) continue;
                onRoof++;
                Assert.Equal(Fdp.Toolkit.Spatial.Eqs.FindRoofPosts.BlueprintId, block.Peek.HideTemplate);
                Assert.Equal(PeekMode.Stance, block.Peek.Mode);
                Assert.Equal((byte)(Fdp.Toolkit.Tkb.Domain.StanceId.Prone + 1), block.Peek.HideStanceOverride);
                Assert.Equal(dto.Post.Point, block.Peek.Anchor);
            }
            Assert.Equal(2, onRoof);
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2275</c> D-2h (DA-19, DA-25, DA-48) — the squad's MACHINE GUNNER gets the post with the longest view down the sector:
        /// once the posts are picked, ONE visibility fan is asked (an origin per held post, each looking at the sector's point), and the
        /// gun swaps posts with the holder of the longest mean view; then the posts are given. A squad with no automatic weapon asks
        /// no fan. ✅ Red-proofs: the swap skipped ⇒ the gun keeps the nearest post; the role read skipped ⇒ no fan.
        /// </summary>
        [Fact]
        public void CE2275_SquadDefend_TheMachineGunnerTakesThePostWithTheLongestView()
        {
            var s = new Squad(members: 4);
            s.Repo.GetComponentRW<UnitSubordinate>(s.Members[1]).Designation = TacticalDesignation.MachineGunner;
            s.Repo.AddComponent(s.Commander, new Fdp.Toolkit.Behavior.Components.EngagementArea
            {
                ArcSet = 1, ArcOrigin = new Vector3(0, 20, 0), LeftBearing = 330f, RightBearing = 30f, MaxRange = 200f, SetBy = BehaviorOrigin.Operator,
            });
            s.Order("{\"Position\":[6,20,0],\"SearchRadius\":25}", BehaviorNames.SquadDefend);
            s.Tick(); s.Tick();
            for (int i = 0; i < 4; i++) s.Teleport(s.Members[i], new Vector3(4f + i, 20f, 0f));
            s.Tick(); s.Tick(); s.Tick();
            var posts = new[] { new Vector3(2, 25, 0), new Vector3(5, 25, 0), new Vector3(8, 25, 0), new Vector3(11, 25, 0) };
            AnswerAll(s, DefendQuery(s, SquadDefendPlay.WindowSite), posts);
            s.Orders.Clear();
            s.Tick(); s.Tick();
            Assert.Empty(s.Orders);   // the fan comes before the posts are given

            var fan = DefendQuery(s, SquadDefendPlay.FanSite);
            Assert.False(fan.IsNull, "the squad has a gun: the fan is asked");
            var request = ((Fdp.ModuleHost.Abstractions.ISimulationView)s.Repo).GetManagedComponentRO<Fdp.Toolkit.Perception.Survey.WorldSurveyRequest>(fan);
            Assert.Equal(Fdp.Toolkit.Perception.Survey.WorldSurveyKind.VisibilityFan, request.Kind);
            Assert.Equal(4, request.Origins.Count);
            var far = new Vector3(11, 25, 0);
            var payload = new Fdp.Toolkit.Perception.Survey.WorldSurveyPayload { Kind = Fdp.Toolkit.Perception.Survey.WorldSurveyKind.VisibilityFan };
            for (int o = 0; o < request.Origins.Count; o++)
            {
                var origin = request.Origins[o];
                Assert.InRange(origin.Bearing, MathF.PI / 2f - 0.3f, MathF.PI / 2f + 0.3f);   // toward the sector's point, north (+Y)
                bool longest = new Vector3(origin.X, origin.Y, origin.Z) == far;
                for (int r = 0; r < request.Rays; r++)
                    payload.Rays.Add(new Fdp.Toolkit.Perception.Survey.SurveyRay { Origin = o, Free = longest ? 120f : 20f });
            }
            var now = s.Repo.GetSingleton<GlobalTime>().TotalTime;
            s.Repo.SetComponent(fan, new Fdp.Toolkit.Perception.Survey.WorldSurveyStatus { LastUpdateTick = 1, LastUpdateTimeSeconds = (float)now, Version = 1 });
            s.Repo.SetManagedComponent(fan, new Fdp.Toolkit.Perception.Survey.WorldSurveyAnswer { Payload = payload });
            s.Tick(); s.Tick();
            var given = s.LastOrders();
            Assert.Equal(4, given.Count);
            Assert.Equal(far, given[s.Members[1]].Objective);   // the gun on the longest view
            Assert.Equal(posts.OrderBy(p => p.X), given.Values.Select(v => v.Objective).OrderBy(p => p.X));   // the same posts, one each

            var plain = Defending(out _);
            Assert.True(DefendQuery(plain, SquadDefendPlay.FanSite).IsNull, "no automatic weapon: no fan");
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2275</c> D-2j (DA-16 "Veteran: set-back posts everywhere", DA-50) — a VETERAN squad keeps only the window points its
        /// set-back pass really moved back (§4.2 bit 8 <c>IsSetBack</c>): a window whose room was too shallow is no post, and the man left
        /// without one takes a cover post; a Regular squad keeps both kinds. ✅ Red-proofs: the filter dropped ⇒ the Veteran squad mans the
        /// shallow windows; the tier read as Regular+ instead of Veteran+ ⇒ the Regular squad drops them too.
        /// </summary>
        [Theory]
        [InlineData(ProficiencyOf.Veteran, 2)]
        [InlineData(ProficiencyOf.Regular, 4)]
        public void CE2275_D2j_AVeteranSquad_TakesOnlyRealSetBackWindows(float tier, int windowPosts)
        {
            var s = new Squad(members: 4);
            if (!s.Repo.IsComponentTypeRegistered<CombatProficiency>()) s.Repo.RegisterComponent<CombatProficiency>();
            foreach (var m in s.Members) s.Repo.AddComponent(m, new CombatProficiency { Authored = tier, Set = CombatProficiency.AuthoredSet });
            s.Repo.AddComponent(s.Commander, new Fdp.Toolkit.Behavior.Components.EngagementArea
            {
                ArcSet = 1, ArcOrigin = new Vector3(0, 20, 0), LeftBearing = 330f, RightBearing = 30f, MaxRange = 200f, SetBy = BehaviorOrigin.Operator,
            });
            s.Order("{\"Position\":[6,20,0],\"SearchRadius\":25}", BehaviorNames.SquadDefend);
            s.Tick(); s.Tick();
            for (int i = 0; i < 4; i++) s.Teleport(s.Members[i], new Vector3(4f + i, 20f, 0f));
            s.Tick(); s.Tick(); s.Tick();
            var ask = DefendQuery(s, SquadDefendPlay.WindowSite);
            Assert.Equal(Fdp.Toolkit.Spatial.Eqs.FindDefensiveWindowSetBackPosts.BlueprintId, s.Repo.GetComponentRO<Fdp.Toolkit.Spatial.Eqs.EqsSensor>(ask).BlueprintId);
            const short bit = Fdp.Toolkit.Spatial.Eqs.CoverPointsGenerator.FlagSetBack;
            var buf = new Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer { Count = 4, LastUpdateTick = 7 };
            var span = buf.GetSpanRW();
            span[0] = new Fdp.Toolkit.Spatial.Eqs.EqsResult { PositionX = 2, PositionY = 25, Score = 1.0f, Flags = bit, FlagsMeaningful = bit };
            span[1] = new Fdp.Toolkit.Spatial.Eqs.EqsResult { PositionX = 6, PositionY = 25, Score = 0.9f, Flags = 0, FlagsMeaningful = bit };    // shallow: at the sill
            span[2] = new Fdp.Toolkit.Spatial.Eqs.EqsResult { PositionX = 10, PositionY = 25, Score = 0.8f, Flags = bit, FlagsMeaningful = bit };
            span[3] = new Fdp.Toolkit.Spatial.Eqs.EqsResult { PositionX = 14, PositionY = 25, Score = 0.7f, Flags = 0, FlagsMeaningful = bit };   // shallow
            s.Orders.Clear();   // with every man on a window the posts go out on the next ticks
            s.Repo.SetComponent(ask, buf);
            s.Tick(); s.Tick();
            if (windowPosts < 4) { AnswerAll(s, DefendQuery(s, SquadDefendPlay.RoofSite)); s.Tick(); s.Tick(); }   // men are left: the roof is asked (empty)
            var cover = DefendQuery(s, SquadDefendPlay.CoverSite);
            if (windowPosts < 4) AnswerAll(s, cover, new Vector3(4, 30, 0), new Vector3(12, 30, 0));
            else Assert.True(cover.IsNull, "every man holds a window post: no cover ask");
            s.Tick(); s.Tick();
            var windows = s.Members.Select(m => PostureOf(s, m)).Where(d => d.Post.Window).Select(d => d.Post.Point.X).OrderBy(x => x).ToArray();
            Assert.Equal(windowPosts, windows.Length);
            if (windowPosts == 2) Assert.Equal(new[] { 2f, 10f }, windows);   // only the windows really set back
        }

        /// <summary>The squad defending at (6, 20) with its sector north, ordered with <paramref name="postsJson"/> (an authored Posts array).</summary>
        private static Squad DefendingWithPosts(string postsJson)
        {
            var s = new Squad(members: 4);
            s.Repo.AddComponent(s.Commander, new Fdp.Toolkit.Behavior.Components.EngagementArea
            {
                ArcSet = 1, ArcOrigin = new Vector3(0, 20, 0), LeftBearing = 330f, RightBearing = 30f, MaxRange = 200f, SetBy = BehaviorOrigin.Operator,
            });
            s.Order("{\"Position\":[6,20,0],\"SearchRadius\":25,\"Posts\":" + postsJson + "}", BehaviorNames.SquadDefend);
            s.Tick(); s.Tick();
            for (int i = 0; i < 4; i++) s.Teleport(s.Members[i], new Vector3(4f + i, 20f, 0f));
            s.Tick(); s.Tick(); s.Tick();
            return s;
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2275</c> D-2f (DA-5, DA-46) — AUTHORED fighting positions come first: four for four men ⇒ every man holds one and the
        /// terrain is never asked; the authored window post is fought the window way and NOT set back (the author placed him); an authored
        /// <c>watchTowards</c> turns that man's arc to it (here west, against a north sector). Two for four men ⇒ two men take them and the
        /// window ask still runs for the other two.
        /// </summary>
        [Fact]
        public void CE2275_SquadDefend_AuthoredPostsComeFirst_TheTerrainIsAskedOnlyForTheMenLeft_AndAWatchPointTurnsTheArc()
        {
            var s = DefendingWithPosts("[{\"point\":[2,24,0],\"window\":true},{\"point\":[5,24,3]},{\"point\":[9,24,0]},"
                                     + "{\"point\":[12,22,0],\"watchTowards\":[-60,22,0]}]");
            Assert.Equal(SquadDefendManeuver.PhasePosting, s.Phase);
            Assert.True(DefendQuery(s, SquadDefendPlay.WindowSite).IsNull, "every man holds an authored post: no window ask");
            Assert.True(DefendQuery(s, SquadDefendPlay.CoverSite).IsNull, "… and no cover ask");
            var given = s.LastOrders();
            var authored = new[] { new Vector3(2, 24, 0), new Vector3(5, 24, 3), new Vector3(9, 24, 0), new Vector3(12, 22, 0) };
            Assert.Equal(authored.OrderBy(p => p.X), given.Values.Select(v => v.Objective).OrderBy(p => p.X));
            foreach (var m in s.Members)
            {
                var dto = PostureOf(s, m);
                bool window = dto.Post.Point == authored[0];
                Assert.Equal(window, dto.Post.Window);
                Assert.False(dto.Post.SetBack, "an authored post is where the author put him");
                ref readonly var arc = ref s.Repo.GetComponentRO<Fdp.Toolkit.Behavior.Components.EngagementArea>(m);
                bool west = Fdp.Toolkit.Behavior.Components.EngagementAreas.InArc(dto.Post.Point, arc.LeftBearing, arc.RightBearing, 0f, 0f, dto.Post.Point + new Vector3(-50, 0, 0));
                bool north = Fdp.Toolkit.Behavior.Components.EngagementAreas.InArc(dto.Post.Point, arc.LeftBearing, arc.RightBearing, 0f, 0f, dto.Post.Point + new Vector3(0, 50, 0));
                if (dto.Post.Point == authored[3]) Assert.True(west && !north, "his watch point turned his arc west");
                else Assert.True(north && !west, "the others face the sector");
            }

            var s2 = DefendingWithPosts("[{\"point\":[2,24,0],\"window\":true},{\"point\":[12,22,0]}]");
            Assert.False(DefendQuery(s2, SquadDefendPlay.WindowSite).IsNull, "two men are left: the window ask runs");
            AnswerAll(s2, DefendQuery(s2, SquadDefendPlay.WindowSite), new Vector3(6, 25, 0), new Vector3(8, 25, 3));
            s2.Orders.Clear();
            s2.Tick(); s2.Tick();
            var points = s2.LastOrders().Values.Select(v => v.Objective).ToList();
            Assert.Contains(new Vector3(2, 24, 0), points);
            Assert.Contains(new Vector3(12, 22, 0), points);
            Assert.Contains(new Vector3(6, 25, 0), points);
            Assert.Contains(new Vector3(8, 25, 3), points);
            foreach (var m in s2.Members)   // a Regular squad sets its window posts back — but not the one the author placed (D-2b, D-2f)
            {
                var dto = PostureOf(s2, m);
                if (dto.Post.Point == new Vector3(2, 24, 0)) Assert.True(dto.Post.Window && !dto.Post.SetBack, "the authored window: where he was put");
                if (dto.Post.Point == new Vector3(6, 25, 0)) Assert.True(dto.Post.Window && dto.Post.SetBack, "the terrain's window: set back");
            }
        }

        private static CombatPostureParamsJsonDto PostureOf(Squad s, Entity m)
        {
            var o = s.Orders.Last(x => x.Entity.Equals(m) && x.IntentId == BehaviorNames.CombatPosture);
            return System.Text.Json.JsonSerializer.Deserialize<CombatPostureParamsJsonDto>(o.JsonParams, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
        }

        /// <summary>
        /// ⭐ <c>CE-2275</c> D-1 (DA-38) — three rounds at one man within 10 s move him to his alternate post (the nearest free point the
        /// terrain gave) with PROGRESS Displacing; two do not. ✅ Red-proof: the count never reaching three (a hit counted once per window)
        /// ⇒ he stays.
        /// </summary>
        [Fact]
        public void CE2275_SquadDefend_AManUnderFire_MovesToHisAlternatePost_AndReportsDisplacing()
        {
            var s = new Squad(members: 3);
            s.Order("{\"Position\":[4,0,0],\"SearchRadius\":25}", BehaviorNames.SquadDefend);
            s.Tick(); s.Tick(); s.Tick();
            AnswerAll(s, DefendQuery(s, SquadDefendPlay.WindowSite),
                new Vector3(0, 5, 0), new Vector3(4, 5, 0), new Vector3(8, 5, 0), new Vector3(0, 9, 0));
            s.Orders.Clear();
            s.Tick(); s.Tick();
            var given = s.LastOrders();
            var first = s.Members.Single(m => given[m].Objective == new Vector3(0, 5, 0));
            foreach (var m in s.Members) s.Teleport(m, given[m].Objective);
            s.Tick();

            void RoundAt(Entity m) { s.Repo.Bus.Publish(new Fdp.Toolkit.Perception.Events.SensorChangedEvent { Unit = m, What = Fdp.Toolkit.Perception.Events.SensorChange.NearMiss }); s.Tick(); s.Tick(); }
            s.Orders.Clear();
            RoundAt(first); RoundAt(first);
            Assert.DoesNotContain(s.Orders, o => o.Entity.Equals(first));
            RoundAt(first);
            Assert.Equal(new Vector3(0, 9, 0), s.LastOrders()[first].Objective);
            Assert.True(HasProgress(s, Fdp.Toolkit.Squad.Command.ProgressKind.Displacing));
        }

        /// <summary>A squad of three defending at (4, 0) whose window ask gave (0,5) (4,5) (8,5) and a spare behind each — (0,9), and the
        /// nearer (4,8) and (8,8), so one man's move changes nobody else's alternate; every man on his post and running his order (as
        /// SimHost would have him). Returns the man on (0,5).</summary>
        private static Entity PostedAndRunning(Squad s)
        {
            s.Order("{\"Position\":[4,0,0],\"SearchRadius\":25}", BehaviorNames.SquadDefend);
            s.Tick(); s.Tick(); s.Tick();
            AnswerAll(s, DefendQuery(s, SquadDefendPlay.WindowSite),
                new Vector3(0, 5, 0), new Vector3(4, 5, 0), new Vector3(8, 5, 0), new Vector3(0, 9, 0), new Vector3(4, 8, 0), new Vector3(8, 8, 0));
            s.Tick(); s.Tick();
            var given = s.LastOrders();
            foreach (var m in s.Members)
            {
                s.Teleport(m, given[m].Objective);
                s.Repo.AddComponent(m, new BehaviorState { ActiveBehaviorHash = BehaviorHash.FromName(BehaviorNames.CombatPosture), Origin = BehaviorOrigin.Superior });
            }
            s.Tick();
            return s.Members.Single(m => given[m].Objective == new Vector3(0, 5, 0));
        }

        private static void RoundAt(Squad s, Entity m)
        {
            s.Repo.Bus.Publish(new Fdp.Toolkit.Perception.Events.SensorChangedEvent { Unit = m, What = Fdp.Toolkit.Perception.Events.SensorChange.NearMiss });
            s.Tick(); s.Tick();
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2304</c> — a re-issue re-sends only the orders that CHANGED: one man displaced gets his new post, the two still running
        /// theirs get nothing (an unchanged re-send stopped each of them and started him again — measured live, every posted man's move
        /// restarting in step). A man no longer running his order (a reaction took him) gets it again on the next re-issue.
        /// ✅ Red-proof: <c>Keep</c> publishing regardless ⇒ all three get an order.
        /// </summary>
        [Fact]
        public void CE2304_AReissue_ResendsOnlyTheMenWhoseOrderChanged_AndAManWhoStoppedRunningHisGetsItAgain()
        {
            var s = new Squad(members: 3);
            var first = PostedAndRunning(s);
            s.Orders.Clear();
            RoundAt(s, first); RoundAt(s, first); RoundAt(s, first);
            Assert.Equal(new Vector3(0, 9, 0), s.LastOrders()[first].Objective);
            Assert.Equal(new[] { first }, s.Orders.Select(o => o.Entity).Distinct().ToArray());

            var other = s.Members.First(m => !m.Equals(first));
            s.Repo.GetComponentRW<BehaviorState>(other).ActiveBehaviorHash = BehaviorHash.FromName(BehaviorNames.Suppress);   // his own reaction
            s.Repo.GetComponentRW<BehaviorState>(first).ActiveBehaviorHash = BehaviorHash.FromName(BehaviorNames.CombatPosture);
            s.Orders.Clear();
            s.Pause(1.0);   // back from a pause: the runner re-issues every order (S1-G)
            s.Tick(); s.Tick();
            Assert.Equal(new[] { other }, s.Orders.Select(o => o.Entity).Distinct().ToArray());
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2304</c> (DA-58) — PROGRESS Set once every man has REACHED his post, not once all stand on them in one frame: the
        /// first man gets there and then fights 6 m out (a hide point, a peek); the others arrive later ⇒ Set. Not before the last arrives.
        /// 📌 Live, without it: every man at his post, and no frame in 4½ minutes with all seven within 4 m. ✅ Red-proof: "within
        /// <see cref="SquadDefendPlay.PostRadius"/> now" instead of the latch ⇒ no Set.
        /// </summary>
        [Fact]
        public void CE2304_Set_CountsAManWhoReachedHisPost_EvenWhenHeFightsFromBesideIt()
        {
            var s = new Squad(members: 3);
            s.Order("{\"Position\":[4,0,0],\"SearchRadius\":25}", BehaviorNames.SquadDefend);
            s.Tick(); s.Tick(); s.Tick();
            AnswerAll(s, DefendQuery(s, SquadDefendPlay.WindowSite),
                new Vector3(0, 5, 0), new Vector3(4, 5, 0), new Vector3(8, 5, 0), new Vector3(0, 9, 0));
            s.Tick(); s.Tick();
            var given = s.LastOrders();
            var first = s.Members.Single(m => given[m].Objective == new Vector3(0, 5, 0));
            s.Teleport(first, given[first].Objective);
            s.Tick();
            s.Teleport(first, given[first].Objective + new Vector3(-6, 0, 0));   // he fights from beside his post
            s.Tick(); s.Tick();
            Assert.False(HasProgress(s, Fdp.Toolkit.Squad.Command.ProgressKind.Set), "the others are not there yet");
            foreach (var m in s.Members.Where(m => !m.Equals(first))) s.Teleport(m, given[m].Objective);
            s.Tick(); s.Tick();
            Assert.True(HasProgress(s, Fdp.Toolkit.Squad.Command.ProgressKind.Set));
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2304</c> (DA-56) — a displaced man is not sent straight back by the rounds of the fire he moves through: for
        /// <see cref="SquadDefendPlay.DisplacedGraceSeconds"/> after the move his rounds do not count; after it, three more move him again.
        /// 📌 Live, without it: 43 displacements in 14 s, every man ping-ponging between two posts. ✅ Red-proof: the grace check dropped ⇒
        /// three rounds right after the move send him back.
        /// </summary>
        [Fact]
        public void CE2304_ADisplacedMan_IsNotSentStraightBack_TheRoundsWhileHeMovesDoNotCount()
        {
            var s = new Squad(members: 3);
            var first = PostedAndRunning(s);
            RoundAt(s, first); RoundAt(s, first); RoundAt(s, first);
            Assert.Equal(new Vector3(0, 9, 0), s.LastOrders()[first].Objective);
            s.Teleport(first, new Vector3(0, 9, 0));

            s.Orders.Clear();
            RoundAt(s, first); RoundAt(s, first); RoundAt(s, first); RoundAt(s, first);
            Assert.DoesNotContain(s.Orders, o => o.Entity.Equals(first));   // still inside the grace: he stays

            for (int i = 0; i < (int)(SquadDefendPlay.DisplacedGraceSeconds / 0.016f) + 5; i++) s.Tick();
            RoundAt(s, first); RoundAt(s, first); RoundAt(s, first);
            Assert.Equal(new Vector3(0, 5, 0), s.LastOrders()[first].Objective);   // untenable again: the free point is his old post
        }

        /// <summary>
        /// ⭐⭐ <c>CE-2275</c> D-2c (DA-21, DA-43) — a seen enemy 25 m behind the posts, outside every man's arc, FLANKS them: the man with the
        /// free point takes it as his alternate and his arc now faces the enemy; PROGRESS Displacing; the same enemy never turns him twice;
        /// an enemy out beyond 50 m flanks nobody. ✅ Red-proofs: the arc test dropped ⇒ a man turns at a contact in front; the once-per-
        /// contact guard dropped ⇒ a second order; the arc not turned ⇒ he still cannot shoot the flanker.
        /// </summary>
        [Fact]
        public void CE2275_SquadDefend_AFlankedManTakesHisAlternate_FacingTheFlanker_OncePerContact()
        {
            var s = new Squad(members: 3);
            s.Order("{\"Position\":[4,0,0],\"SearchRadius\":25}", BehaviorNames.SquadDefend);
            s.Tick(); s.Tick(); s.Tick();
            AnswerAll(s, DefendQuery(s, SquadDefendPlay.WindowSite),
                new Vector3(0, 5, 0), new Vector3(4, 5, 0), new Vector3(8, 5, 0), new Vector3(0, 9, 0));
            s.Tick(); s.Tick();
            var given = s.LastOrders();
            var first = s.Members.Single(m => given[m].Objective == new Vector3(0, 5, 0));
            foreach (var m in s.Members) s.Teleport(m, given[m].Objective);
            s.Tick();

            s.Orders.Clear();
            s.Pool(s.Repo.CreateEntity(), new Vector3(4, -80, 0), reporters: 1);   // 85 m behind: too far to flank
            s.Tick(); s.Tick();
            s.Pool(s.Repo.CreateEntity(), new Vector3(4, 30, 0), reporters: 1);    // 25 m in front: inside every arc
            s.Tick(); s.Tick();
            Assert.Empty(s.Orders);

            var flanker = new Vector3(20, -10, 0);   // 25 m behind and beside the posts, outside every arc
            var enemy = s.Repo.CreateEntity();
            s.Pool(enemy, flanker, reporters: 1);
            s.Tick(); s.Tick();
            Assert.Equal(new Vector3(0, 9, 0), s.LastOrders()[first].Objective);
            ref readonly var arc = ref s.Repo.GetComponentRO<Fdp.Toolkit.Behavior.Components.EngagementArea>(first);
            Assert.True(Fdp.Toolkit.Behavior.Components.EngagementAreas.InArc(new Vector3(0, 9, 0), arc.LeftBearing, arc.RightBearing, 0f, 0f, flanker),
                "his arc faces the flanker");
            Assert.True(HasProgress(s, Fdp.Toolkit.Squad.Command.ProgressKind.Displacing));

            s.Orders.Clear();
            s.Pool(enemy, new Vector3(-20, -5, 0), reporters: 1);   // the same man works round behind him again
            s.Tick(); s.Tick(); s.Tick();
            Assert.Empty(s.Orders);   // the same flanker turns nobody twice (no dithering)
        }

        /// <summary>A squad of three at (4, 0) whose own sector looks north (arc from (4, 0), 330°–30°, to 200 m), HOLDING FIRE by an order
        /// of <paramref name="holdBy"/>, ordered to defend where it stands.</summary>
        private static Squad HoldingFire(BehaviorOrigin holdBy)
        {
            var s = new Squad(members: 3);
            s.Repo.AddComponent(s.Commander, new Fdp.Toolkit.Behavior.Components.EngagementArea
            {
                ArcSet = 1, ArcOrigin = new Vector3(4, 0, 0), LeftBearing = 330f, RightBearing = 30f, MaxRange = 200f, SetBy = BehaviorOrigin.Operator,
            });
            s.Repo.AddComponent(s.Commander, new Fdp.Toolkit.Behavior.Components.Roe { Fire = Fdp.Toolkit.Behavior.Components.RoeFire.HoldFire, SetBy = holdBy });
            s.Order("{\"Position\":[4,0,0],\"SearchRadius\":25}", BehaviorNames.SquadDefend);
            s.Tick(); s.Tick();
            return s;
        }

        private static Fdp.Toolkit.Behavior.Components.RoeFire SquadFire(Squad s) => s.Repo.GetComponentRO<Fdp.Toolkit.Behavior.Components.Roe>(s.Commander).Fire;

        /// <summary>
        /// ⭐ <c>CE-2275</c> DA-35 (corrected; Platoon §6a "alone, it lifts its own hold-fire", DA-7) — a squad defending ALONE under an
        /// operator's hold keeps holding while the only contact is outside its sector, and lifts to FireAtWill the moment one is inside it;
        /// the lift keeps the operator's rank. ✅ Red-proof: the sector test dropped (any contact lifts) ⇒ the outside contact lifts it.
        /// </summary>
        [Fact]
        public void CE2275_SquadDefendAlone_LiftsAnOperatorsHoldFire_OnAContactInsideItsSector_NotOutside()
        {
            var s = HoldingFire(BehaviorOrigin.Operator);
            s.Pool(s.Repo.CreateEntity(), new Vector3(4, -120, 0), reporters: 1);   // behind the squad
            s.Tick(); s.Tick();
            Assert.Equal(Fdp.Toolkit.Behavior.Components.RoeFire.HoldFire, SquadFire(s));
            s.Pool(s.Repo.CreateEntity(), new Vector3(4, 120, 0), reporters: 1);    // inside the sector
            s.Tick(); s.Tick();
            Assert.Equal(Fdp.Toolkit.Behavior.Components.RoeFire.FireAtWill, SquadFire(s));
            Assert.Equal(BehaviorOrigin.Operator, s.Repo.GetComponentRO<Fdp.Toolkit.Behavior.Components.Roe>(s.Commander).SetBy);
        }

        /// <summary>
        /// ⭐ <c>CE-2275</c> DA-35 — a round at one of its men lifts an alone squad's hold too (DA-7: "a post is fired upon"), and a hold a
        /// SUPERIOR set (a platoon play's) is never lifted by the squad — the platoon lifts it (Platoon §5 Engage). ✅ Red-proofs: the round
        /// trigger dropped ⇒ the first squad keeps holding; the Superior guard dropped ⇒ the second squad lifts.
        /// </summary>
        [Fact]
        public void CE2275_SquadDefend_ARoundAtAManLiftsItsOwnHold_ButNeverAPlatoonsHold()
        {
            var alone = HoldingFire(BehaviorOrigin.Operator);
            alone.Repo.Bus.Publish(new Fdp.Toolkit.Perception.Events.SensorChangedEvent { Unit = alone.Members[1], What = Fdp.Toolkit.Perception.Events.SensorChange.NearMiss });
            alone.Tick(); alone.Tick(); alone.Tick();
            Assert.Equal(Fdp.Toolkit.Behavior.Components.RoeFire.FireAtWill, SquadFire(alone));

            var underPlatoon = HoldingFire(BehaviorOrigin.Superior);
            underPlatoon.Pool(underPlatoon.Repo.CreateEntity(), new Vector3(4, 120, 0), reporters: 1);
            underPlatoon.Repo.Bus.Publish(new Fdp.Toolkit.Perception.Events.SensorChangedEvent { Unit = underPlatoon.Members[1], What = Fdp.Toolkit.Perception.Events.SensorChange.NearMiss });
            underPlatoon.Tick(); underPlatoon.Tick(); underPlatoon.Tick();
            Assert.Equal(Fdp.Toolkit.Behavior.Components.RoeFire.HoldFire, SquadFire(underPlatoon));
        }

        private static bool HasProgress(Squad s, Fdp.Toolkit.Squad.Command.ProgressKind kind)
        {
            if (!s.Repo.HasComponent<Fdp.Toolkit.Squad.Command.ReportOutbox>(s.Commander)) return false;
            var q = s.Repo.GetComponentRO<Fdp.Toolkit.Squad.Command.ReportOutbox>(s.Commander).Queued;
            for (int i = 0; i < q.Count; i++)
                if (q.Items[i].Report.Kind == Fdp.Toolkit.Squad.Command.ReportKind.Progress && q.Items[i].Report.Subtype == (byte)kind) return true;
            return false;
        }

        // ── CE-2294 CC-D — the first chain: a platoon's task and SOP over its squads' reports (Command Chain §1, §6, §8) ─────────

        /// <summary>A body-less PLATOON (the fixture's commander) over two squads (its members: Alpha in slot 0, Bravo in slot 1); the
        /// squads are stand-ins whose reports the test publishes, folded by the platoon's inbox each frame.</summary>
        private sealed class Platoon
        {
            public readonly Squad S;
            public readonly Fdp.Toolkit.Squad.Command.StaffInboxSystem Inbox = new();
            public Entity Alpha => S.Members[0];
            public Entity Bravo => S.Members[1];
            public Entity Charlie => S.Members[2];

            public Platoon(int squads = 2) => S = new Squad(members: squads);

            /// <summary>⭐ CE-2276 E-2 (DA-63) — the SITREP the platoon asks for lands.</summary>
            public readonly Fdp.Toolkit.Squad.Command.ReportingRequirementsSystem Reporting = new();

            public void Tick() { S.Tick(); Inbox.Execute(S.Repo, 0.016f); Reporting.Execute(S.Repo, 0.016f); }
            public void Ticks(int n) { for (int i = 0; i < n; i++) Tick(); }

            public void Report(Entity from, Fdp.Toolkit.Squad.Command.CommandReport r, float observedAgo = 0f)
            {
                float now = Fdp.Toolkit.Squad.Command.CommandPictureOps.Now(S.Repo);
                r.ObservedAt = now - observedAgo;
                S.Repo.Bus.PublishManaged(new Fdp.Toolkit.Squad.Command.SquadReportNotification { FromUnit = from, ToUnit = S.Commander, Report = r, SentAt = now });
            }

            public void ReportSet(Entity from, float observedAgo = 0f)
                => Report(from, new Fdp.Toolkit.Squad.Command.CommandReport
                {
                    Kind = Fdp.Toolkit.Squad.Command.ReportKind.Progress, Subtype = (byte)Fdp.Toolkit.Squad.Command.ProgressKind.Set,
                }, observedAgo);

            public void ReportSpot(Entity from, Vector3 at, byte count)
                => Report(from, new Fdp.Toolkit.Squad.Command.CommandReport
                {
                    Kind = Fdp.Toolkit.Squad.Command.ReportKind.Spot, At = at, Count = count,
                    Size = Fdp.Toolkit.Squad.Command.StaffComposerSystem.SizeOf(count),
                });

            public int OrdersTo(Entity squad, string intent) => S.Orders.Count(o => o.Entity.Equals(squad) && o.IntentId == intent);

            public void Bound(float sitrep = 0f)
                => S.Order("{\"SupportPoint\":[0,100,0],\"Objective\":[50,200,0],\"SitrepEverySeconds\":" + sitrep.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}",
                           BehaviorNames.PlatoonBound);
        }

        /// <summary>⭐⭐ <c>CE-2294</c> CC-D (cc-platoon-bound ①②) — <c>PlatoonBound</c> orders the SUPPORT squad (Bravo) once and keeps that
        /// order (no re-send while it stands); the ASSAULT squad (Alpha) is ordered ONLY after Bravo's SET is in the picture; a Set observed
        /// before Alpha's order (the last order's, still on its way) does not finish the task; Alpha's own Set does. ✅ Red-proofs: the
        /// support guard always true ⇒ Alpha ordered at once; the fold ignoring <c>OrderedAt</c> ⇒ the stale Set finishes the task.</summary>
        [Fact]
        public void CE2294_PlatoonBound_OrdersTheSupportSquad_ThenTheAssaultOnlyOnItsSet()
        {
            var p = new Platoon();
            p.Bound(sitrep: 15f);
            p.Ticks(3);
            Assert.Equal(BehaviorNames.PlatoonBound, p.S.TaskName);
            Assert.Equal(1, p.OrdersTo(p.Bravo, "Order.Move"));
            Assert.Equal(0, p.OrdersTo(p.Alpha, "Order.Move"));
            p.Ticks(60);
            Assert.Equal(1, p.OrdersTo(p.Bravo, "Order.Move"));          // the order stands: not re-sent
            Assert.Equal(0, p.OrdersTo(p.Alpha, "Order.Move"));

            p.ReportSet(p.Bravo);
            p.Ticks(3);
            Assert.Equal(1, p.OrdersTo(p.Alpha, "Order.Move"));
            var move = System.Text.Json.JsonSerializer.Deserialize<Hrot.AI.Behaviors.Orders.MoveOrder>(
                p.S.Orders.Last(o => o.Entity.Equals(p.Alpha)).JsonParams, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
            var back = p.S.Repo.GetSingletonManaged<IGeographicTransform>().ToCartesian(move.Location.Latitude, move.Location.Longitude, 0.0);
            Assert.InRange(Vector2.Distance(new Vector2(back.X, back.Y), new Vector2(50f, 200f)), 0f, 0.1f);

            p.ReportSet(p.Alpha, observedAgo: 5f);                       // observed before Alpha's order: the last order's
            p.Ticks(3);
            Assert.False(p.S.Finished(out _));
            Assert.Equal(BehaviorNames.PlatoonBound, p.S.TaskName);

            p.ReportSet(p.Alpha);
            Fbt.NodeStatus? result = null;
            for (int i = 0; i < 6 && result == null; i++) { p.Tick(); if (p.S.Finished(out var r)) result = r; }
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }

        /// <summary>⭐⭐ <c>CE-2294</c> CC-D (cc-platoon-bound ③④) — the platoon's SOP (<c>BasicPlatoonSop</c>) takes a reported enemy team as
        /// the edge <c>ReportReceived</c> + <c>ElementReported</c> and REACTS with <c>PlatoonHold</c>: the task pauses, BOTH squads are
        /// ordered to hold (once — the order stands); the lost SPOT ends the hold, its orders are released, and the task RESUMES in its state
        /// and orders its leg's squad (Alpha) again — Bravo, whose leg is behind it, is not re-ordered. ✅ Red-proof: <c>MoveLeg</c> never
        /// re-ordering (orders given once per state) ⇒ Alpha left un-ordered after the hold.</summary>
        [Fact]
        public void CE2294_ASpotPausesTheTask_BothSquadsHold_ThenTheTaskResumesAndReordersItsLeg()
        {
            var p = new Platoon();
            p.S.StartSop("BasicPlatoonSop");
            p.Bound();
            p.Ticks(3);
            p.ReportSet(p.Bravo);
            p.Ticks(3);
            Assert.Equal(1, p.OrdersTo(p.Alpha, "Order.Move"));

            p.ReportSpot(p.Alpha, new Vector3(0f, 250f, 0f), 3);
            p.Ticks(5);
            Assert.Equal(BehaviorNames.PlatoonHold, p.S.TaskName);
            Assert.True(p.S.Repo.HasComponent<PausedTask>(p.S.Commander));
            Assert.Equal((1, 1), (p.OrdersTo(p.Alpha, "Order.Hold"), p.OrdersTo(p.Bravo, "Order.Hold")));
            p.Ticks(60);
            Assert.Equal((1, 1), (p.OrdersTo(p.Alpha, "Order.Hold"), p.OrdersTo(p.Bravo, "Order.Hold")));   // the hold stands

            p.ReportSpot(p.Alpha, new Vector3(0f, 250f, 0f), 0);         // lost: the element is gone
            p.Ticks(6);
            Assert.Equal(BehaviorNames.PlatoonBound, p.S.TaskName);
            Assert.Contains(p.S.Clears, c => c.Entity.Equals(p.Alpha));
            Assert.Contains(p.S.Clears, c => c.Entity.Equals(p.Bravo));
            Assert.Equal(2, p.OrdersTo(p.Alpha, "Order.Move"));          // the leg re-ordered on resume
            Assert.Equal(1, p.OrdersTo(p.Bravo, "Order.Move"));          // the support leg is behind the task

            p.ReportSet(p.Alpha);
            Fbt.NodeStatus? result = null;
            for (int i = 0; i < 6 && result == null; i++) { p.Tick(); if (p.S.Finished(out var r)) result = r; }
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }

        /// <summary>⭐⭐ <c>CE-2288</c> (CC-16, R-2050) — every slot of a commander, named: the running task's params DECODED (PlatoonBound's
        /// legs), the SOP's slots, and — once a reaction pauses the task — the paused task's slots under their own owner, the reaction's as
        /// the task's. ✅ Red-proof: the catalog naming no candidates ⇒ every slot Unknown.</summary>
        [Fact]
        public void CE2288_EverySlotOfACommander_NamedByOwnerAndRole_TheParamsDecoded()
        {
            var p = new Platoon();
            p.S.StartSop("BasicPlatoonSop");
            p.Bound();
            p.Ticks(3);
            var slots = Fdp.Toolkit.Behavior.OccurrenceSlotCatalog.Read(p.S.Repo, p.S.Commander, p.S.Registry);
            Assert.NotEmpty(slots);
            var task = Assert.Single(slots, x => x.Owner == Fdp.Toolkit.Behavior.SlotOwnerKind.Task && x.Role == Fdp.Toolkit.Behavior.SlotRole.Params);
            Assert.Equal(BehaviorNames.PlatoonBound, task.Behaviour);
            var bb = Assert.IsType<Dictionary<string, object?>>(task.Value);              // the diagnostic mapper's shape
            var support = Assert.IsType<Dictionary<string, object?>>(bb["Support"]);
            Assert.Equal(1, support["Slot"]);                                            // Bravo supports
            Assert.Contains(slots, x => x.Owner == Fdp.Toolkit.Behavior.SlotOwnerKind.Sop && x.Behaviour == "BasicPlatoonSop");

            p.ReportSet(p.Bravo);
            p.Ticks(3);
            p.ReportSpot(p.Alpha, new Vector3(0f, 250f, 0f), 3);
            p.Ticks(5);
            Assert.Equal(BehaviorNames.PlatoonHold, p.S.TaskName);
            slots = Fdp.Toolkit.Behavior.OccurrenceSlotCatalog.Read(p.S.Repo, p.S.Commander, p.S.Registry);
            Assert.Contains(slots, x => x.Owner == Fdp.Toolkit.Behavior.SlotOwnerKind.PausedTask && x.Behaviour == BehaviorNames.PlatoonBound);
            Assert.Contains(slots, x => x.Owner == Fdp.Toolkit.Behavior.SlotOwnerKind.Task && x.Behaviour == BehaviorNames.PlatoonHold);
        }

        /// <summary>⭐ <c>CE-2294</c> (R-214, CV-8) — only contacts still IN THE FIGHT make the element: dead men stay in memory by design
        /// and a neutral is no enemy, so neither is SPOTted; when the last enemy dies the squad sends the lost SPOT and the platoon's
        /// picture empties. 📌 Found live in cc-platoon-bound (three dead enemies, a count-3 SPOT every 15 s, the hold never ending).
        /// ✅ Red-proof: the in-the-fight filter removed ⇒ the dead are still an element.</summary>
        [Fact]
        public void CE2294_DeadOrNeutralContacts_AreNoElement_TheLastDeathClearsThePicture()
        {
            var c = new Chain(members: 3);
            var enemies = c.S.PoolMany(3, new Vector3(0f, 200f, 0f));
            foreach (var e in enemies) c.S.Repo.AddComponent(e, new Fdp.Toolkit.Combat.Components.Health { Current = 100f, Max = 100f });
            c.S.Repo.AddComponent(enemies[2], new EntityInfo { ForceId = ForceId.Neutral });
            c.Ticks(2); c.S.Pause(3.1); c.Ticks(2);
            Assert.Equal(1, c.Picture.Elements.Count);
            Assert.Equal((byte)2, c.Picture.Elements.Items[0].Count);                    // the neutral is not counted

            foreach (var e in enemies) c.S.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.Health>(e).Current = 0f;
            c.Ticks(2); c.S.Pause(3.1); c.Ticks(2);
            Assert.Equal(0, c.Picture.Elements.Count);                                   // the lost SPOT, at once (not at the refresh)
        }

        /// <summary>⭐ <c>CE-2294</c> (CB-33, CB-34) — a squad re-SPOTs an element it still holds every 15 s, so the platoon's copy stays
        /// fresh while it is there (a stale one then means it is gone). ✅ Red-proof: the refresh check removed ⇒ no second SPOT.</summary>
        [Fact]
        public void CE2294_AHeldElement_IsReSpotted_Every15Seconds()
        {
            var c = new Chain(members: 3);
            c.S.PoolMany(3, new Vector3(0f, 200f, 0f));
            c.Ticks(2); c.S.Pause(3.1); c.Ticks(2);
            int sent = c.Composer.SentCount;
            float first = c.Picture.Elements.Items[0].ObservedAt;
            c.S.Pause(8); c.Ticks(2); c.S.Pause(3.1); c.Ticks(2);
            Assert.Equal(sent, c.Composer.SentCount);                    // ~11 s: not yet
            c.S.Pause(5); c.Ticks(2); c.S.Pause(3.1); c.Ticks(2);
            Assert.Equal(sent + 1, c.Composer.SentCount);
            Assert.Equal(1, c.Picture.Elements.Count);
            Assert.True(c.Picture.Elements.Items[0].ObservedAt > first + 14f);
        }

        // ── CE-2276 E-1 — PlatoonDefend: the platoon holds a position over its squads (Defend & Attack §8d) ─────────────────────

        /// <summary>The order: defend the origin, the engagement area north (330°…30°, out to 150 m), no squads named (DA-51 defaults).</summary>
        private static void Defend(Platoon p)
            => p.S.Order("{\"Position\":[0,0,0],\"Left\":330,\"Right\":30,\"MaxRange\":150}", BehaviorNames.PlatoonDefend);

        private static Fdp.Toolkit.Behavior.Components.RoeFire FireOf(Platoon p, Entity squad)
            => p.S.Repo.HasComponent<Fdp.Toolkit.Behavior.Components.Roe>(squad)
                ? p.S.Repo.GetComponentRO<Fdp.Toolkit.Behavior.Components.Roe>(squad).Fire : Fdp.Toolkit.Behavior.Components.RoeFire.FireUnset;

        private static Fdp.Toolkit.Behavior.Components.EngagementArea SectorOf(Platoon p, Entity squad)
            => p.S.Repo.HasComponent<Fdp.Toolkit.Behavior.Components.EngagementArea>(squad)
                ? p.S.Repo.GetComponentRO<Fdp.Toolkit.Behavior.Components.EngagementArea>(squad) : default;

        private static SquadDefendParamsJsonDto DefendOrderTo(Platoon p, Entity squad)
            => System.Text.Json.JsonSerializer.Deserialize<SquadDefendParamsJsonDto>(
                   p.S.Orders.Last(o => o.Entity.Equals(squad) && o.IntentId == BehaviorNames.SquadDefend).JsonParams,
                   Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);

        /// <summary>⭐⭐ <c>CE-2276</c> E-1 (DA-51, DA-52, DA-54) — three squads, none named: the first two hold the line at the position and
        /// each gets one half of the area (30° each, 10 % wider: 328.5°…1.5° and 358.5°…31.5°), the third is the reserve 40 m behind with no
        /// sector; the line holds fire, the reserve returns fire; each order is given once and kept. ✅ Red-proofs: no overlap ⇒ the slices
        /// meet edge to edge · every squad on the line ⇒ the reserve gets a sector · the order re-sent every tick ⇒ more than one order.</summary>
        [Fact]
        public void CE2276_PlatoonDefend_OrdersEverySquad_SplitsTheAreaAmongTheLine_AndHoldsItsFire()
        {
            var p = new Platoon(squads: 3);
            Defend(p);
            p.Ticks(6);
            Assert.Equal((1, 1, 1), (p.OrdersTo(p.Alpha, BehaviorNames.SquadDefend), p.OrdersTo(p.Bravo, BehaviorNames.SquadDefend),
                                     p.OrdersTo(p.Charlie, BehaviorNames.SquadDefend)));
            var a = SectorOf(p, p.Alpha);
            var b = SectorOf(p, p.Bravo);
            Assert.Equal((1, 328.5f, 1.5f, 150f), (a.ArcSet, MathF.Round(a.LeftBearing, 2), MathF.Round(a.RightBearing, 2), a.MaxRange));
            Assert.Equal((1, 358.5f, 31.5f), (b.ArcSet, MathF.Round(b.LeftBearing, 2), MathF.Round(b.RightBearing, 2)));
            Assert.Equal(0, SectorOf(p, p.Charlie).ArcSet);
            var reserve = DefendOrderTo(p, p.Charlie).Position;
            Assert.True(Vector3.Distance(reserve, new Vector3(0f, -40f, 0f)) < 0.01f, $"the reserve's point {reserve}");
            Assert.Equal(Vector3.Zero, DefendOrderTo(p, p.Alpha).Position);
            Assert.Equal((Fdp.Toolkit.Behavior.Components.RoeFire.HoldFire, Fdp.Toolkit.Behavior.Components.RoeFire.HoldFire,
                          Fdp.Toolkit.Behavior.Components.RoeFire.ReturnFire), (FireOf(p, p.Alpha), FireOf(p, p.Bravo), FireOf(p, p.Charlie)));
            p.Ticks(10);
            Assert.Equal(1, p.OrdersTo(p.Alpha, BehaviorNames.SquadDefend));
        }

        /// <summary>⭐⭐ <c>CE-2276</c> E-1 (DA-53) — once the line is set, a SPOT OUTSIDE the area changes nothing; one INSIDE opens the line's
        /// fire (the reserve keeps returning fire only); 120 s with nothing inside and the line holds again. ✅ Red-proofs: the area ignored
        /// ⇒ the outside SPOT opens fire · the quiet guard never true ⇒ the line keeps firing at will.</summary>
        [Fact]
        public void CE2276_PlatoonDefend_AnEnemyInsideTheArea_OpensTheLinesFire_OutsideItDoesNot_AndQuietClosesItAgain()
        {
            var p = new Platoon(squads: 3);
            Defend(p);
            p.Ticks(6);
            p.ReportSet(p.Alpha); p.ReportSet(p.Bravo);
            p.Ticks(3);
            p.ReportSpot(p.Alpha, new Vector3(0f, -100f, 0f), 2);   // south: behind the position
            p.Ticks(3);
            Assert.Equal(Fdp.Toolkit.Behavior.Components.RoeFire.HoldFire, FireOf(p, p.Alpha));
            p.ReportSpot(p.Bravo, new Vector3(20f, 100f, 0f), 2);   // ~11°, 102 m: inside
            p.Ticks(3);
            Assert.Equal((Fdp.Toolkit.Behavior.Components.RoeFire.FireAtWill, Fdp.Toolkit.Behavior.Components.RoeFire.FireAtWill,
                          Fdp.Toolkit.Behavior.Components.RoeFire.ReturnFire), (FireOf(p, p.Alpha), FireOf(p, p.Bravo), FireOf(p, p.Charlie)));
            p.S.Pause(PlatoonDefendBehavior.QuietSeconds + 1f);
            p.Ticks(3);
            Assert.Equal(Fdp.Toolkit.Behavior.Components.RoeFire.HoldFire, FireOf(p, p.Alpha));
        }

        /// <summary>⭐ <c>CE-2276</c> E-1 (DA-53, Platoon §5 Occupy) — the line opens fire only once EVERY line squad is set: an enemy inside
        /// while one is still moving changes nothing; that squad's Set then opens it at once (the SPOT is still fresh). ✅ Red-proof: the
        /// Set guard always true ⇒ fire opens while Bravo is still moving.</summary>
        [Fact]
        public void CE2276_PlatoonDefend_TheLineOpensFireOnlyOnceEveryLineSquadIsSet()
        {
            var p = new Platoon(squads: 3);
            Defend(p);
            p.Ticks(6);
            p.ReportSet(p.Alpha);
            p.ReportSpot(p.Charlie, new Vector3(0f, 120f, 0f), 3);
            p.Ticks(3);
            Assert.Equal(Fdp.Toolkit.Behavior.Components.RoeFire.HoldFire, FireOf(p, p.Alpha));
            p.ReportSet(p.Bravo);
            p.Ticks(4);
            Assert.Equal(Fdp.Toolkit.Behavior.Components.RoeFire.FireAtWill, FireOf(p, p.Alpha));
        }

        /// <summary>⭐ <c>CE-2276</c> E-1 (DA-54, Platoon §5 "below Regular") — a Militia platoon never holds its fire: every squad fires at
        /// will from the start, the reserve too. ✅ Red-proof: the tier ignored ⇒ the militia line holds fire.</summary>
        [Fact]
        public void CE2276_PlatoonDefend_AMilitiaPlatoon_FiresAtWillFromTheStart()
        {
            var p = new Platoon(squads: 3);
            if (!p.S.Repo.IsComponentTypeRegistered<CombatProficiency>()) p.S.Repo.RegisterComponent<CombatProficiency>();
            p.S.Repo.AddComponent(p.S.Commander, new CombatProficiency { Authored = ProficiencyOf.Militia, Set = CombatProficiency.AuthoredSet });
            Defend(p);
            p.Ticks(6);
            Assert.Equal((Fdp.Toolkit.Behavior.Components.RoeFire.FireAtWill, Fdp.Toolkit.Behavior.Components.RoeFire.FireAtWill),
                         (FireOf(p, p.Alpha), FireOf(p, p.Charlie)));
        }

        /// <summary>⭐⭐ <c>CE-2276</c> E-1 (DA-55) — a defending platoon WITH its SOP (<c>BasicPlatoonSop</c>): an enemy reported inside the
        /// area does not pause the defence for <c>PlatoonHold</c> (no squad is ordered off its post to hold) — the task stays on and opens
        /// the line's fire. ✅ Red-proof: the guard always true ⇒ the hold reaction pauses the defence and orders every squad Hold.</summary>
        [Fact]
        public void CE2276_PlatoonDefend_WithItsSop_AReportedEnemy_DoesNotPauseTheDefence()
        {
            var p = new Platoon(squads: 3);
            p.S.StartSop("BasicPlatoonSop");
            Defend(p);
            p.Ticks(6);
            p.ReportSet(p.Alpha); p.ReportSet(p.Bravo);
            p.Ticks(3);
            p.ReportSpot(p.Bravo, new Vector3(20f, 100f, 0f), 3);
            p.Ticks(6);
            Assert.Equal(BehaviorNames.PlatoonDefend, p.S.TaskName);
            Assert.False(p.S.Repo.HasComponent<PausedTask>(p.S.Commander));
            Assert.Equal(0, p.OrdersTo(p.Alpha, "Order.Hold"));
            Assert.Equal(Fdp.Toolkit.Behavior.Components.RoeFire.FireAtWill, FireOf(p, p.Alpha));
        }

        // ── CE-2276 E-2 — the control region (Defend & Attack §8d E-2, DA-59…DA-65) ─────────────────────────────────────────────

        private static void Sitrep(Platoon p, Entity from, Fdp.Toolkit.Squad.Command.StatusColour strength)
            => p.Report(from, new Fdp.Toolkit.Squad.Command.CommandReport { Kind = Fdp.Toolkit.Squad.Command.ReportKind.Sitrep, Strength = strength });

        private static int DefendOrders(Platoon p, Entity squad) => p.OrdersTo(squad, BehaviorNames.SquadDefend);

        /// <summary>⭐⭐ <c>CE-2276</c> E-2 (DA-59) — an OP forward at (0, 250): a SPOT 350 m from it changes nothing; one 250 m from it brings
        /// it back to its <c>Back</c> point (0, −30), where it is a reserve (no sector) and is ordered once. ✅ Red-proof: the OP's 300 m
        /// ignored ⇒ the far SPOT brings it back.</summary>
        [Fact]
        public void CE2276_E2_AnEnemyNearTheOp_BringsItBack_AsAReserve()
        {
            var p = new Platoon(squads: 3);
            p.S.Order("{\"Position\":[0,0,0],\"Left\":330,\"Right\":30,\"MaxRange\":400,\"Squads\":[{\"Squad\":1},{\"Squad\":2},"
                    + "{\"Squad\":3,\"Op\":true,\"Point\":[0,250,0],\"Back\":[0,-30,0]}]}", BehaviorNames.PlatoonDefend);
            p.Ticks(6);
            Assert.Equal(new Vector3(0, 250, 0), DefendOrderTo(p, p.Charlie).Position);
            p.ReportSpot(p.Alpha, new Vector3(0f, 600f, 0f), 2);
            p.Ticks(4);
            Assert.Equal(1, DefendOrders(p, p.Charlie));
            p.ReportSpot(p.Alpha, new Vector3(0f, 500f, 0f), 2);
            p.Ticks(4);
            Assert.Equal(2, DefendOrders(p, p.Charlie));
            Assert.Equal(new Vector3(0, -30, 0), DefendOrderTo(p, p.Charlie).Position);
            Assert.Equal(0, SectorOf(p, p.Charlie).ArcSet);
            p.Ticks(10);
            Assert.Equal(2, DefendOrders(p, p.Charlie));   // once
        }

        /// <summary>⭐⭐ <c>CE-2276</c> E-2 (DA-60) — a SPOT 100 m east of a north-facing position is a flank: the nearest line squad (Alpha)
        /// is ordered again with a slice centred on it; a second one west turns Bravo; a third turns nobody (both turned), and an enemy in
        /// front turns nobody. ✅ Red-proofs: the area's bearings ignored ⇒ the front SPOT turns a squad · the once-per-squad mark dropped ⇒
        /// Alpha turned twice · the faced test dropped ⇒ the east SPOT turns Bravo too.</summary>
        [Fact]
        public void CE2276_E2_AFlankEnemy_TurnsTheNearestLineSquadToFaceIt_OncePerSquad()
        {
            var p = new Platoon(squads: 3);
            Defend(p);
            p.Ticks(6);
            p.ReportSpot(p.Alpha, new Vector3(10f, 100f, 0f), 2);   // in front: inside the area's bearings
            p.Ticks(4);
            Assert.Equal((1, 1), (DefendOrders(p, p.Alpha), DefendOrders(p, p.Bravo)));
            p.ReportSpot(p.Bravo, new Vector3(100f, 0f, 0f), 2);    // east, 100 m
            p.Ticks(12);
            Assert.Equal((2, 1), (DefendOrders(p, p.Alpha), DefendOrders(p, p.Bravo)));   // Alpha faces it; Bravo is not turned too
            Assert.True(Fdp.Toolkit.Behavior.Components.EngagementAreas.InArc(Vector3.Zero, SectorOf(p, p.Alpha).LeftBearing,
                SectorOf(p, p.Alpha).RightBearing, 0f, 0f, new Vector3(100f, 0f, 0f)), "Alpha's slice faces the flank");
            p.ReportSpot(p.Alpha, new Vector3(-100f, 0f, 0f), 2);   // west
            p.Ticks(12);
            Assert.Equal((2, 2), (DefendOrders(p, p.Alpha), DefendOrders(p, p.Bravo)));
            p.ReportSpot(p.Bravo, new Vector3(0f, -100f, 0f), 2);   // south: nobody left to turn
            p.Ticks(12);
            Assert.Equal((2, 2, 1), (DefendOrders(p, p.Alpha), DefendOrders(p, p.Bravo), DefendOrders(p, p.Charlie)));
        }

        /// <summary>⭐⭐ <c>CE-2276</c> E-2 (DA-61, DA-63) — every squad is asked for a 15 s SITREP; Bravo reports RED but the enemy is not in its
        /// slice ⇒ nothing; an enemy in its slice ⇒ the reserve (Charlie) goes 15 m behind Bravo with Bravo's slice and fires at will; once.
        /// ✅ Red-proofs: the colour ignored ⇒ the reserve commits on the first SPOT · the slice test dropped ⇒ it commits at once.</summary>
        [Fact]
        public void CE2276_E2_ALineSquadReportingRed_WithTheEnemyInItsSlice_GetsTheReserveBehindIt()
        {
            var p = new Platoon(squads: 3);
            Defend(p);
            p.Ticks(6);
            Assert.Equal(PlatoonDefendBehavior.SitrepSeconds,
                p.S.Repo.GetComponentRO<Fdp.Toolkit.Squad.Command.ReportingRequirements>(p.Bravo).SitrepEverySeconds);
            p.ReportSpot(p.Alpha, new Vector3(30f, 100f, 0f), 2);   // ~17°: inside Bravo's slice (358.5°…31.5°), not Alpha's
            p.Ticks(4);
            Assert.Equal(1, DefendOrders(p, p.Charlie));
            Sitrep(p, p.Alpha, Fdp.Toolkit.Squad.Command.StatusColour.Red);   // Alpha is red, but nothing in ITS slice
            p.Ticks(4);
            Assert.Equal(1, DefendOrders(p, p.Charlie));
            Sitrep(p, p.Bravo, Fdp.Toolkit.Squad.Command.StatusColour.Red);
            p.Ticks(4);
            Assert.Equal(2, DefendOrders(p, p.Charlie));
            var at = DefendOrderTo(p, p.Charlie).Position;
            Assert.InRange(Vector3.Distance(at, Vector3.Zero), 14.9f, 15.1f);
            Assert.True(at.Y < 0f, "behind the squad, away from its slice");
            Assert.Equal((SectorOf(p, p.Bravo).LeftBearing, SectorOf(p, p.Bravo).RightBearing),
                         (SectorOf(p, p.Charlie).LeftBearing, SectorOf(p, p.Charlie).RightBearing));
            Assert.Equal(Fdp.Toolkit.Behavior.Components.RoeFire.FireAtWill, FireOf(p, p.Charlie));
            Sitrep(p, p.Alpha, Fdp.Toolkit.Squad.Command.StatusColour.Black);
            p.ReportSpot(p.Alpha, new Vector3(-20f, 100f, 0f), 2);   // Alpha's slice too: no second reserve
            p.Ticks(4);
            Assert.Equal(2, DefendOrders(p, p.Charlie));
        }

        /// <summary>⭐ <c>CE-2276</c> E-2 (DA-61 "once") — with TWO reserves, a second failing line squad gets no second reserve: the
        /// platoon commits its reserve once. ✅ Red-proof: the <c>Committed</c> latch dropped ⇒ the second reserve goes behind Alpha.</summary>
        [Fact]
        public void CE2276_E2_TwoReserves_TheReserveIsCommittedOnce()
        {
            var p = new Platoon(squads: 4);
            p.S.Order("{\"Position\":[0,0,0],\"Left\":330,\"Right\":30,\"MaxRange\":150,\"Squads\":[{\"Squad\":1},{\"Squad\":2},"
                    + "{\"Squad\":3,\"Reserve\":true,\"Point\":[0,-50,0]},{\"Squad\":4,\"Reserve\":true,\"Point\":[10,-50,0]}]}",
                      BehaviorNames.PlatoonDefend);
            p.Ticks(6);
            var delta = p.S.Members[3];
            p.ReportSpot(p.Alpha, new Vector3(30f, 100f, 0f), 2);    // Bravo's slice
            Sitrep(p, p.Bravo, Fdp.Toolkit.Squad.Command.StatusColour.Red);
            p.Ticks(12);
            Assert.Equal(3, DefendOrders(p, p.Charlie) + DefendOrders(p, delta));   // one reserve re-ordered
            p.ReportSpot(p.Bravo, new Vector3(-20f, 100f, 0f), 2);   // Alpha's slice
            Sitrep(p, p.Alpha, Fdp.Toolkit.Squad.Command.StatusColour.Black);
            p.Ticks(12);
            Assert.Equal(3, DefendOrders(p, p.Charlie) + DefendOrders(p, delta));   // and no second
        }

        /// <summary>⭐ <c>CE-2276</c> E-2 (DA-65) — a Militia platoon's control region does nothing: a flank SPOT turns nobody, a red squad
        /// with the enemy in its slice gets no reserve. ✅ Red-proof: the tier ignored ⇒ the flank turns Alpha.</summary>
        [Fact]
        public void CE2276_E2_AMilitiaPlatoon_NeverSupplementsOrReinforces()
        {
            var p = new Platoon(squads: 3);
            if (!p.S.Repo.IsComponentTypeRegistered<CombatProficiency>()) p.S.Repo.RegisterComponent<CombatProficiency>();
            p.S.Repo.AddComponent(p.S.Commander, new CombatProficiency { Authored = ProficiencyOf.Militia, Set = CombatProficiency.AuthoredSet });
            Defend(p);
            p.Ticks(6);
            p.ReportSpot(p.Alpha, new Vector3(100f, 0f, 0f), 2);
            p.ReportSpot(p.Bravo, new Vector3(30f, 100f, 0f), 2);
            Sitrep(p, p.Bravo, Fdp.Toolkit.Squad.Command.StatusColour.Red);
            p.Ticks(6);
            Assert.Equal((1, 1, 1), (DefendOrders(p, p.Alpha), DefendOrders(p, p.Bravo), DefendOrders(p, p.Charlie)));
        }

        // ── CE-2276 E-3a — FallBack (Defend & Attack §8d E-3a, DA-66…DA-71) ───────────────────────────────────────────────────

        private static Hrot.Map.Definitions.Behavior.BreakContactParamsJsonDto BreakOrderTo(Platoon p, Entity squad)
            => System.Text.Json.JsonSerializer.Deserialize<Hrot.Map.Definitions.Behavior.BreakContactParamsJsonDto>(
                   p.S.Orders.Last(o => o.Entity.Equals(squad) && o.IntentId == BehaviorNames.BreakContact).JsonParams,
                   Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);

        private static void EngagedThreeLines(Platoon p)
        {
            Defend(p);
            p.Ticks(6);
            p.ReportSet(p.Alpha); p.ReportSet(p.Bravo); p.ReportSet(p.Charlie);
            p.ReportSpot(p.Charlie, new Vector3(30f, 100f, 0f), 3);   // ~17°: inside the area, Charlie's slice only
            p.Ticks(8);
        }

        /// <summary>⭐⭐ <c>CE-2276</c> E-3a (DA-66…DA-70) — three line squads and a reserve, engaged: one line squad Red is not half ⇒ nothing;
        /// a second ⇒ FALL BACK: every line squad <c>BreakContact</c>s to the default point 100 m behind (0, −100), the reserve
        /// <c>SquadDefend</c>s there and fires at will; every line squad's Set on that order ends the run in Success. ✅ Red-proofs: "half"
        /// read as "any" ⇒ the first Red falls back · the end guard dropped ⇒ the run never finishes.</summary>
        [Fact]
        public void CE2276_E3a_HalfTheLineRed_FallsBack_BehindTheReserve_AndEndsWhenTheLineIsBack()
        {
            var p = new Platoon(squads: 4);
            var delta = p.S.Members[3];
            EngagedThreeLines(p);
            Assert.Equal(Fdp.Toolkit.Behavior.Components.RoeFire.FireAtWill, FireOf(p, p.Alpha));   // engaged
            Sitrep(p, p.Alpha, Fdp.Toolkit.Squad.Command.StatusColour.Red);
            p.Ticks(8);
            Assert.Equal(0, p.OrdersTo(p.Alpha, BehaviorNames.BreakContact));
            Sitrep(p, p.Bravo, Fdp.Toolkit.Squad.Command.StatusColour.Black);
            p.Ticks(8);
            Assert.Equal((1, 1, 1), (p.OrdersTo(p.Alpha, BehaviorNames.BreakContact), p.OrdersTo(p.Bravo, BehaviorNames.BreakContact),
                                     p.OrdersTo(p.Charlie, BehaviorNames.BreakContact)));
            Assert.InRange(Vector3.Distance(new Vector3(0f, -100f, 0f), BreakOrderTo(p, p.Alpha).To), 0f, 0.01f);
            Assert.InRange(Vector3.Distance(new Vector3(0f, -100f, 0f), DefendOrderTo(p, delta).Position), 0f, 0.01f);
            Assert.Equal(Fdp.Toolkit.Behavior.Components.RoeFire.FireAtWill, FireOf(p, delta));
            Assert.Equal(0, SectorOf(p, p.Alpha).ArcSet);   // no slice while falling back
            p.Ticks(20);
            Assert.Equal(1, p.OrdersTo(p.Alpha, BehaviorNames.BreakContact));   // kept, not re-sent
            Assert.False(p.S.Finished(out _));
            p.ReportSet(p.Alpha); p.ReportSet(p.Bravo);
            p.Ticks(4);
            Assert.Equal(BehaviorNames.PlatoonDefend, p.S.TaskName);   // Charlie is not back yet
            p.ReportSet(p.Charlie);
            Fbt.NodeStatus? result = null;
            for (int i = 0; i < 8 && result == null; i++) { p.Tick(); if (p.S.Finished(out var r)) result = r; }
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }

        /// <summary>⭐ <c>CE-2276</c> E-3a (DA-66, DA-71) — an authored <c>FallBackTo</c> is where the line goes; while falling back the
        /// control region rests: a flank SPOT turns nobody. ✅ Red-proof: the <c>Falling</c> test dropped from <c>Flanked</c> ⇒ a line squad
        /// is ordered to <c>SquadDefend</c> again mid-withdrawal.</summary>
        [Fact]
        public void CE2276_E3a_AnAuthoredFallBackPoint_AndTheControlRegionRestsWhileFallingBack()
        {
            var p = new Platoon(squads: 2);
            p.S.Order("{\"Position\":[0,0,0],\"Left\":330,\"Right\":30,\"MaxRange\":150,\"FallBackTo\":[50,-80,0]}", BehaviorNames.PlatoonDefend);
            p.Ticks(6);
            p.ReportSet(p.Alpha); p.ReportSet(p.Bravo);
            p.ReportSpot(p.Alpha, new Vector3(0f, 100f, 0f), 3);
            p.Ticks(8);
            Sitrep(p, p.Bravo, Fdp.Toolkit.Squad.Command.StatusColour.Red);   // one of two is half
            p.Ticks(8);
            Assert.Equal(new Vector3(50f, -80f, 0f), BreakOrderTo(p, p.Bravo).To);
            p.ReportSpot(p.Bravo, new Vector3(100f, 0f, 0f), 2);   // a flank
            p.Ticks(12);
            Assert.Equal((1, 1), (DefendOrders(p, p.Alpha), DefendOrders(p, p.Bravo)));
            Assert.Equal((1, 1), (p.OrdersTo(p.Alpha, BehaviorNames.BreakContact), p.OrdersTo(p.Bravo, BehaviorNames.BreakContact)));
        }

        // ── CE-2276 E-3b — block the stair (Defend & Attack §8d E-3b, DA-72…DA-76) ────────────────────────────────────────────

        /// <summary>The defended building as a survey answer: building 7 round the position, two storeys of one 10 m room (floors 0 and 3),
        /// a stair from the ground floor at (4, 4, 1.5) and one higher at (4, −4, 4.5) — or no stair at all.</summary>
        private static void AnswerBuilding(Platoon p, bool stairs = true)
        {
            var survey = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(p.S.Repo, p.S.Commander, PlatoonDefendBehavior.BuildingSite);
            Assert.False(survey.IsNull, "the platoon surveys its building");
            var answer = new Fdp.Toolkit.Perception.Survey.WorldSurveyPayload { Kind = Fdp.Toolkit.Perception.Survey.WorldSurveyKind.BuildingSurvey };
            answer.Buildings.Add(new Fdp.Toolkit.Perception.Survey.SurveyBuilding { Building = 7, CentreX = 0f, CentreY = 0f });
            foreach (float floor in new[] { 0f, 3f })
                answer.Rooms.Add(new Fdp.Toolkit.Perception.Survey.SurveyRoom
                {
                    Building = 7, FloorZ = floor, Indoors = true, Corners = new[] { -5f, -5f, 5f, -5f, 5f, 5f, -5f, 5f },
                });
            if (stairs)
            {
                answer.Portals.Add(new Fdp.Toolkit.Perception.Survey.SurveyPortal { Building = 7, Kind = 3, X = 4f, Y = -4f, Z = 4.5f });
                answer.Portals.Add(new Fdp.Toolkit.Perception.Survey.SurveyPortal { Building = 7, Kind = 3, X = 4f, Y = 4f, Z = 1.5f });
            }
            answer.Portals.Add(new Fdp.Toolkit.Perception.Survey.SurveyPortal { Building = 7, Kind = 0, X = 0f, Y = -5f, Z = 1f });   // a door
            var now = p.S.Repo.GetSingleton<GlobalTime>().TotalTime;
            p.S.Repo.SetComponent(survey, new Fdp.Toolkit.Perception.Survey.WorldSurveyStatus { LastUpdateTick = 1, LastUpdateTimeSeconds = (float)now, Version = 1 });
            p.S.Repo.SetManagedComponent(survey, new Fdp.Toolkit.Perception.Survey.WorldSurveyAnswer { Payload = answer });
        }

        /// <summary>⭐⭐ <c>CE-2276</c> E-3b (DA-72…DA-74) — the platoon surveys the building round its position (an area ask, 5 m); an enemy
        /// outside it changes nothing; one reported INSIDE (a room, at its storey) sends the reserve to <c>SquadDefend</c> the LOWEST stair,
        /// radius 6 m, firing at will; a second one inside changes nothing (once). ✅ Red-proofs: the inside test dropped ⇒ the outside SPOT
        /// blocks · the lowest-stair choice dropped ⇒ the upper stair · the Committed test dropped ⇒ a second order.</summary>
        [Fact]
        public void CE2276_E3b_AnEnemyInsideTheBuilding_SendsTheReserveToBlockTheLowestStair_Once()
        {
            var p = new Platoon(squads: 3);
            Defend(p);
            p.Ticks(6);
            var survey = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(p.S.Repo, p.S.Commander, PlatoonDefendBehavior.BuildingSite);
            var request = ((Fdp.ModuleHost.Abstractions.ISimulationView)p.S.Repo).GetManagedComponentRO<Fdp.Toolkit.Perception.Survey.WorldSurveyRequest>(survey);
            Assert.Equal((Fdp.Toolkit.Perception.Survey.WorldSurveyKind.BuildingSurvey, 5f), (request.Kind, request.AreaRadius));
            AnswerBuilding(p);
            p.ReportSpot(p.Alpha, new Vector3(0f, 100f, 0f), 3);    // in the area, outside the building
            p.Ticks(12);
            Assert.Equal(1, DefendOrders(p, p.Charlie));
            p.ReportSpot(p.Bravo, new Vector3(2f, 2f, 0.5f), 2);    // inside, the ground floor
            p.Ticks(12);
            Assert.Equal(2, DefendOrders(p, p.Charlie));
            var order = DefendOrderTo(p, p.Charlie);
            Assert.Equal((new Vector3(4f, 4f, 1.5f), PlatoonDefendBehavior.StairRadius), (order.Position, order.SearchRadius));
            Assert.Equal(Fdp.Toolkit.Behavior.Components.RoeFire.FireAtWill, FireOf(p, p.Charlie));
            p.ReportSpot(p.Alpha, new Vector3(-2f, 2f, 3.5f), 2);   // inside, upstairs
            p.Ticks(12);
            Assert.Equal(2, DefendOrders(p, p.Charlie));
        }

        /// <summary>⭐ <c>CE-2276</c> E-3b (DA-76) — a building with no stair: an enemy inside sends nobody; and a Militia platoon never asks
        /// for the survey at all. ✅ Red-proofs: the stair required no more ⇒ the reserve is sent somewhere · the tier gate dropped from the
        /// survey ⇒ the militia asks.</summary>
        [Fact]
        public void CE2276_E3b_NoStair_SendsNobody_AndAMilitiaPlatoonNeverSurveys()
        {
            var p = new Platoon(squads: 3);
            Defend(p);
            p.Ticks(6);
            AnswerBuilding(p, stairs: false);
            p.ReportSpot(p.Bravo, new Vector3(2f, 2f, 0.5f), 2);
            p.Ticks(12);
            Assert.Equal(1, DefendOrders(p, p.Charlie));

            var militia = new Platoon(squads: 3);
            if (!militia.S.Repo.IsComponentTypeRegistered<CombatProficiency>()) militia.S.Repo.RegisterComponent<CombatProficiency>();
            militia.S.Repo.AddComponent(militia.S.Commander, new CombatProficiency { Authored = ProficiencyOf.Militia, Set = CombatProficiency.AuthoredSet });
            Defend(militia);
            militia.Ticks(6);
            Assert.True(Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(militia.S.Repo, militia.S.Commander, PlatoonDefendBehavior.BuildingSite).IsNull);
        }

        /// <summary>⭐ <c>CE-2276</c> E-1 (DA-51, DA-52) — the order NAMES the squads: Bravo on the line west, Alpha on the line east, Charlie
        /// the reserve; the slices follow the squads' bearings from the position, not their roster order (Bravo, further left, gets the
        /// left half). ✅ Red-proof: slices in roster order ⇒ Alpha gets the left half.</summary>
        [Fact]
        public void CE2276_PlatoonDefend_NamedSquads_TakeTheirPoints_AndTheSlicesFollowTheirBearings()
        {
            var p = new Platoon(squads: 3);
            p.S.Order("{\"Position\":[0,0,0],\"Left\":330,\"Right\":30,\"MaxRange\":150,\"Squads\":["
                    + "{\"Squad\":1,\"Point\":[30,10,0]},{\"Squad\":2,\"Point\":[-30,10,0]},{\"Squad\":3,\"Reserve\":true,\"Point\":[0,-50,0]}]}",
                      BehaviorNames.PlatoonDefend);
            p.Ticks(6);
            Assert.Equal(new Vector3(30f, 10f, 0f), DefendOrderTo(p, p.Alpha).Position);
            Assert.Equal(new Vector3(0f, -50f, 0f), DefendOrderTo(p, p.Charlie).Position);
            Assert.Equal(328.5f, MathF.Round(SectorOf(p, p.Bravo).LeftBearing, 2));
            Assert.Equal(358.5f, MathF.Round(SectorOf(p, p.Alpha).LeftBearing, 2));
            Assert.Equal(0, SectorOf(p, p.Charlie).ArcSet);
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

        // ── CE-2173 slice 3 (B10; docs/designs/air-attack/DESIGN_Air_Operations.md §9) — the squad Embark drill, the mirror of Dismount ──

        private static Entity Helicopter(Squad s, Vector3 at, long networkId)
        {
            var v = Carrier(s, at);
            s.Repo.AddComponent(v, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = networkId });
            return v;
        }

        /// <summary>⭐ <c>CE-2173</c> — SquadEmbark tells every member to board the named vehicle (the library <c>Embark</c>, at Superior),
        /// keeps running while anybody is out, and succeeds when all are aboard IT; the members' orders are released. ✅ Red-proofs:
        /// counting "aboard ANY vehicle" (a member left in another carrier) ⇒ Success too early ⇒ red; ordering only the members still
        /// out ⇒ a member aboard the wrong vehicle stays there ⇒ red.</summary>
        [Fact]
        public void CE2173_SquadEmbark_EveryMemberIsToldToBoard_DoneWhenAllAreAboardThatVehicle()
        {
            var s = new Squad(members: 3);
            var heli = Helicopter(s, new Vector3(0f, 20f, 0f), 2173);
            var other = Carrier(s, new Vector3(50f, 0f, 0f), 2);                                    // member 2 sits in ANOTHER vehicle
            s.Order("{\"vehicle\":2173}", BehaviorNames.SquadEmbark);
            Ticks(s, 3);

            Assert.Equal(SquadEmbarkManeuver.ManeuverKind, s.Kind);
            var last = s.Orders.GroupBy(o => o.Entity).ToDictionary(g => g.Key, g => g.Last());
            Assert.All(s.Members, m => Assert.Equal(BehaviorNames.Embark, last[m].IntentId));
            Assert.All(last.Values, o => Assert.Contains("2173", o.JsonParams));
            Assert.All(last.Values, o => Assert.Equal(BehaviorOrigin.Superior, o.Origin));

            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(s.Repo, s.Members[0], heli));
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(s.Repo, s.Members[1], heli));
            Assert.Null(RunUntilFinished(s, 5));                                                     // member 2 is in the other one
            Embarkation.Disembark(s.Repo, s.Members[2], other);
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(s.Repo, s.Members[2], heli));
            Assert.Equal(Fbt.NodeStatus.Success, RunUntilFinished(s, 5));
            Assert.Equal(3, s.Repo.GetComponentRO<PassengerBuffer>(heli).Count);
            Ticks(s, 2);
            Assert.Contains(s.Clears, c => c.Entity == s.Members[0]);                                // its orders released (A5)
        }

        /// <summary>⭐ <c>CE-2173</c> — a member carrying a casualty is told <c>LoadCasualty</c> (seat him, then board), and the drill is not
        /// done while he still carries him; somebody who never gets in fails the drill on its timeout; a vehicle that is gone fails it at
        /// once. ✅ Red-proof: giving the carrier plain <c>Embark</c> ⇒ he would board carrying the man (nested) ⇒ red.</summary>
        [Fact]
        public void CE2173_SquadEmbark_ACarrierLoadsHisCasualtyFirst_AStragglerTimesOut_AGoneVehicleFails()
        {
            var s = new Squad(members: 2);
            var heli = Helicopter(s, new Vector3(0f, 20f, 0f), 2174);
            var down = s.Repo.CreateEntity();
            s.Repo.AddComponent(down, new SimTransform { Position = Vector3.Zero, Rotation = Quaternion.Identity });
            s.Repo.AddComponent(s.Members[0], new PassengerBuffer());                                  // member 0 carries him
            s.Repo.GetComponentRW<PassengerBuffer>(s.Members[0]).Passengers[0] = down;
            s.Repo.GetComponentRW<PassengerBuffer>(s.Members[0]).Count = 1;
            s.Order("{\"vehicle\":2174,\"timeoutSeconds\":1}", BehaviorNames.SquadEmbark);
            Ticks(s, 3);
            var last = s.Orders.GroupBy(o => o.Entity).ToDictionary(g => g.Key, g => g.Last());
            Assert.Equal(BehaviorNames.LoadCasualty, last[s.Members[0]].IntentId);
            Assert.Equal(BehaviorNames.Embark, last[s.Members[1]].IntentId);
            Assert.Equal(Fbt.NodeStatus.Failure, RunUntilFinished(s, 120));                          // nobody got in within 1 s

            var t = new Squad(members: 2);
            var gone = Helicopter(t, Vector3.Zero, 2175);
            t.Order("{\"vehicle\":2175}", BehaviorNames.SquadEmbark);
            Ticks(t, 2);
            t.Repo.DestroyEntity(gone);
            Assert.Equal(Fbt.NodeStatus.Failure, RunUntilFinished(t, 5));
        }

        // ── CE-2173 slice 3 (B8, B10; Air Operations §9b) — riding, unloading, and the helicopter brain's hold release ──────────────

        /// <summary>A helicopter (Rotorcraft, a route phase) at <paramref name="at"/> with <paramref name="aboard"/> seated.</summary>
        private static Entity AirCarrier(Squad s, Vector3 at, NavigationPhase phase, long networkId, params int[] aboard)
        {
            if (!s.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Motion.Locomotion>()) s.Repo.RegisterComponent<Fdp.Toolkit.Motion.Locomotion>();
            if (!s.Repo.IsComponentTypeRegistered<NavigationStatus>()) s.Repo.RegisterComponent<NavigationStatus>();
            var v = Carrier(s, at, aboard);
            s.Repo.AddComponent(v, new Fdp.Toolkit.Motion.Locomotion { Class = Fdp.Toolkit.Tkb.Domain.LocomotionClass.Rotorcraft });
            s.Repo.AddComponent(v, new NavigationStatus { Phase = phase });
            s.Repo.AddComponent(v, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = networkId });
            return v;
        }

        private static void SetPhase(Squad s, Entity v, NavigationPhase phase) => s.Repo.GetComponentRW<NavigationStatus>(v).Phase = phase;

        /// <summary>⭐ <c>CE-2173</c> — a squad all aboard a FLYING helicopter rides (<c>SquadRide</c>, a reaction: its task waits); shot at in
        /// the air it rides on — nobody gets out of a flying aircraft; when the helicopter holds at an Unload stop the squad dismounts, and
        /// once everyone is out its task resumes. ✅ Red-proofs: the under-fire dismount row without the on-the-ground gate ⇒ SquadDismount in
        /// the air ⇒ red; no Unloading row ⇒ the squad rides on at the LZ ⇒ red.</summary>
        [Fact]
        public void CE2173_BasicSquadSop_ASquadInAFlyingHelicopter_RidesThroughFire_AndDismountsWhenItUnloads()
        {
            var s = new Squad(members: 3);
            s.Order("{\"Objective\":[0,100,0],\"BoundMetres\":30}");
            s.StartSop("BasicSquadSop");
            Ticks(s, 20);
            var heli = AirCarrier(s, new Vector3(0f, 0f, 80f), NavigationPhase.Following, 2176, 0, 1, 2);
            Ticks(s, 20);   // the SOP decides every 0.2 s (BrainTickSystem.SopTickPeriod)
            Assert.Equal(BehaviorNames.SquadRide, s.TaskName);                                       // all aboard ⇒ ride
            Assert.Equal(BehaviorOrigin.Reaction, s.Repo.GetComponent<BehaviorState>(s.Commander).Origin);

            s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.SquadUnderFire);
            Ticks(s, 20);   // the SOP decides every 0.2 s (BrainTickSystem.SopTickPeriod)
            Assert.Equal(BehaviorNames.SquadRide, s.TaskName);                                       // shot at in the air: ride on

            s.Teleport(heli, Vector3.Zero);
            SetPhase(s, heli, NavigationPhase.Unloading);
            Ticks(s, 20);   // the SOP decides every 0.2 s (BrainTickSystem.SopTickPeriod)
            Assert.Equal(BehaviorNames.SquadDismount, s.TaskName);                                   // the LZ: get out

            foreach (var m in s.Members) Embarkation.Disembark(s.Repo, m);
            Ticks(s, 10);
            Assert.Equal(BehaviorNames.BoundingOverwatch, s.TaskName);                               // out: the task resumes
            Assert.Equal(0, s.Repo.GetComponent<SopState>(s.Commander).SopFaulted);
        }

        /// <summary>⭐ <c>CE-2173</c> — boarding is not paused by the first man in (the Ride row needs EVERY live member aboard); once the last
        /// one is in, the squad rides; under fire at the Pickup stop (<c>Loading</c>, on the ground) it stays aboard — the aircraft is its
        /// way out, and the Unloading row does not fire at a pickup. ✅ Red-proofs: Riding as "any member aboard" ⇒ the boarding is paused
        /// by a Ride ⇒ red; the dismount row accepting a Loading carrier ⇒ SquadDismount ⇒ red.</summary>
        [Fact]
        public void CE2173_BasicSquadSop_BoardingIsNotPausedByTheFirstManIn_AndAtAPickupTheSquadStaysAboard()
        {
            var s = new Squad(members: 3);
            var heli = AirCarrier(s, Vector3.Zero, NavigationPhase.Loading, 2177);
            s.Order("{\"vehicle\":2177}", BehaviorNames.SquadEmbark);
            s.StartSop("BasicSquadSop");
            Ticks(s, 20);   // the SOP decides every 0.2 s (BrainTickSystem.SopTickPeriod)
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(s.Repo, s.Members[0], heli));
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(s.Repo, s.Members[1], heli));
            Ticks(s, 20);   // the SOP decides every 0.2 s (BrainTickSystem.SopTickPeriod)
            Assert.Equal(BehaviorNames.SquadEmbark, s.TaskName);                                     // two of three: still boarding

            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(s.Repo, s.Members[2], heli));
            Ticks(s, 20);   // the SOP decides every 0.2 s (BrainTickSystem.SopTickPeriod)
            Assert.Equal(BehaviorNames.SquadRide, s.TaskName);                                       // all in: ride

            s.Sense(Fdp.Toolkit.Perception.Events.SensorChange.SquadUnderFire);
            Ticks(s, 20);   // the SOP decides every 0.2 s (BrainTickSystem.SopTickPeriod)
            Assert.Equal(BehaviorNames.SquadRide, s.TaskName);                                       // under fire at the pickup: stay in
            Assert.Equal(3, s.Repo.GetComponentRO<PassengerBuffer>(heli).Count);
        }

        /// <summary>⭐ <c>CE-2173</c> — the helicopter's BRAIN ends its holds (the FollowRoute node fills <c>ReleaseHold</c>): an Unload stop when
        /// nobody alive is aboard; a Pickup stop when every live member of the waypoint's unit is aboard THIS helicopter; never while it is
        /// just flying. ✅ Red-proofs: releasing a Pickup on "anybody aboard" ⇒ released with one man ⇒ red; releasing an Unload while a man
        /// is aboard ⇒ red.</summary>
        [Fact]
        public void CE2173_TheHelicoptersBrain_ReleasesAnUnloadWhenEmpty_AndAPickupWhenItsUnitIsAboard()
        {
            var s = new Squad(members: 2);
            s.Repo.AddComponent(s.Commander, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 2180 });
            var heli = AirCarrier(s, new Vector3(5f, 0f, 0f), NavigationPhase.Unloading, 2178, 0);
            var route = s.Repo.CreateEntity();
            s.Repo.AddComponent(route, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 2179 });
            var plan = new Hrot.Map.Common.Components.RoutePlan();
            plan.Mutate(w =>
            {
                w.Add(new Hrot.Map.Common.Components.RouteWaypoint { Position = new Vector3(500f, 0f, 0f), Kind = RouteWaypointKind.Ground, Hold = RouteHold.Unload });
                w.Add(new Hrot.Map.Common.Components.RouteWaypoint { Position = Vector3.Zero, Kind = RouteWaypointKind.Ground, Hold = RouteHold.Pickup, HoldUnit = 2180 });
            });
            s.Repo.SetManagedComponent(route, plan);                                                  // RoutePlan: SimHostComponentRegistry

            Assert.False(AirHolds.ShouldRelease(s.Repo, heli, 2179));                                // a man still aboard at the Unload stop
            Embarkation.Disembark(s.Repo, s.Members[0], heli);
            Assert.True(AirHolds.ShouldRelease(s.Repo, heli, 2179));                                 // empty: release

            SetPhase(s, heli, NavigationPhase.Loading);
            Assert.False(AirHolds.ShouldRelease(s.Repo, heli, 2179));                                // nobody of the unit aboard
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(s.Repo, s.Members[0], heli));
            Assert.False(AirHolds.ShouldRelease(s.Repo, heli, 2179));                                // one of two
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(s.Repo, s.Members[1], heli));
            Assert.True(AirHolds.ShouldRelease(s.Repo, heli, 2179));                                 // the whole unit: release

            SetPhase(s, heli, NavigationPhase.Following);
            Assert.False(AirHolds.ShouldRelease(s.Repo, heli, 2179));                                // flying: nothing to release
        }

        /// <summary>⭐ Airborne insertion B-3 (docs/designs/airborne/DESIGN_Airborne_Insertion.md D-7) — a ROPE hold ends only when nobody is
        /// aboard AND nobody hangs on its ropes (a fast-rope Descent anchored to it, short of Landed): the men leave the passenger list at
        /// the door, so Unload's rule alone would fly off with them on the ropes. A DROP leg ends when nobody is aboard. Neither is "on the
        /// ground". ✅ Red-proof: Roping released on "nobody aboard" alone ⇒ released with a man mid-rope ⇒ red.</summary>
        [Fact]
        public void B3_TheHelicoptersBrain_ReleasesARopeHold_OnlyWhenNobodyIsOnItsRopes_AndADropWhenEmpty()
        {
            var s = new Squad(members: 1);
            var heli = AirCarrier(s, new Vector3(0f, 0f, 24f), NavigationPhase.Roping, 2190, 0);
            if (!s.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Motion.Descent>()) s.Repo.RegisterComponent<Fdp.Toolkit.Motion.Descent>();

            Assert.False(AirHolds.ShouldRelease(s.Repo, heli, 0));                                   // a man still aboard
            Assert.False(AirHolds.OnTheGround(s.Repo, heli));                                         // a hover is not the ground
            Embarkation.Disembark(s.Repo, s.Members[0], heli);
            s.Repo.AddComponent(s.Members[0], new Fdp.Toolkit.Motion.Descent
                { Kind = Fdp.Toolkit.Motion.DescentKind.FastRope, Phase = Fdp.Toolkit.Motion.DescentPhase.Slide, Anchor = heli });
            Assert.False(AirHolds.ShouldRelease(s.Repo, heli, 0));                                   // out, but on the rope
            s.Repo.GetComponentRW<Fdp.Toolkit.Motion.Descent>(s.Members[0]).Phase = Fdp.Toolkit.Motion.DescentPhase.Landed;
            Assert.True(AirHolds.ShouldRelease(s.Repo, heli, 0));                                    // down: release

            SetPhase(s, heli, NavigationPhase.Dropping);
            Assert.True(AirHolds.ShouldRelease(s.Repo, heli, 0));                                    // the stick is out
            Assert.False(AirHolds.OnTheGround(s.Repo, heli));
            Assert.Equal(EmbarkResult.Applied, Embarkation.TryEmbark(s.Repo, s.Members[0], heli));
            Assert.False(AirHolds.ShouldRelease(s.Repo, heli, 0));                                   // a jumper still aboard

            // ⭐ CE-1118 — the CREW stays aboard and holds nothing: a door gunner in a Gunner seat (the doors-off UH-60's seats 8–11)
            //   ✅ Red-proof: every live passenger counted ⇒ the gunner holds the Drop for ever ⇒ red.
            Embarkation.Disembark(s.Repo, s.Members[0], heli);
            var gunner = s.Repo.CreateEntity();
            var crew = new CarrierSeats { SeatCount = 2 };
            crew.Roles[0] = SeatRole.Passenger; crew.Roles[1] = SeatRole.Gunner;
            s.Repo.AddComponent(heli, crew);
            ref var buf = ref s.Repo.GetComponentRW<PassengerBuffer>(heli);
            buf.Passengers[1] = gunner; buf.Count = 1;
            Assert.True(AirHolds.ShouldRelease(s.Repo, heli, 0));                                    // only the crew: release
        }

        // ── CE-1118 H-1 (docs/designs/airborne/DESIGN_Airborne_Insertion.md §6) — the airborne drills ────────────────────────────────

        private static void Descents(Squad s)
        {
            if (!s.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Motion.Descent>()) s.Repo.RegisterComponent<Fdp.Toolkit.Motion.Descent>();
            if (!s.Repo.IsComponentTypeRegistered<SimVelocity>()) s.Repo.RegisterComponent<SimVelocity>();
        }

        private static DisembarkParamsJsonDto ExitOf(AssignTacticalIntentEvent o)
            => System.Text.Json.JsonSerializer.Deserialize<DisembarkParamsJsonDto>(o.JsonParams, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);

        private static Vector3 ObjectiveOf(AssignTacticalIntentEvent o)
            => System.Text.Json.JsonSerializer.Deserialize<CombatPostureParamsJsonDto>(o.JsonParams, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed).Advance.Objective;

        /// <summary>A flat 10 × 10 m roof 12 m up at [−5, 5]², the ground at 0 elsewhere.</summary>
        private sealed class RoofWorld : Fdp.Toolkit.World.IWorldQuery
        {
            private static bool OnRoof(float x, float y) => x >= -5f && x <= 5f && y >= -5f && y <= 5f;
            public float GroundHeightAt(float x, float y) => 0f;
            public float SurfaceZ(float x, float y, float zHint) => OnRoof(x, y) && zHint >= 6f ? 12f : 0f;
            public IReadOnlyList<float> SurfacesAt(float x, float y, out int groundIndex) { groundIndex = 0; return OnRoof(x, y) ? new[] { 0f, 12f } : new[] { 0f }; }
            public float ResolveLevel(float x, float y, int level) => level > 0 && OnRoof(x, y) ? 12f : 0f;
            public bool TryStandAt(float x, float y, float zHint, out float z) { z = SurfaceZ(x, y, zHint); return true; }
            public bool SightBlocked(Vector3 from, Vector3 to) => false;
            public void Trace(Vector3 from, Vector3 to, Fdp.Toolkit.World.TracePurpose purpose, List<Fdp.Toolkit.World.TraceCrossing> into) { }
        }

        /// <summary>⭐ D-20 — the ring keeps every spot ON the roof it is centred on (shrunk to fit a 10 × 10 m roof), never over the ledge;
        /// on open ground it is the full ring. ✅ Red-proof: without the surface test the 5 m ring's corners-free spots at 5 m on the axes sit
        /// on the edge and the diagonal ones (3.5 m) fit, but the 3 m-offset centre's far spots fall off ⇒ red.</summary>
        [Fact]
        public void CE1118_TheRing_StaysOnTheRoof_AndIsWholeOnOpenGround()
        {
            var roof = new RoofWorld();
            var centre = new Vector3(2f, 0f, 12f);   // the rope point, off the roof's middle
            for (int k = 0; k < 8; k++)
            {
                var spot = AirborneDrill.RingSpot(roof, centre, k, 8, 5f);
                Assert.InRange(spot.X, -5f, 5f);
                Assert.InRange(spot.Y, -5f, 5f);
                Assert.Equal(12f, spot.Z);
            }
            var open = AirborneDrill.RingSpot(roof, new Vector3(50f, 0f, 0f), 2, 8, 5f);
            Assert.Equal(5f, Vector2.Distance(new Vector2(open.X, open.Y), new Vector2(50f, 0f)), 3);
        }

        /// <summary>
        /// ⭐ CE-1118 H-1 ★ — eight men down two ropes: never two men on one rope closer than 3 m (the next leaves when the one below is 3 m
        /// down), the ropes alternate, all eight down within 15 s of the first, then each takes a DISTINCT spot in the ring round the rope
        /// point, and the drill ends in Success. The test plays the carrier's owner and the rope: an exit applies 1.2 s after its order (the
        /// interaction crossing nodes), a man slides at a slow 2 m/s, lands at the ground and is off the rope 1 s later. ⚠ The two are
        /// chosen so the mutants SHOW: the one below slides on while the next man's exit is in flight, so a shorter gap is only caught while
        /// 0.5 m + 2 m/s × 1.2 s stays under 3 m — and eight men still come down in ≈ 14 s.
        /// ✅ Red-proofs (mutants, both red): <c>RopeClear</c> true while the last man is still aboard ⇒ two exits queued on one rope ⇒ red;
        /// the 3 m gap cut to 0.5 m ⇒ the next man out with the one below 2 m down ⇒ red.
        /// </summary>
        [Fact]
        public void CE1118_SquadFastRope_OneManPerRopeAtATime_AllDownIn15s_ThenARing()
        {
            var s = new Squad(members: 8);
            Descents(s);
            var heli = AirCarrier(s, new Vector3(0f, 0f, 12f), NavigationPhase.Roping, 11180, 0, 1, 2, 3, 4, 5, 6, 7);
            s.Order("{}", BehaviorNames.SquadFastRope);
            var landedAt = new Dictionary<Entity, double>();
            var pending = new Dictionary<Entity, (double At, byte Rope)>();   // exits ordered, not yet applied by the carrier's owner
            var ropes = new List<byte>();
            double t = 0, first = -1, lastLanded = 0;
            int seen = 0;
            Fbt.NodeStatus? result = null;
            for (int tick = 0; tick < 2500 && result == null; tick++)
            {
                s.Tick(); t += 0.016;
                for (; seen < s.Orders.Count; seen++)
                {
                    var o = s.Orders[seen];
                    if (o.IntentId == BehaviorNames.Disembark)
                    {
                        var exit = ExitOf(o);
                        Assert.Equal((byte)Fdp.Toolkit.Motion.ExitKind.RopeDown, exit.Exit);
                        if (!pending.ContainsKey(o.Entity) && Embarkation.IsAboard(s.Repo, o.Entity, heli)) pending[o.Entity] = (t + 1.2, exit.AnchorIndex);
                    }
                    else if (o.IntentId == BehaviorNames.CombatPosture) s.Teleport(o.Entity, ObjectiveOf(o));   // he walks to his spot
                }
                foreach (var (man, (at, rope)) in pending.ToList())   // the carrier's owner applies the exit, 1.2 s on
                {
                    if (t < at) continue;
                    pending.Remove(man);
                    Assert.True(Fdp.Toolkit.Motion.DescentRules.DoorPoint(s.Repo, heli, rope, out var door));
                    foreach (var m in s.Members)
                        if (s.Repo.HasComponent<Fdp.Toolkit.Motion.Descent>(m) && s.Repo.GetComponentRO<Fdp.Toolkit.Motion.Descent>(m).AnchorIndex == rope
                            && s.Repo.GetComponentRO<Fdp.Toolkit.Motion.Descent>(m).Phase != Fdp.Toolkit.Motion.DescentPhase.Landed)
                            Assert.True(door.Z - s.At(m).Z >= 2.99f, $"a man left rope {rope} with the one below only {door.Z - s.At(m).Z:0.00} m down");
                    Assert.True(Embarkation.Disembark(s.Repo, man, heli, Fdp.Toolkit.Motion.ExitKind.RopeDown, rope));
                    ropes.Add(rope);
                    if (first < 0) first = t;
                }
                foreach (var m in s.Members)   // the rope: a slow 2 m/s, landed on the ground, off it 1 s later
                {
                    if (!s.Repo.HasComponent<Fdp.Toolkit.Motion.Descent>(m)) continue;
                    ref var d = ref s.Repo.GetComponentRW<Fdp.Toolkit.Motion.Descent>(m);
                    ref var tf = ref s.Repo.GetComponentRW<SimTransform>(m);
                    if (d.Phase != Fdp.Toolkit.Motion.DescentPhase.Landed)
                    {
                        tf.Position.Z -= 2f * 0.016f;
                        if (tf.Position.Z <= 0f) { tf.Position.Z = 0f; d.Phase = Fdp.Toolkit.Motion.DescentPhase.Landed; landedAt[m] = t; lastLanded = t; }
                    }
                    else if (t - landedAt[m] >= 1.0) s.Repo.RemoveComponent<Fdp.Toolkit.Motion.Descent>(m);
                }
                if (s.Finished(out var r)) result = r;
            }

            Assert.Equal(Fbt.NodeStatus.Success, result);
            Assert.Equal(8, ropes.Count);
            Assert.Equal(4, ropes.Count(r => r == 0));                                                  // both ropes, four each
            Assert.Equal(4, ropes.Count(r => r == 1));
            Assert.InRange(lastLanded - first, 0.0, 15.0);                                             // ★ eight down in ≤ 15 s
            Assert.Equal(0, s.Repo.GetComponentRO<PassengerBuffer>(heli).Count);
            var spots = s.Members.Select(m => new Vector2(s.At(m).X, s.At(m).Y)).ToList();
            for (int i = 0; i < 8; i++)
                for (int j = i + 1; j < 8; j++) Assert.True(Vector2.Distance(spots[i], spots[j]) > 1f, "two men on one spot");   // ★ a ring
            Assert.All(spots, p => Assert.InRange(p.Length(), 4f, 6f));                                // round the rope point
        }

        /// <summary>
        /// ⭐ CE-1118 H-1 ★ — the jumpmaster: a stick of eight from a helicopter flying its Drop leg at 400 m, 35 m/s — one man a second,
        /// alternating doors, each at his release point, each carrying his landing point into his Descent (D-18); the landing points lie
        /// along the leg one second of flight apart; once down they assemble round the stick's middle and the drill ends in Success.
        /// ✅ Red-proof: the jump not waiting for the release point ⇒ the first man out at the leg's start, 47 m short ⇒ red.
        /// </summary>
        [Fact]
        public void CE1118_SquadAirborneExit_TheStickJumpsAtItsReleasePoints_OneASecond_IntoItsLandingPoints_ThenAssembles()
        {
            var s = new Squad(members: 8);
            Descents(s);
            var heli = AirCarrier(s, new Vector3(0f, 0f, 400f), NavigationPhase.Dropping, 11181, 0, 1, 2, 3, 4, 5, 6, 7);
            s.Repo.AddComponent(heli, new SimVelocity { Linear = new Vector3(35f, 0f, 0f) });
            s.Order("{}", BehaviorNames.SquadAirborneExit);
            var jumps = new List<(double T, byte Door, Vector3 Land, float HeliX)>();
            var outAt = new Dictionary<Entity, (double T, Vector3 Land)>();
            double t = 0;
            int seen = 0;
            Fbt.NodeStatus? result = null;
            for (int tick = 0; tick < 4000 && result == null; tick++)
            {
                s.Repo.GetComponentRW<SimTransform>(heli).Position.X += 35f * 0.016f;
                s.Tick(); t += 0.016;
                for (; seen < s.Orders.Count; seen++)
                {
                    var o = s.Orders[seen];
                    if (o.IntentId == BehaviorNames.Disembark)
                    {
                        if (!Embarkation.IsAboard(s.Repo, o.Entity, heli)) continue;
                        var exit = ExitOf(o);
                        Assert.Equal((byte)Fdp.Toolkit.Motion.ExitKind.JumpOut, exit.Exit);
                        Assert.True(Embarkation.Disembark(s.Repo, o.Entity, heli, Fdp.Toolkit.Motion.ExitKind.JumpOut, exit.AnchorIndex, exit.Landing));
                        Assert.Equal(exit.Landing, s.Repo.GetComponentRO<Fdp.Toolkit.Motion.Descent>(o.Entity).Target);   // D-18
                        jumps.Add((t, exit.AnchorIndex, exit.Landing, s.At(heli).X));
                        outAt[o.Entity] = (t, exit.Landing);
                    }
                    else if (o.IntentId == BehaviorNames.CombatPosture) s.Teleport(o.Entity, ObjectiveOf(o));
                }
                foreach (var (m, (when, land)) in outAt.ToList())   // under the canopy for 5 s, then down at his point
                    if (s.Repo.HasComponent<Fdp.Toolkit.Motion.Descent>(m) && t - when >= 5.0)
                    {
                        s.Repo.RemoveComponent<Fdp.Toolkit.Motion.Descent>(m);
                        s.Teleport(m, land);
                    }
                if (s.Finished(out var r)) result = r;
            }

            Assert.Equal(Fbt.NodeStatus.Success, result);
            Assert.Equal(8, jumps.Count);
            for (int i = 1; i < 8; i++)
            {
                Assert.True(jumps[i].T - jumps[i - 1].T >= 0.98, $"jump {i} only {jumps[i].T - jumps[i - 1].T:0.00} s after the last");
                Assert.NotEqual(jumps[i].Door, jumps[i - 1].Door);                                     // alternating doors
                Assert.Equal(35f, jumps[i].Land.X - jumps[i - 1].Land.X, 0);                          // one second of flight apart
            }
            float throw_ = 35f * 4f / 3f;                                                              // a round canopy's door throw (B-5)
            Assert.All(jumps, j => Assert.InRange(j.HeliX - (j.Land.X - throw_), -0.01f, 1.2f));      // ★ each at his release point (the bus delivers an order a tick late: ≤ 2 ticks × 0.56 m)
            Assert.All(jumps, j => Assert.Equal(0f, j.Land.Y, 1));                                     // along the leg
            Assert.True(jumps[0].Land.X - throw_ >= 0f);                                               // the first release on the leg
        }

        /// <summary>⭐ CE-1118 H-1 (D-22) — AssembleAfterDrop: a man still coming down gets nothing; every man down takes his spot round the
        /// rally point; Success when all hold them.</summary>
        [Fact]
        public void CE1118_AssembleAfterDrop_TheMenDownTakeTheirSpots_AManStillComingDownWaits()
        {
            var s = new Squad(members: 3);
            Descents(s);
            s.Repo.AddComponent(s.Members[2], new Fdp.Toolkit.Motion.Descent { Kind = Fdp.Toolkit.Motion.DescentKind.StaticLine, Phase = Fdp.Toolkit.Motion.DescentPhase.Canopy });
            s.Order("{\"point\":[100,0,0]}", BehaviorNames.AssembleAfterDrop);
            Ticks(s, 3);
            Assert.DoesNotContain(s.Orders, o => o.Entity == s.Members[2]);
            foreach (var o in s.Orders.Where(o => o.IntentId == BehaviorNames.CombatPosture).ToList()) s.Teleport(o.Entity, ObjectiveOf(o));
            Assert.Null(RunUntilFinished(s, 5));                                                     // one still under his canopy
            s.Repo.RemoveComponent<Fdp.Toolkit.Motion.Descent>(s.Members[2]);
            Ticks(s, 2);
            var his = s.Orders.Last(o => o.Entity == s.Members[2]);
            Assert.Equal(BehaviorNames.CombatPosture, his.IntentId);
            s.Teleport(s.Members[2], ObjectiveOf(his));
            Assert.Equal(Fbt.NodeStatus.Success, RunUntilFinished(s, 10));
        }

        /// <summary>⭐ CE-1118 H-1 — the squad's SOP: a helicopter holding at a Rope stop puts the squad down its ropes; one flying a Drop leg
        /// starts the jumpmaster. ✅ Red-proof: either row missing ⇒ the squad rides on (SquadRide) ⇒ red.</summary>
        [Fact]
        public void CE1118_BasicSquadSop_ARopeHoldFastRopes_ADropLegJumps()
        {
            foreach (var (phase, drill) in new[] { (NavigationPhase.Roping, BehaviorNames.SquadFastRope), (NavigationPhase.Dropping, BehaviorNames.SquadAirborneExit) })
            {
                var s = new Squad(members: 3);
                Descents(s);
                s.Order("{\"Objective\":[0,100,0],\"BoundMetres\":30}");
                s.StartSop("BasicSquadSop");
                Ticks(s, 20);
                var heli = AirCarrier(s, new Vector3(0f, 0f, 80f), NavigationPhase.Following, 11182, 0, 1, 2);
                Ticks(s, 20);
                Assert.Equal(BehaviorNames.SquadRide, s.TaskName);                                     // flying: it rides
                SetPhase(s, heli, phase);
                Ticks(s, 20);
                Assert.Equal(drill, s.TaskName);
                Assert.Equal(0, s.Repo.GetComponent<SopState>(s.Commander).SopFaulted);
            }
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

        /// <summary>⭐ <c>CE-2302</c> (DA-24, DA-33) — each near-side security man gets HIS side of the road as his sector (an arc of
        /// ±45° from his spot, origin Superior — `DangerCrossingPlay`); the queue gets none; a security man ordered across loses his; leaving the play (the
        /// task cleared — the deactivator's <c>Leave</c>) withdraws every one the play gave. 📄 Defend &amp; Attack §13a-1.</summary>
        [Fact]
        public void CE2302_DangerCrossing_EachSecurityManCoversHisSideOfTheRoad_UntilHeCrosses()
        {
            var s = Crossing();
            s.Tick();                                                                   // the issue's sector events land
            var orders = s.LastOrders();
            var security = s.Members.Skip(3).ToArray();
            for (int r = 0; r < security.Length; r++)
            {
                var a = Sector(s, security[r]);
                Assert.Equal(1, a.ArcSet);
                Assert.Equal(BehaviorOrigin.Superior, a.SetBy);
                Assert.Equal(orders[security[r]].Objective, a.ArcOrigin);              // from his spot
                // the crossing runs north (+Y): rank 0 stands west of it and covers the west, rank 1 the east
                float centre = r % 2 == 0 ? 270f : 90f;
                Assert.Equal(centre - DangerCrossingPlay.SecurityArcHalfWidth, a.LeftBearing, 0.01f);
                Assert.Equal(centre + DangerCrossingPlay.SecurityArcHalfWidth, a.RightBearing, 0.01f);
            }
            foreach (var m in s.Members.Take(3)) Assert.Equal(0, Sector(s, m).ArcSet);   // the queue waits with no sector

            Arrive(s, 3, 4, 5); Arrive(s, 0, 1); Arrive(s, 2); Arrive(s, 0, 1, 2);     // across; security collapses by pairs
            Assert.Equal(DangerAreaCrossingManeuver.PhaseCollapseSecurity, s.Phase);
            s.Tick();
            Assert.Equal(0, Sector(s, s.Members[3]).ArcSet);                           // ordered across: his sector is withdrawn
            Assert.Equal(1, Sector(s, s.Members[5]).ArcSet);                           // the last one still watches

            // the task CLEARED (no squad play follows to re-issue): only the deactivator's Leave can withdraw the last man's sector
            s.Repo.Bus.Publish(new ClearBehaviorEvent { Entity = s.Commander, Origin = BehaviorOrigin.Superior });
            for (int i = 0; i < 3; i++) s.Tick();
            Assert.NotEqual(BehaviorNames.SquadDangerCrossing, s.TaskName);
            foreach (var m in s.Members) Assert.Equal(0, Sector(s, m).ArcSet);
        }

        private static Fdp.Toolkit.Behavior.Components.EngagementArea Sector(Squad s, Entity m)
            => s.Repo.HasComponent<Fdp.Toolkit.Behavior.Components.EngagementArea>(m)
                ? s.Repo.GetComponentRO<Fdp.Toolkit.Behavior.Components.EngagementArea>(m) : default;

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

        /// <summary>⭐ <c>CE-2161</c> — the march asks its route sensor for OPEN GROUND only when the order says <c>OpenGround</c>
        /// (CE-3081 A5); otherwise the crossings alone, as every existing march. 🔴 Red before: no way to ask, so D3's Bound scored 0 live.</summary>
        [Fact]
        public void CE2161_TheMarch_AsksForOpenGround_OnlyWhenTheOrderSaysSo()
        {
            Fdp.Toolkit.Squad.DangerArea.DangerAreaKinds KindsOf(string order)
            {
                var w = new Squad(4);
                w.Order(order, BehaviorNames.SquadMarch);
                w.Tick(); w.Tick(); w.Repo.FlushCommandBuffers();
                var sensor = Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(w.Repo, w.Commander, SquadMarchPlay.DangerSensorSite);
                Assert.False(sensor.IsNull);
                return w.Repo.GetComponentRO<Fdp.Toolkit.Squad.DangerArea.DangerAreaSensor>(sensor).Settings.Kinds;
            }
            Assert.Equal(Fdp.Toolkit.Squad.DangerArea.DangerAreaKinds.Crossings, KindsOf("{\"Objective\":[0,100,0],\"DangerAreas\":true}"));
            var open = KindsOf("{\"Objective\":[0,100,0],\"DangerAreas\":true,\"OpenGround\":true}");
            Assert.True(Fdp.Toolkit.Squad.DangerArea.DangerAreaKindsOf.Wants(open, Fdp.Toolkit.Squad.DangerArea.DangerAreaKind.OpenGround));
            Assert.True(Fdp.Toolkit.Squad.DangerArea.DangerAreaKindsOf.Wants(open, Fdp.Toolkit.Squad.DangerArea.DangerAreaKind.StreetCrossing));
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
        // ── CE-2176 IF-2 — the battery fire mission (docs/designs/indirect-fire/DESIGN_Indirect_Fire.md §3.4, FM-1…FM-8) ─────────────

        private const long IfGunType = 9961;
        private static readonly Fdp.Toolkit.Tkb.Domain.WeaponMountDto IfMount = new()
        {
            Category = Fdp.Toolkit.Tkb.Domain.WeaponCategory.Howitzer, Arc = Fdp.Toolkit.Tkb.Domain.IndirectArc.Both,
            MinElevationDeg = -3f, MaxElevationDeg = 75f, MuzzleVelocity = 300f, InitialAmmunition = 20,
        };

        /// <summary>A battery of <paramref name="guns"/> stand-in howitzers (one indirect mount, 300 m/s, 20 rounds) at
        /// <paramref name="at"/>; the commander is force 1.</summary>
        private static Squad Battery(params Vector3[] at)
        {
            var s = new Squad(at.Length);
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t = new Fdp.Interfaces.TkbTemplate("gun", IfGunType);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { IfMount } });
            db.Register(t);
            s.Repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            for (int i = 0; i < at.Length; i++)
            {
                s.Teleport(s.Members[i], at[i]);
                s.Repo.AddComponent(s.Members[i], new Fdp.Toolkit.Replication.Components.TkbIdentity { TkbType = IfGunType });
                s.Repo.AddComponent(s.Members[i], new Fdp.Toolkit.Combat.Components.WeaponState { Ammo = 20, MuzzleVelocity = 300f });
            }
            s.Repo.AddComponent(s.Commander, new Fdp.Core.EntityInfo { ForceId = Fdp.Core.ForceId.Friend });
            return s;
        }

        private static void Strike(Squad s, Vector3 point, Hrot.AI.Behaviors.Orders.FireMissionControl control,
                                   Hrot.AI.Behaviors.Orders.FireMissionSheaf sheaf = Hrot.AI.Behaviors.Orders.FireMissionSheaf.Converged,
                                   float width = 0f, bool dangerClose = false)
            => s.Order(System.Text.Json.JsonSerializer.Serialize(new Hrot.AI.Behaviors.Orders.StrikeOrder
            {
                Location = Hrot.AI.Behaviors.Orders.OrderGeo.ToGeo(s.Repo, point), Control = control, Sheaf = sheaf, SheafWidthM = width,
                DangerClose = dangerClose,
            }, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed), BehaviorNames.ArtilleryUnitStrike);

        /// <summary>Each gun's last FireAtPoint order.</summary>
        private static Dictionary<Entity, FireAtPointParamsJsonDto> FireOrders(Squad s)
        {
            var d = new Dictionary<Entity, FireAtPointParamsJsonDto>();
            foreach (var o in s.Orders)
                if (o.IntentId == BehaviorNames.FireAtPoint)
                    d[o.Entity] = System.Text.Json.JsonSerializer.Deserialize<FireAtPointParamsJsonDto>(o.JsonParams, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
            return d;
        }

        /// <summary>
        /// ⭐ <c>CE-2176</c> IF-2 (FM-4) — TIME ON TARGET: three guns at different ranges each get the point, 3 rounds and a fire time
        /// <c>T − tof</c> from their own fire solution, so every first round lands at the same T (≥ now + the longest flight + the lay
        /// time); the farthest gun fires first. 🔴 Red-proof: fire times 0 (when ready) ⇒ the impacts spread by the flight difference ⇒ red.
        /// </summary>
        [Fact]
        public void CE2176_ABattery_TimeOnTarget_EveryFirstRoundLandsTogether()
        {
            var point = new Vector3(5000f, 0f, 0f);
            var s = Battery(new Vector3(0f, 0f, 0f), new Vector3(-1500f, 40f, 0f), new Vector3(-3000f, 80f, 0f));
            double t0 = s.Repo.GetSingleton<GlobalTime>().TotalTime;
            Strike(s, point, Hrot.AI.Behaviors.Orders.FireMissionControl.TimeOnTarget);
            for (int i = 0; i < 4; i++) s.Tick();

            var orders = FireOrders(s);
            Assert.Equal(3, orders.Count);
            var impacts = new List<double>();
            var fireAt = new List<double>();
            foreach (var g in s.Members)
            {
                var o = orders[g];
                Assert.Equal(3, o.Rounds);
                Assert.InRange(Vector2.Distance(new Vector2(o.X, o.Y), new Vector2(point.X, point.Y)), 0f, 2f);   // converged
                var sol = Fdp.Toolkit.Combat.FireSolution.For(null, s.At(g), new Vector3(o.X, o.Y, o.Z), IfMount, null);
                Assert.True(sol.Ok);
                impacts.Add(o.FireAt + sol.TimeOfFlight);
                fireAt.Add(o.FireAt);
            }
            Assert.True(impacts.Max() - impacts.Min() < 1e-3, $"impacts {string.Join(", ", impacts)}");
            Assert.True(impacts[0] >= t0 + IndirectFireNodes.LayMarginSeconds);
            Assert.True(fireAt[2] < fireAt[1] && fireAt[1] < fireAt[0], "the farthest gun fires first");
        }

        /// <summary>
        /// ⭐ <c>CE-1109</c> (FM-4) — TIME ON TARGET WITH REAL TURRETS: three M109-rate guns (10 °/s traverse, 5 °/s elevation), the
        /// FARTHEST one (it fires first, so it has no flight-time slack) facing AWAY from the target. Every gun's fire time leaves its own
        /// turret the seconds it needs to be laid from where it points now (the far-facing gun's 180° ≈ 18 s), and every first round still
        /// lands together. 🔴 Red-proof: the old fixed 10 s lay ⇒ that gun's fire time comes ~8 s before its turret can be on ⇒ red.
        /// </summary>
        [Fact]
        public void CE1109_TimeOnTarget_LeavesEachTurretItsOwnLayTime_AGunFacingAwayIsLaidBeforeItFires()
        {
            var point = new Vector3(5000f, 0f, 0f);
            var s = Battery(new Vector3(0f, 0f, 0f), new Vector3(-1500f, 40f, 0f), new Vector3(-3000f, 80f, 0f));
            var turret = new Fdp.Toolkit.Tkb.Domain.TurretDto
            {
                TraverseRateDegPerSec = 10f, ElevationRateDegPerSec = 5f, ElevationMinDeg = -3f, ElevationMaxDeg = 75f,
            };
            var mount = new Fdp.Toolkit.Tkb.Domain.WeaponMountDto
            {
                Category = IfMount.Category, Arc = IfMount.Arc, MinElevationDeg = IfMount.MinElevationDeg, MaxElevationDeg = IfMount.MaxElevationDeg,
                MuzzleVelocity = IfMount.MuzzleVelocity, InitialAmmunition = IfMount.InitialAmmunition, TurretIndex = 0,
            };
            var t = new Fdp.Interfaces.TkbTemplate("m109", IfGunType);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.WeaponSuiteDto { Mounts = { mount } });
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.TurretSuiteDto { Turrets = { turret } });
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            db.Register(t);
            s.Repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            if (!s.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Replication.Components.PartMetadata>()) s.Repo.RegisterComponent<Fdp.Toolkit.Replication.Components.PartMetadata>();
            if (!s.Repo.IsComponentTypeRegistered<Fdp.Toolkit.Combat.Components.TurretPose>()) s.Repo.RegisterComponent<Fdp.Toolkit.Combat.Components.TurretPose>();
            var parts = s.Members.Select(g => Fdp.Toolkit.Combat.TurretParts.Ensure(s.Repo, g, 0)).ToArray();
            Assert.All(parts, p => Assert.False(p.IsNull));
            s.Repo.SetComponent(parts[2], new Fdp.Toolkit.Combat.Components.TurretPose { Azimuth = MathF.PI });   // the farthest gun (it fires first) faces away

            double t0 = s.Repo.GetSingleton<GlobalTime>().TotalTime;
            Strike(s, point, Hrot.AI.Behaviors.Orders.FireMissionControl.TimeOnTarget);
            for (int i = 0; i < 4; i++) s.Tick();

            var orders = FireOrders(s);
            Assert.Equal(3, orders.Count);
            var impacts = new List<double>();
            for (int i = 0; i < 3; i++)
            {
                var g = s.Members[i];
                var o = orders[g];
                var aim = new Vector3(o.X, o.Y, o.Z);
                var sol = Fdp.Toolkit.Combat.FireSolution.For(null, s.At(g), aim, mount, null);
                Assert.True(sol.Ok);
                impacts.Add(o.FireAt + sol.TimeOfFlight);
                float lay = IndirectFireNodes.LaySecondsFor(s.Repo, g, mount, aim, in sol);
                Assert.True(o.FireAt >= t0 + lay, $"gun {i} fires at +{o.FireAt - t0:F1} s, its turret needs {lay:F1} s");
            }
            Assert.InRange(IndirectFireNodes.LaySecondsFor(s.Repo, s.Members[2], mount, new Vector3(orders[s.Members[2]].X, orders[s.Members[2]].Y, 0f),
                                                           Fdp.Toolkit.Combat.FireSolution.For(null, s.At(s.Members[2]), point, mount, null)), 17.5f, 18.5f);
            Assert.True(impacts.Max() - impacts.Min() < 1e-3, $"impacts {string.Join(", ", impacts)}");
        }

        /// <summary>
        /// ⭐ <c>CE-2176</c> IF-2 (FM-3) — the SHEAF: three guns abreast across the line of fire. Parallel ⇒ each aims at its own lateral place
        /// (−50 / 0 / +50); Open over 300 m ⇒ −100 / 0 / +100; when ready ⇒ fire times 0. 🔴 Red-proof: every aim on the point ⇒ red.
        /// </summary>
        [Fact]
        public void CE2176_ABattery_ParallelAndOpenSheafs_SpreadTheAimPoints()
        {
            var point = new Vector3(5000f, 0f, 0f);
            foreach (var (sheaf, width, want) in new[]
            {
                (Hrot.AI.Behaviors.Orders.FireMissionSheaf.Parallel, 0f, new[] { -50f, 0f, 50f }),
                (Hrot.AI.Behaviors.Orders.FireMissionSheaf.Open, 300f, new[] { -100f, 0f, 100f }),
            })
            {
                var s = Battery(new Vector3(0f, -50f, 0f), new Vector3(0f, 0f, 0f), new Vector3(0f, 50f, 0f));
                Strike(s, point, Hrot.AI.Behaviors.Orders.FireMissionControl.WhenReady, sheaf, width);
                for (int i = 0; i < 4; i++) s.Tick();
                var orders = FireOrders(s);
                Assert.Equal(3, orders.Count);
                for (int i = 0; i < 3; i++)
                {
                    var o = orders[s.Members[i]];
                    Assert.InRange(o.X, point.X - 1f, point.X + 1f);
                    Assert.InRange(o.Y, want[i] - 1f, want[i] + 1f);
                    Assert.Equal(0d, o.FireAt);
                }
            }
        }

        /// <summary>
        /// ⭐ <c>CE-2176</c> IF-2 (FM-1) — a gun that cannot reach is left out (it holds); the others fire. ⭐ (FM-5) a friend 300 m from the
        /// point refuses the mission — no gun fires, the behaviour fails — unless the order says danger close.
        /// </summary>
        [Fact]
        public void CE2176_ABattery_LeavesOutAGunOutOfRange_AndRefusesDangerClose()
        {
            var point = new Vector3(5000f, 0f, 0f);
            var s = Battery(new Vector3(0f, 0f, 0f), new Vector3(-20000f, 0f, 0f));   // 300 m/s reaches ~9.2 km
            Strike(s, point, Hrot.AI.Behaviors.Orders.FireMissionControl.WhenReady);
            for (int i = 0; i < 4; i++) s.Tick();
            var orders = FireOrders(s);
            Assert.True(orders.ContainsKey(s.Members[0]));
            Assert.False(orders.ContainsKey(s.Members[1]));

            foreach (bool dangerClose in new[] { false, true })
            {
                var b = Battery(new Vector3(0f, 0f, 0f));
                var friend = b.Repo.CreateEntity();
                b.Repo.AddComponent(friend, new SimTransform { Position = point + new Vector3(0f, 300f, 0f), Rotation = Quaternion.Identity });
                b.Repo.AddComponent(friend, new Fdp.Core.EntityInfo { ForceId = Fdp.Core.ForceId.Friend });
                Strike(b, point, Hrot.AI.Behaviors.Orders.FireMissionControl.WhenReady, dangerClose: dangerClose);
                bool done = false; Fbt.NodeStatus result = default;
                for (int i = 0; i < 4; i++) { b.Tick(); done |= b.Finished(out var r) && (result = r) == r; }
                Assert.Equal(dangerClose, FireOrders(b).ContainsKey(b.Members[0]));
                if (!dangerClose) { Assert.True(done); Assert.Equal(Fbt.NodeStatus.Failure, result); }
            }
        }

        /// <summary>
        /// ⭐ <c>CE-2176</c> IF-2 — a mission resumed after a pause re-sends each gun only the rounds it has LEFT (one fired ⇒ 2), never the
        /// full count again; once every gun's rounds are gone it ends in Success (rounds complete). 🔴 Red-proof: re-send <c>Rounds</c>
        /// on every issue ⇒ the resumed order asks 3 ⇒ red.
        /// </summary>
        [Fact]
        public void CE2176_ABattery_ResumedMission_FiresOnlyTheRoundsLeft_ThenRoundsComplete()
        {
            var s = Battery(new Vector3(0f, 0f, 0f), new Vector3(0f, 40f, 0f));
            Strike(s, new Vector3(5000f, 0f, 0f), Hrot.AI.Behaviors.Orders.FireMissionControl.WhenReady);
            for (int i = 0; i < 4; i++) s.Tick();
            Assert.Equal(3, FireOrders(s)[s.Members[0]].Rounds);

            s.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.WeaponState>(s.Members[0]).Ammo -= 1;   // one round left the tube
            s.Orders.Clear();
            s.Pause(1.0);
            for (int i = 0; i < 4; i++) s.Tick();
            var resumed = FireOrders(s);
            Assert.Equal(2, resumed[s.Members[0]].Rounds);
            Assert.Equal(3, resumed[s.Members[1]].Rounds);

            foreach (var g in s.Members) s.Repo.GetComponentRW<Fdp.Toolkit.Combat.Components.WeaponState>(g).Ammo = 17;
            bool done = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 4 && !done; i++) { s.Tick(); done = s.Finished(out result); }
            Assert.True(done);
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }

        // ── CE-2176 IF-3 — adjust fire: the observer and the adjusting round (DESIGN_Indirect_Fire.md §3.5, §3.8, IF-E, FM-6) ─────────

        /// <summary>
        /// ⭐ <c>CE-2176</c> IF-3 (FM-6) — an ADJUST Strike fires ONE round from ONE gun — the one nearest the battery's centre — on the point,
        /// whatever sheaf it names. 🔴 Red-proof: ignore <c>Adjust</c> in the plan ⇒ three guns, three rounds each ⇒ red.
        /// </summary>
        [Fact]
        public void CE2176_AnAdjustingRound_IsOneRoundFromTheGunNearestTheBatteryCentre()
        {
            var point = new Vector3(5000f, 0f, 0f);
            var s = Battery(new Vector3(0f, -60f, 0f), new Vector3(0f, 5f, 0f), new Vector3(0f, 50f, 0f));
            s.Order(System.Text.Json.JsonSerializer.Serialize(new Hrot.AI.Behaviors.Orders.StrikeOrder
            {
                Location = Hrot.AI.Behaviors.Orders.OrderGeo.ToGeo(s.Repo, point), Method = Hrot.AI.Behaviors.Orders.FireMissionMethod.Adjust,
                Rounds = 3, Sheaf = Hrot.AI.Behaviors.Orders.FireMissionSheaf.Parallel,
            }, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed), BehaviorNames.ArtilleryUnitStrike);
            for (int i = 0; i < 4; i++) s.Tick();

            var orders = FireOrders(s);
            var only = Assert.Single(orders);
            Assert.Equal(s.Members[1], only.Key);
            Assert.Equal(1, only.Value.Rounds);
            Assert.InRange(Vector2.Distance(new Vector2(only.Value.X, only.Value.Y), new Vector2(point.X, point.Y)), 0f, 2f);
        }

        /// <summary>An observer at <paramref name="at"/> told to call <paramref name="s"/>'s fire on <paramref name="target"/>.</summary>
        private static Entity Observer(Squad s, Vector3 at, Vector3 target, int rounds = 3)
        {
            s.Repo.AddComponent(s.Commander, new Fdp.Toolkit.Replication.Components.NetworkIdentity { Value = 4242 });
            var o = s.Repo.CreateEntity();
            s.Repo.AddComponent(o, new SimTransform { Position = at, Rotation = Quaternion.Identity });
            s.Repo.AddComponent(o, new BehaviorState());
            s.Repo.Bus.PublishManaged(new AssignBehaviorEvent
            {
                Entity = o, BehaviorName = BehaviorNames.PersonObserveFire, Origin = BehaviorOrigin.Superior,
                JsonParams = System.Text.Json.JsonSerializer.Serialize(new Hrot.AI.Behaviors.Orders.ObserveFireOrder
                {
                    Location = Hrot.AI.Behaviors.Orders.OrderGeo.ToGeo(s.Repo, target), Battery = new Fdp.Toolkit.Replication.EntityRef(4242),
                    Rounds = rounds, Sheaf = Hrot.AI.Behaviors.Orders.FireMissionSheaf.Parallel,
                }, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed),
            });
            return o;
        }

        /// <summary>The Strikes the battery was sent, in order, with each one's point.</summary>
        private static List<(Hrot.AI.Behaviors.Orders.StrikeOrder Order, Vector3 At)> Calls(Squad s)
        {
            var list = new List<(Hrot.AI.Behaviors.Orders.StrikeOrder, Vector3)>();
            foreach (var o in s.Orders)
                if (o.Entity.Equals(s.Commander) && o.IntentId == Hrot.Map.Definitions.Behavior.Orders.OrderAttribute.IntentFor("Strike"))
                {
                    var order = System.Text.Json.JsonSerializer.Deserialize<Hrot.AI.Behaviors.Orders.StrikeOrder>(o.JsonParams, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
                    var at = Hrot.AI.Behaviors.Orders.OrderGeo.ToLocal(s.Repo, order.Location, 0f);
                    list.Add((order, new Vector3(at.X, at.Y, 0f)));
                }
            return list;
        }

        private static void Burst(Squad s, Vector3 at, Entity shooter)
            => Fdp.Toolkit.Combat.RecentDetonations.Add(s.Repo, at, s.Repo.GetSingleton<GlobalTime>().TotalTime + 0.001, shooter, 0);

        /// <summary>
        /// ⭐⭐ <c>CE-2176</c> IF-3 acceptance (FM-6) — the observer, 1.5 km south of the target, calls an adjusting round; the guns shoot
        /// 720 m over and 120 m right of every call (their error, and his); from what he SEES burst he takes the 120 m out at once and
        /// brackets the range — drop 800, add 400, drop 200, drop 100 — and with the round within 50 m calls FIRE FOR EFFECT with the
        /// mission's rounds and sheaf. The next burst ends his mission. 🔴 Red-proof: never halve the step ⇒ the second correction is
        /// 800 again ⇒ red.
        /// </summary>
        [Fact]
        public void CE2176_TheObserver_BracketsTheRoundsOntoTheTarget_ThenCallsFireForEffect()
        {
            var target = new Vector3(5000f, 0f, 0f);
            var s = Battery(new Vector3(0f, 0f, 0f), new Vector3(0f, 40f, 0f));
            var observer = Observer(s, new Vector3(5000f, -1500f, 0f), target);
            for (int i = 0; i < 3; i++) s.Tick();

            var calls = Calls(s);
            var first = Assert.Single(calls);
            Assert.Equal(Hrot.AI.Behaviors.Orders.FireMissionMethod.Adjust, first.Order.Method);
            Assert.Equal(1, first.Order.Rounds);
            Assert.InRange(Vector2.Distance(new Vector2(first.At.X, first.At.Y), new Vector2(target.X, target.Y)), 0f, 151f);   // map-located: ≤ 150 m off
            var bias = target + new Vector3(120f, 720f, 0f) - first.At;   // looking north: +Y is over, +X is right

            var steps = new List<float>();
            for (int round = 0; round < 8; round++)
            {
                var last = Calls(s)[^1];
                if (last.Order.Method == Hrot.AI.Behaviors.Orders.FireMissionMethod.FireForEffect) break;
                Burst(s, last.At + bias, s.Members[1]);
                for (int i = 0; i < 3; i++) s.Tick();
                calls = Calls(s);
                Assert.Equal(round + 2, calls.Count);
                steps.Add(MathF.Abs(calls[^1].At.Y - calls[^2].At.Y));
            }
            // four corrections, then fire for effect on the last adjusted point (the round within 50 m: no move)
            Assert.Equal(new[] { 800f, 400f, 200f, 100f, 0f }, steps.Select(x => MathF.Round(x)).ToArray());
            var effect = Calls(s)[^1].Order;
            Assert.Equal(Hrot.AI.Behaviors.Orders.FireMissionMethod.FireForEffect, effect.Method);
            Assert.Equal(3, effect.Rounds);
            Assert.Equal(Hrot.AI.Behaviors.Orders.FireMissionSheaf.Parallel, effect.Sheaf);
            Assert.InRange(MathF.Abs(Calls(s)[^1].At.X + bias.X - target.X), 0f, 50f);   // the lateral error is gone

            Burst(s, Calls(s)[^1].At + bias, s.Members[0]);
            bool done = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 3 && !done; i++) { s.Tick(); done = ObserverFinished(s, observer, out result); }
            Assert.True(done);
            Assert.Equal(Fbt.NodeStatus.Success, result);
        }

        private static bool ObserverFinished(Squad s, Entity observer, out Fbt.NodeStatus result)
        {
            result = default;
            foreach (var evt in s.Repo.Bus.Read<BehaviorFinishedEvent>())
                if (evt.Entity.Equals(observer)) { result = evt.Result; return true; }
            return false;
        }

        /// <summary>
        /// ⭐ <c>CE-2176</c> IF-3 (IF-E) — the observer judges only HIS battery's rounds: a stranger's burst brings no correction; and with
        /// none of the battery's rounds seen for 180 s he is LOST — his mission fails and no call follows. 🔴 Red-proof: accept any
        /// shooter ⇒ the stranger's burst brings a second call ⇒ red.
        /// </summary>
        [Fact]
        public void CE2176_TheObserver_IgnoresAStrangersBurst_AndIsLostWhenHeSeesNoRound()
        {
            var target = new Vector3(5000f, 0f, 0f);
            var s = Battery(new Vector3(0f, 0f, 0f));
            var observer = Observer(s, new Vector3(5000f, -1500f, 0f), target);
            for (int i = 0; i < 3; i++) s.Tick();
            Assert.Single(Calls(s));

            var stranger = s.Repo.CreateEntity();
            Burst(s, target + new Vector3(0f, 400f, 0f), stranger);
            for (int i = 0; i < 3; i++) s.Tick();
            Assert.Single(Calls(s));

            s.Pause(Hrot.AI.Behaviors.Brains.PersonObserveFireBehavior.BurstWaitSeconds + 1.0);
            bool done = false; Fbt.NodeStatus result = default;
            for (int i = 0; i < 3 && !done; i++) { s.Tick(); done = ObserverFinished(s, observer, out result); }
            Assert.True(done);
            Assert.Equal(Fbt.NodeStatus.Failure, result);
            Assert.Single(Calls(s));
        }

        /// <summary>⭐ <c>CE-2176</c> IF-3 — <c>DetonationMemorySystem</c> keeps each burst the node sees (where, whose); the bracket reads
        /// them. 🔴 Red-proof: an empty system ⇒ count 0 ⇒ red.</summary>
        [Fact]
        public void CE2176_TheBurstMemory_RecordsEachDetonation()
        {
            var s = Battery(new Vector3(0f, 0f, 0f));
            s.Repo.Bus.Publish(new Fdp.Toolkit.Combat.Contracts.DetonationNotification { Shooter = s.Members[0], HitX = 10f, HitY = 20f, HitZ = 1f, Ammo = 6029 });
            s.Tick();
            new Fdp.Toolkit.Combat.DetonationMemorySystem().Execute(s.Repo, 0.016f);
            Assert.Equal(1, Fdp.Toolkit.Combat.RecentDetonations.CountOf(s.Repo));
            Span<Fdp.Toolkit.Combat.RecentBurst> got = new Fdp.Toolkit.Combat.RecentBurst[4];
            Assert.Equal(1, Fdp.Toolkit.Combat.RecentDetonations.Since(s.Repo, -1.0, got));
            Assert.Equal(new Vector3(10f, 20f, 1f), got[0].At);
            Assert.Equal(s.Members[0], got[0].Shooter);
        }
    }
}
