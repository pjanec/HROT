using System;
using CycloneDDS.Runtime;
using Fdp.Interfaces;
using Fdp.Kernel;
using ModuleHost.Network.Cyclone.Translators;
using FDP.Toolkit.Time.Messages;

namespace FDP.Toolkit.Time
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
    /// <para>
    /// <b>Deprecated path — <see cref="RegisterTranslators"/>:</b>
    /// Returns a raw <see cref="BlitEventTranslator{T}"/> for <see cref="SwitchTimeModeEvent"/>.
    /// This method is marked <see cref="ObsoleteAttribute"/>: the blit translator
    /// cannot carry <see cref="SwitchTimeModeWireDto"/> and is incompatible with the
    /// <c>CycloneNetworkModule</c> composition root.  Use
    /// <see cref="CreateDescriptorTranslator"/> instead.
    /// </para>
    /// </summary>
    public static class TimeNetworkModule
    {
        /// <summary>
        /// Creates and returns a <see cref="BlitEventTranslator{T}"/> for
        /// <see cref="SwitchTimeModeEvent"/>.
        /// <para>
        /// Call <c>translator.ScanAndPublish(view)</c> from the Export phase to egress master
        /// events and <c>translator.PollIngress(bus)</c> from the Input phase to ingress slave
        /// events on every simulation frame.
        /// </para>
        /// </summary>
        /// <param name="participant">The CycloneDDS domain participant shared by this node.</param>
        /// <returns>A configured translator ready for per-frame egress/ingress calls.</returns>
        [Obsolete("Use CreateDescriptorTranslator(participant, eventBus) instead. RegisterTranslators produces a BlitEventTranslator that cannot carry SwitchTimeModeWireDto and is incompatible with the CycloneNetworkModule composition root.")]
        public static BlitEventTranslator<SwitchTimeModeEvent> RegisterTranslators(
            DdsParticipant participant)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));

            return new BlitEventTranslator<SwitchTimeModeEvent>(
                participant,
                topicName: "SwitchTimeModeEvent");
        }

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
        /// Creates an <see cref="IDescriptorTranslator"/> that bridges
        /// <see cref="Messages.FrameOrderDescriptor"/> and <see cref="Messages.FrameAckDescriptor"/>
        /// between the local <see cref="FdpEventBus"/> and the CycloneDDS wire, enabling
        /// distributed lockstep time-stepping.
        ///
        /// <para>
        /// <b>Add to every simulation-kernel node</b> (Master and all Slaves):
        /// the master egresses <c>FrameOrder</c> to DDS and ingresses <c>FrameAck</c> from DDS;
        /// each slave egresses <c>FrameAck</c> and ingresses <c>FrameOrder</c>.  Running both
        /// directions symmetrically on every node is harmless — each controller only reacts to
        /// the message type it cares about.
        /// </para>
        /// </summary>
        /// <param name="participant">
        /// DDS domain participant.  Pass <see langword="null"/> for test-only hosts — both
        /// directions become safe no-ops.
        /// </param>
        /// <param name="eventBus">
        /// The event bus shared with <see cref="Controllers.SteppedMasterController"/> and/or
        /// <see cref="Controllers.SteppedSlaveController"/> on this node.
        /// </param>
        public static IDescriptorTranslator CreateLockstepTranslator(
            DdsParticipant? participant, FdpEventBus eventBus, int localNodeId = 0)
        {
            if (eventBus == null) throw new ArgumentNullException(nameof(eventBus));
            return new FrameLockstepDescriptorTranslator(participant, eventBus, localNodeId);
        }

        /// <summary>
        /// Creates an egress translator that reads <see cref="Messages.TimePulseDescriptor"/>
        /// events from <paramref name="eventBus"/> and publishes them to the
        /// <c>"TimePulse"</c> DDS topic.
        /// <para>
        /// Wire this on every node that <em>owns</em> the authoritative simulation clock
        /// (master node / Orchestrator) so slave nodes and UI caches can receive pulses.
        /// </para>
        /// </summary>
        public static IDescriptorTranslator CreateTimePulseEgressTranslator(
            DdsParticipant participant, FdpEventBus eventBus)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            if (eventBus    == null) throw new ArgumentNullException(nameof(eventBus));
            return new TimePulseEgressTranslator(participant, eventBus);
        }

        /// <summary>
        /// Creates an ingress translator that reads the <c>"TimePulse"</c> DDS topic and
        /// publishes <see cref="Messages.TimePulseDescriptor"/> events into
        /// <paramref name="eventBus"/> for the local <c>SlaveTimeController</c> PLL.
        /// <para>
        /// Wire this on every node that <em>follows</em> the master clock (IG, CGF, and
        /// any other slave-only kernel nodes).
        /// </para>
        /// </summary>
        public static IDescriptorTranslator CreateTimePulseIngressTranslator(
            DdsParticipant? participant, FdpEventBus eventBus)
        {
            if (eventBus == null) throw new ArgumentNullException(nameof(eventBus));
            return new TimePulseIngressTranslator(participant, eventBus);
        }
    }
}
