using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Fdp.Toolkit.Replication.Messages;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ <c>CE-3048</c> (V7) — a node that GAINS a unit's Brain runs what the previous owner published: the slots that
    /// differ are replaced (whatever their rank), the ones that are the same keep running. Runs the real ingress.
    /// 📄 <c>docs/DESIGN_Sensors_And_Doctrine.md</c> §7.7.
    /// </summary>
    public sealed class BrainHandOverTests
    {
        private static readonly long IntentKey = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(98, 0),
                                    OtherKey  = Fdp.Toolkit.Replication.Extensions.OwnershipExtensions.PackKey(52, 0);

        private static NodeStatus Runs(ref byte bb, ref BehaviorTreeState s, ref BTreeContext ctx, int p) => NodeStatus.Running;

        private static BehaviorDefinition Tree(string name)
        {
            var b = new BTreeBuilder<byte, BTreeContext>().Sequence(seq => seq.Action(Runs));
            return new BehaviorDefinition
            {
                Name = name, BrainTier = BehaviorConstants.BrainTierBTree, WritesChannels = System.Array.Empty<System.Type>(),
                BTreeInterpreter = new Interpreter<byte, BTreeContext>(b.Compile(name), b.GetRegistry()),
            };
        }

        private sealed class Fixture
        {
            public readonly EntityRepository World = TestWorldFactory.Create();
            public readonly BehaviorRegistry Registry = new();
            public readonly BehaviorIngressSystem Ingress;
            public readonly BrainHandOverSystem HandOver;
            public readonly Entity Unit;

            public Fixture()
            {
                BlueprintTierTable.RegisterAll(World);
                if (!World.IsComponentTypeRegistered<SopState>()) World.RegisterComponent<SopState>();
                if (!World.IsComponentTypeRegistered<Roe>()) World.RegisterComponent<Roe>();
                if (!World.Bus.IsRegisteredManaged<AssignBehaviorEvent>()) World.Bus.RegisterManaged<AssignBehaviorEvent>();
                if (!World.Bus.IsRegistered<DescriptorAuthorityChanged>()) World.RegisterEvent<DescriptorAuthorityChanged>();
                World.RegisterManagedComponent<ReplicatedBrainIntent>();
                Registry.Register(0x4801, "HoT_Default", Tree("HoT_Default"));
                Registry.Register(0x4802, "HoT_Order",   Tree("HoT_Order"));
                Registry.Register(0x4803, "HoT_Stale",   Tree("HoT_Stale"));
                Registry.Register(0x4804, "HoT_Sop",     Tree("HoT_Sop"));
                Registry.Register(0x4805, "HoT_Cover",   Tree("HoT_Cover"));
                Ingress  = new BehaviorIngressSystem(Registry);
                HandOver = new BrainHandOverSystem(Registry, IntentKey);
                Unit     = World.CreateEntity();
                World.AddComponent(Unit, new BehaviorState());
            }

            public BehaviorState Task => World.GetComponentRO<BehaviorState>(Unit);

            public void Publish(InitialBrainIntent intent) => World.SetManagedComponent(Unit, new ReplicatedBrainIntent { Intent = intent });

            public void Gain(long key, bool gained = true)
            {
                World.Bus.Publish(new DescriptorAuthorityChanged { Entity = Unit, PackedKey = key, IsAuthoritative = gained });
                World.Bus.SwapBuffers();
                HandOver.Execute(World, 0.016f);
            }

            public void React(string name)
            {
                World.Bus.PublishManaged(new AssignBehaviorEvent
                {
                    Entity = Unit, BehaviorName = name, JsonParams = "{}", Origin = BehaviorOrigin.Reaction, Urgency = ReactionUrgency.Hit,
                });
                World.Bus.SwapBuffers();
                Ingress.Execute(World, 0.016f);
            }
        }

        private static SavedBrainSlot Slot(string name, BehaviorOrigin origin, string json = "{}")
            => new() { Name = name, Params = json, Origin = origin };

        [Fact]
        public void CE3048_AGain_ReplacesAStaleHigherOrder_WithWhatWasPublished_TaskSopAndRoe()
        {
            var f = new Fixture();
            Assert.True(f.Ingress.AssignNow(f.World, f.Unit, "HoT_Stale", "{}", BehaviorOrigin.Operator));   // a stale Operator order
            f.Publish(new InitialBrainIntent
            {
                Behavior = Slot("HoT_Order", BehaviorOrigin.Superior, "{\"Speed\":3}"),
                Sop      = Slot("HoT_Sop", BehaviorOrigin.Sop),
                Roe      = new SavedRoe { Fire = RoeFire.HoldFire, Reactions = RoeReactions.StayOnTask, SetBy = BehaviorOrigin.Superior },
            });

            f.Gain(IntentKey);

            Assert.Equal(1, f.HandOver.HandOverCount);
            Assert.True(f.Registry.TryGetId("HoT_Order", out int order));
            Assert.Equal(order, f.Task.ActiveBehaviorHash);
            Assert.Equal(BehaviorOrigin.Superior, f.Task.Origin);                 // the PUBLISHED origin, below the stale one
            var now = BrainIntentReader.Read(f.World, f.Unit, f.Registry, BrainIntentScope.Running);
            Assert.Equal("{\"Speed\":3}", now.Behavior!.Params);
            Assert.Equal("HoT_Sop", now.Sop!.Name);
            Assert.Equal(RoeFire.HoldFire, now.Roe!.Fire);
            Assert.Equal(BehaviorOrigin.Superior, now.Roe.SetBy);
        }

        [Fact]
        public void CE3048_ASlotThatIsTheSame_KeepsRunning_NoRestart()
        {
            var f = new Fixture();
            Assert.True(f.Ingress.AssignNow(f.World, f.Unit, "HoT_Default", "{}", BehaviorOrigin.Sop));        // the template default
            uint run = f.Task.InstanceId;
            f.Publish(new InitialBrainIntent { Behavior = Slot("HoT_Default", BehaviorOrigin.Sop) });

            f.Gain(IntentKey);

            Assert.Equal(1, f.HandOver.HandOverCount);
            Assert.Equal(run, f.Task.InstanceId);                                  // not restarted
        }

        [Fact]
        public void CE3048_AStaleReaction_IsEnded_ItsPausedTaskDropped_AndThePublishedTaskRuns()
        {
            var f = new Fixture();
            Assert.True(f.Ingress.AssignNow(f.World, f.Unit, "HoT_Stale", "{}", BehaviorOrigin.Superior));
            f.React("HoT_Cover");
            Assert.Equal(BehaviorOrigin.Reaction, f.Task.Origin);
            Assert.NotNull(BehaviorIngressSystem.PausedTaskOf(f.World, f.Unit));
            f.Publish(new InitialBrainIntent { Behavior = Slot("HoT_Order", BehaviorOrigin.Operator) });

            f.Gain(IntentKey);

            Assert.True(f.Registry.TryGetId("HoT_Order", out int order));
            Assert.Equal(order, f.Task.ActiveBehaviorHash);
            Assert.Equal(BehaviorOrigin.Operator, f.Task.Origin);
            Assert.Null(BehaviorIngressSystem.PausedTaskOf(f.World, f.Unit));      // the stale reaction's pause went with it
        }

        [Fact]
        public void CE3048_NothingPublishedForASlot_EmptiesIt()
        {
            var f = new Fixture();
            Assert.True(f.Ingress.AssignNow(f.World, f.Unit, "HoT_Stale", "{}", BehaviorOrigin.Operator));
            f.Publish(new InitialBrainIntent());
            f.Gain(IntentKey);
            Assert.Equal(BehaviorIds.None, f.Task.ActiveBehaviorHash);
        }

        [Fact]
        public void CE3048_AnotherKey_ALoss_OrNoReplica_ChangesNothing()
        {
            var f = new Fixture();
            Assert.True(f.Ingress.AssignNow(f.World, f.Unit, "HoT_Stale", "{}", BehaviorOrigin.Operator));
            uint run = f.Task.InstanceId;
            f.Gain(IntentKey);                                                      // no replica yet
            f.Publish(new InitialBrainIntent { Behavior = Slot("HoT_Order", BehaviorOrigin.Superior) });
            f.Gain(OtherKey);                                                       // not the intent's descriptor
            f.Gain(IntentKey, gained: false);                                       // a loss
            Assert.Equal(0, f.HandOver.HandOverCount);
            Assert.Equal(run, f.Task.InstanceId);
        }

        [Fact]
        public void CE3048_TheReader_RunningScopeReportsTheDefault_OrderedScopeDoesNot()
        {
            var f = new Fixture();
            Assert.True(f.Ingress.AssignNow(f.World, f.Unit, "HoT_Default", "{}", BehaviorOrigin.Sop));
            f.World.AddComponent(f.Unit, new Roe { Fire = RoeFire.FireAtWill, SetBy = BehaviorOrigin.Unmarked });
            var running = BrainIntentReader.Read(f.World, f.Unit, f.Registry, BrainIntentScope.Running);
            var ordered = BrainIntentReader.Read(f.World, f.Unit, f.Registry, BrainIntentScope.Ordered);
            Assert.Equal("HoT_Default", running.Behavior!.Name);
            Assert.NotNull(running.Roe);
            Assert.Null(ordered.Behavior);
            Assert.Null(ordered.Roe);
        }
    }
}
