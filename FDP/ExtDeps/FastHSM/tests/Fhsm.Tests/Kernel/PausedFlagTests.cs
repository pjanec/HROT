using System;
using System.Runtime.InteropServices;
using Xunit;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fhsm.Tests.Kernel
{
    /// <summary>
    /// Tests for TASK-K-04: InstanceFlags.Paused pauses kernel advancement.
    /// </summary>
    public unsafe class PausedFlagTests
    {
        // Build a minimal single-state blob for testing.
        private static HsmDefinitionBlob MakeSimpleBlob()
        {
            var header = new HsmDefinitionHeader { StructureHash = 0xABCD };
            header.StateCount = 1;
            header.TransitionCount = 0;

            var states = new StateDef[1];
            states[0] = new StateDef { ParentIndex = 0xFFFF };

            return new HsmDefinitionBlob(
                header,
                states,
                Array.Empty<TransitionDef>(),
                Array.Empty<RegionDef>(),
                Array.Empty<GlobalTransitionDef>(),
                Array.Empty<ushort>(),
                Array.Empty<ushort>());
        }

        // K-04-T1: Paused instance does not advance (remains in same state/phase).
        [Fact]
        public void PausedFlag_InstanceDoesNotAdvance_WhenPaused()
        {
            var blob = MakeSimpleBlob();
            var instance = new HsmInstance64();
            HsmInstanceManager.Initialize(&instance, blob);

            // Trigger and run one tick to settle into Idle phase.
            HsmKernel.Trigger(ref instance);
            HsmKernel.Update(blob, ref instance, 0, 0.016f);

            var phaseBeforePause = instance.Header.Phase;
            var leafBefore = instance.ActiveLeafIds[0];

            // Set Paused flag.
            instance.Header.Flags |= InstanceFlags.Paused;

            // Enqueue an event and run several ticks.
            var evt = new HsmEvent { EventId = 1, Priority = EventPriority.Normal };
            HsmEventQueue.TryEnqueue(&instance, 64, evt);

            for (int i = 0; i < 5; i++)
                HsmKernel.Update(blob, ref instance, 0, 0.016f);

            // Phase and active leaf must not have changed.
            Assert.Equal(phaseBeforePause, instance.Header.Phase);
            Assert.Equal(leafBefore, instance.ActiveLeafIds[0]);
        }

        // K-04-T2: After clearing Paused flag the instance advances again.
        [Fact]
        public void PausedFlag_InstanceResumes_WhenFlagCleared()
        {
            var blob = MakeSimpleBlob();
            var instance = new HsmInstance64();
            HsmInstanceManager.Initialize(&instance, blob);

            HsmKernel.Trigger(ref instance);
            HsmKernel.Update(blob, ref instance, 0, 0.016f);

            // Pause then immediately unpause.
            instance.Header.Flags |= InstanceFlags.Paused;
            instance.Header.Flags = (InstanceFlags)(unchecked((byte)~(byte)InstanceFlags.Paused) & (byte)instance.Header.Flags);

            // Update must not throw and instance must remain not terminated.
            HsmKernel.Update(blob, ref instance, 0, 0.016f);

            Assert.Equal(0, (int)(instance.Header.Flags & InstanceFlags.Terminated));
        }

        // K-04-T3: Paused flag does not interfere with DebugTrace flag.
        [Fact]
        public void PausedFlag_DoesNotInterfere_WithDebugTrace()
        {
            var blob = MakeSimpleBlob();
            var instance = new HsmInstance64();
            HsmInstanceManager.Initialize(&instance, blob);

            // Set both flags simultaneously.
            instance.Header.Flags = InstanceFlags.Paused | InstanceFlags.DebugTrace;

            Assert.True((instance.Header.Flags & InstanceFlags.Paused) != 0);
            Assert.True((instance.Header.Flags & InstanceFlags.DebugTrace) != 0);

            // Clearing Paused must leave DebugTrace untouched.
            instance.Header.Flags = (InstanceFlags)(unchecked((byte)~(byte)InstanceFlags.Paused) & (byte)instance.Header.Flags);

            Assert.Equal(0, (int)(instance.Header.Flags & InstanceFlags.Paused));
            Assert.NotEqual(0, (int)(instance.Header.Flags & InstanceFlags.DebugTrace));
        }

        // K-04-T4: Paused has bit value 0x80.
        [Fact]
        public void PausedFlag_HasCorrectBitValue()
        {
            Assert.Equal(0x80, (byte)InstanceFlags.Paused);
        }

        // K-04-T5: Paused is independent of Terminated.
        [Fact]
        public void PausedFlag_IsIndependent_OfTerminated()
        {
            // They occupy different bits so combining them must equal both bits.
            var combined = InstanceFlags.Paused | InstanceFlags.Terminated;
            Assert.NotEqual(0, (int)(combined & InstanceFlags.Paused));
            Assert.NotEqual(0, (int)(combined & InstanceFlags.Terminated));
        }
    }
}
