using System;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Core;
using Fdp.Toolkit.Time.Messages;
using Fdp.Toolkit.Time.Translators;

namespace Fdp.Toolkit.Time
{
    /// <summary>
    /// Composition-root helper for wiring <see cref="SwitchTimeModeEvent"/> over CycloneDDS
    /// via the <see cref="SwitchTimeModeWireDto"/> wire DTO.
    /// <para>
    /// <b>Supported path — <see cref="CreateDescriptorTranslator"/>:</b>
    /// Returns a <see cref="SwitchTimeModeDescriptorTranslator"/> that bridges
    /// <see cref="SwitchTimeModeEvent"/> events between the local <see cref="FdpEventBus"/>
    /// and the DDS topic <c>"SwitchTimeModeEvent"</c> using
    /// <see cref="SwitchTimeModeWireDto"/> (integer <c>TargetModeInt</c> field avoids
    /// Cyclone IDL limitations with <c>enum</c> types).  Add the returned translator to
    /// the <c>customTranslators</c> list of every node's <c>CycloneNetworkModule</c> so
    /// that mode-switch events cross DDS on both the Master and every Slave.
    /// </para>
    /// </summary>
    public static class TimeNetworkModule
    {
        /// <summary>
        /// Creates an <see cref="IDescriptorTranslator"/> that bridges
        /// <see cref="SwitchTimeModeEvent"/> between the local <see cref="FdpEventBus"/> and
        /// the CycloneDDS wire.
        ///
        /// <para>
        /// Add the returned translator to the <c>customTranslators</c> list passed to
        /// <c>CycloneNetworkModule</c> during application startup.  It handles both egress
        /// (coordinator → DDS) and ingress (DDS → listener) so all cluster nodes wire the
        /// same instance.
        /// </para>
        /// </summary>
        /// <param name="participant">
        /// DDS domain participant.  Pass <see langword="null"/> for test-only hosts — egress and
        /// ingress become safe no-ops.
        /// </param>
        /// <param name="eventBus">
        /// The event bus shared with the local <see cref="Controllers.DistributedTimeCoordinator"/>
        /// / <see cref="Controllers.SlaveTimeModeListener"/>.
        /// </param>
        public static IDescriptorTranslator CreateDescriptorTranslator(
            DdsParticipant? participant, FdpEventBus eventBus)
        {
            if (eventBus == null) throw new ArgumentNullException(nameof(eventBus));
            return new SwitchTimeModeDescriptorTranslator(participant, eventBus);
        }

        /// <summary>
        /// Creates a <see cref="MasterLockstepTranslator"/> that bridges
        /// <see cref="Domain.AdvanceFrameIntent"/> → <c>FrameOrder</c> DDS (egress) and
        /// <c>FrameAck</c> DDS → <see cref="Domain.FrameStepCompletedEvent"/> (ingress).
        /// Use on the master node only.
        /// </summary>
        /// <param name="participant">
        /// DDS domain participant.  Pass <see langword="null"/> for test-only hosts — both
        /// directions become safe no-ops.
        /// </param>
        /// <param name="eventBus">
        /// The event bus shared with <see cref="Controllers.MasterSyncController"/>.
        /// </param>
        public static MasterLockstepTranslator CreateMasterLockstepTranslator(
            DdsParticipant? participant, FdpEventBus eventBus)
        {
            if (eventBus == null) throw new ArgumentNullException(nameof(eventBus));
            return new MasterLockstepTranslator(participant, eventBus);
        }

        /// <summary>
        /// Creates a <see cref="SlaveLockstepTranslator"/> that bridges
        /// <c>FrameOrder</c> DDS → <see cref="Domain.AdvanceFrameIntent"/> (ingress) and
        /// <see cref="Domain.FrameStepCompletedEvent"/> → <c>FrameAck</c> DDS (egress).
        /// Use on slave nodes only.
        /// </summary>
        /// <param name="participant">
        /// DDS domain participant.  Pass <see langword="null"/> for test-only hosts — both
        /// directions become safe no-ops.
        /// </param>
        /// <param name="eventBus">
        /// The event bus shared with <see cref="Controllers.SlaveSyncController"/>.
        /// </param>
        /// <param name="localNodeId">
        /// This node's ID, embedded in ACK messages so the master can attribute them.
        /// </param>
        public static SlaveLockstepTranslator CreateSlaveLockstepTranslator(
            DdsParticipant? participant, FdpEventBus eventBus, int localNodeId = 0)
        {
            if (eventBus == null) throw new ArgumentNullException(nameof(eventBus));
            return new SlaveLockstepTranslator(participant, eventBus, localNodeId);
        }

        /// <summary>
        /// Creates a <see cref="Translators.MasterTimeSyncTranslator"/> that handles the
        /// NTP-style two-way clock sync handshake for the master/orchestrator node.
        /// <para>
        /// Add the returned translator to the <c>customTranslators</c> list of the master
        /// node's <c>CycloneNetworkModule</c> during application startup.
        /// </para>
        /// </summary>
        /// <param name="participant">
        /// DDS domain participant.  Pass <see langword="null"/> for test-only hosts —
        /// all DDS operations become safe no-ops.
        /// </param>
        /// <param name="tickSource">
        /// Optional tick source override (<c>HighResUtcClock.GetTicks</c> by default).
        /// Inject a controlled counter in unit tests.
        /// </param>
        public static IDescriptorTranslator CreateMasterTimeSyncTranslator(
            DdsParticipant? participant,
            Func<long>?     tickSource = null)
        {
            return new Translators.MasterTimeSyncTranslator(participant, tickSource);
        }

        /// <summary>
        /// Creates a <see cref="Translators.SlaveTimeSyncTranslator"/> for slave nodes
        /// (IG, ExCon, SimHost-slave).
        /// <para>
        /// Add the returned translator to the <c>customTranslators</c> list of the slave
        /// node's <c>CycloneNetworkModule</c> during application startup.
        /// </para>
        /// </summary>
        /// <param name="participant">
        /// DDS domain participant.  Pass <see langword="null"/> for test-only hosts —
        /// all DDS operations become safe no-ops.
        /// </param>
        /// <param name="eventBus">
        /// The event bus shared with the local <see cref="Controllers.SlaveSyncController"/>.
        /// Must not be null.
        /// </param>
        /// <param name="localNodeId">
        /// This node's ID — used to filter incoming <see cref="Messages.TimeSyncResponse"/>
        /// samples to those addressed to this specific slave.
        /// </param>
        /// <param name="tickSource">
        /// Optional tick source override (<c>HighResUtcClock.GetTicks</c> by default).
        /// Inject a controlled counter in unit tests.
        /// </param>
        public static IDescriptorTranslator CreateSlaveTimeSyncTranslator(
            DdsParticipant? participant,
            FdpEventBus     eventBus,
            int             localNodeId,
            Func<long>?     tickSource = null)
        {
            if (eventBus == null) throw new ArgumentNullException(nameof(eventBus));
            return new Translators.SlaveTimeSyncTranslator(participant, eventBus, localNodeId, tickSource);
        }
    }
}
