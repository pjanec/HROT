using System;
using Xunit;
using Fhsm.Compiler;
using Fhsm.Compiler.Graph;
using Fhsm.Kernel.Data;

namespace Fhsm.Tests.Compiler
{
    /// <summary>
    /// StateBuilder.OutputLanes — the setter that was missing from the output-lane feature.
    ///
    /// <para>The kernel has arbitrated orthogonal-region output lanes since Task 8
    /// (HsmKernelCore.ArbitrateOutputLanes, called from every ExecuteTransition), StateDef carries the mask and
    /// HsmEmitter writes it into the blob — but nothing could SET StateNode.OutputLaneMask except a test, so the
    /// arbitration read zeros for every machine built through this builder and protected nothing.</para>
    ///
    /// <para>These rails cover the declaration and the flatten, i.e. that a declared lane reaches StateDef.</para>
    /// </summary>
    public class OutputLaneDeclarationTests
    {
        [Fact]
        public void OutputLanes_SetsTheBitForTheLane()
        {
            var builder = new HsmBuilder("M");
            builder.State("Idle").Initial().OutputLanes(CommandLane.Animation);

            var state = builder.GetGraph().RootState.Children[0];
            Assert.Equal((byte)(1 << (int)CommandLane.Animation), state.OutputLaneMask);
        }

        [Fact]
        public void OutputLanes_SetsEveryBitGiven()
        {
            var builder = new HsmBuilder("M");
            builder.State("Idle").Initial().OutputLanes(CommandLane.Animation, CommandLane.Navigation);

            var expected = (byte)((1 << (int)CommandLane.Animation) | (1 << (int)CommandLane.Navigation));
            Assert.Equal(expected, builder.GetGraph().RootState.Children[0].OutputLaneMask);
        }

        [Fact]
        public void OutputLanes_Accumulates_AcrossCalls()
        {
            var builder = new HsmBuilder("M");
            builder.State("Idle").Initial()
                   .OutputLanes(CommandLane.Animation)
                   .OutputLanes(CommandLane.Audio);

            var expected = (byte)((1 << (int)CommandLane.Animation) | (1 << (int)CommandLane.Audio));
            Assert.Equal(expected, builder.GetGraph().RootState.Children[0].OutputLaneMask);
        }

        [Fact]
        public void OutputLanes_IgnoresNoneAndCount()
        {
            // None is the 0xFF sentinel and Count is the enum's bound; neither is a lane, and shifting by
            // either would corrupt the mask rather than describe it.
            var builder = new HsmBuilder("M");
            builder.State("Idle").Initial().OutputLanes(CommandLane.None, CommandLane.Count);

            Assert.Equal(0, builder.GetGraph().RootState.Children[0].OutputLaneMask);
        }

        [Fact]
        public void OutputLanes_WithNoArguments_LeavesTheMaskAlone()
        {
            var builder = new HsmBuilder("M");
            builder.State("Idle").Initial().OutputLanes();

            Assert.Equal(0, builder.GetGraph().RootState.Children[0].OutputLaneMask);
        }

        [Fact]
        public void AStateThatDeclaresNoLane_StaysZero_AndIsNeverArbitrated()
        {
            var builder = new HsmBuilder("M");
            builder.State("Idle").Initial();

            Assert.Equal(0, builder.GetGraph().RootState.Children[0].OutputLaneMask);
        }

        [Fact]
        public void OutputLaneMask_SetsTheBitsOfTheMask()
        {
            var builder = new HsmBuilder("M");
            builder.State("Idle").Initial().OutputLaneMask(0x05);   // lanes 0 and 2

            Assert.Equal(0x05, builder.GetGraph().RootState.Children[0].OutputLaneMask);
        }

        [Fact]
        public void OutputLaneMask_DropsBitsThatAddressNoLane()
        {
            // 0xFF asks for all eight bits; only Count of them are lanes, and a bit above that would make the
            // kernel arbitrate a conflict on a lane that does not exist.
            var builder = new HsmBuilder("M");
            builder.State("Idle").Initial().OutputLaneMask(0xFF);

            var expected = (byte)((1 << (byte)CommandLane.Count) - 1);
            Assert.Equal(expected, builder.GetGraph().RootState.Children[0].OutputLaneMask);
        }

        [Fact]
        public void OutputLaneMask_AndOutputLanes_AgreeAndCombine()
        {
            var viaEnum = new HsmBuilder("A");
            viaEnum.State("S").Initial().OutputLanes(CommandLane.Animation, CommandLane.Navigation);

            var viaMask = new HsmBuilder("B");
            viaMask.State("S").Initial().OutputLaneMask(
                (byte)((1 << (int)CommandLane.Animation) | (1 << (int)CommandLane.Navigation)));

            Assert.Equal(viaEnum.GetGraph().RootState.Children[0].OutputLaneMask,
                         viaMask.GetGraph().RootState.Children[0].OutputLaneMask);
        }

        [Fact]
        public void ADeclaredLane_ReachesStateDef_ThroughTheFlattener()
        {
            // The end of the chain this feature was missing: builder -> StateNode -> flatten -> StateDef,
            // which is what the kernel reads.
            var builder = new HsmBuilder("M");
            builder.State("Idle").Initial().OutputLanes(CommandLane.Navigation);

            var graph = builder.GetGraph();
            HsmNormalizer.Normalize(graph);
            var flat = HsmFlattener.Flatten(graph);

            var declared = Array.FindAll(flat.States, s => s.OutputLaneMask != 0);
            Assert.Single(declared);
            Assert.Equal((byte)(1 << (int)CommandLane.Navigation), declared[0].OutputLaneMask);
        }
    }
}
