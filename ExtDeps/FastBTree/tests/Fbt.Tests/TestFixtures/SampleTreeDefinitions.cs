using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;

namespace Fbt.Tests.TestFixtures
{
    public static class SampleTreeDefinitions
    {
        [BTreeDefinition("Sample_BT")]
        public static BehaviorTreeBlob BuildSampleTree()
        {
            return new BTreeBuilder<TestBlackboard, MockContext>()
                .Action(static (ref TestBlackboard bb, ref BehaviorTreeState s, ref MockContext ctx, int p)
                    => NodeStatus.Success)
                .Compile("Sample_BT");
        }
    }
}
