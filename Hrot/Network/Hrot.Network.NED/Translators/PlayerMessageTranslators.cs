using System;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.ScenarioRules;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Network.Translators
{
    /// <summary>
    /// ⭐ rules S2-f (📄 docs/DESIGN_Scenario_Rules.md §00.9, SR-121) — a <see cref="PlayerMessageEvent"/> raised on this node leaves it: drains the local events this node
    /// raised (<see cref="PlayerMessageEvent.OriginNodeId"/> 0 — one that ARRIVED is never sent again) and writes one <see cref="PlayerMessage"/> each. The
    /// <c>SquadReport</c> egress's shape. Installed by <c>SharedTranslatorPack</c>.
    /// </summary>
    public sealed class PlayerMessageEgressTranslator : IDescriptorTranslator
    {
        private readonly IDdsWriter<PlayerMessage> _writer;
        private readonly int _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<PlayerMessage>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtPlayerMessage;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public PlayerMessageEgressTranslator(DdsParticipant participant, int localNodeId)
            : this(new DdsWriterAdapter<PlayerMessage>(participant), localNodeId) { }

        /// <summary>Testable constructor — accepts an injected writer.</summary>
        internal PlayerMessageEgressTranslator(IDdsWriter<PlayerMessage> writer, int localNodeId)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _localNodeId = localNodeId;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (view is EntityRepository repo && !repo.Bus.IsRegisteredManaged<PlayerMessageEvent>()) return;
            foreach (var m in view.ReadManagedEvents<PlayerMessageEvent>())
            {
                if (m is null || m.OriginNodeId != 0) continue;   // it arrived from another node
                _writer.Write(ToWire(m, _localNodeId));
                SentSampleCount++;
            }
        }

        internal static PlayerMessage ToWire(PlayerMessageEvent m, int origin) => new()
        {
            MessageId = m.MessageId, OriginNodeId = origin, ToKind = m.ToKind, ToValue = m.ToValue, Text = m.Text,
            Seconds = m.Seconds, Priority = m.Priority, SentAt = m.SentAt, Source = m.Source,
        };

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }

    /// <summary>
    /// ⭐ rules S2-f — a message raised on another node arrives here and is raised again as the same local <see cref="PlayerMessageEvent"/>
    /// (<see cref="PlayerMessageEvent.OriginNodeId"/> = the sender's node). A node skips its own samples. This is the contract the Player's subtitle + log (ui) read.
    /// </summary>
    public sealed class PlayerMessageIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private readonly DdsReader<PlayerMessage>? _reader;
        private readonly int _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<PlayerMessage>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtPlayerMessage;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        /// <summary>Production constructor. A <c>null</c> participant makes <see cref="PollIngress"/> a no-op (tests).</summary>
        public PlayerMessageIngressTranslator(DdsParticipant? participant, int localNodeId)
        {
            _reader = participant is not null ? new DdsReader<PlayerMessage>(participant) : null;
            _localNodeId = localNodeId;
        }

        /// <inheritdoc />
        public int DiscardIngress() => _reader.DiscardAll();

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null || view is not EntityRepository repo) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                var data = sample.Data;
                ProcessSample(in data, repo);
            }
        }

        /// <summary>One received message; <c>true</c> when it raised the local event. Exposed for tests (no DDS stack needed).</summary>
        internal bool ProcessSample(in PlayerMessage m, EntityRepository repo)
        {
            ReceivedSampleCount++;
            if (m.OriginNodeId == _localNodeId) return false;   // our own message — raised here already
            if (!repo.Bus.IsRegisteredManaged<PlayerMessageEvent>()) repo.Bus.RegisterManaged<PlayerMessageEvent>();
            repo.Bus.PublishManaged(new PlayerMessageEvent
            {
                MessageId = m.MessageId, OriginNodeId = (Fdp.Core.NodeId)(m.OriginNodeId), ToKind = m.ToKind ?? "All", ToValue = m.ToValue, Text = m.Text ?? "",
                Seconds = m.Seconds, Priority = m.Priority, SentAt = m.SentAt, Source = m.Source,
            });
            return true;
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }
}
