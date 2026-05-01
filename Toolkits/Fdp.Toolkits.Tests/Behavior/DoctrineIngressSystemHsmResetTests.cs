using System.Runtime.CompilerServices;
using Fdp.Core;
using Fhsm.Kernel.Data;
using Fdp.Toolkit.Behavior;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Events;
using Fdp.Toolkit.Behavior.Systems;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// Tests for BHU-016: <see cref="DoctrineIngressSystem"/> resets HSM instance state
    /// on doctrine assignment so the new doctrine always starts clean.
    /// </summary>
    public unsafe class DoctrineIngressSystemHsmResetTests
    {
        private static (EntityRepository world, DoctrineIngressSystem sys, DoctrineRegistry registry)
            CreateFixture()
        {
            var world    = TestWorldFactory.Create();
            var registry = new DoctrineRegistry();
            var sys      = new DoctrineIngressSystem(registry);
            return (world, sys, registry);
        }

        /// <summary>
        /// Helper: publish an AssignDoctrineEvent, swap buffers, then run the ingress system.
        /// </summary>
        private static void AssignDoctrine(
            EntityRepository world,
            DoctrineIngressSystem sys,
            Entity entity,
            string doctrineName)
        {
            world.Bus.PublishManaged(new AssignDoctrineEvent
            {
                Entity       = entity,
                DoctrineName = doctrineName,
            });
            world.Bus.SwapBuffers();
            sys.Execute(world, 0.016f);
        }

        // ---- Tests ----

        [Fact]
        public void DoctrineIngress_HsmReset_ClearsTerminatedFlagAndSetsPhaseIdle()
        {
            var (world, sys, registry) = CreateFixture();

            const string doctrineName = "HsmResetDoc";
            registry.Register(9300, doctrineName, new DoctrineDefinition
            {
                Name      = doctrineName,
                BrainTier = BehaviorConstants.BrainTierHsm,
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState());
            world.AddComponent(e, new BrainHsm64());

            // Manually set Terminated flag and a non-Idle phase to simulate
            // an HSM that ended its previous doctrine in a terminal state.
            ref var brain = ref world.GetComponentRW<BrainHsm64>(e);
            brain.State.Header.Flags |= InstanceFlags.Terminated;
            brain.State.Header.Phase  = InstancePhase.RTC;

            // Assign a new doctrine -- ingress system must reset the HSM.
            AssignDoctrine(world, sys, e, doctrineName);

            var brainAfter = world.GetComponent<BrainHsm64>(e);
            Assert.Equal(0, (int)(brainAfter.State.Header.Flags & InstanceFlags.Terminated));
            Assert.Equal(InstancePhase.Idle, brainAfter.State.Header.Phase);

            world.Dispose();
        }

        [Fact]
        public void DoctrineIngress_HsmReset_ClearsActiveLeafIds()
        {
            var (world, sys, registry) = CreateFixture();

            const string doctrineName = "HsmResetDoc2";
            registry.Register(9301, doctrineName, new DoctrineDefinition
            {
                Name      = doctrineName,
                BrainTier = BehaviorConstants.BrainTierHsm,
            });

            var e = world.CreateEntity();
            world.AddComponent(e, new DoctrineState());
            world.AddComponent(e, new BrainHsm64());

            // Simulate a machine that was mid-run: set ActiveLeafIds to non-sentinel values.
            ref var brain = ref world.GetComponentRW<BrainHsm64>(e);
            brain.State.ActiveLeafIds[0] = 2;
            brain.State.ActiveLeafIds[1] = 5;

            // Assign doctrine -- ingress system resets leaf IDs to 0xFFFF (uninitialized).
            AssignDoctrine(world, sys, e, doctrineName);

            var brainAfter = world.GetComponent<BrainHsm64>(e);
            Assert.Equal(0xFFFF, brainAfter.State.ActiveLeafIds[0]);
            Assert.Equal(0xFFFF, brainAfter.State.ActiveLeafIds[1]);

            world.Dispose();
        }
    }
}
