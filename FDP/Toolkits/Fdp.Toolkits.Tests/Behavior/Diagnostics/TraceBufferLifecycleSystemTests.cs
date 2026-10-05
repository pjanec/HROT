using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests.Diagnostics
{
    /// <summary>
    /// ⭐ <c>CE-3069</c> G2 — arming a unit's trace (<c>POST /trace/observe</c> → <see cref="DebugState"/>) also attaches its
    /// utility record, and disarming removes it: ONE switch for "observe this unit's AI".
    /// 📄 <c>docs/DESIGN_Utility_AI_Demo_Scenarios.md</c> §5.1.
    /// </summary>
    public class TraceBufferLifecycleSystemTests
    {
        [Fact]
        public void ArmingTheTrace_AttachesTheUtilityRecord_AndDisarmingRemovesIt()
        {
            using var world = NewWorld(registerUtility: true);
            var unit = world.CreateEntity();
            world.AddComponent(unit, new BehaviorState { BrainTier = BehaviorConstants.BrainTierBTree });
            world.AddComponent(unit, new DebugState { Behavior = BehaviorDebugFlags.EnableTraceBuffer });
            var system = new TraceBufferLifecycleSystem();

            system.Execute(world, 0f);

            Assert.True(world.HasComponent<BTreeTraceWorkingMemory1024>(unit));
            Assert.True(world.HasComponent<UtilityDecisionLog>(unit));
            Assert.True(world.HasComponent<UtilityTraceWorkingMemory1024>(unit));
            Assert.Equal(1, world.GetComponentRO<UtilityDebugFlags>(unit).TraceEnabled);

            world.GetComponentRW<DebugState>(unit).Behavior = 0;
            system.Execute(world, 0f);

            Assert.False(world.HasComponent<UtilityDecisionLog>(unit));
            Assert.False(world.HasComponent<UtilityTraceWorkingMemory1024>(unit));
            Assert.False(world.HasComponent<UtilityDebugFlags>(unit));
        }

        [Fact]
        public void AWorldWithoutTheUtilityComponents_StillArmsTheBehaviourTrace()
        {
            using var world = NewWorld(registerUtility: false);
            var unit = world.CreateEntity();
            world.AddComponent(unit, new BehaviorState { BrainTier = BehaviorConstants.BrainTierBTree });
            world.AddComponent(unit, new DebugState { Behavior = BehaviorDebugFlags.EnableTraceBuffer });

            new TraceBufferLifecycleSystem().Execute(world, 0f);

            Assert.True(world.HasComponent<BTreeTraceWorkingMemory1024>(unit));
        }

        private static EntityRepository NewWorld(bool registerUtility)
        {
            var world = new EntityRepository();
            world.RegisterComponent<BehaviorState>();
            world.RegisterComponent<DebugState>();
            world.RegisterComponent<BTreeTraceWorkingMemory1024>();
            world.RegisterComponent<HsmTraceWorkingMemory1024>();
            if (registerUtility)
            {
                world.RegisterComponent<UtilityDecisionLog>();
                world.RegisterComponent<UtilityDebugFlags>();
                world.RegisterComponent<UtilityTraceWorkingMemory1024>();
            }
            return world;
        }
    }
}
