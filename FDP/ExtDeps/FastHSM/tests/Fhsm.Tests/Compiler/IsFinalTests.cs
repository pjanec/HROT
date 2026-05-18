using Xunit;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fhsm.Tests.Compiler
{
    public class IsFinalTests
    {
        // ---- Helper ----

        private static HsmDefinitionBlob CompileBlob(HsmBuilder builder)
        {
            var graph    = builder.Build();
            HsmNormalizer.Normalize(graph);
            var flat     = HsmFlattener.Flatten(graph);
            return HsmEmitter.Emit(flat);
        }

        // ---- StateNode: IsFinal property ----

        [Fact]
        public void StateNode_IsFinal_DefaultFalse()
        {
            var node = new StateNode("S");
            Assert.False(node.IsFinal);
        }

        // ---- HsmBuilder: Final() fluent method ----

        [Fact]
        public void Builder_Final_SetsFinalFlag_OnStateNode()
        {
            var builder = new HsmBuilder("M");
            builder.State("Done").Initial().Final();

            var graph = builder.Build();
            var state = graph.FindState("Done");
            Assert.NotNull(state);
            Assert.True(state!.IsFinal, "IsFinal should be set via Final().");
        }

        // ---- HsmFlattener: IsFinal -> StateFlags.IsFinal ----

        [Fact]
        public void Flattener_SetsIsFinalFlag_InStateDef()
        {
            var builder = new HsmBuilder("M");
            builder.State("Active").Initial();
            builder.State("Done").Final();

            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            var flat  = HsmFlattener.Flatten(graph);

            // Find the Done state in flattened data.
            bool found = false;
            foreach (var def in flat.States)
            {
                if ((def.Flags & StateFlags.IsFinal) != 0)
                {
                    found = true;
                    break;
                }
            }
            Assert.True(found, "HsmFlattener must propagate IsFinal -> StateFlags.IsFinal.");
        }

        [Fact]
        public void Flattener_NoIsFinalFlag_WhenNoFinalState()
        {
            var builder = new HsmBuilder("M");
            builder.State("Idle").Initial();
            builder.State("Running");

            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            var flat  = HsmFlattener.Flatten(graph);

            foreach (var def in flat.States)
            {
                bool isFinal = (def.Flags & StateFlags.IsFinal) != 0;
                Assert.False(isFinal,
                    "No state should carry IsFinal when none was marked Final().");
            }
        }

        // ---- Full pipeline: blob carries IsFinal ----

        [Fact]
        public void Blob_IsFinal_SurvivesEmit()
        {
            var builder = new HsmBuilder("M");
            builder.State("Active").Initial();
            builder.State("Done").Final();

            var blob = CompileBlob(builder);

            bool found = false;
            for (int i = 0; i < blob.Header.StateCount; i++)
            {
                if ((blob.States[i].Flags & StateFlags.IsFinal) != 0)
                {
                    found = true;
                    break;
                }
            }
            Assert.True(found, "StateFlags.IsFinal must survive the full compiler pipeline.");
        }
    }
}
