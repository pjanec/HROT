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
