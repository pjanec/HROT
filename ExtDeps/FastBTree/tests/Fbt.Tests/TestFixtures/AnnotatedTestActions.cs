using Fbt.Runtime;

namespace Fbt.Tests.TestFixtures
{
    // This class is scanned by BTreeActionGenerator and FbtAutoDiscovery in tests.
    // All methods are 4-param (NodeLogicDelegate) for generator registration.
    public static class AnnotatedTestActions
    {
        [BTreeAction]
        public static NodeStatus AlwaysSuccessAction(
            ref TestBlackboard bb,
            ref BehaviorTreeState state,
            ref MockContext ctx,
            int paramIndex)
            => NodeStatus.Success;

        [BTreeCondition]
        public static NodeStatus AlwaysSuccessCondition(
            ref TestBlackboard bb,
            ref BehaviorTreeState state,
            ref MockContext ctx,
            int paramIndex)
            => NodeStatus.Success;
    }
}
