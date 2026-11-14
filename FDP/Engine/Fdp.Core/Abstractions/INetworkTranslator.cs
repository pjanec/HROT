using Fdp.ModuleHost.Abstractions;

namespace Fdp.Interfaces
{
    /// <summary>
    /// Base interface for all network translators (both descriptor and event).
    /// </summary>
    public interface INetworkTranslator
    {
        /// <summary>
        /// DDS topic name.
        /// </summary>
        string TopicName { get; }

        /// <summary>
        /// Declares which network phases this translator participates in.
        /// </summary>
        TranslatorDirection Direction { get; }

        /// <summary>
        /// ⭐⭐ Which traffic class this translator carries (<c>DQ30-C</c>). Read by the ingress
        /// systems to decide what may still be polled while a debugger holds the world frozen.
        ///
        /// <para>🔒 <b>Defaulted deliberately, and defaulted to
        /// <see cref="TranslatorClass.WorldState"/>:</b> a default interface member gives every one
        /// of the existing implementations the fail-safe answer without touching a single one of
        /// them, and the fail-safe direction is *"stop"* — see <see cref="TranslatorClass"/> for why
        /// the opposite default would fail silently instead of loudly.</para>
        /// </summary>
        TranslatorClass Category => TranslatorClass.WorldState;

        /// <summary>
        /// Number of valid ingress samples consumed by this translator.
        /// </summary>
        long ReceivedSampleCount { get; }

        /// <summary>
        /// Number of samples published by this translator.
        /// </summary>
        long SentSampleCount { get; }

        /// <summary>
        /// Processes incoming network data and updates ECS entities or publishes events.
        /// </summary>
        void PollIngress(IEntityCommandBuffer cmd, ISimulationView view);

        /// <summary>
        /// Scans ECS state and publishes updates to the network.
        /// </summary>
        void ScanAndPublish(ISimulationView view);
    }
}
