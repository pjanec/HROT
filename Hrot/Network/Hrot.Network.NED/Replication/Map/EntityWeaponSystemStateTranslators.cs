using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ Weapon systems WS-1 (W-C, R-136; 📄 docs/designs/weapons/DESIGN_Weapon_Systems.md §2) — publishes a weapon system's
    /// <see cref="WeaponSystemState"/> from the node owning its <c>dtWeaponSystemState</c> (the weapon's brain, where
    /// <see cref="WeaponDeploySystem"/> writes it), on change. Same shape as <see cref="EntityPassengersEgressTranslator"/>.
    /// </summary>
    public sealed class EntityWeaponSystemStateEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityWeaponSystemState";
        private readonly DdsWriter<EntityWeaponSystemState>? _writer;
        private readonly Dictionary<long, EntityWeaponSystemState> _last = new();
        private int _worldEpoch;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtWeaponSystemState;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityWeaponSystemStateEgressTranslator(DdsParticipant? participant)
            => _writer = participant is not null ? new DdsWriter<EntityWeaponSystemState>(participant, DdsTopicName) : null;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch)) _last.Clear();
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<WeaponSystemState>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var weapon in view.Query().With<WeaponSystemState>().DefaultRoutePublishable().Build())
            {
                if (!view.HasAuthority(weapon, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(weapon).Value;
                var sample = ToSample(netId, view.GetComponentRO<WeaponSystemState>(weapon));
                if (_last.TryGetValue(netId, out var prev) && prev.Equals(sample)) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityWeaponSystemState ToSample(long netId, in WeaponSystemState s)
            => new() { EntityId = (int)netId, Deploy = (byte)s.Deploy, Until = s.Until };

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>
    /// ⭐ Weapon systems WS-1 — applies a weapon system's published deploy state on every node that does not own it. A sample for a weapon
    /// this node does not know yet is HELD (the newest per weapon) until it does — it is sent once per change (TransientLocal). The recorded
    /// owner never takes its own sample back (S8 / F-5).
    /// </summary>
    public sealed class EntityWeaponSystemStateIngressTranslator : IDescriptorTranslator, IDiscardableIngress
    {
        private const string DdsTopicName = "EntityWeaponSystemState";
        private const long OrdinalValue = (long)EDescriptorType.dtWeaponSystemState;
        private readonly DdsReader<EntityWeaponSystemState>? _reader;
        private readonly NetworkEntityMap _entityMap;
        private readonly Dictionary<long, EntityWeaponSystemState> _pending = new();
        private readonly List<long> _done = new();

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        /// <summary>Samples waiting for a weapon this node does not know yet.</summary>
        public int PendingCount => _pending.Count;
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityWeaponSystemStateIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityWeaponSystemState>(participant, DdsTopicName) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        /// <inheritdoc />
        public int DiscardIngress() => _reader.DiscardAll();   // ⭐ CE-3329 W5

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
        internal void Receive(in EntityWeaponSystemState d) { ReceivedSampleCount++; _pending[d.EntityId] = d; }

        /// <summary>Applies every held sample whose weapon is known.</summary>
        internal void Flush(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_pending.Count == 0) return;
            foreach (var (id, d) in _pending)
                if (TryApply(in d, cmd, view)) _done.Add(id);
            foreach (var id in _done) _pending.Remove(id);
            _done.Clear();
        }

        private bool TryApply(in EntityWeaponSystemState d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var weapon) || !view.IsAlive(weapon)) return false;
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, weapon, OwnershipExtensions.PackKey(OrdinalValue, 0))) return true;   // ours: drop
            cmd.AddComponent(weapon, new WeaponSystemState { Deploy = (WeaponDeploy)d.Deploy, Until = d.Until });
            return true;
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) => _pending.Remove(networkEntityId);
    }
}
