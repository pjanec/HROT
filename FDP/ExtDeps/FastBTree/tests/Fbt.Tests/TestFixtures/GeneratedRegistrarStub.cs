using System.Runtime.CompilerServices;
using Fbt.Runtime;
using Fbt.Tests.TestFixtures;

// Hand-written stand-in for the output of Fbt.SourceGen, which is not present in this
// repository. Provides the minimum surface required to compile SharedAiGeneratorTests.
namespace Fbt.Tests.Generated
{
    public static class FbtActionRegistrar
    {
        // Registers all generated delegates for the (SharedAiTestBlackboard, SharedAiTestContext) pair.
        public static void RegisterAll(ActionRegistry<SharedAiTestBlackboard, SharedAiTestContext> registry)
        {
            // Group anchor: registered under plain method name.
            registry.Register("GroupAnchorAction", SharedAiTestActions.GroupAnchorAction);

            // SequentialCondition@4: reads float at byte offset 4 in SharedAiTestBlackboard.Memory,
            // delegates to SharedAiTestActions.SequentialCondition(ref field, self, world).
            registry.RegisterCondition("SequentialCondition@4",
                (ref SharedAiTestBlackboard bb, ref BehaviorTreeState state, ref SharedAiTestContext ctx, int p) =>
                {
                    unsafe
                    {
                        float* field = (float*)((byte*)Unsafe.AsPointer(ref bb) + 4);
                        bool result = SharedAiTestActions.SequentialCondition(ref *field, ctx.Self, ctx.World);
                        return result ? NodeStatus.Success : NodeStatus.Failure;
                    }
                });

            // ExplicitAction@12: reads float at byte offset 12 in SharedAiTestBlackboard.Memory,
            // delegates to SharedAiTestActions.ExplicitAction(ref field, self, world).
            registry.Register("ExplicitAction@12",
                (ref SharedAiTestBlackboard bb, ref BehaviorTreeState state, ref SharedAiTestContext ctx, int p) =>
                {
                    unsafe
                    {
                        float* field = (float*)((byte*)Unsafe.AsPointer(ref bb) + 12);
                        return SharedAiTestActions.ExplicitAction(ref *field, ctx.Self, ctx.World);
                    }
                });
        }
    }
}
