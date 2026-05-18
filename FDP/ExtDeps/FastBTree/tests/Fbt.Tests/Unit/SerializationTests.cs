using Xunit;
using Fbt.Serialization;
using System.Linq;

namespace Fbt.Tests.Unit
{
    public class SerializationTests
    {
        [Fact]
        public void CompileFromJson_SimpleSequence_ParsesCorrectly()
        {
            string json = @"{
                ""TreeName"": ""TestSeq"",
                ""Root"": {
                    ""Type"": ""Sequence"",
                    ""Children"": [
                        { ""Type"": ""Action"", ""Action"": ""Move"" },
                        { ""Type"": ""Action"", ""Action"": ""Attack"" }
                    ]
                }
            }";

            var blob = TreeCompiler.CompileFromJson(json);

            Assert.Equal("TestSeq", blob.TreeName);
            Assert.Equal(3, blob.Nodes.Length);
            
            // Check Node Types
            Assert.Equal(NodeType.Sequence, blob.Nodes[0].Type);
            Assert.Equal(NodeType.Action, blob.Nodes[1].Type);
            Assert.Equal(NodeType.Action, blob.Nodes[2].Type);
            
            // Check Payload
            Assert.Contains("Move", blob.MethodNames);
            Assert.Contains("Attack", blob.MethodNames);
        }

        [Fact]
        public void FlattenToBlob_SimpleSequence_CorrectSubtreeOffsets()
        {
            // Root(Seq) -> [Action1, Action2]
            // Index 0: Seq. SubtreeSize = 1(Seq) + 1(A1) + 1(A2) = 3. Offset = 3.
            // Index 1: A1. SubtreeSize = 1. Offset = 1.
            // Index 2: A2. SubtreeSize = 1. Offset = 1.

            string json = @"{
                ""TreeName"": ""Offsets"",
                ""Root"": {
                    ""Type"": ""Sequence"",
                    ""Children"": [
                        { ""Type"": ""Action"", ""Action"": ""A1"" },
                        { ""Type"": ""Action"", ""Action"": ""A2"" }
                    ]
                }
            }";

            var blob = TreeCompiler.CompileFromJson(json);

            Assert.Equal(3, blob.Nodes[0].SubtreeOffset);
            Assert.Equal(1, blob.Nodes[1].SubtreeOffset);
            Assert.Equal(1, blob.Nodes[2].SubtreeOffset);
        }

        [Fact]
        public void FlattenToBlob_NestedTrees_CorrectOffsets()
        {
            // Root(Seq) 
            //   -> Child1(Inverter) -> Child1_1(Action)
            //   -> Child2(Action)
            
            // Tree Layout:
            // 0: Seq
            // 1: Inverter
            // 2: Action (A1)
            // 3: Action (A2)
            
            // Calculations:
            // Node 2 (A1): Size 1. Offset 1.
            // Node 1 (Inv): Size = 1(Inv) + 1(A1) = 2. Offset = 2.
            // Node 3 (A2): Size 1. Offset 1.
            // Node 0 (Seq): Size = 1(Seq) + 2(Inv) + 1(A2) = 4. Offset = 4.

            string json = @"{
                ""TreeName"": ""Nested"",
                ""Root"": {
                    ""Type"": ""Sequence"",
                    ""Children"": [
                        { 
                            ""Type"": ""Inverter"",
                            ""Children"": [ { ""Type"": ""Action"", ""Action"": ""A1"" } ]
                        },
                        { ""Type"": ""Action"", ""Action"": ""A2"" }
                    ]
                }
            }";

            var blob = TreeCompiler.CompileFromJson(json);

            Assert.Equal(4, blob.Nodes[0].SubtreeOffset); // Seq
            Assert.Equal(2, blob.Nodes[1].SubtreeOffset); // Inverter
            Assert.Equal(1, blob.Nodes[2].SubtreeOffset); // A1
            Assert.Equal(1, blob.Nodes[3].SubtreeOffset); // A2
        }

        [Fact]
        public void FlattenToBlob_DeduplicateMethodNames()
        {
             string json = @"{
                ""TreeName"": ""Dedup"",
                ""Root"": {
                    ""Type"": ""Sequence"",
                    ""Children"": [
                        { ""Type"": ""Action"", ""Action"": ""Move"" },
                        { ""Type"": ""Action"", ""Action"": ""Move"" }
                    ]
                }
            }";

            var blob = TreeCompiler.CompileFromJson(json);
            
            Assert.Single(blob.MethodNames);
            Assert.Equal("Move", blob.MethodNames[0]);
            
            Assert.Equal(0, blob.Nodes[1].PayloadIndex);
            Assert.Equal(0, blob.Nodes[2].PayloadIndex);
        }

        [Fact]
        public void FlattenToBlob_WaitNode_StoresFloatParam()
        {
            string json = @"{
                ""TreeName"": ""WaitTest"",
                ""Root"": {
                    ""Type"": ""Wait"",
                    ""WaitTime"": 5.5
                }
            }";

            var blob = TreeCompiler.CompileFromJson(json);

            Assert.Single(blob.FloatParams);
            Assert.Equal(5.5f, blob.FloatParams[0]);
            Assert.Equal(0, blob.Nodes[0].PayloadIndex);
        }

        [Fact]
        public void CalculateStructureHash_SameTree_SameHash()
        {
            string json1 = @"{ ""TreeName"": ""T1"", ""Root"": { ""Type"": ""Action"", ""Action"": ""A"" } }";
            string json2 = @"{ ""TreeName"": ""T2"", ""Root"": { ""Type"": ""Action"", ""Action"": ""B"" } }"; // Different payload

            var blob1 = TreeCompiler.CompileFromJson(json1);
            var blob2 = TreeCompiler.CompileFromJson(json2);

            // Structure is same (1 root node of type Action)
            Assert.Equal(blob1.StructureHash, blob2.StructureHash);
        }

        [Fact]
        public void CalculateStructureHash_DifferentStructure_DifferentHash()
        {
            string json1 = @"{ ""TreeName"": ""T1"", ""Root"": { ""Type"": ""Action"", ""Action"": ""A"" } }";
            string json2 = @"{ ""TreeName"": ""T2"", ""Root"": { ""Type"": ""Sequence"", ""Children"": [] } }";

            var blob1 = TreeCompiler.CompileFromJson(json1);
            var blob2 = TreeCompiler.CompileFromJson(json2);

            Assert.NotEqual(blob1.StructureHash, blob2.StructureHash);
        }

        [Fact]
        public void CalculateParamHash_SameParams_SameHash()
        {
            string json = @"{ ""TreeName"": ""T"", ""Root"": { ""Type"": ""Wait"", ""WaitTime"": 1.0 } }";
            var blob1 = TreeCompiler.CompileFromJson(json);
            var blob2 = TreeCompiler.CompileFromJson(json);
            
            Assert.Equal(blob1.ParamHash, blob2.ParamHash);
        }
        
         [Fact]
        public void CalculateParamHash_DifferentParams_DifferentHash()
        {
            string json1 = @"{ ""TreeName"": ""T"", ""Root"": { ""Type"": ""Wait"", ""WaitTime"": 1.0 } }";
            string json2 = @"{ ""TreeName"": ""T"", ""Root"": { ""Type"": ""Wait"", ""WaitTime"": 2.0 } }";
            
            var blob1 = TreeCompiler.CompileFromJson(json1);
            var blob2 = TreeCompiler.CompileFromJson(json2);
            
            Assert.NotEqual(blob1.ParamHash, blob2.ParamHash);
        }
    }
}
