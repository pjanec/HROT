using System;
using System.IO;
using Xunit;
using Fbt;
using Fbt.Compiler;
using Fbt.Runtime;
using Fbt.Serialization;
using Fbt.Tests.TestFixtures;

namespace Fbt.Tests.Unit
{
    /// <summary>Tests for EQL-011: Binary serialization versioning and V1 legacy fallback.</summary>
    public class BinarySerializationVersioningTests
    {
        // T1: BehaviorTreeBlob produced by FlattenToBlob has blob.Version == 2.
        [Fact]
        public void T1_FlattenToBlob_StampsVersion2()
        {
            var action = new BuilderNode { Type = NodeType.Action, MethodName = "A" };
            var root = new BuilderNode { Type = NodeType.Sequence };
            root.Children.Add(action);

            var blob = TreeCompiler.FlattenToBlob(root, "T1");
            Assert.Equal(2, blob.Version);
        }

        // T2 (V2 round-trip): Compile a tree with resource-owning action via BTreeBuilder.
        //     Save and Load. Assert: (a) loaded blob.Version == 2; (b) IsResourceOwning bit preserved.
        [Fact]
        public void T2_V2RoundTrip_IsResourceOwningBitPreserved()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Success));
            var tmp = builder.Compile("T2tmp");
            string key = tmp.MethodNames[tmp.Nodes[1].PayloadIndex];
            builder.GetRegistry().RegisterDeactivator(key,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) => { });

            var blob = builder.Compile("T2");
            Assert.True(blob.Nodes[1].IsResourceOwning);
            Assert.Equal(2, blob.Version);

            string path = System.IO.Path.GetTempFileName();
            try
            {
                BinaryTreeSerializer.Save(blob, path);
                var loaded = BinaryTreeSerializer.Load(path);

                Assert.Equal(2, loaded.Version);
                Assert.True(loaded.Nodes[1].IsResourceOwning);
            }
            finally { System.IO.File.Delete(path); }
        }

        // T3 (V1 round-trip): Manually set blob.Version = 1 (simulating an old disk file).
        //     Load via stream manually (or use a builder then force Version=1).
        //     Assert: before Interpreter construction, IsResourceOwning == false.
        //     After Interpreter construction with a registered deactivator, IsResourceOwning == true.
        [Fact]
        public void T3_V1LegacyFallback_PatchesResourceOwningBit()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            builder.Sequence(s => s.Action(
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Running));
            // Don't register deactivator during compile -> bit NOT set by AOT
            var blob = builder.Compile("T3");
            // Simulate a V1 blob (bit not set, version=1)
            blob.Version = 1;
            Assert.False(blob.Nodes[1].IsResourceOwning);

            // Register deactivator in the registry
            string key = blob.MethodNames[blob.Nodes[1].PayloadIndex];
            var registry = builder.GetRegistry();
            registry.RegisterDeactivator(key,
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) => { });

            // Construct Interpreter -> V1 fallback fires -> bit gets set
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            Assert.True(blob.Nodes[1].IsResourceOwning);
        }

        // T4 (V2 skips patch): V2 blob with IsResourceOwning bit NOT set by AOT (null delegate),
        //     but a deactivator IS registered in registry. After Interpreter construction, bit
        //     must remain FALSE because V1 fallback does not run for V2 blobs.
        [Fact]
        public void T4_V2Blob_SkipsV1Patching()
        {
            // Build a V2 blob WITHOUT AOT bits (FlattenToBlob with null delegate)
            var action = new BuilderNode { Type = NodeType.Action, MethodName = "ActionA" };
            var root = new BuilderNode { Type = NodeType.Sequence };
            root.Children.Add(action);
            var blob = TreeCompiler.FlattenToBlob(root, "T4", null); // no isResourceOwning delegate
            Assert.Equal(2, blob.Version);
            Assert.False(blob.Nodes[1].IsResourceOwning); // bit NOT set by AOT

            // Register a deactivator in the registry
            var registry = new ActionRegistry<TestBlackboard, MockContext>();
            registry.Register("ActionA",
                (ref TestBlackboard bb, ref BehaviorTreeState st, ref MockContext ctx, int p) =>
                    NodeStatus.Success);
            registry.RegisterDeactivator("ActionA",
                (ref TestBlackboard bb2, ref BehaviorTreeState st, ref MockContext ctx2, int p) => { });

            // Construct Interpreter with V2 blob -> V1 fallback must NOT run
            var interpreter = new Interpreter<TestBlackboard, MockContext>(blob, registry);
            // Bit remains false: V1 loop skipped
            Assert.False(blob.Nodes[1].IsResourceOwning);
        }

        // T5 (regression): All L-01 through L-08 tests in HybridLifecycleTests pass.
        //     Verified by running dotnet test filtering HybridLifecycleTests; no assertion here.

        // T6: Invalid version in binary stream -> InvalidDataException.
        [Fact]
        public void T6_InvalidVersion_ThrowsInvalidDataException()
        {
            string path = System.IO.Path.GetTempFileName();
            try
            {
                using (var fs = System.IO.File.OpenWrite(path))
                using (var w = new System.IO.BinaryWriter(fs))
                {
                    w.Write((byte)'F'); w.Write((byte)'B'); w.Write((byte)'T'); w.Write((byte)0); // magic
                    w.Write(99);         // invalid version
                    w.Write(0);          // StructureHash
                    w.Write(0);          // ParamHash
                    w.Write("");         // TreeName
                    w.Write(0);          // node count
                    w.Write(0);          // method count
                    w.Write(0);          // float count
                    w.Write(0);          // int count
                }
                Assert.Throws<InvalidDataException>(() => BinaryTreeSerializer.Load(path));
            }
            finally { System.IO.File.Delete(path); }
        }

        // T7: Projects build without errors (verified by dotnet build; no assertion here).
    }
}
