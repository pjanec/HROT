using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Systems;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests
{
    /// <summary>
    /// Unit test for <see cref="CognitiveCleanupSystem"/> (BHU-015).
    /// Verifies that interrupt bytes 126 and 127 are cleared each frame.
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

            // Set both interrupt bytes.
            ref var bb = ref world.GetComponentRW<BrainBlackboard>(e);
            bb.Memory[126] = 1;
            bb.Memory[127] = 1;

            sys.Execute(world, 0.016f);

            var bbAfter = world.GetComponent<BrainBlackboard>(e);
            Assert.Equal(0, bbAfter.Memory[126]);
            Assert.Equal(0, bbAfter.Memory[127]);

            world.Dispose();
        }
    }
}
