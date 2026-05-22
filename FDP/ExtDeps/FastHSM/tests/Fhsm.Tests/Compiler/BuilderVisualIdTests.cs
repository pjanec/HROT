using System;
using Xunit;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;

namespace Fhsm.Tests.Compiler
{
    /// <summary>
    /// Tests for TASK-K-02: stableId on HsmBuilder.State() and StateBuilder.Child().
    /// Tests for TASK-K-03: visualId on TransitionBuilder.GoTo() and HsmBuilder.GlobalTransition().
    /// </summary>
    public class BuilderVisualIdTests
    {
        // K-02-T1: State with default stableId gets a non-empty auto-generated Guid.
        [Fact]
        public void HsmBuilder_State_WithDefaultStableId_GeneratesNonEmptyGuid()
        {
            var builder = new HsmBuilder("M");
            builder.State("Idle");
            var state = builder.GetGraph().FindState("Idle");
            Assert.NotNull(state);
            Assert.NotEqual(Guid.Empty, state!.StableId);
        }

        // K-02-T2: State with explicit stableId preserves the supplied value.
        [Fact]
        public void HsmBuilder_State_WithExplicitStableId_UsesProvidedValue()
        {
            var id = Guid.NewGuid();
            var builder = new HsmBuilder("M");
            builder.State("Idle", id);
            var state = builder.GetGraph().FindState("Idle");
            Assert.NotNull(state);
            Assert.Equal(id, state!.StableId);
        }

        // K-02-T3: Two states with default stableId get different Guids.
        [Fact]
        public void HsmBuilder_TwoStates_WithDefaultStableId_GetDifferentGuids()
        {
            var builder = new HsmBuilder("M");
            builder.State("A");
            builder.State("B");
            var graph = builder.GetGraph();
            var idA = graph.FindState("A")!.StableId;
            var idB = graph.FindState("B")!.StableId;
            Assert.NotEqual(idA, idB);
        }

        // K-02-T4: Child state with explicit stableId preserves value.
        [Fact]
        public void HsmBuilder_Child_WithExplicitStableId_UsesProvidedValue()
        {
            var childId = Guid.NewGuid();
            var builder = new HsmBuilder("M");
            builder.State("Parent").Child("Child1", _ => { }, childId);
            var child = builder.GetGraph().FindState("Child1");
            Assert.NotNull(child);
            Assert.Equal(childId, child!.StableId);
        }

        // K-02-T5: Child state with default stableId gets an auto-generated non-empty Guid.
        [Fact]
        public void HsmBuilder_Child_WithDefaultStableId_GeneratesNonEmptyGuid()
        {
            var builder = new HsmBuilder("M");
            builder.State("Parent").Child("Child2", _ => { });
            var child = builder.GetGraph().FindState("Child2");
            Assert.NotNull(child);
            Assert.NotEqual(Guid.Empty, child!.StableId);
        }

        // K-03-T1: GoTo(string) with default visualId generates a non-empty VisualId.
        [Fact]
        public void TransitionBuilder_GoTo_WithDefaultVisualId_GeneratesNonEmptyGuid()
        {
            var builder = new HsmBuilder("M");
            builder.Event("Evt", 1);
            var a = builder.State("A");
            builder.State("B");
            a.On("Evt").GoTo("B");

            var stateA = builder.GetGraph().FindState("A")!;
            Assert.NotEmpty(stateA.Transitions);
            Assert.NotEqual(Guid.Empty, stateA.Transitions[0].VisualId);
        }

        // K-03-T2: GoTo(string) with explicit visualId preserves the supplied value.
        [Fact]
        public void TransitionBuilder_GoTo_WithExplicitVisualId_UsesProvidedValue()
        {
            var vid = Guid.NewGuid();
            var builder = new HsmBuilder("M");
            builder.Event("Evt", 1);
            var a = builder.State("A");
            builder.State("B");
            a.On("Evt").GoTo("B", vid);

            var stateA = builder.GetGraph().FindState("A")!;
            Assert.NotEmpty(stateA.Transitions);
            Assert.Equal(vid, stateA.Transitions[0].VisualId);
        }

        // K-03-T3: GoTo(StateBuilder) with explicit visualId preserves the supplied value.
        [Fact]
        public void TransitionBuilder_GoTo_StateBuilder_WithExplicitVisualId_UsesProvidedValue()
        {
            var vid = Guid.NewGuid();
            var builder = new HsmBuilder("M");
            builder.Event("Evt", 1);
            var a = builder.State("A");
            var b = builder.State("B");
            a.On("Evt").GoTo(b, vid);

            var stateA = builder.GetGraph().FindState("A")!;
            Assert.NotEmpty(stateA.Transitions);
            Assert.Equal(vid, stateA.Transitions[0].VisualId);
        }

        // K-03-T4: Two transitions with default visualId get different Guids.
        [Fact]
        public void TransitionBuilder_TwoTransitions_DefaultVisualId_GetDifferentGuids()
        {
            var builder = new HsmBuilder("M");
            builder.Event("E1", 1);
            builder.Event("E2", 2);
            var a = builder.State("A");
            builder.State("B");
            builder.State("C");
            a.On("E1").GoTo("B");
            a.On("E2").GoTo("C");

            var stateA = builder.GetGraph().FindState("A")!;
            Assert.Equal(2, stateA.Transitions.Count);
            Assert.NotEqual(stateA.Transitions[0].VisualId, stateA.Transitions[1].VisualId);
        }

        // K-03-T5: GlobalTransition with explicit visualId is added to GlobalTransitions with correct VisualId.
        [Fact]
        public void HsmBuilder_GlobalTransition_WithExplicitVisualId_IsAddedCorrectly()
        {
            var vid = Guid.NewGuid();
            var builder = new HsmBuilder("M");
            builder.Event("Reset", 99);
            builder.State("Idle");
            builder.State("Error");
            builder.GlobalTransition("Reset", "Error", vid);

            var graph = builder.GetGraph();
            Assert.Single(graph.GlobalTransitions);
            Assert.Equal(vid, graph.GlobalTransitions[0].VisualId);
            Assert.Equal("Error", graph.GlobalTransitions[0].Target!.Name);
        }

        // K-03-T6: GlobalTransition with default visualId auto-generates a non-empty Guid.
        [Fact]
        public void HsmBuilder_GlobalTransition_WithDefaultVisualId_GeneratesNonEmptyGuid()
        {
            var builder = new HsmBuilder("M");
            builder.Event("Kill", 10);
            builder.State("Alive");
            builder.State("Dead");
            builder.GlobalTransition("Kill", "Dead");

            var gt = builder.GetGraph().GlobalTransitions[0];
            Assert.NotEqual(Guid.Empty, gt.VisualId);
        }
    }
}
