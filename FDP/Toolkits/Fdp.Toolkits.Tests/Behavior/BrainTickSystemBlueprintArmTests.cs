using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Fbt;
using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Fdp.Toolkit.Blueprints.Partitioning;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// ⭐⭐ <b><c>CE-446</c> — ARM 3 of <see cref="BrainTickSystem"/>: a behaviour implemented by a blueprint.</b>
    /// 📄 <c>Architect_Question_77</c> §3 C/D.
    ///
    /// <para>
    /// ⭐ Driven through the REAL ingress (so the store and the root block are provisioned exactly as in production)
    /// with a hand-written <see cref="BlueprintBehaviorTickDelegate"/> standing in for the generated one — the runtime
    /// contract is what these rails pin; the compiler half has its own.
    /// </para>
    /// </summary>
    public unsafe class BrainTickSystemBlueprintArmTests
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Block
        {
            public int Target;   // In — from the intent JSON
            public int Count;    // St — the behaviour's own state
        }

        private const string Name = "CountToTarget";
        private const int    Id   = 9301;

        private static (EntityRepository world, BehaviorIngressSystem ingress, BrainTickSystem brain, Entity e, Func<int> ticks)
            Fixture(string json)
        {
            var world = TestWorldFactory.Create();
            BlueprintTierTable.RegisterAll(world);

            int ticks = 0;
            var registry = new BehaviorRegistry();
            registry.Register(Id, Name, new BehaviorDefinition
            {
                Name                 = Name,
                BrainTier            = BehaviorConstants.BrainTierBlueprint,
                BlackboardLayoutType = typeof(Block),
                ParseParams = static (string j, byte* mem, int capacity, EntityRepository w, Entity self) =>
                    ((Block*)mem)->Target = int.Parse(j),
                BlueprintTick = (ref byte block, EntityRepository w, Entity self, float time, float dt) =>
                {
                    ticks++;
                    ref var b = ref Unsafe.As<byte, Block>(ref block);
                    b.Count++;
                    return b.Count >= b.Target ? NodeStatus.Success : NodeStatus.Running;
                },
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new BehaviorState());
            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = Name, JsonParams = json });
            world.Bus.SwapBuffers();

            var ingress = new BehaviorIngressSystem(registry);
            ingress.Execute(world, 0.016f);
            return (world, ingress, new BrainTickSystem(registry), e, () => ticks);
        }

        private static int TickAndCountFinished(EntityRepository world, BrainTickSystem brain, Entity e, int frames)
        {
            int finished = 0;
            for (int i = 0; i < frames; i++)
            {
                brain.Execute(world, 0.016f);
                world.Bus.SwapBuffers();
                foreach (var evt in world.Bus.Read<BehaviorFinishedEvent>())
                    if (evt.Entity.Index == e.Index && evt.Result == NodeStatus.Success) finished++;
            }
            return finished;
        }

        /// <summary>
        /// ⭐⭐⭐ Assign → the tier is committed → it runs over the root block (the JSON reached <c>In</c>, the state
        /// persists across frames) → it finishes on <c>Success</c> → <see cref="BehaviorFinishedEvent"/> EXACTLY once →
        /// and it is NOT ticked again afterwards.
        /// <para>⚠ Inverse-edit red-proof: drop the "already finished" guard in <c>TickBlueprint</c> and the tick count
        /// runs past 3 (and the count past the target).</para>
        /// </summary>
        [Fact]
        public void CE446_ABlueprintBehaviour_RunsOverItsRootBlock_AndFinishesExactlyOnce()
        {
            var (world, _, brain, e, ticks) = Fixture("3");

            Assert.Equal(BehaviorConstants.BrainTierBlueprint, world.GetComponent<BehaviorState>(e).BrainTier);

            int finished = TickAndCountFinished(world, brain, e, frames: 6);

            Assert.Equal(1, finished);
            Assert.Equal(3, ticks());
            ref var block = ref Unsafe.As<byte, Block>(ref RootParamsAccess.RootRef(world, e));
            Assert.Equal(3, block.Target);
            Assert.Equal(3, block.Count);

            world.Dispose();
        }

        /// <summary>
        /// ⭐⭐ A new assign bumps <c>InstanceId</c>, starts the block from empty (<c>R-153</c>) and ticks again — ⇒
        /// "finished" is per instance, never per entity. ⭐ This is also cancellation (<c>Q77</c> D): the new assign
        /// replaces the running one.
        /// </summary>
        [Fact]
        public void CE446_ReassigningAFinishedBlueprintBehaviour_RunsItAgainFromAnEmptyBlock()
        {
            var (world, ingress, brain, e, ticks) = Fixture("2");
            Assert.Equal(1, TickAndCountFinished(world, brain, e, frames: 4));

            world.Bus.PublishManaged(new AssignBehaviorEvent { Entity = e, BehaviorName = Name, JsonParams = "2" });
            world.Bus.SwapBuffers();
            ingress.Execute(world, 0.016f);

            Assert.Equal(1, TickAndCountFinished(world, brain, e, frames: 4));
            Assert.Equal(4, ticks());
            Assert.Equal(2, Unsafe.As<byte, Block>(ref RootParamsAccess.RootRef(world, e)).Count);

            world.Dispose();
        }

        /// <summary>
        /// ⛔ The root block is declared <see cref="OccurrenceKind.BlueprintBehavior"/> — NOT <see cref="OccurrenceKind.Blueprint"/>,
        /// which <c>BlueprintTickSystem</c> walks as an attached Instance and ingress sweeps as a hosted occurrence.
        /// </summary>
        [Fact]
        public void CE446_TheRootBlock_IsDeclaredAsABlueprintBehaviourSlot_NotAnInstance()
        {
            var (world, _, _, e, _) = Fixture("1");

            byte* memory = OccurrenceStoreAccess.TryGetStore(world, e, out _);
            Assert.True(memory != null, "the store must exist");
            int key = RootParamsAccess.KeyFor(world, e);

            ref var header = ref Unsafe.AsRef<BlueprintBlackboardHeader>(memory);
            byte* table = memory + sizeof(BlueprintBlackboardHeader);
            int found = -1;
            for (int i = 0; i < header.SlotCount; i++)
                if (Unsafe.AsRef<BlueprintSlotEntry>(table + i * BlueprintBlackboardPartitions.SlotEntrySize).BlueprintId == key)
                    found = i;

            Assert.True(found >= 0, "the root block must be attached");
            Assert.Equal(OccurrenceKind.BlueprintBehavior, BlueprintBlackboardPartitions.GetSlotKind(memory, found));

            world.Dispose();
        }
    }
}
