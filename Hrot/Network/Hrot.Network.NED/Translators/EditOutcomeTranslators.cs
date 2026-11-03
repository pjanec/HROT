using System;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Edits;
using Fdp.Toolkit.Replication.Services;
using Hrot.Map.Common.Dds;
using Hrot.NED.Messages;

namespace Hrot.Network.Translators
{
    /// <summary>
    /// ⭐ CE-2380 part 1 (R-2082, 📄 docs/DESIGN_Remote_Edit_Outcome.md §2) — an <see cref="EditOutcome"/> reported on this node leaves it: drains the local events
    /// (<c>FromNetwork</c> false — one that ARRIVED is never sent again) and writes one <see cref="EntityEditOutcome"/> each. Installed by <c>SharedTranslatorPack</c>.
    /// </summary>
    public sealed class EditOutcomeEgressTranslator : INetworkEventTranslator
    {
        private readonly IDdsWriter<EntityEditOutcome> _writer;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityEditOutcome>();
        public TranslatorDirection Direction => TranslatorDirection.Egress;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }

        public EditOutcomeEgressTranslator(DdsParticipant participant) : this(new DdsWriterAdapter<EntityEditOutcome>(participant)) { }

        /// <summary>Testable constructor — accepts an injected writer.</summary>
        public EditOutcomeEgressTranslator(IDdsWriter<EntityEditOutcome> writer)
            => _writer = writer ?? throw new ArgumentNullException(nameof(writer));

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (view is EntityRepository repo && !repo.Bus.IsRegisteredManaged<EditOutcome>()) return;
            foreach (var o in view.ReadManagedEvents<EditOutcome>())
            {
                if (o is null || o.FromNetwork) continue;
                _writer.Write(ToWire(o));
                SentSampleCount++;
            }
        }

        public static EntityEditOutcome ToWire(EditOutcome o) => new()
        {
            RequestId = o.RequestId, EntityId = o.NetworkId, Status = (int)o.Status, RespondingNode = o.RespondingNode, Reason = o.Reason ?? string.Empty,
        };
    }

    /// <summary>
    /// ⭐ CE-2380 part 1 — an outcome answered by another node arrives here: it is added to this node's <see cref="EditOutcomeLog"/> and raised again as an
    /// <see cref="EditOutcome"/> with <c>FromNetwork</c> true (the editor / HTTP reads either). A node skips its own samples (<c>RespondingNode</c>).
    /// </summary>
    public sealed class EditOutcomeIngressTranslator : INetworkEventTranslator, IDiscardableIngress
    {
        private readonly DdsReader<EntityEditOutcome>? _reader;
        private readonly int _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<EntityEditOutcome>();
        public TranslatorDirection Direction => TranslatorDirection.Ingress;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }

        /// <summary>Production constructor. A <c>null</c> participant makes <see cref="PollIngress"/> a no-op (tests).</summary>
        public EditOutcomeIngressTranslator(DdsParticipant? participant, int localNodeId)
        {
            _reader = participant is not null ? new DdsReader<EntityEditOutcome>(participant) : null;
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

        /// <summary>One received outcome; <c>true</c> when it was taken in. Exposed for tests (no DDS stack needed).</summary>
        public bool ProcessSample(in EntityEditOutcome m, EntityRepository repo)
        {
            ReceivedSampleCount++;
            if (m.RespondingNode == _localNodeId) return false;   // our own answer — reported here already
            var outcome = EditOutcome.Create(m.RequestId, m.EntityId, (EditStatus)m.Status, m.Reason, m.RespondingNode, at: repo.HasSingletonUnmanaged<GlobalTime>() ? repo.GetSingletonUnmanaged<GlobalTime>().TotalTime : 0d, fromNetwork: true);
            EditOutcomeLog.Of(repo).Add(outcome);
            EditOutcomes.Publish(repo, outcome);
            return true;
        }

        public void ScanAndPublish(ISimulationView view) { }
    }
}
