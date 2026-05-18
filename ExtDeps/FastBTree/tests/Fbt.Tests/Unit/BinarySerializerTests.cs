using Xunit;
using Fbt.Serialization;
using System.IO;

namespace Fbt.Tests.Unit
{
    public class BinarySerializerTests
    {
        [Fact]
        public void BinarySerializer_SaveLoad_RoundTrips()
        {
            var blob = new BehaviorTreeBlob
            {
                TreeName = "TestTree",
                Version = 1,
                StructureHash = 123,
                ParamHash = 456,
                Nodes = new[] 
                { 
                    new NodeDefinition { Type = NodeType.Action, ChildCount = 0, SubtreeOffset = 1, PayloadIndex = 0 } 
                },
                MethodNames = new[] { "TestAction" },
                FloatParams = new[] { 1.5f },
                IntParams = new[] { 10 }
            };

            string path = "test_tree.bin";
            try
            {
                BinaryTreeSerializer.Save(blob, path);
                var loaded = BinaryTreeSerializer.Load(path);

                Assert.Equal(blob.TreeName, loaded.TreeName);
                Assert.Equal(blob.Version, loaded.Version);
                Assert.Equal(blob.StructureHash, loaded.StructureHash);
                Assert.Equal(blob.ParamHash, loaded.ParamHash);
                
                Assert.Equal(blob.Nodes.Length, loaded.Nodes.Length);
                Assert.Equal(blob.Nodes[0].Type, loaded.Nodes[0].Type);
                Assert.Equal(blob.Nodes[0].SubtreeOffset, loaded.Nodes[0].SubtreeOffset);
                
                Assert.Single(loaded.MethodNames);
                Assert.Equal("TestAction", loaded.MethodNames[0]);
                
                Assert.Single(loaded.FloatParams);
                Assert.Equal(1.5f, loaded.FloatParams[0]);
                
                Assert.Single(loaded.IntParams);
                Assert.Equal(10, loaded.IntParams[0]);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }

        [Fact]
        public void BinarySerializer_Load_InvalidMagic_Throws()
        {
            string path = "invalid_magic.bin";
            File.WriteAllBytes(path, new byte[] { 0, 0, 0, 0 });
            
            try
            {
                Assert.Throws<InvalidDataException>(() => BinaryTreeSerializer.Load(path));
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
