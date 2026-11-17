using System;
using Fdp.Core;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Aggregate;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Replication.Components;
using Hrot.CGF.Systems;
using Hrot.Core.Tkb;
using Xunit;

namespace Hrot.SimHost.Tests
{
    /// <summary>⭐⭐ <c>CE-8301</c> (agg-behaviors ST-5, F-5, R-8327) — a unit that CHANGES resolution (a merge, a split) re-resolves its template-default SOP.</summary>
    public partial class SquadManeuverTests
    {
        private const string PhysicalSop = "BasicSquadSop";

        private static void AdvanceOneSecond(Squad s, UnitSopResolutionSystem sys)
        {
            s.Pause(0.984);
            s.Tick();
            sys.Execute(s.Repo, 1f);
            s.Tick(); s.Tick();   // the ingress takes the AssignSopEvent the system published (a frame later)
        }

        private static void GiveTheTemplateASop(EntityRepository repo, Entity u)
        {
            const long Type = 7701;
            var db = new Fdp.Toolkit.Tkb.TkbDatabase();
            var t = new Fdp.Interfaces.TkbTemplate("TestSquad", Type);
            t.AddDescriptor(new Fdp.Toolkit.Tkb.Domain.BehaviorProfileDto { DefaultSop = PhysicalSop, DefaultSopParamsJson = "{}" });
            db.Register(t);
            repo.SetSingletonManaged<Fdp.Interfaces.ITkbDatabase>(db);
            if (!repo.IsComponentTypeRegistered<TkbIdentity>()) repo.RegisterComponent<TkbIdentity>();
            if (repo.HasComponent<TkbIdentity>(u)) repo.SetComponent(u, new TkbIdentity { TkbType = (TkbTypeId)Type });
            else repo.AddComponent(u, new TkbIdentity { TkbType = (TkbTypeId)Type });
        }

        /// <summary>⭐⭐ F-5 ACCEPTANCE — a physical unit spawned with its template's <c>BasicSquadSop</c> and then MERGED (the manager flips its resolution to stochastic) runs the table's
        /// <c>StochasticUnitSop</c> within a second; SPLIT again (back to physical) it runs the template's own. ✅ Red-proofs: the system not publishing on a stochastic unit ⇒ the SOP
        /// stays <c>BasicSquadSop</c> after the merge ⇒ red · not publishing the template's on a physical one ⇒ red after the split.</summary>
        [Fact]
        public void F5_AUnitThatMerges_GetsTheTablesSop_AndOneThatSplits_GetsTheTemplatesBack()
        {
            var (s, _, repo, u) = StochasticMover();
            repo.GetComponentRW<AggregateUnit>(u).Resolution = AggregateResolution.Physical;
            GiveTheTemplateASop(repo, u);
            SpawnWithTheTemplatesSop(s, u);
            s.Tick(); s.Tick();
            Assert.Equal(BehaviorHash.FromName(PhysicalSop), repo.GetComponentRO<SopState>(u).SopHash);   // physical: its own
            var sys = new UnitSopResolutionSystem(s.Registry);

            repo.GetComponentRW<AggregateUnit>(u).Resolution = AggregateResolution.Stochastic;            // the merge
            AdvanceOneSecond(s, sys);
            Assert.Equal(BehaviorHash.FromName(Sop), repo.GetComponentRO<SopState>(u).SopHash);           // the table's row

            repo.GetComponentRW<AggregateUnit>(u).Resolution = AggregateResolution.Physical;              // the split
            AdvanceOneSecond(s, sys);
            Assert.Equal(BehaviorHash.FromName(PhysicalSop), repo.GetComponentRO<SopState>(u).SopHash);   // the template's own again
        }

        /// <summary>⭐ F-5 — an SOP somebody CHOSE (an author's, a commander's: origin above the template's) is never replaced by a resolution change. ✅ Red-proof: the system asking at an origin that outranks the template (an operator) and with no origin guard ⇒ the choice is replaced ⇒ red (the ingress refuses a lower origin too, so the guard alone is not what the rail pins).</summary>
        [Fact]
        public void F5_AnExplicitSop_IsNeverReplaced()
        {
            var (s, _, repo, u) = StochasticMover();
            repo.GetComponentRW<AggregateUnit>(u).Resolution = AggregateResolution.Physical;
            GiveTheTemplateASop(repo, u);
            StochasticSopRouting.Resolver = Hrot.CGF.Systems.TacticalIntentResolutionSystem.SopFor;
            repo.AddComponent(u, new SopState());
            repo.Bus.PublishManaged(new AssignSopEvent { Entity = u, BehaviorName = PhysicalSop, JsonParams = "{}", Origin = BehaviorOrigin.Operator });
            s.Tick(); s.Tick();
            Assert.Equal(BehaviorOrigin.Operator, repo.GetComponentRO<SopState>(u).SopOrigin);
            var sys = new UnitSopResolutionSystem(s.Registry);
            repo.GetComponentRW<AggregateUnit>(u).Resolution = AggregateResolution.Stochastic;
            AdvanceOneSecond(s, sys);
            Assert.Equal(BehaviorHash.FromName(PhysicalSop), repo.GetComponentRO<SopState>(u).SopHash);   // his choice stands
        }

        /// <summary>⭐ F-5 (0 B) — the steady state (nothing changed) allocates nothing: the check is a hash compare. ✅ Red-proof: a <c>new List</c> per unit in <c>Execute</c> ⇒ red.</summary>
        [Fact]
        public void F5_TheSteadyCheck_AllocatesNothing()
        {
            var (s, _, repo, u) = StochasticMover();
            SpawnWithTheTemplatesSop(s, u);
            s.Tick(); s.Tick();
            var sys = new UnitSopResolutionSystem(s.Registry);
            for (int i = 0; i < 4; i++) AdvanceOneSecond(s, sys);   // settled: the table's SOP, and the warm-up
            Assert.Equal(BehaviorHash.FromName(Sop), repo.GetComponentRO<SopState>(u).SopHash);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 200; i++)
            {
                var gt = repo.GetSingleton<GlobalTime>(); gt.TotalTime += 1.0; repo.SetSingleton(gt);
                sys.Execute(repo, 1f);
            }
            Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        /// <summary>⭐ F-5 FORWARDING RAIL (the silent-default rule) — the brain pack registers the system, so every brain host has it. ✅ Red-proof: the registration removed ⇒ red.</summary>
        [Fact]
        public void F5_TheBrainPack_RegistersTheSopResolutionSystem()
        {
            var pack = new Hrot.CGF.CgfLogicPack(new BehaviorRegistry(), new NetworkEntityMap(),
                Hrot.AI.Behaviors.Mappers.DefaultTacticalMappers.Create(), new Fdp.Toolkit.Blueprints.BlueprintRegistry());
            Assert.Contains(pack.SimulationSystems, x => x is UnitSopResolutionSystem);
            var mgr = Assert.Single(pack.SimulationSystems, x => x is ResolutionManagerSystem) as ResolutionManagerSystem;
            // ⭐ R-8326 — the manager's policy is the World Environment's component (CE-8300), not the SV-9 default every host would silently keep
            using var repo = new EntityRepository();
            repo.RegisterComponent<ResolutionPolicy>();
            repo.AddComponent(repo.CreateEntity(), new ResolutionPolicy { PhysicalBand = 777f });
            Assert.Equal(777f, mgr!.Policy(repo).PhysicalBand);
        }
    }
}
