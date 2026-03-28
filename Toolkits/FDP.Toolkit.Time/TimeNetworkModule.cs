using System;
using CycloneDDS.Runtime;
using Fdp.Kernel;
using ModuleHost.Network.Cyclone.Translators;
using FDP.Toolkit.Time.Messages;

namespace FDP.Toolkit.Time
{
    /// <summary>
    /// Composition-root helper for wiring <see cref="SwitchTimeModeEvent"/> over CycloneDDS.
    /// <para>
    /// Must be called on <strong>every</strong> node (both Master and Slaves) during
    /// application startup, before the simulation loop begins.  The
    /// <see cref="BlitEventTranslator{T}"/> performs a zero-allocation raw memcpy from/to
    /// the DDS wire format, so <see cref="SwitchTimeModeEvent"/> must remain an
    /// <c>unmanaged</c> struct (e.g. no reference-type fields).
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
        public static BlitEventTranslator<SwitchTimeModeEvent> RegisterTranslators(
            DdsParticipant participant)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));

            return new BlitEventTranslator<SwitchTimeModeEvent>(
                participant,
                topicName: "SwitchTimeModeEvent");
        }
    }
}
