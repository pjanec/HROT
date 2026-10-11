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

            public void Order(string json)
                => Repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = Commander, BehaviorName = BehaviorNames.BoundingOverwatch, JsonParams = json, Origin = BehaviorOrigin.Superior });

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
    }
}
