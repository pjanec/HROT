using System;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;

namespace Fdp.Network.Cyclone.Translators
{
    /// <summary>
    /// Non-generic abstract base for all Cyclone translators.
    /// Carries the shared INetworkTranslator members (TopicName, counts, Direction).
    /// Derived classes supply the generic DDS types and method implementations.
    /// </summary>
    public abstract class CycloneBaseTranslator : INetworkTranslator, IDiscardableIngress
    {
        public string TopicName { get; }
        public long ReceivedSampleCount { get; protected set; }
        public long SentSampleCount { get; protected set; }
        public abstract TranslatorDirection Direction { get; }

        protected CycloneBaseTranslator(string topicName)
        {
            TopicName = topicName ?? throw new ArgumentNullException(nameof(topicName));
        }

        public abstract void PollIngress(IEntityCommandBuffer cmd, ISimulationView view);
        public abstract void ScanAndPublish(ISimulationView view);

        /// <inheritdoc />
        /// <remarks>⭐ CE-3329 W5 — every Cyclone translator says how its readers are emptied (a join, a replay's exit).</remarks>
        public abstract int DiscardIngress();
    }
}
