using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// Unit test for <see cref="CognitiveCleanupSystem"/> (BHU-015).
    /// Verifies that interrupt fields are cleared each frame.
    /// </summary>
    public unsafe class CognitiveCleanupSystemTests
    {
        [Fact]
        public void CognitiveCleanup_ClearsInterruptBytes()
        {
            var world = TestWorldFactory.Create();
            var sys   = new CognitiveCleanupSystem();

            var e = world.CreateEntity();
            world.AddComponent(e, new BrainBlackboard());

            // Set both interrupt fields.
            ref var bb = ref world.GetComponentRW<BrainBlackboard>(e);
            bb.Interrupt_MobilityLost = 1;
            bb.Interrupt_Reserved     = 1;

            sys.Execute(world, 0.016f);

            var bbAfter = world.GetComponent<BrainBlackboard>(e);
            Assert.Equal(0, bbAfter.Interrupt_MobilityLost);
            Assert.Equal(0, bbAfter.Interrupt_Reserved);

            world.Dispose();
        }
    }
}
