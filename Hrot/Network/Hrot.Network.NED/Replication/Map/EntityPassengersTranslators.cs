using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Behavior.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-3110</c> I-2b (R-224; 📄 docs/DESIGN_Entity_Interactions.md §7.1c) — publishes a vehicle's <see cref="PassengerBuffer"/> from
    /// the node owning its <c>dtPassengerBuffer</c> (the vehicle's brain, where <c>EmbarkationSystem</c> writes it), on change. Every brained
    /// entity carries an empty list (the TKB gives one), so a list that was never filled is never sent.
    /// </summary>
    public sealed class EntityPassengersEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityPassengers";
        private readonly DdsWriter<EntityPassengers>? _writer;
        private readonly Dictionary<long, EntityPassengers> _last = new();
        private int _worldEpoch;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtPassengerBuffer;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityPassengersEgressTranslator(DdsParticipant? participant)
            => _writer = participant is not null ? new DdsWriter<EntityPassengers>(participant, DdsTopicName) : null;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch)) _last.Clear();
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<PassengerBuffer>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var vehicle in view.Query().With<PassengerBuffer>().With<NetworkIdentity>().Build())
            {
                if (!view.HasAuthority(vehicle, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(vehicle).Value;
                var sample = ToSample(view, netId, view.GetComponentRO<PassengerBuffer>(vehicle));
                if (_last.TryGetValue(netId, out var prev) ? prev.Equals(sample) : sample.Count == 0) continue;   // unchanged, or never filled
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam): seat i carries the network id of the passenger in seat i, 0 for an empty seat or one with
        /// no network id; <c>Count</c> = the occupied seats. ⭐ CE-2151 B-b (R-2009 B4): slot-indexed — a slot IS a seat, so a hole is kept.</summary>
        internal static EntityPassengers ToSample(ISimulationView view, long netId, in PassengerBuffer b)
        {
            Span<long> ids = stackalloc long[PassengerBuffer.Capacity];
            int n = 0;
            for (int i = 0; i < PassengerBuffer.Capacity; i++)
            {
                var p = b.Passengers[i];
                if (!p.IsNull && view.IsAlive(p) && view.HasComponent<NetworkIdentity>(p)) { ids[i] = view.GetComponentRO<NetworkIdentity>(p).Value; n++; }
            }
            return new EntityPassengers
            {
                EntityId = netId, Count = (byte)n,
                Passenger0 = ids[0], Passenger1 = ids[1], Passenger2 = ids[2], Passenger3 = ids[3],
                Passenger4 = ids[4], Passenger5 = ids[5], Passenger6 = ids[6], Passenger7 = ids[7],
                Modes = b.Modes,   // ⭐ CE-2152 (R-2011 C5) — carried / dragged travels with the seat
            };
        }

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ <c>CE-3110</c> I-2b — applies a vehicle's published passenger list on this node; <c>EmbarkedStateSystem</c> derives the passengers'
    /// side from it. A sample naming a passenger this node does not know yet is HELD (the newest per vehicle) and applied once every id
    /// resolves — it is sent once (TransientLocal), so dropping it would leave the list wrong until it next changed. The recorded owner
    /// never takes its own sample back.
    /// </summary>
    public sealed class EntityPassengersIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityPassengers";
        private const long OrdinalValue = (long)EDescriptorType.dtPassengerBuffer;
        private readonly DdsReader<EntityPassengers>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly Dictionary<long, EntityPassengers> _pending = new();
        private readonly List<long> _done = new();

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        /// <summary>Samples waiting for a vehicle or a passenger this node does not know yet.</summary>
        public int PendingCount => _pending.Count;
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityPassengersIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityPassengers>(participant, DdsTopicName) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is not null)
            {
                using var loan = _reader.Take();
                foreach (var sample in loan)
                {
                    if (!sample.IsValid || sample.Info.InstanceState != DdsInstanceState.Alive) continue;
                    ReceivedSampleCount++;
                    _pending[sample.Data.EntityId] = sample.Data;
                }
            }
            Flush(cmd, view);
        }

        /// <summary>Takes one sample (the test seam) — it is applied by the next <see cref="Flush"/>.</summary>
        internal void Receive(in EntityPassengers d) { ReceivedSampleCount++; _pending[d.EntityId] = d; }

        /// <summary>Applies every held sample whose vehicle and passengers are all known.</summary>
        internal void Flush(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_pending.Count == 0) return;
            foreach (var (vehicleId, d) in _pending)
                if (TryApply(in d, cmd, view)) _done.Add(vehicleId);
            foreach (var id in _done) _pending.Remove(id);
            _done.Clear();
        }

        private bool TryApply(in EntityPassengers d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var vehicle) || !view.IsAlive(vehicle)) return false;
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, vehicle, OwnershipExtensions.PackKey(OrdinalValue, 0))) return true;   // ours: drop
            var buffer = new PassengerBuffer();
            Span<long> ids = stackalloc long[] { d.Passenger0, d.Passenger1, d.Passenger2, d.Passenger3, d.Passenger4, d.Passenger5, d.Passenger6, d.Passenger7 };
            for (int i = 0; i < PassengerBuffer.Capacity; i++)   // ⭐ CE-2151 B-b — seat i from slot i; 0 = an empty seat
            {
                if (ids[i] == 0) continue;
                if (!_entityMap.TryGetEntity(ids[i], out var passenger) || !view.IsAlive(passenger)) return false;   // not known yet: hold
                buffer.Passengers[i] = passenger;
                buffer.Count++;
            }
            buffer.Modes = d.Modes;   // ⭐ CE-2152 (R-2011 C5)
            cmd.AddComponent(vehicle, buffer);
            return true;
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) => _pending.Remove(networkEntityId);
    }
}
