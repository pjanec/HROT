using Xunit;
using Fbt.Runtime;
using Fbt.Tests.TestFixtures;
using Fbt;

namespace Fbt.Tests.Unit
{
    public class ActionRegistryTests
    {
        [Fact]
        public void Register_StoresAction()
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            NodeLogicDelegate<TestBlackboard, MockContext> action = (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) => NodeStatus.Success;
            
            registry.Register("TestAction", action);
            
            Assert.True(registry.TryGetAction("TestAction", out var retrieved));
            Assert.Same(action, retrieved);
        }

        [Fact]
        public void TryGetAction_ReturnsFalse_ForMissingAction()
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            Assert.False(registry.TryGetAction("Missing", out _));
        }

        [Fact]
        public void Register_OverwritesExisting()
        {
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            NodeLogicDelegate<TestBlackboard, MockContext> action1 = (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) => NodeStatus.Success;
            NodeLogicDelegate<TestBlackboard, MockContext> action2 = (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) => NodeStatus.Failure;
            
            registry.Register("Action", action1);
            registry.Register("Action", action2);
            
            registry.TryGetAction("Action", out var retrieved);
            Assert.Same(action2, retrieved);
        }
    }
}
