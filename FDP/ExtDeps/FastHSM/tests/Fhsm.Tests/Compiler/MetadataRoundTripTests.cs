using System;
using Xunit;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Data;

namespace Fhsm.Tests.Compiler
{
    /// <summary>
    /// Tests for TASK-K-02 / TASK-K-03: compile -> blob.Metadata round-trip.
    /// Verifies that StateStableIds and TransitionVisualIds are populated
    /// correctly by HsmEmitter.BuildMachineMetadata and attached to the
    /// HsmDefinitionBlob by StateMachineGraph.Compile().
    /// </summary>
    public class MetadataRoundTripTests
    {
        // RT-T1: Compile() populates blob.Metadata (not null).
        [Fact]
        public void Compile_PopulatesMetadata_NotNull()
        {
            var builder = new HsmBuilder("M");
            builder.State("Idle");
            var blob = builder.GetGraph().Compile();

            Assert.NotNull(blob.Metadata);
        }

        // RT-T2: Explicit stableId on a state round-trips through Metadata.StateStableIds.
        [Fact]
        public void Compile_StateStableId_RoundTrips_ThroughMetadata()
        {
            var stableId = Guid.NewGuid();
            var builder  = new HsmBuilder("M");
            builder.State("Idle", stableId: stableId);
            var blob = builder.GetGraph().Compile();

            Assert.NotNull(blob.Metadata);
            bool found = false;
            foreach (var kv in blob.Metadata!.StateStableIds)
            {
                if (kv.Value == stableId) { found = true; break; }
            }
            Assert.True(found, "Explicit stableId not found in blob.Metadata.StateStableIds");
        }

        // RT-T3: Default stableId is auto-generated (non-empty Guid) and present in Metadata.
        [Fact]
        public void Compile_DefaultStateStableId_IsNonEmpty_InMetadata()
        {
            var builder = new HsmBuilder("M");
            builder.State("Idle");
            var blob = builder.GetGraph().Compile();

            Assert.NotNull(blob.Metadata);
            bool hasNonEmpty = false;
            foreach (var kv in blob.Metadata!.StateStableIds)
            {
                if (kv.Value != Guid.Empty) { hasNonEmpty = true; break; }
            }
            Assert.True(hasNonEmpty, "No non-empty stableId found in Metadata.StateStableIds");
        }

        // RT-T4: Two states have distinct stableIds in Metadata, matching what was authored.
        [Fact]
        public void Compile_TwoStates_BothStableIds_InMetadata()
        {
            var id0 = Guid.NewGuid();
            var id1 = Guid.NewGuid();
            var builder = new HsmBuilder("M");
            builder.Event("Evt", 1);
            var active = builder.State("Active", stableId: id1);
            var idle   = builder.State("Idle",   stableId: id0);
            active.On("Evt").GoTo("Idle");
            var blob = builder.GetGraph().Compile();

            Assert.NotNull(blob.Metadata);
            var ids = blob.Metadata!.StateStableIds;
            // Both authored Guids must appear somewhere in the dictionary.
            bool foundId0 = false, foundId1 = false;
            foreach (var kv in ids)
            {
                if (kv.Value == id0) foundId0 = true;
                if (kv.Value == id1) foundId1 = true;
            }
            Assert.True(foundId0, "stableId for Idle not found in Metadata.StateStableIds");
            Assert.True(foundId1, "stableId for Active not found in Metadata.StateStableIds");
        }

        // RT-T5: Explicit transition visualId round-trips through Metadata.TransitionVisualIds.
        [Fact]
        public void Compile_TransitionVisualId_RoundTrips_ThroughMetadata()
        {
            var visualId = Guid.NewGuid();
            var builder  = new HsmBuilder("M");
            builder.Event("Evt", 1);
            var active = builder.State("Active");
            builder.State("Idle");
            active.On("Evt").GoTo("Idle", visualId);
            var blob = builder.GetGraph().Compile();

            Assert.NotNull(blob.Metadata);
            bool found = false;
            foreach (var kv in blob.Metadata!.TransitionVisualIds)
            {
                if (kv.Value == visualId) { found = true; break; }
            }
            Assert.True(found, "Explicit visualId not found in blob.Metadata.TransitionVisualIds");
        }

        // RT-T6: Default transition visualId is auto-generated (non-empty) and present.
        [Fact]
        public void Compile_DefaultTransitionVisualId_IsNonEmpty_InMetadata()
        {
            var builder = new HsmBuilder("M");
            builder.Event("Evt", 1);
            var active = builder.State("Active");
            builder.State("Idle");
            active.On("Evt").GoTo("Idle");
            var blob = builder.GetGraph().Compile();

            Assert.NotNull(blob.Metadata);
            bool hasNonEmpty = false;
            foreach (var kv in blob.Metadata!.TransitionVisualIds)
            {
                if (kv.Value != Guid.Empty) { hasNonEmpty = true; break; }
            }
            Assert.True(hasNonEmpty, "No non-empty transition visualId in Metadata.TransitionVisualIds");
        }
    }
}
