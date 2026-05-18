using Xunit;
using Fbt;

namespace Fbt.Tests.Unit
{
    public class BehaviorTreeBlobTests
    {
        [Fact]
        public void BehaviorTreeBlob_DefaultVersion_Is1()
        {
            var blob = new BehaviorTreeBlob();
            Assert.Equal(1, blob.Version);
        }

        [Fact]
        public void BehaviorTreeBlob_Nodes_CanStoreArray()
        {
            var blob = new BehaviorTreeBlob
            {
                Nodes = new[]
                {
                    new NodeDefinition { Type = NodeType.Root },
                    new NodeDefinition { Type = NodeType.Action }
                }
            };
            
            Assert.Equal(2, blob.Nodes.Length);
            Assert.Equal(NodeType.Root, blob.Nodes[0].Type);
        }

        [Fact]
        public void BehaviorTreeBlob_LookupTables_AreDense()
        {
            var blob = new BehaviorTreeBlob
            {
                MethodNames = new[] { "Attack", "Patrol" },
                FloatParams = new[] { 1.0f, 2.0f, 3.0f },
                IntParams = new[] { 10, 20 }
            };
            
            Assert.Equal(2, blob.MethodNames.Length);
            Assert.Equal(3, blob.FloatParams.Length);
            Assert.Equal(2, blob.IntParams.Length);
        }
        [Fact]
        public void BehaviorTreeBlob_Hashes_ArePublic()
        {
            var blob = new BehaviorTreeBlob
            {
                StructureHash = 12345,
                ParamHash = 67890
            };
            
            Assert.Equal(12345, blob.StructureHash);
            Assert.Equal(67890, blob.ParamHash);
        }
    }
}
