using Xunit;
using FluentAssertions;
using Fbt;
using Fbt.Serialization;

namespace Fbt.Tests.Unit
{
    /// <summary>
    /// BPF-018: FlattenToBlob must populate SubtreeAssetIds when the tree contains Subtree nodes.
    /// </summary>
    public class TreeCompilerSubtreeTests
    {
        [Fact]
        public void FlattenToBlob_NoSubtreeNodes_SubtreeAssetIdsIsEmpty()
        {
            var root = new BuilderNode { Type = NodeType.Sequence };
            root.Children.Add(new BuilderNode { Type = NodeType.Action, MethodName = "Move" });

            var blob = TreeCompiler.FlattenToBlob(root, "TreeNoSubtrees");

            blob.SubtreeAssetIds.Should().NotBeNull();
            blob.SubtreeAssetIds.Should().BeEmpty();
        }

        [Fact]
        public void FlattenToBlob_SingleSubtreeNode_PopulatesSubtreeAssetIds()
        {
            const string treeName = "PatrolTree";
            var root = new BuilderNode { Type = NodeType.Sequence };
            root.Children.Add(new BuilderNode { Type = NodeType.Subtree, MethodName = treeName });

            var blob = TreeCompiler.FlattenToBlob(root, "HostTree");

            blob.SubtreeAssetIds.Should().HaveCount(1);
            blob.SubtreeAssetIds[0].Should().Be(treeName);
        }

        [Fact]
        public void FlattenToBlob_TwoDistinctSubtrees_PopulatesBothIds()
        {
            const string tree1 = "PatrolTree";
            const string tree2 = "CombatTree";
            var root = new BuilderNode { Type = NodeType.Sequence };
            root.Children.Add(new BuilderNode { Type = NodeType.Subtree, MethodName = tree1 });
            root.Children.Add(new BuilderNode { Type = NodeType.Subtree, MethodName = tree2 });

            var blob = TreeCompiler.FlattenToBlob(root, "HostTree");

            blob.SubtreeAssetIds.Should().HaveCount(2);
            blob.SubtreeAssetIds.Should().Contain(tree1);
            blob.SubtreeAssetIds.Should().Contain(tree2);
        }

        [Fact]
        public void FlattenToBlob_DuplicateSubtree_DeduplicatesIds()
        {
            const string treeName = "PatrolTree";
            var root = new BuilderNode { Type = NodeType.Sequence };
            root.Children.Add(new BuilderNode { Type = NodeType.Subtree, MethodName = treeName });
            root.Children.Add(new BuilderNode { Type = NodeType.Subtree, MethodName = treeName });

            var blob = TreeCompiler.FlattenToBlob(root, "HostTree");

            blob.SubtreeAssetIds.Should().HaveCount(1);
            blob.SubtreeAssetIds[0].Should().Be(treeName);
        }

        [Fact]
        public void FlattenToBlob_SubtreeNode_PayloadIndexPointsToCorrectName()
        {
            const string treeName = "GuardTree";
            var root = new BuilderNode { Type = NodeType.Sequence };
            root.Children.Add(new BuilderNode { Type = NodeType.Subtree, MethodName = treeName });

            var blob = TreeCompiler.FlattenToBlob(root, "HostTree");

            // Subtree node is index 1 (root seq is 0, subtree is 1)
            var subtreeNodeDef = blob.Nodes[1];
            subtreeNodeDef.Type.Should().Be(NodeType.Subtree);
            int idx = subtreeNodeDef.PayloadIndex;
            blob.SubtreeAssetIds[idx].Should().Be(treeName);
        }

        [Fact]
        public void FlattenToBlob_MixedNodeTypes_SubtreeAssetIdsContainsOnlySubtreeNames()
        {
            var root = new BuilderNode { Type = NodeType.Sequence };
            root.Children.Add(new BuilderNode { Type = NodeType.Action, MethodName = "SomeAction" });
            root.Children.Add(new BuilderNode { Type = NodeType.Subtree, MethodName = "ChildTree" });
            root.Children.Add(new BuilderNode { Type = NodeType.Condition, MethodName = "SomeCondition" });

            var blob = TreeCompiler.FlattenToBlob(root, "HostTree");

            blob.SubtreeAssetIds.Should().HaveCount(1);
            blob.SubtreeAssetIds[0].Should().Be("ChildTree");
            // Action/Condition names go into MethodNames, not SubtreeAssetIds
            blob.MethodNames.Should().Contain("SomeAction");
            blob.MethodNames.Should().Contain("SomeCondition");
            blob.MethodNames.Should().NotContain("ChildTree");
        }
    }
}
