using Fbt;

namespace Fbt.Tests.TestFixtures
{
    public static class TestActions
    {
        public static NodeStatus AlwaysSuccess(
            ref TestBlackboard bb,
            ref BehaviorTreeState state,
            ref MockContext ctx,
            int paramIndex)
        {
            ctx.CallCount++;
            return NodeStatus.Success;
        }
        
        public static NodeStatus AlwaysFailure(
            ref TestBlackboard bb,
            ref BehaviorTreeState state,
            ref MockContext ctx,
            int paramIndex)
        {
            ctx.CallCount++;
            return NodeStatus.Failure;
        }
        
        public static NodeStatus IncrementCounter(
            ref TestBlackboard bb,
            ref BehaviorTreeState state,
            ref MockContext ctx,
            int paramIndex)
        {
            bb.Counter++;
            return NodeStatus.Success;
        }
        
        public static NodeStatus ReturnRunningOnce(
            ref TestBlackboard bb,
            ref BehaviorTreeState state,
            ref MockContext ctx,
            int paramIndex)
        {
            // First call: Running, second call: Success
            unsafe
            {
                ref int counter = ref state.LocalRegisters[0];
                counter++;
                return counter >= 2 ? NodeStatus.Success : NodeStatus.Running;
            }
        }
        
        public static NodeStatus CheckFlag(
            ref TestBlackboard bb,
            ref BehaviorTreeState state,
            ref MockContext ctx,
            int paramIndex)
        {
            return bb.Flag ? NodeStatus.Success : NodeStatus.Failure;
        }
    }
}
