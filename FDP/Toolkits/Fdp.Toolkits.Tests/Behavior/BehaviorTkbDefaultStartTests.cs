using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Behavior.Translators;
using Fdp.Toolkit.Tkb.Domain;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐ <c>CE-3047</c> — a TKB template's default behaviour STARTS THROUGH THE INGRESS (params parsed, start record
    /// written, origin Sop) instead of being stamped into <see cref="BehaviorState"/>. No suite covered
    /// <see cref="BehaviorTkbTranslator"/>'s default before (measured: no test sets <c>DefaultBehaviorHash</c>).
    /// </summary>
    public sealed class BehaviorTkbDefaultStartTests
    {
        private const int DefaultId = 3201, OrderId = 3202;

        private static (EntityRepository world, BehaviorIngressSystem ingress, Entity e) Spawn(bool registerStartEvent = true)
        {
            var world = TestWorldFactory.Create();
            Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(world);
            if (registerStartEvent) world.Bus.Register<AssignBehaviorHashEvent>();

            var registry = new BehaviorRegistry();
            registry.Register(DefaultId, "TkbDefault", new BehaviorDefinition { Name = "TkbDefault", BrainTier = BehaviorConstants.BrainTierBTree });
            registry.Register(OrderId,   "Ordered",    new BehaviorDefinition { Name = "Ordered",    BrainTier = BehaviorConstants.BrainTierBTree });

            var template = new TkbTemplate("TkbDefaultUnit", 1);
            template.AddDescriptor(new BehaviorProfileDto { BrainTier = BehaviorConstants.BrainTierBTree, DefaultBehaviorHash = DefaultId });

            var e = world.CreateEntity();
            new BehaviorTkbTranslator().Inject(world, e, template);
            return (world, new BehaviorIngressSystem(registry), e);
        }

        [Fact]
        public void CE3047_TheDefault_IsNotStamped_ItStartsThroughTheIngress_AtTheLowestRank()
        {
            var (world, ingress, e) = Spawn();
            Assert.Equal(BehaviorIds.None, world.GetComponent<BehaviorState>(e).ActiveBehaviorHash);   // not stamped

            world.Bus.SwapBuffers();
            ingress.Execute(world, 0.016f);

            var state = world.GetComponent<BehaviorState>(e);
            Assert.Equal(DefaultId, state.ActiveBehaviorHash);
            Assert.Equal(BehaviorOrigin.Sop, state.Origin);
            Assert.True(world.HasManagedComponent<BehaviorStartRecord>(e));   // a real start, restartable on hot reload
            world.Dispose();
        }

        [Fact]
        public void CE3047_AnOrder_ReplacesTheDefault()
        {
            var (world, ingress, e) = Spawn();
            world.Bus.SwapBuffers();
            ingress.Execute(world, 0.016f);

            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = "Ordered", JsonParams = "", Origin = BehaviorOrigin.Superior });
            world.Bus.SwapBuffers();
            ingress.Execute(world, 0.016f);

            Assert.Equal(OrderId, world.GetComponent<BehaviorState>(e).ActiveBehaviorHash);
            world.Dispose();
        }

        [Fact]
        public void CE2077_TheTemplatesSop_StartsThroughTheIngress()
        {
            var world = TestWorldFactory.Create();
            Fdp.Toolkit.Blueprints.Partitioning.BlueprintTierTable.RegisterAll(world);
            if (!world.IsComponentTypeRegistered<SopState>()) world.RegisterComponent<SopState>();
            world.Bus.RegisterManaged<AssignSopEvent>();
            var registry = new BehaviorRegistry();
            registry.Register(DefaultId, "TkbSop", new BehaviorDefinition { Name = "TkbSop", BrainTier = BehaviorConstants.BrainTierBTree });

            var template = new TkbTemplate("TkbSopUnit", 1);
            template.AddDescriptor(new BehaviorProfileDto { BrainTier = BehaviorConstants.BrainTierBTree, DefaultSop = "TkbSop" });
            var e = world.CreateEntity();
            new BehaviorTkbTranslator().Inject(world, e, template);
            world.Bus.SwapBuffers();
            new BehaviorIngressSystem(registry).Execute(world, 0.016f);

            var sop = world.GetComponent<SopState>(e);
            Assert.Equal(DefaultId, sop.SopHash);
            Assert.Equal(BehaviorOrigin.Sop, sop.SopOrigin);   // any order may replace the template's SOP
            world.Dispose();
        }

        [Fact]
        public void CE3047_WhereNoIngressRuns_TheUnitSimplyHasNoBehaviour()
        {
            var (world, _, e) = Spawn(registerStartEvent: false);   // a host without the start event: no throw, no stamp
            Assert.Equal(BehaviorIds.None, world.GetComponent<BehaviorState>(e).ActiveBehaviorHash);
            world.Dispose();
        }
    }
}
