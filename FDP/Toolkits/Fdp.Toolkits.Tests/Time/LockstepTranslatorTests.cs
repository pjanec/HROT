using System.Linq;
using Fdp.Core;
using Fdp.Toolkit.Time.Domain;
using Fdp.Toolkit.Time.Translators;
using Xunit;

namespace Fdp.Toolkit.Time.Tests
{
    /// <summary>
    /// Unit tests for TCU-TR001 (MasterLockstepTranslator) and TCU-TR002
    /// (SlaveLockstepTranslator).  All tests use null-participant construction
    /// so no DDS infrastructure is required.
    /// </summary>
    public class LockstepTranslatorTests
    {
        // ─────────────────────────────────────────────────────────────────────
        // MasterLockstepTranslator
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>TCU-TR001 §1 — null participant must not throw on any code path.</summary>
        [Fact]
        public void MasterLockstepTranslator_NullParticipant_DoesNotThrow()
        {
            var bus        = new FdpEventBus();
            var translator = new MasterLockstepTranslator(participant: null, eventBus: bus);

            // Both code paths must be safe no-ops.
            translator.ScanAndPublish(null!);
            translator.PollIngress(null!, null!);
        }

        /// <summary>
        /// TCU-TR001 §2 — ScanAndPublish drains AdvanceFrameIntent from the bus even when
        /// the DDS writer is absent (null participant).  After the call the event must not
        /// remain in the bus.
        /// </summary>
        [Fact]
        public void MasterLockstepTranslator_Egress_PublishesFrameOrderFromAdvanceFrameIntent()
        {
            var bus        = new FdpEventBus();
            var translator = new MasterLockstepTranslator(participant: null, eventBus: bus);

            // Publish intent and make it readable.
            bus.PublishManaged(new AdvanceFrameIntent { FrameID = 7, FixedDelta = 0.016f });
            bus.SwapBuffers();

            // Translator drains the intent (DDS write is no-op with null participant).
            translator.ScanAndPublish(null!);

            // Swap to bring next write buffer into view — should be empty.
            bus.SwapBuffers();
            var remaining = bus.ReadManaged<AdvanceFrameIntent>().ToList();

            Assert.Empty(remaining);
        }

        /// <summary>
        /// TCU-TR001 §3 — PollIngress with null DDS is a no-op; no FrameStepCompletedEvent
        /// must appear on the bus.  This documents the null-participant contract.
        /// </summary>
        [Fact]
        public void MasterLockstepTranslator_Ingress_PublishesFrameStepCompletedEvent()
        {
            var bus        = new FdpEventBus();
            var translator = new MasterLockstepTranslator(participant: null, eventBus: bus);

            // No samples in DDS (null participant) → no event published.
            translator.PollIngress(null!, null!);
            bus.SwapBuffers();

            var events = bus.ReadManaged<FrameStepCompletedEvent>().ToList();

            Assert.Empty(events);
        }

        /// <summary>TCU-TR001 metadata — TopicName must be "FrameOrder".</summary>
        [Fact]
        public void MasterLockstepTranslator_TopicName_IsFrameOrder()
        {
            var bus        = new FdpEventBus();
            var translator = new MasterLockstepTranslator(participant: null, eventBus: bus);

            Assert.Equal("FrameOrder", translator.TopicName);
        }

        /// <summary>TCU-TR001 metadata — DescriptorOrdinal must be 202.</summary>
        [Fact]
        public void MasterLockstepTranslator_DescriptorOrdinal_Is202()
        {
            var bus        = new FdpEventBus();
            var translator = new MasterLockstepTranslator(participant: null, eventBus: bus);

            Assert.Equal(202L, translator.DescriptorOrdinal);
        }

        // ─────────────────────────────────────────────────────────────────────
        // SlaveLockstepTranslator
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>TCU-TR002 §1 — null participant must not throw on any code path.</summary>
        [Fact]
        public void SlaveLockstepTranslator_NullParticipant_DoesNotThrow()
        {
            var bus        = new FdpEventBus();
            var translator = new SlaveLockstepTranslator(
                participant: null, eventBus: bus, localNodeId: 3);

            translator.ScanAndPublish(null!);
            translator.PollIngress(null!, null!);
        }

        /// <summary>
        /// TCU-TR002 §2 — PollIngress with null DDS is a no-op; no AdvanceFrameIntent
        /// must appear on the bus.  This documents the null-participant contract.
        /// </summary>
        [Fact]
        public void SlaveLockstepTranslator_Ingress_PublishesAdvanceFrameIntent()
        {
            var bus        = new FdpEventBus();
            var translator = new SlaveLockstepTranslator(
                participant: null, eventBus: bus, localNodeId: 3);

            // No samples in DDS (null participant) → no event published.
            translator.PollIngress(null!, null!);
            bus.SwapBuffers();

            var events = bus.ReadManaged<AdvanceFrameIntent>().ToList();

            Assert.Empty(events);
        }

        /// <summary>
        /// TCU-TR002 §3 — ScanAndPublish drains FrameStepCompletedEvent from the bus even
        /// when the DDS writer is absent (null participant).  After the call the event must
        /// not remain in the bus.
        /// </summary>
        [Fact]
        public void SlaveLockstepTranslator_Egress_DrainFrameStepCompletedEvent()
        {
            var bus        = new FdpEventBus();
            var translator = new SlaveLockstepTranslator(
                participant: null, eventBus: bus, localNodeId: 10);

            // Publish completion event and make it readable.
            bus.PublishManaged(new FrameStepCompletedEvent { FrameID = 3, NodeID = 10 });
            bus.SwapBuffers();

            // Translator drains the event (DDS write is no-op with null participant).
            translator.ScanAndPublish(null!);

            // Swap to bring next write buffer into view — should be empty.
            bus.SwapBuffers();
            var remaining = bus.ReadManaged<FrameStepCompletedEvent>().ToList();

            Assert.Empty(remaining);
        }

        /// <summary>TCU-TR002 metadata — DescriptorOrdinal must be 203.</summary>
        [Fact]
        public void SlaveLockstepTranslator_DescriptorOrdinal_Is203()
        {
            var bus        = new FdpEventBus();
            var translator = new SlaveLockstepTranslator(
                participant: null, eventBus: bus, localNodeId: 0);

            Assert.Equal(203L, translator.DescriptorOrdinal);
        }
    }
}
