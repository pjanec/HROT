using Xunit;
using Fbt;
using Fbt.Serialization;

namespace Fbt.Tests.Unit
{
    public class TreeCompilerTests
    {
        // ---- FBT-001 Tests ----

        [Fact]
        public void FlattenToBlob_EquivalentTree_ProducesSameBlob()
        {
            // Build a Sequence[Action("Move"), Action("Attack")] manually
            var root = new BuilderNode { Type = NodeType.Sequence };
            root.Children.Add(new BuilderNode { Type = NodeType.Action, MethodName = "Move" });
            root.Children.Add(new BuilderNode { Type = NodeType.Action, MethodName = "Attack" });

            var blobDirect = TreeCompiler.FlattenToBlob(root, "TestSeq");

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
            var blobJson = TreeCompiler.CompileFromJson(json);

            // Same node count and types
            Assert.Equal(blobJson.Nodes.Length, blobDirect.Nodes.Length);
            Assert.Equal(NodeType.Sequence, blobDirect.Nodes[0].Type);
            Assert.Equal(NodeType.Action, blobDirect.Nodes[1].Type);
            Assert.Equal(NodeType.Action, blobDirect.Nodes[2].Type);

            // Same method names (order may differ, but both present)
            Assert.Contains("Move", blobDirect.MethodNames);
            Assert.Contains("Attack", blobDirect.MethodNames);

            // Same structure hash
            Assert.Equal(blobJson.StructureHash, blobDirect.StructureHash);
        }

        [Fact]
        public void FlattenToBlob_StructureHash_IgnoresMethodNames()
        {
            // Same shape (Sequence + Action), different method names
            var rootA = new BuilderNode { Type = NodeType.Sequence };
            rootA.Children.Add(new BuilderNode { Type = NodeType.Action, MethodName = "Move" });

            var rootB = new BuilderNode { Type = NodeType.Sequence };
            rootB.Children.Add(new BuilderNode { Type = NodeType.Action, MethodName = "Attack" });

            var blobA = TreeCompiler.FlattenToBlob(rootA, "TreeA");
            var blobB = TreeCompiler.FlattenToBlob(rootB, "TreeB");

            Assert.Equal(blobA.StructureHash, blobB.StructureHash);
            Assert.Equal(blobA.ParamHash, blobB.ParamHash); // Both have no float/int params
        }

        [Fact]
        public void FlattenToBlob_ParamHash_DiffersOnFloatParamChange()
        {
            var rootA = new BuilderNode { Type = NodeType.Wait, WaitTime = 1.0f };
            var rootB = new BuilderNode { Type = NodeType.Wait, WaitTime = 2.0f };

            var blobA = TreeCompiler.FlattenToBlob(rootA, "WaitA");
            var blobB = TreeCompiler.FlattenToBlob(rootB, "WaitB");

            Assert.NotEqual(blobA.ParamHash, blobB.ParamHash);
        }

        [Fact]
        public void FlattenToBlob_NestedRepeater_ThrowsBehaviorTreeBuildException()
        {
            var inner = new BuilderNode { Type = NodeType.Action, MethodName = "A" };
            var repeaterInner = new BuilderNode { Type = NodeType.Repeater, RepeatCount = 2 };
            repeaterInner.Children.Add(inner);

            var repeaterOuter = new BuilderNode { Type = NodeType.Repeater, RepeatCount = 3 };
            repeaterOuter.Children.Add(repeaterInner);

            var ex = Assert.Throws<BehaviorTreeBuildException>(
                () => TreeCompiler.FlattenToBlob(repeaterOuter, "NestedRepeater"));

            Assert.Contains("Repeater", ex.Message);
            Assert.Contains("nested", ex.Message.ToLowerInvariant());
        }

        [Fact]
        public void FlattenToBlob_NestedParallel_ThrowsBehaviorTreeBuildException()
        {
            var leaf = new BuilderNode { Type = NodeType.Action, MethodName = "A" };

            var innerParallel = new BuilderNode { Type = NodeType.Parallel, Policy = 0 };
            innerParallel.Children.Add(leaf);

            var outerParallel = new BuilderNode { Type = NodeType.Parallel, Policy = 0 };
            outerParallel.Children.Add(innerParallel);

            var ex = Assert.Throws<BehaviorTreeBuildException>(
                () => TreeCompiler.FlattenToBlob(outerParallel, "NestedParallel"));

            Assert.Contains("Parallel", ex.Message);
            Assert.Contains("nested", ex.Message.ToLowerInvariant());
        }
    }
}
