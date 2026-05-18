using System.IO;
using Xunit;
using Fbt;
using Fbt.Compiler;
using Fbt.Serialization;
using Fbt.Tests.TestFixtures;

namespace Fbt.Tests.Unit
{
    public class NodeDebugMetadataTests
    {
        // ---- Shared delegates ----

        private static NodeStatus AlwaysSuccess(
            ref TestBlackboard bb, ref BehaviorTreeState state, ref MockContext ctx, int p)
            => NodeStatus.Success;

        // ---- FBT-004: SC1 (caller info captured) ----

        [Fact]
        public void DebugMetadata_IsPopulatedByBuilder_WithCallerInfo()
        {
            // Capture expected line numbers around the builder call
            var blob = new BTreeBuilder<TestBlackboard, MockContext>()
                .Action(AlwaysSuccess)  // line is captured automatically
                .Compile("CallerInfo");

            Assert.NotNull(blob.DebugMetadata);
            var meta = blob.DebugMetadata![0];

            // SourceFile should be the filename of THIS test file
            Assert.Equal("NodeDebugMetadataTests.cs", meta.SourceFile);
            Assert.True(meta.LineNumber > 0, "LineNumber should be a positive source line");
        }

        // ---- FBT-004: SC3 (auto-label for Sequence) ----

        [Fact]
        public void DebugMetadata_AutoLabel_SequenceNode()
        {
            var blob = new BTreeBuilder<TestBlackboard, MockContext>()
                .Sequence(s => s.Action(AlwaysSuccess))
                .Compile("LabelSeq");

            Assert.NotNull(blob.DebugMetadata);
            // Node index 0 is the Sequence
            Assert.Equal("Sequence", blob.DebugMetadata![0].Label);
        }

        // ---- FBT-004: SC4 (auto-label for Wait includes duration) ----

        [Fact]
        public void DebugMetadata_AutoLabel_WaitNode_IncludesDuration()
        {
            var blob = new BTreeBuilder<TestBlackboard, MockContext>()
                .Wait(2.5f)
                .Compile("LabelWait");

            Assert.NotNull(blob.DebugMetadata);
            Assert.StartsWith("Wait(", blob.DebugMetadata![0].Label);
            Assert.Contains("2.5", blob.DebugMetadata![0].Label);
        }

        // ---- FBT-004: SC2 (BinarySerializer round-trip: DebugMetadata is null after load) ----

        [Fact]
        public void DebugMetadata_BinarySerializerRoundTrip_MetadataIsNull()
        {
            var builder = new BTreeBuilder<TestBlackboard, MockContext>();
            var blob = builder
                .Action(AlwaysSuccess)
                .Compile("RoundTrip");

            // Metadata is populated after Compile
            Assert.NotNull(blob.DebugMetadata);

            // Serialize and reload
            string path = System.IO.Path.GetTempFileName();
            try
            {
                BinaryTreeSerializer.Save(blob, path);
                var loaded = BinaryTreeSerializer.Load(path);

                // After deserialization, [NonSerialized] DebugMetadata must be null
                Assert.Null(loaded.DebugMetadata);

                // And the blob must still execute correctly
                var registry = builder.GetRegistry();
                var interpreter = new Runtime.Interpreter<TestBlackboard, MockContext>(loaded, registry);
                var bb = new TestBlackboard();
                var state = new BehaviorTreeState();
                var ctx = new MockContext();
                Assert.Equal(NodeStatus.Success, interpreter.Tick(ref bb, ref state, ref ctx));
            }
            finally
            {
                File.Delete(path);
            }
        }

        // ---- FBT-004: DebugMetadata.Length == Nodes.Length ----

        [Fact]
        public void DebugMetadata_Length_EqualsNodeCount()
        {
            var blob = new BTreeBuilder<TestBlackboard, MockContext>()
                .Sequence(s => s
                    .Action(AlwaysSuccess)
                    .Action(AlwaysSuccess))
                .Compile("LenCheck");

            Assert.NotNull(blob.DebugMetadata);
            Assert.Equal(blob.Nodes.Length, blob.DebugMetadata!.Length);
        }

        // ---- FBT-004: JSON-compiled blobs have null DebugMetadata ----

        [Fact]
        public void DebugMetadata_JsonCompiledBlob_IsNull()
        {
            string json = @"{
                ""TreeName"": ""JsonTree"",
                ""Root"": {
                    ""Type"": ""Action"",
                    ""Action"": ""Move""
                }
            }";

            var blob = TreeCompiler.CompileFromJson(json);
            Assert.Null(blob.DebugMetadata);
        }
    }
}
