using Xunit;
using Fhsm.Compiler;
using Fhsm.Kernel;
using Fhsm.Kernel.Data;

namespace Fhsm.Tests.Kernel
{
    /// <summary>
    /// Tests for BHU-006: final-state detection and the Terminated instance flag.
    /// </summary>
    public unsafe class TerminatedFlagTests
    {
        // ---- Helpers ----

        private static HsmDefinitionBlob BuildBlob(HsmBuilder builder)
        {
            var graph = builder.Build();
            HsmNormalizer.Normalize(graph);
            var flat  = HsmFlattener.Flatten(graph);
            return HsmEmitter.Emit(flat);
        }

        // The kernel processes one phase per Update call (Entry->Activity->Idle->Entry->RTC...).
        // Pump multiple times to let the machine fully settle or complete a transition.
        private static void Pump(HsmDefinitionBlob blob, ref HsmInstance64 instance, int ctx, int steps = 10)
        {
            for (int i = 0; i < steps; i++)
                HsmKernel.Update(blob, ref instance, ctx, 0f);
        }

        // ---- Tests ----

        [Fact]
        public void InitializeMachine_SetsTerminated_WhenInitialStateIsFinal()
        {
            // A machine whose ONLY state is both Initial and Final.
            var builder = new HsmBuilder("M");
            builder.State("Done").Initial().Final();
            var blob = BuildBlob(builder);

            var instance = new HsmInstance64();
            instance.Header.MachineId = blob.Header.StructureHash;
            instance.Header.Phase = InstancePhase.Entry;
            instance.ActiveLeafIds[0] = 0xFFFF;

            int ctx = 0;
            HsmKernel.Update(blob, ref instance, ctx, 0f);

            Assert.True(
                (instance.Header.Flags & InstanceFlags.Terminated) != 0,
                "Terminated flag must be set after entering a final state during initialization.");
        }

        [Fact]
        public void Transition_SetsTerminated_WhenEnteringFinalState()
        {
            // Two-state machine: Active --[event 1]--> Done (final).
            // Active is added first so Children[0] points to it (correct initial state).
            const ushort EventDone = 1;

            var builder = new HsmBuilder("M");
            builder.Event("Done", EventDone);
            var active = builder.State("Active").Initial();
            builder.State("Done").Final();
            active.On(EventDone).GoTo("Done");
            var blob = BuildBlob(builder);

            var instance = new HsmInstance64();
            instance.Header.MachineId = blob.Header.StructureHash;
            instance.Header.Phase = InstancePhase.Entry;
            instance.ActiveLeafIds[0] = 0xFFFF;

            int ctx = 0;
            // Pump until settled in Active.
            Pump(blob, ref instance, ctx);
            Assert.True((instance.Header.Flags & InstanceFlags.Terminated) == 0,
                "Should not be Terminated after entering Active.");

            // Enqueue the event that drives to Done.
            HsmEventQueue.TryEnqueue(&instance, new HsmEvent { EventId = EventDone });

            // Pump until the transition to Done (final) is processed.
            Pump(blob, ref instance, ctx);

            Assert.True(
                (instance.Header.Flags & InstanceFlags.Terminated) != 0,
                "Terminated flag must be set after transitioning into a final state.");
        }

        [Fact]
        public void SecondUpdate_AfterTerminated_DoesNotCrash()
        {
            const ushort EventDone = 1;

            var builder = new HsmBuilder("M");
            builder.Event("Done", EventDone);
            var active = builder.State("Active").Initial();
            builder.State("Done").Final();
            active.On(EventDone).GoTo("Done");
            var blob = BuildBlob(builder);

            var instance = new HsmInstance64();
            instance.Header.MachineId = blob.Header.StructureHash;
            instance.Header.Phase = InstancePhase.Entry;
            instance.ActiveLeafIds[0] = 0xFFFF;

            int ctx = 0;
            Pump(blob, ref instance, ctx);  // settle into Active
            HsmEventQueue.TryEnqueue(&instance, new HsmEvent { EventId = EventDone });
            Pump(blob, ref instance, ctx);  // transition to Done (Terminated)

            Assert.True((instance.Header.Flags & InstanceFlags.Terminated) != 0);

            // Further updates on a terminated instance must be no-ops.
            ushort leafBefore = instance.ActiveLeafIds[0];
            Pump(blob, ref instance, ctx);
            Assert.Equal(leafBefore, instance.ActiveLeafIds[0]);
            Assert.True((instance.Header.Flags & InstanceFlags.Terminated) != 0);
        }

        [Fact]
        public void NonFinalState_DoesNotSetTerminated()
        {
            var builder = new HsmBuilder("M");
            builder.State("Active").Initial();
            builder.State("Idle");
            var blob = BuildBlob(builder);

            var instance = new HsmInstance64();
            instance.Header.MachineId = blob.Header.StructureHash;
            instance.Header.Phase = InstancePhase.Entry;
            instance.ActiveLeafIds[0] = 0xFFFF;

            int ctx = 0;
            Pump(blob, ref instance, ctx);

            Assert.True((instance.Header.Flags & InstanceFlags.Terminated) == 0,
                "Terminated must NOT be set for a non-final state.");
        }
    }
}
