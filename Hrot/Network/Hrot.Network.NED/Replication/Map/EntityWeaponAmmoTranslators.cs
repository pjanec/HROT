using System;
using System.Collections.Generic;
using Hrot.NED.Descriptors;
using CycloneDDS.Runtime;
using Fdp.Core;
using Fdp.Interfaces;
using Fdp.Toolkit.Combat.Components;
using Fdp.Toolkit.Replication.Components;
using Fdp.Toolkit.Replication.Extensions;
using Fdp.Toolkit.Replication.Services;
using Fdp.ModuleHost.Abstractions;

namespace Hrot.Map.Common.Replication.Egress
{
    /// <summary>
    /// ⭐ <c>CE-3174</c> (C-7; docs/DESIGN_Entity_Control.md §8d) — publishes a unit's <see cref="WeaponAmmo"/> from the node owning its
    /// <c>dtWeaponAmmo</c> (the brain, where <c>WeaponAmmoMirrorSystem</c> writes it), on change.
    /// </summary>
    public sealed class EntityWeaponAmmoEgressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityWeaponAmmo";
        private readonly DdsWriter<EntityWeaponAmmo>? _writer;
        private readonly Dictionary<long, EntityWeaponAmmo> _last = new();
        private int _worldEpoch;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => (long)EDescriptorType.dtWeaponAmmo;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Egress;

        public EntityWeaponAmmoEgressTranslator(DdsParticipant? participant)
            => _writer = participant is not null ? new DdsWriter<EntityWeaponAmmo>(participant, DdsTopicName) : null;

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view) { }

        public void ScanAndPublish(ISimulationView view)
        {
            if (WorldEpoch.Moved(view, ref _worldEpoch)) _last.Clear();
            if (view is EntityRepository r && !r.IsComponentTypeRegistered<WeaponAmmo>()) return;
            long packedKey = OwnershipExtensions.PackKey(DescriptorOrdinal, 0);
            foreach (var unit in view.Query().With<WeaponAmmo>().With<NetworkIdentity>().Build())
            {
                if (!view.HasAuthority(unit, packedKey)) continue;
                long netId = view.GetComponentRO<NetworkIdentity>(unit).Value;
                var sample = ToSample(netId, view.GetComponentRO<WeaponAmmo>(unit));
                if (_last.TryGetValue(netId, out var prev) && prev.Equals(sample)) continue;
                _last[netId] = sample;
                _writer?.Write(sample);
                SentSampleCount++;
            }
        }

        /// <summary>The wire form (the test seam).</summary>
        internal static EntityWeaponAmmo ToSample(long netId, in WeaponAmmo a) => new()
        {
            EntityId = netId, Count = a.Count,
            Ammo0 = a.Ammo0, Ammo1 = a.Ammo1, Ammo2 = a.Ammo2, Ammo3 = a.Ammo3,
            Max0 = a.Max0, Max1 = a.Max1, Max2 = a.Max2, Max3 = a.Max3,
            Mag0 = a.Mag0, Mag1 = a.Mag1, Mag2 = a.Mag2, Mag3 = a.Mag3,
            Load0 = a.Load0, Load1 = a.Load1, Load2 = a.Load2, Load3 = a.Load3,   // ⭐ CE-1079
            LoadRounds0 = a.LoadRounds0, LoadRounds1 = a.LoadRounds1, LoadRounds2 = a.LoadRounds2, LoadRounds3 = a.LoadRounds3,
            Busy = a.Busy,
        };

        public void Dispose(long networkEntityId) => _last.Remove(networkEntityId);
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
    }
}

namespace Hrot.Map.Common.Replication.Ingress
{
    /// <summary>⭐ <c>CE-3174</c> (C-7) — applies a unit's published <see cref="WeaponAmmo"/> on this node (the station's HUD reads it).
    /// An unknown unit is dropped; the recorded owner never takes its own sample back.</summary>
    public sealed class EntityWeaponAmmoIngressTranslator : IDescriptorTranslator
    {
        private const string DdsTopicName = "EntityWeaponAmmo";
        private const long OrdinalValue = (long)EDescriptorType.dtWeaponAmmo;
        private readonly DdsReader<EntityWeaponAmmo>? _reader;
        private readonly NetworkEntityMap _entityMap;

        public string TopicName => DdsTopicName;
        public long DescriptorOrdinal => OrdinalValue;
        public long ReceivedSampleCount { get; private set; }
        public long SentSampleCount { get; private set; }
        public long DroppedSampleCount { get; private set; }
        public TranslatorDirection Direction => TranslatorDirection.Ingress;

        public EntityWeaponAmmoIngressTranslator(DdsParticipant? participant, NetworkEntityMap entityMap)
        {
            _reader = participant is not null ? new DdsReader<EntityWeaponAmmo>(participant, DdsTopicName) : null;
            _entityMap = entityMap ?? throw new ArgumentNullException(nameof(entityMap));
        }

        public void PollIngress(IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (_reader is null) return;
            using var loan = _reader.Take();
            foreach (var sample in loan)
            {
                if (!sample.IsValid || sample.Info.InstanceState != DdsInstanceState.Alive) continue;
                ReceivedSampleCount++;
                Apply(sample.Data, cmd, view);
            }
        }

        /// <summary>Applies one sample (the test seam).</summary>
        internal void Apply(in EntityWeaponAmmo d, IEntityCommandBuffer cmd, ISimulationView view)
        {
            if (!_entityMap.TryGetEntity(d.EntityId, out var unit) || !view.IsAlive(unit)) { DroppedSampleCount++; return; }
            if (view is EntityRepository repo
                && AuthorityExtensions.IsRecordedOwner(repo, unit, OwnershipExtensions.PackKey(OrdinalValue, 0))) return;
            cmd.AddComponent(unit, new WeaponAmmo
            {
                Count = d.Count,
                Ammo0 = d.Ammo0, Ammo1 = d.Ammo1, Ammo2 = d.Ammo2, Ammo3 = d.Ammo3,
                Max0 = d.Max0, Max1 = d.Max1, Max2 = d.Max2, Max3 = d.Max3,
                Mag0 = d.Mag0, Mag1 = d.Mag1, Mag2 = d.Mag2, Mag3 = d.Mag3,
                Load0 = d.Load0, Load1 = d.Load1, Load2 = d.Load2, Load3 = d.Load3,   // ⭐ CE-1079
                LoadRounds0 = d.LoadRounds0, LoadRounds1 = d.LoadRounds1, LoadRounds2 = d.LoadRounds2, LoadRounds3 = d.LoadRounds3,
                Busy = d.Busy,
            });
        }

        public void ScanAndPublish(ISimulationView view) { }
        public void ApplyToEntity(Entity entity, object data, EntityRepository repo) { }
        public void Dispose(long networkEntityId) { }
    }
}
