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
    public class SquadManeuverTests
    {
        private sealed class Squad
        {
            public readonly EntityRepository Repo = new();
            public readonly BehaviorRegistry Registry = new();
            public readonly Entity Commander;
            public readonly Entity[] Members;
            private readonly BehaviorIngressSystem _ingress;
            private readonly BrainTickSystem _brain;
            private double _time;
            private long _frame;
            public readonly List<AssignTacticalIntentEvent> Orders = new();

            public Squad(int members)
            {
                SimHostComponentRegistry.RegisterAll(Repo);
                CognitiveComponentRegistry.RegisterAll(Repo);
                BlueprintTierTable.RegisterAll(Repo);
                var geo = new WGS84Transform();
                geo.SetOrigin(0.0, 0.0, 0.0);
                Repo.SetSingletonManaged<IGeographicTransform>(geo);
                CgfBehaviorSetup.LoadFromAiAssembly(Registry);
                Fdp.Toolkit.Utility.StandardInputs.RegisterAll();
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
                foreach (var o in Repo.Bus.ReadManaged<AssignTacticalIntentEvent>()) Orders.Add(o);
                _ingress.Execute(Repo, 0.016f);
                _brain.Execute(Repo, 0.016f);
                Repo.FlushCommandBuffers();
            }

            public void Order(string json, string behaviour = BehaviorNames.BoundingOverwatch)
                => Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Commander, BehaviorName = behaviour, JsonParams = json, Origin = BehaviorOrigin.Superior });

            /// <summary>⭐ CE-2123 — time passes with the commander's task not ticked (a pause).</summary>
            public void Pause(double seconds) => _time += seconds;

            /// <summary>⭐ CE-2123 — the last order each member got, whole.</summary>
            public Dictionary<Entity, CombatPostureParamsJsonDto> LastOrders()
            {
                var d = new Dictionary<Entity, CombatPostureParamsJsonDto>();
                foreach (var o in Orders)
                    d[o.Entity] = System.Text.Json.JsonSerializer.Deserialize<CombatPostureParamsJsonDto>(
                        o.JsonParams, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed);
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

            /// <summary>⭐ CE-2123 — the commander's flank query, and SimHost's answer to it.</summary>
            public Entity FlankQuery() => Fdp.Toolkit.Spatial.Eqs.EqsChildSensor.Find(Repo, Commander, SuppressAndManeuverPlay.FlankSite);

            public void Answer(Entity sensor, Vector3? best)
            {
                var buf = new Fdp.Toolkit.Spatial.Eqs.EqsCognitiveBuffer { Count = best.HasValue ? 1 : 0, LastUpdateTick = 7 };
                if (best.HasValue) buf.GetSpanRW()[0] = new Fdp.Toolkit.Spatial.Eqs.EqsResult { PositionX = best.Value.X, PositionY = best.Value.Y, Score = 1f };
                Repo.SetComponent(sensor, buf);
            }

            /// <summary>The last order each member got, as CombatPosture's contract.</summary>
            public Dictionary<Entity, Vector3> LastObjectives()
            {
                var d = new Dictionary<Entity, Vector3>();
                foreach (var o in Orders)
                {
                    Assert.Equal(BehaviorNames.CombatPosture, o.IntentId);
                    Assert.Equal(BehaviorOrigin.Superior, o.Origin);
                    d[o.Entity] = System.Text.Json.JsonSerializer.Deserialize<CombatPostureParamsJsonDto>(
                        o.JsonParams, Fdp.Core.Serialization.FdpJsonOptionsRegistry.DefaultRelaxed).Advance.Objective;
                }
                return d;
            }

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
            s.Pool(enemy, new Vector3(0f, 60f, 0f), reporters: 0b0011);          // members 0 and 1 see him
            s.Order("{}", BehaviorNames.SuppressAndManeuver);
            s.Tick(); s.Tick();

            Assert.Equal(SuppressAndManeuverManeuver.PhaseSuppressing, s.Phase);
            var first = s.LastOrders();
            Assert.Equal(4, first.Count);
            foreach (var m in s.Members) Assert.Equal(s.At(m), first[m].Advance.Objective);    // everyone holds until the terrain answers
            Assert.False(first[s.Members[0]].Assault);

            var query = s.FlankQuery();
            Assert.False(query.IsNull, "the commander owns a flank query");
            var cfg = s.Repo.GetComponentRO<Fdp.Toolkit.Spatial.Eqs.EqsSensor>(query);
            Assert.Equal(Fdp.Toolkit.Spatial.Eqs.FindFlankingPosition.BlueprintId, cfg.BlueprintId);
            Assert.Equal(s.Members[0], cfg.ContextSlot0);                            // measured from a member that sees the enemy
            Assert.Equal(enemy, cfg.ContextSlot1);                                   // aimed at the enemy

            s.Orders.Clear();
            s.Answer(query, new Vector3(40f, 60f, 0f));
            s.Tick(); s.Tick();
            var flank = s.LastOrders();
            Assert.Equal(new Vector3(40f, 60f, 0f), flank[s.Members[2]].Advance.Objective);
            Assert.Equal(new Vector3(40f, 60f, 0f), flank[s.Members[3]].Advance.Objective);
            Assert.True(flank[s.Members[2]].Assault);
            Assert.Equal(s.At(s.Members[0]), flank[s.Members[0]].Advance.Objective);   // the base keeps holding
            Assert.True(s.FlankQuery().IsNull, "the answered query goes");

            s.Orders.Clear();
            s.Teleport(s.Members[2], new Vector3(39f, 60f, 0f));
            s.Teleport(s.Members[3], new Vector3(41f, 61f, 0f));
            s.Tick(); s.Tick();
            Assert.Equal(SuppressAndManeuverManeuver.PhaseAssaulting, s.Phase);
            var through = s.LastOrders();
            Assert.Equal(new Vector3(0f, 60f, 0f), through[s.Members[2]].Advance.Objective);   // onto the enemy's position

            s.Teleport(s.Members[2], new Vector3(2f, 60f, 0f));
            s.Teleport(s.Members[3], new Vector3(1f, 58f, 0f));
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
            Assert.Equal(new Vector3(0f, 60f, 0f), s.LastOrders()[s.Members[3]].Advance.Objective);
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
    }
}
