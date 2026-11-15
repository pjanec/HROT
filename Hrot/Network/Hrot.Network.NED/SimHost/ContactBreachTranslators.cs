using System;
using System.Collections.Generic;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.ModuleHost.Abstractions;
using Fdp.Toolkit.Replication.Services;
using Fdp.Toolkit.Terrain;
using Hrot.Map.Common.Dds;
using Hrot.NED.Descriptors;
using Hrot.NED.Messages;

namespace Hrot.Network.NED.SimHost
{
    /// <summary>
    /// ⭐⭐ <c>CE-5001</c> (B-3b, V-17; 📄 docs/DESIGN_Vehicle_Breaching.md §7.16) — the deciding node's <see cref="ContactBreachEvent"/>s → the <see cref="ContactBreach"/> topic, stamped
    /// with this node's id, so the driver's HUD cue, the dust and the crash reach a station that is not the hull's muscle (possession never moves the hull, Entity Control M-6).
    /// ⭐ A remote copy is never sent again; a break (Broke / Bumped) is always sent; a push or a "too strong" — the same kind again for the same hull — at most every
    /// <see cref="RepeatSeconds"/> (the hull raises one every frame while it presses; the cue only needs to stay lit: the HUD holds a push 0.3 s, a "too strong" 2 s).
    /// </summary>
    public sealed class ContactBreachEgressTranslator : IDescriptorTranslator
    {
        /// <summary>A repeated push / too-strong of one hull is sent at most this often (s).</summary>
        public const double RepeatSeconds = 0.2;

        private readonly IDdsWriter<ContactBreach> _writer;
        private readonly NetworkEntityMap _entityMap;
        private readonly NodeId _localNodeId;
        private readonly Dictionary<int, (byte Kind, double At)> _last = new();

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<ContactBreach>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtContactBreach;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public ContactBreachEgressTranslator(DdsParticipant participant, NetworkEntityMap entityMap, int localNodeId)
            : this(new DdsWriterAdapter<ContactBreach>(participant), entityMap, localNodeId) { }

        internal ContactBreachEgressTranslator(IDdsWriter<ContactBreach> writer, NetworkEntityMap entityMap, int localNodeId = 0)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = (NodeId)localNodeId;
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (view is EntityRepository repo && !repo.Bus.IsRegistered<ContactBreachEvent>()) return;
            double now = double.NaN;   // read only when a contact arrived — a steady frame allocates nothing (R-2084)
            foreach (ref readonly var e in view.ReadEvents<ContactBreachEvent>())
            {
                if (double.IsNaN(now)) now = view is EntityRepository r ? Fdp.Toolkit.Time.SimClock.Of(r).TotalTime : 0.0;
                if (e.IsRemote) continue;
                if (!_entityMap.TryGetNetworkId(e.Vehicle, out int id)) continue;   // a hull no other node knows: nobody to tell
                bool repeat = e.Kind is (byte)ContactBreachKind.Pushing or (byte)ContactBreachKind.TooStrong;
                if (repeat && _last.TryGetValue(id, out var last) && last.Kind == e.Kind && now - last.At < RepeatSeconds && now >= last.At) continue;
                _last[id] = (e.Kind, now);
                _writer.Write(new ContactBreach { VehicleEntityId = id, Kind = e.Kind, X = e.X, Y = e.Y, Z = e.Z, Progress = e.Progress, SourceNodeId = _localNodeId });
                SentSampleCount++;
            }
        }

        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) => _last.Remove(networkEntityId);
    }

    /// <summary>
    /// ⭐⭐ <c>CE-5001</c> (§7.16) — another node's <see cref="ContactBreach"/> → this node's <see cref="ContactBreachEvent"/>, marked remote (this node's own sample dropped by
    /// <c>SourceNodeId</c>; a hull this node does not know dropped). A one-shot world event: discarded while the node is frozen for a join (R-4007).
    /// </summary>
    public sealed class ContactBreachIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private readonly DdsReader<ContactBreach>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly NodeId _localNodeId;

        public string TopicName { get; } = CycloneDDS.Schema.DdsTopicName.Of<ContactBreach>();
        public long DescriptorOrdinal => (long)EDescriptorType.dtContactBreach;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;
        public TranslatorClass Category => TranslatorClass.WorldEvent;

        public ContactBreachIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap, int localNodeId)
        {
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
            _localNodeId = (NodeId)localNodeId;
            _reader = participant is not null ? new DdsReader<ContactBreach>(participant) : null;
        }

        public int DiscardIngress() => _reader?.DiscardAll() ?? 0;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid) continue;
                ReceivedSampleCount++;
                var data = sample.Data;
                ProcessSample(in data, cmd);
            }
        }

        internal void ProcessSample(in ContactBreach msg, IEntityCommandBuffer cmd)
        {
            if (msg.SourceNodeId != 0 && msg.SourceNodeId == _localNodeId) return;   // already a local event here
            if (!_entityMap.TryGetEntity(msg.VehicleEntityId, out var vehicle)) return;
            cmd.PublishEvent(new ContactBreachEvent { Vehicle = vehicle, Kind = msg.Kind, X = msg.X, Y = msg.Y, Z = msg.Z, Progress = msg.Progress, IsRemote = true });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(int networkEntityId) { }
    }
}
