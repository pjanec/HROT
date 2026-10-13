using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Fdp.Core;
using Fdp.Core.CommandHierarchy;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Replication.Components;
using Hrot.CGF.Configuration;
using Hrot.SimHost.Systems;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>
    /// ⭐ <c>R-2006</c> — the RUNTIME proof of <c>SubordinateForEachDemo.bp.json</c> (a corpus blueprint behaviour, compiled by the real
    /// <c>Hrot.AI.Behaviors</c> generator): <c>FlowForEach</c> over a commander's <c>UnitRoster</c> visits every subordinate once, its
    /// <c>CurrentItem</c> / <c>CurrentIndex</c> / <c>Count</c> outs carry the right values, <c>SendIntent</c> publishes one order each,
    /// and the <c>Not</c>-guarded branch finishes on the next tick. It replaces the runtime coverage <c>PlatoonHillAttackBp</c> gave
    /// these nodes (design <c>DESIGN_Orders_Params_Entity_Classes.md</c> §13.5).
    /// </summary>
    public sealed class SubordinateForEachDemoTests : IDisposable
    {
        private const string Bp = "SubordinateForEachDemo";

        private readonly EntityRepository _repo = new();
        private readonly BehaviorIngressSystem _ingress;
        private readonly BrainTickSystem _brain;
        private readonly List<AssignTacticalIntentEvent> _orders = new();
        private readonly List<BehaviorFinishedEvent> _finished = new();

        public SubordinateForEachDemoTests()
        {
            SimHostComponentRegistry.RegisterAll(_repo);
            CognitiveComponentRegistry.RegisterAll(_repo);
            _repo.RegisterComponent<NetworkIdentity>();
            BlueprintTierTable.RegisterAll(_repo);
            var registry = new BehaviorRegistry();
            CgfBehaviorSetup.LoadFromAiAssembly(registry);
            _ingress = new BehaviorIngressSystem(registry);
            _brain = new BrainTickSystem(registry);
        }

        public void Dispose() => _repo.Dispose();

        private Entity Commander(int subordinates, out Entity[] subs)
        {
            subs = new Entity[subordinates];
            for (int i = 0; i < subordinates; i++)
            {
                subs[i] = _repo.CreateEntity();
                _repo.AddComponent<BehaviorState>(subs[i], default);
            }
            var c = _repo.CreateEntity();
            _repo.AddComponent<BehaviorState>(c, default);
            RootStateAccess.EnsureRootState(_repo, c);
            var roster = new UnitRoster { Count = subordinates };
            for (int i = 0; i < subordinates; i++) roster.SubordinateEntities[i] = subs[i];
            _repo.AddComponent(c, roster);
            return c;
        }

        private void Tick(float dt = 0.1f)
        {
            _repo.Bus.SwapBuffers();
            _ingress.Execute(_repo, dt);
            _brain.Execute(_repo, dt);
            _repo.Bus.SwapBuffers();
            _orders.AddRange(_repo.Bus.ReadManaged<AssignTacticalIntentEvent>().Where(o => o != null));
            foreach (var f in _repo.Bus.Read<BehaviorFinishedEvent>()) _finished.Add(f);
            ((EntityCommandBuffer)((ISimulationView)_repo).GetCommandBuffer()).Playback(_repo);
        }

        private static (float X, float Y) XY(AssignTacticalIntentEvent o)
        {
            using var d = JsonDocument.Parse(o.JsonParams);
            float F(string name)   // case-insensitive, as every params reader is (R-2005); the DTO writes "x" / "y"
                => d.RootElement.EnumerateObject().First(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)).Value.GetSingle();
            return (F("X"), F("Y"));
        }

        /// <summary>
        /// Three subordinates. Tick 1 (the roster loop): exactly one MoveToLocation each, X = roster index × 10, Y = the roster count.
        /// Tick 2 (the struct list walked back, each slot read off the enum list): exactly one confirming order each, X = index × 10,
        /// Y = -1 — which only happens if the struct list recorded the right unit and index and the enum list holds Ordered at that
        /// index. Afterwards it holds: no further orders, and it does not finish.
        /// </summary>
        [Fact]
        public void R2006_RosterLoop_ThenRecordedOrders_ConfirmEachOnce_ThenHolds()
        {
            var c = Commander(3, out var subs);
            _repo.Bus.PublishManaged(new AssignBehaviorEvent { Entity = c, BehaviorName = Bp, JsonParams = "{}" });

            for (int t = 0; t < 10; t++) Tick();

            var moves = _orders.Where(o => o.IntentId == "MoveToLocation").ToList();
            Assert.Equal(6, moves.Count);
            for (int i = 0; i < subs.Length; i++)
            {
                var mine = moves.Where(o => o.Entity == subs[i]).Select(XY).ToList();
                Assert.Equal(new[] { (i * 10f, 3f), (i * 10f, -1f) }, mine);
            }
            Assert.DoesNotContain(_finished, f => f.Entity == c);
        }
    }
}
