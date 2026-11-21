using Fdp.Core;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Behavior.Diagnostics;
using Fdp.Toolkit.Utility;
using Xunit;

namespace Fdp.Toolkit.Behavior.Tests.Diagnostics
{
    /// <summary>
    /// ⭐ <c>CE-3069</c> G2 — arming a unit's trace (<c>POST /trace/observe</c> → <see cref="DebugState"/>) also attaches its
    /// utility TRACE, and disarming removes it: ONE switch for "observe this unit's AI" (the decision LOG is always on, CE-1319 BV-61).
    /// 📄 <c>docs/DESIGN_Utility_AI_Demo_Scenarios.md</c> §5.1.
    /// </summary>
    public class TraceBufferLifecycleSystemTests
    {
        /// <summary>⭐ <c>CE-1319</c> (Brain View §12e BV-61, R-1040; ⛔ SUPERSEDES CE-3069's "disarming removes the record") — every brain
        /// unit carries the decision LOG always, observed or not, so the Brain tab's Why replays for any unit; arming adds only the
        /// per-consideration TRACE, and disarming takes only the trace away. ✅ Red-proof: the always-on attach dropped ⇒ the unobserved
        /// unit has no log.</summary>
        [Fact]
        public void CE1319_EveryBrainUnit_KeepsTheDecisionLog_AndOnlyTheTraceFollowsTheSwitch()
        {
            using var world = NewWorld(registerUtility: true);
            var unit = world.CreateEntity();
            world.AddComponent(unit, new BehaviorState { BrainTier = BehaviorConstants.BrainTierBTree });
            world.AddComponent(unit, new DebugState { Behavior = BehaviorDebugFlags.EnableTraceBuffer });
            var quiet = world.CreateEntity();                                             // never observed
            world.AddComponent(quiet, new BehaviorState { BrainTier = BehaviorConstants.BrainTierBTree });
            var system = new TraceBufferLifecycleSystem();

            system.Execute(world, 0f);

            Assert.True(world.HasComponent<BTreeTraceWorkingMemory1024>(unit));
            Assert.True(world.HasComponent<UtilityDecisionLog>(unit));
            Assert.True(world.HasComponent<UtilityTraceWorkingMemory1024>(unit));
            Assert.Equal(1, world.GetComponentRO<UtilityDebugFlags>(unit).TraceEnabled);
            Assert.True(world.HasComponent<UtilityDecisionLog>(quiet));                  // ⭐ BV-61: the log without the trace
            Assert.False(world.HasComponent<UtilityTraceWorkingMemory1024>(quiet));

            world.GetComponentRW<DebugState>(unit).Behavior = 0;
            system.Execute(world, 0f);

            Assert.True(world.HasComponent<UtilityDecisionLog>(unit));                   // the log stays
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
